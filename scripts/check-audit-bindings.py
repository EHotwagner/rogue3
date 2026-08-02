#!/usr/bin/env python3
"""Fail a commit that touches a file any audit under feedback/audits/ binds.

A cycle audit (`feedback/audits/*.audit.json`) pins a sha256 over every file it
cites: the report itself (`report` / `reportSha256`) and every
`findings[].checkedEvidence[]` entry whose locator starts with `file:`.  Those
digests are what make a merged feedback report evidence rather than prose.

Nothing in the repository noticed when a later commit changed one of those
files, so the digests rotted invisibly -- a two-line documentation-only PR
invalidated the evidence binding of an already-merged cycle report, passed CI,
and was caught only because host acceptance happened to re-run the validator
(feedback/2026-08-01-Rogue3-12.md 4.13, feedback/2026-08-02-Rogue3.md 4.1).

This checker makes that failure loud.  Every binding must either be FRESH (the
file's current digest equals the pinned digest) or be listed EXPLICITLY in the
exceptions ledger together with BOTH the digest the audit pins and the digest
the file actually has.  An exception that names a digest the file no longer has
does not excuse anything, so a second edit to an already-excused file fails
again; and because the pinned digest is part of the exception's identity,
rebinding an audit retires the exception rather than silently reusing it.
Exceptions that no longer correspond to a violation are reported as obsolete
and also fail, which stops the ledger from rotting the way the digests did.

Remedies, in preference order:

  1. Rebind the audit -- re-run the feedback tool for that cycle so the audit
     pins the bytes that now exist.
  2. Excuse it explicitly:
       python3 scripts/check-audit-bindings.py --grandfather --reason "<why>"
     which rewrites the ledger from the current violations and prunes obsolete
     entries.  The diff is the record of what was excused and why.

Digest rule: sha256 over the file's text with CRLF/CR normalized to LF, encoded
UTF-8.  This is byte-for-byte the rule the feedback tool applies
(`FeedbackReportTool.sha256Text` over `File.ReadAllText`), so a file this
checker calls fresh is a file the validator calls fresh.

Exit codes: 0 clean, 1 violations found, 2 usage/IO error.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import os
import shutil
import sys
import tempfile
from typing import Any

AUDIT_GLOB_DIR = "feedback/audits"
AUDIT_SUFFIX = ".audit.json"
LEDGER_RELPATH = "scripts/audit-binding-exceptions.json"
LEDGER_SCHEMA = 1
MISSING = "<missing>"

LEDGER_NOTE = (
    "Explicit exceptions to the audit-binding check "
    "(scripts/check-audit-bindings.py). Each entry excuses ONE stale binding "
    "at ONE observed digest: change the file again and it fails again. Prefer "
    "rebinding the audit over adding an entry here."
)


# --------------------------------------------------------------------------
# digests
# --------------------------------------------------------------------------


def digest_text(raw: bytes) -> str:
    """sha256 of newline-normalized UTF-8 text -- the feedback tool's rule."""
    text = raw.decode("utf-8-sig", errors="strict")
    text = text.replace("\r\n", "\n").replace("\r", "\n")
    return hashlib.sha256(text.encode("utf-8")).hexdigest()


def digest_file(path: str) -> str | None:
    if not os.path.isfile(path):
        return None
    with open(path, "rb") as handle:
        return digest_text(handle.read())


# --------------------------------------------------------------------------
# model
# --------------------------------------------------------------------------


class Binding:
    """One (audit, kind, locator) -> pinned digest pin."""

    __slots__ = ("audit", "kind", "locator", "path", "bound", "actual")

    def __init__(self, audit: str, kind: str, locator: str, path: str, bound: str | None):
        self.audit = audit
        self.kind = kind
        self.locator = locator
        self.path = path
        self.bound = bound
        self.actual: str | None = None

    @property
    def key(self) -> tuple[str, str, str, str]:
        # The pinned digest is part of the identity: rebinding an audit retires
        # any exception written against the digest it used to pin.
        return (self.audit, self.kind, self.locator, self.bound or "")

    @property
    def observed(self) -> str:
        return self.actual if self.actual is not None else MISSING

    @property
    def fresh(self) -> bool:
        return self.bound is not None and self.actual == self.bound

    def sort_key(self) -> tuple[str, str, str, str]:
        return self.key


def _rel(root: str, path: str) -> str:
    return os.path.relpath(path, root).replace(os.sep, "/")


def collect_bindings(root: str) -> list[Binding]:
    """Every file binding declared by every audit under feedback/audits/."""
    audit_dir = os.path.join(root, *AUDIT_GLOB_DIR.split("/"))
    bindings: list[Binding] = []
    if not os.path.isdir(audit_dir):
        return bindings

    for name in sorted(os.listdir(audit_dir)):
        if not name.endswith(AUDIT_SUFFIX):
            continue
        audit_abs = os.path.join(audit_dir, name)
        audit_rel = _rel(root, audit_abs)
        with open(audit_abs, "rb") as handle:
            try:
                doc: Any = json.loads(handle.read().decode("utf-8-sig"))
            except (ValueError, UnicodeDecodeError) as exc:
                raise SystemExit(f"{audit_rel}: not readable as JSON: {exc}")
        if not isinstance(doc, dict):
            raise SystemExit(f"{audit_rel}: audit root must be a JSON object")

        report = doc.get("report")
        if isinstance(report, str) and report.strip():
            rel = report.strip()
            bindings.append(
                Binding(audit_rel, "report", f"file:{rel}", rel, _sha(doc.get("reportSha256")))
            )

        findings = doc.get("findings")
        if isinstance(findings, list):
            for finding in findings:
                if not isinstance(finding, dict):
                    continue
                checks = finding.get("checkedEvidence")
                if not isinstance(checks, list):
                    continue
                for check in checks:
                    if not isinstance(check, dict):
                        continue
                    locator = check.get("locator")
                    if not isinstance(locator, str):
                        continue
                    locator = locator.strip()
                    if not locator.startswith("file:"):
                        continue
                    rel = locator[len("file:") :].strip()
                    if not rel or os.path.isabs(rel):
                        continue
                    bindings.append(
                        Binding(audit_rel, "evidence", f"file:{rel}", rel, _sha(check.get("sha256")))
                    )

    for binding in bindings:
        binding.actual = digest_file(os.path.join(root, *binding.path.split("/")))

    # One audit routinely cites the same file at the same digest from several
    # findings. That is one binding, not several.
    unique: dict[tuple[str, str, str, str], Binding] = {}
    for binding in bindings:
        unique.setdefault(binding.key, binding)
    return sorted(unique.values(), key=Binding.sort_key)


def _sha(value: Any) -> str | None:
    if isinstance(value, str) and value.strip():
        return value.strip().lower()
    return None


# --------------------------------------------------------------------------
# ledger
# --------------------------------------------------------------------------


def ledger_path(root: str) -> str:
    return os.path.join(root, *LEDGER_RELPATH.split("/"))


def entry_key(entry: dict[str, str]) -> tuple[str, str, str, str]:
    return (entry["audit"], entry["kind"], entry["locator"], entry["boundSha256"])


def load_ledger(root: str) -> dict[tuple[str, str, str, str], dict[str, str]]:
    path = ledger_path(root)
    if not os.path.isfile(path):
        return {}
    with open(path, "rb") as handle:
        try:
            doc = json.loads(handle.read().decode("utf-8-sig"))
        except (ValueError, UnicodeDecodeError) as exc:
            raise SystemExit(f"{LEDGER_RELPATH}: not readable as JSON: {exc}")
    if not isinstance(doc, dict):
        raise SystemExit(f"{LEDGER_RELPATH}: root must be a JSON object")
    entries = doc.get("entries")
    if entries is None:
        entries = []
    if not isinstance(entries, list):
        raise SystemExit(f"{LEDGER_RELPATH}: 'entries' must be a list")

    required = ("audit", "kind", "locator", "boundSha256", "observedSha256", "reason")
    out: dict[tuple[str, str, str, str], dict[str, str]] = {}
    for index, entry in enumerate(entries):
        if not isinstance(entry, dict):
            raise SystemExit(f"{LEDGER_RELPATH}: entry {index} must be an object")
        missing = [field for field in required if not entry.get(field)]
        if missing:
            raise SystemExit(
                f"{LEDGER_RELPATH}: entry {index} is missing required field(s): {', '.join(missing)}"
            )
        normalized = {field: str(entry[field]) for field in required}
        key = entry_key(normalized)
        if key in out:
            raise SystemExit(f"{LEDGER_RELPATH}: duplicate entry for {key[0]} {key[2]}")
        out[key] = normalized
    return out


def write_ledger(root: str, entries: list[dict[str, str]]) -> None:
    ordered = sorted(entries, key=entry_key)
    doc = {"grandfatherSchema": LEDGER_SCHEMA, "note": LEDGER_NOTE, "entries": ordered}
    path = ledger_path(root)
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "w", encoding="utf-8", newline="\n") as handle:
        json.dump(doc, handle, indent=2, sort_keys=False, ensure_ascii=True)
        handle.write("\n")


# --------------------------------------------------------------------------
# check
# --------------------------------------------------------------------------


def evaluate(root: str) -> dict[str, Any]:
    bindings = collect_bindings(root)
    ledger = load_ledger(root)

    fresh: list[Binding] = []
    excused: list[dict[str, str]] = []
    violations: list[dict[str, Any]] = []
    used_keys: set[tuple[str, str, str, str]] = set()

    for binding in bindings:
        if binding.bound is None:
            violations.append(_violation(binding, "audit pins no sha256 for this file locator"))
            continue
        if binding.fresh:
            fresh.append(binding)
            continue

        entry = ledger.get(binding.key)
        if entry is not None:
            used_keys.add(binding.key)
            if entry["observedSha256"] != binding.observed:
                violations.append(
                    _violation(
                        binding,
                        "file changed again since it was excused "
                        f"(ledger excused {_short(entry['observedSha256'])}, file is now "
                        f"{_short(binding.observed)})",
                    )
                )
                continue
            excused.append(dict(entry))
            continue

        if binding.actual is None:
            violations.append(_violation(binding, "bound file does not exist"))
        else:
            violations.append(_violation(binding, "file changed since the audit bound it"))

    obsolete = [ledger[key] for key in sorted(ledger.keys()) if key not in used_keys]

    return {
        "root": root,
        "audits": len({b.audit for b in bindings}),
        "bindings": len(bindings),
        "fresh": len(fresh),
        "excused": len(excused),
        "excusedEntries": excused,
        "violations": violations,
        "obsoleteExceptions": obsolete,
        "ok": not violations and not obsolete,
    }


def _short(sha: str) -> str:
    return sha if sha == MISSING else sha[:12]


def _violation(binding: Binding, why: str) -> dict[str, Any]:
    return {
        "audit": binding.audit,
        "kind": binding.kind,
        "locator": binding.locator,
        "path": binding.path,
        "boundSha256": binding.bound or "",
        "observedSha256": binding.observed,
        "reason": why,
    }


def grandfather(root: str, reason: str) -> dict[str, Any]:
    """Rewrite the ledger from the current violations, pruning obsolete entries."""
    previous = load_ledger(root)
    result = evaluate(root)
    # Entries that still excuse a live violation are carried forward verbatim;
    # only newly stale bindings pick up the new --reason. Anything that excuses
    # nothing at all is simply not re-emitted, which is the prune.
    entries: list[dict[str, str]] = [dict(entry) for entry in result["excusedEntries"]]
    for violation in result["violations"]:
        if not violation["boundSha256"]:
            # An audit that pins no digest at all cannot be excused -- there is
            # nothing to excuse it against. Rebind that audit.
            continue
        entries.append(
            {
                "audit": violation["audit"],
                "kind": violation["kind"],
                "locator": violation["locator"],
                "boundSha256": violation["boundSha256"],
                "observedSha256": violation["observedSha256"],
                "reason": reason,
            }
        )
    write_ledger(root, entries)
    kept = {entry_key(entry) for entry in entries}
    return {
        "written": LEDGER_RELPATH,
        "entries": len(entries),
        "carriedForward": sum(1 for entry in entries if previous.get(entry_key(entry)) == entry),
        "pruned": len([key for key in previous if key not in kept]),
    }


# --------------------------------------------------------------------------
# reporting
# --------------------------------------------------------------------------


def report_text(result: dict[str, Any], stream) -> None:
    violations = result["violations"]
    obsolete = result["obsoleteExceptions"]

    print(
        "audit-bindings: {audits} audits, {bindings} bindings, {fresh} fresh, "
        "{excused} explicitly excused".format(**result),
        file=stream,
    )

    if violations:
        print(
            f"\naudit-bindings: {len(violations)} STALE BINDING(S) "
            "-- a file changed but the audit that binds it did not:",
            file=stream,
        )
        by_audit: dict[str, list[dict[str, Any]]] = {}
        for violation in violations:
            by_audit.setdefault(violation["audit"], []).append(violation)
        for audit in sorted(by_audit):
            print(f"\n  {audit}", file=stream)
            for violation in by_audit[audit]:
                print(f"    {violation['locator']}", file=stream)
                print(
                    f"      bound {_short(violation['boundSha256']) or '(none)'}"
                    f"  now {_short(violation['observedSha256'])}"
                    f"  -- {violation['reason']}",
                    file=stream,
                )

    if obsolete:
        print(
            f"\naudit-bindings: {len(obsolete)} OBSOLETE EXCEPTION(S) in {LEDGER_RELPATH} "
            "-- these no longer excuse anything and must be pruned:",
            file=stream,
        )
        for entry in obsolete:
            print(f"    {entry['audit']}  {entry['locator']}", file=stream)

    if violations or obsolete:
        print(
            "\naudit-bindings: fix by REBINDING the audit (re-run the feedback tool for that\n"
            "cycle so it pins the bytes that now exist), or by excusing each one EXPLICITLY:\n"
            '    python3 scripts/check-audit-bindings.py --grandfather --reason "<why>"\n'
            "then commit the ledger. An exception is pinned to one observed digest, so the\n"
            "next change to the same file fails again.",
            file=stream,
        )
    else:
        print("audit-bindings: OK -- every audit binding is fresh or explicitly excused.", file=stream)


# --------------------------------------------------------------------------
# self-test
# --------------------------------------------------------------------------


def _write(root: str, rel: str, text: str) -> None:
    path = os.path.join(root, *rel.split("/"))
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "w", encoding="utf-8", newline="\n") as handle:
        handle.write(text)


def _make_audit(root: str, name: str, report_rel: str, evidence: list[str]) -> None:
    """Write a report + an audit that correctly binds it and its evidence."""
    _write(root, report_rel, f"# report {name}\n")
    findings = [
        {
            "id": "§4.1",
            "checkedEvidence": [
                {"locator": f"file:{rel}", "result": "verified", "sha256": digest_file(os.path.join(root, *rel.split("/")))}
                for rel in evidence
            ]
            + [{"locator": "command:dotnet test", "result": "verified"}],
        }
    ]
    doc = {
        "auditSchema": 1,
        "report": report_rel,
        "reportSha256": digest_file(os.path.join(root, *report_rel.split("/"))),
        "findings": findings,
    }
    _write(root, f"feedback/audits/{name}.audit.json", json.dumps(doc, indent=2) + "\n")


def selftest() -> int:
    failures: list[str] = []

    def check(label: str, condition: bool) -> None:
        if condition:
            print(f"  ok   {label}")
        else:
            print(f"  FAIL {label}")
            failures.append(label)

    root = tempfile.mkdtemp(prefix="audit-bindings-selftest-")
    try:
        _write(root, "src/thing.fs", "let a = 1\n")
        _write(root, "docs/note.md", "note\n")
        _make_audit(root, "cycle-1", "feedback/cycle-1.md", ["src/thing.fs", "docs/note.md"])

        print("audit-bindings selftest")

        r = evaluate(root)
        check("a freshly bound tree passes", r["ok"] and r["bindings"] == 3 and r["fresh"] == 3)

        # 1. an edit to a bound file is a violation
        _write(root, "src/thing.fs", "let a = 2\n")
        r = evaluate(root)
        check("editing a bound file fails", not r["ok"] and len(r["violations"]) == 1)
        check(
            "the violation names the file and the audit",
            r["violations"][0]["locator"] == "file:src/thing.fs"
            and r["violations"][0]["audit"] == "feedback/audits/cycle-1.audit.json",
        )

        # 2. a non-bound file is invisible to the check
        _write(root, "src/unbound.fs", "let b = 1\n")
        check("an unbound file is not a violation", len(evaluate(root)["violations"]) == 1)

        # 3. an explicit exception excuses exactly that digest
        grandfather(root, "selftest: excused")
        r = evaluate(root)
        check("--grandfather excuses the violation", r["ok"] and r["excused"] == 1)

        # 4. the exception does NOT survive a second edit
        _write(root, "src/thing.fs", "let a = 3\n")
        r = evaluate(root)
        check(
            "a second edit to an excused file fails again",
            not r["ok"] and "changed again" in r["violations"][0]["reason"],
        )

        # 5. restoring the bytes makes the binding fresh, and the exception obsolete
        _write(root, "src/thing.fs", "let a = 1\n")
        r = evaluate(root)
        check(
            "an exception that no longer excuses anything is obsolete",
            not r["ok"] and len(r["obsoleteExceptions"]) == 1 and not r["violations"],
        )

        # 6. --grandfather prunes obsolete entries
        grandfather(root, "selftest: prune")
        r = evaluate(root)
        check("--grandfather prunes obsolete entries", r["ok"] and not r["obsoleteExceptions"])
        check("the pruned ledger holds no entries", load_ledger(root) == {})

        # 7. deleting a bound file fails
        os.remove(os.path.join(root, "docs", "note.md"))
        r = evaluate(root)
        check(
            "deleting a bound file fails",
            not r["ok"] and r["violations"][0]["observedSha256"] == MISSING,
        )
        _write(root, "docs/note.md", "note\n")

        # 8. the report binding is checked, not just evidence
        _write(root, "feedback/cycle-1.md", "# report cycle-1 EDITED\n")
        r = evaluate(root)
        check(
            "the report's own binding is checked",
            not r["ok"] and r["violations"][0]["kind"] == "report",
        )
        _write(root, "feedback/cycle-1.md", "# report cycle-1\n")

        # 9. rebinding the audit clears the violation without a ledger entry
        _write(root, "src/thing.fs", "let a = 9\n")
        _make_audit(root, "cycle-1", "feedback/cycle-1.md", ["src/thing.fs", "docs/note.md"])
        r = evaluate(root)
        check("rebinding the audit clears the violation", r["ok"])

        # 10. CRLF is normalized the way the feedback tool normalizes it
        crlf = os.path.join(root, "src", "crlf.fs")
        os.makedirs(os.path.dirname(crlf), exist_ok=True)
        with open(crlf, "wb") as handle:
            handle.write(b"let a = 1\r\n")
        check(
            "CRLF and LF digest identically",
            digest_file(crlf) == digest_text(b"let a = 1\n"),
        )

        # 11. a second audit binding the same file is reported independently
        _make_audit(root, "cycle-2", "feedback/cycle-2.md", ["src/thing.fs"])
        _write(root, "src/thing.fs", "let a = 10\n")
        r = evaluate(root)
        check("each audit's binding is reported separately", len(r["violations"]) == 2)

        # 12. the ledger is written deterministically
        grandfather(root, "selftest: determinism")
        with open(ledger_path(root), "rb") as handle:
            first = handle.read()
        grandfather(root, "selftest: determinism")
        with open(ledger_path(root), "rb") as handle:
            second = handle.read()
        check("the ledger is byte-stable across reruns", first == second)

        # 13. rebinding an audit retires its exception instead of reusing it
        _write(root, "src/rebind.fs", "let c = 1\n")
        _make_audit(root, "cycle-3", "feedback/cycle-3.md", ["src/rebind.fs"])
        _write(root, "src/rebind.fs", "let c = 2\n")
        grandfather(root, "selftest: excused before rebind")
        before = len(load_ledger(root))
        check("the pre-rebind edit is excused", evaluate(root)["ok"])
        _make_audit(root, "cycle-3", "feedback/cycle-3.md", ["src/rebind.fs"])
        r = evaluate(root)
        check(
            "rebinding retires the exception instead of reusing it",
            not r["ok"] and len(r["obsoleteExceptions"]) == 1 and not r["violations"],
        )
        grandfather(root, "selftest: prune after rebind")
        check(
            "pruning after a rebind removes exactly the retired exception",
            evaluate(root)["ok"] and len(load_ledger(root)) == before - 1,
        )

        # 14. a hand-written exception missing a reason is rejected
        _write(
            root,
            LEDGER_RELPATH,
            json.dumps(
                {
                    "grandfatherSchema": 1,
                    "entries": [
                        {
                            "audit": "feedback/audits/cycle-1.audit.json",
                            "kind": "evidence",
                            "locator": "file:src/thing.fs",
                            "observedSha256": "0" * 64,
                        }
                    ],
                },
                indent=2,
            )
            + "\n",
        )
        rejected = False
        try:
            evaluate(root)
        except SystemExit:
            rejected = True
        check("an exception without a reason is rejected", rejected)
    finally:
        shutil.rmtree(root, ignore_errors=True)

    if failures:
        print(f"audit-bindings selftest: {len(failures)} FAILED")
        return 1
    print("audit-bindings selftest: all checks passed")
    return 0


# --------------------------------------------------------------------------
# entry point
# --------------------------------------------------------------------------


def default_root() -> str:
    return os.path.dirname(os.path.dirname(os.path.abspath(__file__)))


def main(argv: list[str]) -> int:
    parser = argparse.ArgumentParser(
        prog="check-audit-bindings.py",
        description="Fail when a file bound by an audit under feedback/audits/ has changed.",
    )
    parser.add_argument("--root", default=None, help="repository root (default: the script's parent)")
    parser.add_argument("--json", action="store_true", help="emit the machine-readable result")
    parser.add_argument(
        "--grandfather",
        action="store_true",
        help="rewrite the exceptions ledger from the current violations and prune obsolete entries",
    )
    parser.add_argument("--reason", default=None, help="justification recorded with --grandfather")
    parser.add_argument("--selftest", action="store_true", help="exercise the checker against a temporary tree")
    args = parser.parse_args(argv)

    if args.selftest:
        return selftest()

    root = os.path.abspath(args.root or default_root())
    if not os.path.isdir(root):
        print(f"audit-bindings: not a directory: {root}", file=sys.stderr)
        return 2

    if args.grandfather:
        if not args.reason or not args.reason.strip():
            print("audit-bindings: --grandfather requires --reason", file=sys.stderr)
            return 2
        outcome = grandfather(root, args.reason.strip())
        if args.json:
            print(json.dumps(outcome, indent=2))
        else:
            print(
                f"audit-bindings: wrote {outcome['written']} with {outcome['entries']} "
                f"exception(s); pruned {outcome['pruned']}."
            )
        return 0

    result = evaluate(root)
    if args.json:
        print(json.dumps(result, indent=2))
    else:
        report_text(result, sys.stdout)
    return 0 if result["ok"] else 1


if __name__ == "__main__":
    try:
        sys.exit(main(sys.argv[1:]))
    except SystemExit:
        raise
    except OSError as error:  # pragma: no cover - defensive
        print(f"audit-bindings: {error}", file=sys.stderr)
        sys.exit(2)
