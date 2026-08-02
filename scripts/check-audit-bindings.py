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

  1. Rebind the audit, so it pins the bytes that now exist.  NOTE: feedback-tool
     .fsx has no `rebind` subcommand -- it offers `digest <file>` for one hash at
     a time, so this means recomputing and pasting each `sha256` by hand.  It is
     still the right answer whenever the cited evidence still supports the
     finding.
  2. Excuse it explicitly:
       python3 scripts/check-audit-bindings.py --grandfather --reason "<why>"
     which rewrites the ledger from the current violations and prunes obsolete
     entries.  The diff is the record of what was excused and why.

Because (1) has no tooling, expect (2) to be the common path until a rebind
command exists.  That is a known weakness, not the intended design.

NOT BOUND: the checker's own remedy surfaces
--------------------------------------------

Both remedies above WRITE something: (1) writes an audit, (2) writes the
exceptions ledger.  So a binding whose target is itself one of those two
surfaces has no fixed point, and the gate was asking for the impossible
(rogue3#38):

  * An audit that cites `scripts/audit-binding-exceptions.json` -- reasonable
    when the finding is *about* the ledger -- goes stale the moment anything is
    excused, because excusing rewrites the ledger.  Excusing that binding writes
    the ledger, which changes the ledger, which invalidates the excuse just
    written.  Reproduced at d8d0024: four consecutive `--grandfather` runs
    produced four distinct ledger digests and `check` still exited 1.
  * An audit that cites another `*.audit.json` -- reasonable when the finding is
    *about* that audit -- goes stale the moment the cited audit is rebound, and
    rebinding is remedy (1).  Two audits that cite each other have no fixed
    point at all.

So bindings onto those two path classes are NOT checked.  They are not silently
dropped: `evaluate` returns them under `notBound`, the text report names each
one, and `--json` carries them, so a reader can always tell the difference
between "this citation is exempt" and "the checker missed it".

This is a deliberate narrowing of the gate, and it has a cost: an edit to a
merged `*.audit.json` is no longer detected here.  Audit immutability needs a
mechanism that does not live inside the audit set -- git history, not a digest
the same commit can rewrite.

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
import ntpath
import os
import shutil
import subprocess
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
    """sha256 of newline-normalized UTF-8 text -- the feedback tool's rule.

    `errors="replace"` matches .NET: `File.ReadAllText` substitutes U+FFFD for
    undecodable bytes and never throws. Being stricter here would mean the
    first audit citing a rendered PNG as `file:` evidence crashes the gate with
    a traceback -- and crashes `--grandfather` too, leaving no way out.
    """
    text = raw.decode("utf-8-sig", errors="replace")
    text = text.replace("\r\n", "\n").replace("\r", "\n")
    return hashlib.sha256(text.encode("utf-8")).hexdigest()


def digest_file(path: str) -> str | None:
    if not os.path.isfile(path):
        return None
    with open(path, "rb") as handle:
        return digest_text(handle.read())


class UsageError(Exception):
    """An input the checker cannot interpret -- distinct from a violation."""


def resolve_inside(root: str, rel: str) -> str | None:
    """Absolute path of `rel` under `root`, or None when it escapes.

    Mirrors FeedbackReportTool.resolveEvidencePath: reject absolute paths, and
    reject anything that resolves outside the workspace -- including through a
    symlink, which is why both sides are realpath'd. Without this, an audit
    could bind `file:../outside.txt` and the checker would happily digest a
    file the repository does not contain and call the binding fresh.
    """
    if not rel or os.path.isabs(rel) or ntpath.isabs(rel):
        return None
    root_real = os.path.realpath(root)
    candidate = os.path.realpath(os.path.join(root_real, *rel.split("/")))
    if candidate == root_real:
        return None
    if not candidate.startswith(root_real + os.sep):
        return None
    return candidate


# --------------------------------------------------------------------------
# model
# --------------------------------------------------------------------------


class Binding:
    """One (audit, kind, locator) -> pinned digest pin."""

    __slots__ = ("audit", "kind", "locator", "path", "abspath", "bound", "actual")

    def __init__(self, audit: str, kind: str, locator: str, path: str, abspath: str, bound: str | None):
        self.audit = audit
        self.kind = kind
        self.locator = locator
        self.path = path
        self.abspath = abspath
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


# The two path classes this checker must WRITE in order to clear a violation.
# Binding one of them is a fixed-point equation with no fixed point -- see the
# module docstring and rogue3#38.
LEDGER_EXEMPTION = (
    "this is the exceptions ledger itself: the only place an excuse can live, so "
    "excusing a binding on it rewrites it and invalidates the excuse just written"
)
AUDIT_EXEMPTION = (
    "this is another cycle audit: the remedy for a stale binding is to rebind an "
    "audit, so a binding onto an audit is invalidated by its own remedy"
)


def exemption(rel: str) -> str | None:
    """Why `rel` cannot be bound, or None when it is an ordinary file.

    Takes the WORKSPACE-RELATIVE path derived from the RESOLVED location, not
    the locator text, so `file:feedback/../scripts/audit-binding-exceptions.json`
    is recognised as the ledger too. A textual match on the locator would let a
    one-token rewrite reintroduce the unsatisfiable binding this exempts.
    """
    if rel == LEDGER_RELPATH:
        return LEDGER_EXEMPTION
    if rel.startswith(AUDIT_GLOB_DIR + "/") and rel.endswith(AUDIT_SUFFIX):
        return AUDIT_EXEMPTION
    return None


def collect_bindings(root: str) -> tuple[list[Binding], list[dict[str, Any]], list[dict[str, Any]]]:
    """Every checkable file binding, every malformed audit, every exempt citation.

    Shape is checked strictly. An audit whose `findings` or `checkedEvidence`
    is absent, renamed, or not an array would otherwise contribute ZERO
    bindings and pass silently -- which turns this whole gate into a no-op that
    a single mistyped key can open. Malformed structure is a violation, and one
    the exceptions ledger cannot excuse: it pins no digest, so there is nothing
    to excuse it against. Fix the audit.

    The third list holds citations onto the checker's own remedy surfaces (see
    `exemption`). They are reported, never checked, and never a violation.
    """
    audit_dir = os.path.join(root, *AUDIT_GLOB_DIR.split("/"))
    bindings: list[Binding] = []
    malformed: list[dict[str, Any]] = []
    exempt: list[dict[str, Any]] = []
    if not os.path.isdir(audit_dir):
        return bindings, malformed, exempt

    def note_exempt(audit_rel: str, kind: str, locator: str, path: str, why: str) -> None:
        exempt.append(
            {
                "audit": audit_rel,
                "kind": kind,
                "locator": locator,
                "path": path,
                "reason": why,
            }
        )

    def bad(audit_rel: str, where: str, why: str) -> None:
        malformed.append(
            {
                "audit": audit_rel,
                "kind": "structure",
                "locator": where,
                "path": "",
                "boundSha256": "",
                "observedSha256": MISSING,
                "reason": why,
            }
        )

    # Walk, not listdir: an audit filed in a subdirectory must not be invisible.
    audit_files: list[str] = []
    for current, _dirs, names in os.walk(audit_dir):
        for name in names:
            if name.endswith(AUDIT_SUFFIX):
                audit_files.append(os.path.join(current, name))

    for audit_abs in sorted(audit_files):
        audit_rel = _rel(root, audit_abs)
        with open(audit_abs, "rb") as handle:
            try:
                doc: Any = json.loads(handle.read().decode("utf-8-sig"))
            except (ValueError, UnicodeDecodeError) as exc:
                bad(audit_rel, "<document>", f"not readable as JSON: {exc}")
                continue
        if not isinstance(doc, dict):
            bad(audit_rel, "<document>", "audit root must be a JSON object")
            continue

        report = doc.get("report")
        if not isinstance(report, str) or not report.strip():
            bad(audit_rel, "report", "audit must name the report it binds")
        else:
            rel = report.strip()
            resolved = resolve_inside(root, rel)
            if resolved is None:
                bad(
                    audit_rel,
                    f"report {rel}",
                    "report path must be workspace-relative and stay inside the workspace",
                )
            else:
                why = exemption(_rel(root, resolved))
                if why is not None:
                    note_exempt(audit_rel, "report", f"file:{rel}", rel, why)
                else:
                    bindings.append(
                        Binding(
                            audit_rel, "report", f"file:{rel}", rel, resolved,
                            _sha(doc.get("reportSha256")),
                        )
                    )

        findings = doc.get("findings")
        if not isinstance(findings, list):
            bad(
                audit_rel,
                "findings",
                f"'findings' must be an array, found {_typename(findings)} -- an audit whose "
                "findings cannot be read binds nothing and would pass silently",
            )
            continue

        for index, finding in enumerate(findings):
            if not isinstance(finding, dict):
                bad(audit_rel, f"findings[{index}]", "each finding must be a JSON object")
                continue
            fid = finding.get("id") if isinstance(finding.get("id"), str) else f"findings[{index}]"
            checks = finding.get("checkedEvidence")
            if not isinstance(checks, list):
                bad(
                    audit_rel,
                    f"{fid}.checkedEvidence",
                    f"'checkedEvidence' must be an array, found {_typename(checks)} -- absent, "
                    "renamed or mistyped, this finding's file bindings vanish unnoticed",
                )
                continue
            for position, check in enumerate(checks):
                if not isinstance(check, dict):
                    bad(audit_rel, f"{fid}.checkedEvidence[{position}]", "each entry must be a JSON object")
                    continue
                locator = check.get("locator")
                if not isinstance(locator, str) or not locator.strip():
                    bad(
                        audit_rel,
                        f"{fid}.checkedEvidence[{position}]",
                        f"'locator' must be a non-empty string, found {_typename(locator)}",
                    )
                    continue
                locator = locator.strip()
                if not locator.startswith("file:"):
                    continue  # command:/issue: locators pin no bytes; nothing to check
                rel = locator[len("file:") :].strip()
                resolved = resolve_inside(root, rel)
                if resolved is None:
                    bad(
                        audit_rel,
                        f"{fid} {locator}",
                        "file locator must be a non-empty workspace-relative path that stays "
                        "inside the workspace -- an unresolvable locator would otherwise skip "
                        "the binding silently",
                    )
                    continue
                why = exemption(_rel(root, resolved))
                if why is not None:
                    note_exempt(audit_rel, "evidence", locator, rel, why)
                    continue
                bindings.append(
                    Binding(
                        audit_rel, "evidence", f"file:{rel}", rel, resolved,
                        _sha(check.get("sha256")),
                    )
                )

    for binding in bindings:
        binding.actual = digest_file(binding.abspath)

    # One audit routinely cites the same file at the same digest from several
    # findings. That is one binding, not several.
    unique: dict[tuple[str, str, str, str], Binding] = {}
    for binding in bindings:
        unique.setdefault(binding.key, binding)
    unique_exempt: dict[tuple[str, str, str], dict[str, Any]] = {}
    for entry in exempt:
        unique_exempt.setdefault((entry["audit"], entry["kind"], entry["locator"]), entry)
    return (
        sorted(unique.values(), key=Binding.sort_key),
        malformed,
        sorted(unique_exempt.values(), key=lambda e: (e["audit"], e["kind"], e["locator"])),
    )


def _typename(value: Any) -> str:
    return "nothing" if value is None else type(value).__name__


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
            raise UsageError(f"{LEDGER_RELPATH}: not readable as JSON: {exc}")
    if not isinstance(doc, dict):
        raise UsageError(f"{LEDGER_RELPATH}: root must be a JSON object")
    entries = doc.get("entries")
    if entries is None:
        entries = []
    if not isinstance(entries, list):
        raise UsageError(f"{LEDGER_RELPATH}: 'entries' must be a list")

    required = ("audit", "kind", "locator", "boundSha256", "observedSha256", "reason")
    out: dict[tuple[str, str, str, str], dict[str, str]] = {}
    for index, entry in enumerate(entries):
        if not isinstance(entry, dict):
            raise UsageError(f"{LEDGER_RELPATH}: entry {index} must be an object")
        missing = [field for field in required if not entry.get(field)]
        if missing:
            raise UsageError(
                f"{LEDGER_RELPATH}: entry {index} is missing required field(s): {', '.join(missing)}"
            )
        normalized = {field: str(entry[field]) for field in required}
        key = entry_key(normalized)
        if key in out:
            raise UsageError(f"{LEDGER_RELPATH}: duplicate entry for {key[0]} {key[2]}")
        out[key] = normalized
    return out


def write_ledger(root: str, entries: list[dict[str, str]]) -> None:
    ordered = sorted(entries, key=entry_key)
    doc = {"grandfatherSchema": LEDGER_SCHEMA, "note": LEDGER_NOTE, "entries": ordered}
    path = ledger_path(root)
    os.makedirs(os.path.dirname(path), exist_ok=True)
    # Temp + rename: an interrupted rewrite must not truncate the ledger, which
    # is the only record of what was excused.
    temp = path + ".tmp"
    with open(temp, "w", encoding="utf-8", newline="\n") as handle:
        json.dump(doc, handle, indent=2, sort_keys=False, ensure_ascii=True)
        handle.write("\n")
    os.replace(temp, path)


# --------------------------------------------------------------------------
# check
# --------------------------------------------------------------------------


def evaluate(root: str) -> dict[str, Any]:
    bindings, malformed, exempt = collect_bindings(root)
    ledger = load_ledger(root)

    fresh: list[Binding] = []
    excused: list[dict[str, str]] = []
    violations: list[dict[str, Any]] = list(malformed)
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
        "audits": len(
            {b.audit for b in bindings}
            | {m["audit"] for m in malformed}
            | {e["audit"] for e in exempt}
        ),
        "bindings": len(bindings),
        "malformed": len(malformed),
        "fresh": len(fresh),
        "excused": len(excused),
        "excusedEntries": excused,
        "violations": violations,
        "obsoleteExceptions": obsolete,
        # Citations onto the checker's own remedy surfaces: reported, not
        # checked, never a violation. Present so exemption is auditable rather
        # than an invisible hole -- see the module docstring and rogue3#38.
        "notBound": exempt,
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
    unexcusable: list[dict[str, Any]] = []
    for violation in result["violations"]:
        if not violation["boundSha256"]:
            # A malformed audit, or one that pins no digest at all, cannot be
            # excused -- there is nothing to excuse it against. Fix the audit.
            unexcusable.append(violation)
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
        "notExcusable": unexcusable,
    }


# --------------------------------------------------------------------------
# reporting
# --------------------------------------------------------------------------


def report_text(result: dict[str, Any], stream) -> None:
    malformed = [v for v in result["violations"] if v["kind"] == "structure"]
    violations = [v for v in result["violations"] if v["kind"] != "structure"]
    obsolete = result["obsoleteExceptions"]

    not_bound = result.get("notBound", [])

    print(
        "audit-bindings: {audits} audits, {bindings} bindings, {fresh} fresh, "
        "{excused} explicitly excused".format(**result),
        file=stream,
    )

    if not_bound:
        print(
            f"\naudit-bindings: {len(not_bound)} citation(s) NOT BOUND -- a binding onto "
            "the checker's\nown remedy surfaces can never be satisfied, so it is reported "
            "rather than checked:",
            file=stream,
        )
        by_reason: dict[str, list[dict[str, Any]]] = {}
        for entry in not_bound:
            by_reason.setdefault(entry["reason"], []).append(entry)
        for reason in sorted(by_reason):
            for entry in by_reason[reason]:
                print(f"    {entry['audit']}  {entry['locator']}", file=stream)
            print(f"      {reason}", file=stream)

    if malformed:
        print(
            f"\naudit-bindings: {len(malformed)} MALFORMED AUDIT LOCATION(S) "
            "-- these bind nothing and would otherwise pass silently:",
            file=stream,
        )
        for entry in malformed:
            print(f"    {entry['audit']}  {entry['locator']}", file=stream)
            print(f"      {entry['reason']}", file=stream)
        print(
            "\n  The exceptions ledger cannot excuse these: they pin no digest, so there is\n"
            "  nothing to excuse them against. Repair the audit document.",
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
            "\naudit-bindings: fix by REBINDING the audit -- recompute each stale sha256 so it\n"
            "pins the bytes that now exist (feedback-tool.fsx has no rebind subcommand; use\n"
            "`-- digest <file>` per file) -- or by excusing each one EXPLICITLY:\n"
            '    python3 scripts/check-audit-bindings.py --grandfather --reason "<why>"\n'
            "then commit the ledger. An exception is pinned to one observed digest, so the\n"
            "next change to the same file fails again.",
            file=stream,
        )
    elif not malformed:
        print("audit-bindings: OK -- every audit binding is fresh or explicitly excused.", file=stream)


# --------------------------------------------------------------------------
# self-test
# --------------------------------------------------------------------------


def _write(root: str, rel: str, text: str) -> None:
    path = os.path.join(root, *rel.split("/"))
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "w", encoding="utf-8", newline="\n") as handle:
        handle.write(text)


def _bad_root(script: str) -> int:
    return subprocess.run(
        [sys.executable, script, "--root", os.path.join(tempfile.gettempdir(), "no-such-root-xyz")],
        capture_output=True,
        text=True,
    ).returncode


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


def _empty_ledger(root: str) -> None:
    _write(
        root,
        LEDGER_RELPATH,
        json.dumps({"grandfatherSchema": LEDGER_SCHEMA, "note": LEDGER_NOTE, "entries": []}, indent=2)
        + "\n",
    )


def selftest_exemptions(check) -> None:
    """rogue3#38: a binding onto the checker's own remedy surfaces must not exist.

    Every case here gets its OWN tree. These are claims about what REPEATED
    --grandfather runs settle to, and sharing a tree with the cases above would
    let an unrelated leftover violation, rather than the exemption, decide the
    verdict.
    """
    # --- the ledger shape: excusing rewrites the file the audit binds --------
    root = tempfile.mkdtemp(prefix="audit-bindings-selftest-ledger-")
    try:
        _write(root, "src/thing.fs", "let a = 1\n")
        _make_audit(root, "cycle-1", "feedback/cycle-1.md", ["src/thing.fs"])
        _empty_ledger(root)
        # A finding ABOUT the ledger cites the ledger. That is the reasonable
        # thing to do, and before the fix it was unsatisfiable.
        _make_audit(root, "cycle-ledger", "feedback/cycle-ledger.md", [LEDGER_RELPATH])

        # Give --grandfather real work, so it must write the ledger.
        _write(root, "src/thing.fs", "let a = 2\n")
        grandfather(root, "selftest: ledger convergence, first pass")
        first = digest_file(ledger_path(root))
        grandfather(root, "selftest: ledger convergence, second pass")
        second = digest_file(ledger_path(root))
        check("an audit citing the ledger converges after two --grandfather runs", evaluate(root)["ok"])
        check("the second --grandfather run leaves the ledger byte-identical", first == second)
        check(
            "the ledger citation is reported as not bound, not silently dropped",
            any(
                entry["locator"] == f"file:{LEDGER_RELPATH}" and entry["reason"] == LEDGER_EXEMPTION
                for entry in evaluate(root).get("notBound", [])
            ),
        )

        # The exemption is decided on the RESOLVED path, so a traversing locator
        # cannot smuggle the unsatisfiable binding back in.
        _make_audit(root, "cycle-dots", "feedback/cycle-dots.md", ["feedback/../" + LEDGER_RELPATH])
        check(
            "a traversing locator onto the ledger is exempt too",
            any(
                entry["reason"] == LEDGER_EXEMPTION and ".." in entry["locator"]
                for entry in evaluate(root).get("notBound", [])
            ),
        )
        grandfather(root, "selftest: ledger convergence, after traversal")
        check("the tree is still green after the traversing citation", evaluate(root)["ok"])
    finally:
        shutil.rmtree(root, ignore_errors=True)

    # --- the audit shape: rebinding is the remedy, and it rewrites an audit ---
    root = tempfile.mkdtemp(prefix="audit-bindings-selftest-audit-")
    try:
        _write(root, "src/thing.fs", "let a = 1\n")
        _empty_ledger(root)
        _make_audit(root, "cycle-a", "feedback/cycle-a.md", ["src/thing.fs"])
        # A finding ABOUT cycle-a's audit cites that audit.
        _make_audit(root, "cycle-b", "feedback/cycle-b.md", ["feedback/audits/cycle-a.audit.json"])
        check("the seeded audit-cites-audit tree starts green", evaluate(root)["ok"])

        # Remedy (1), rebinding, applied to cycle-a -- which rewrites the very
        # bytes cycle-b binds.
        _write(root, "src/thing.fs", "let a = 2\n")
        _make_audit(root, "cycle-a", "feedback/cycle-a.md", ["src/thing.fs"])
        check("rebinding an audit does not strand the audit that cites it", evaluate(root)["ok"])
        check(
            "the audit-to-audit citation is reported as not bound",
            any(
                entry["locator"] == "file:feedback/audits/cycle-a.audit.json"
                and entry["reason"] == AUDIT_EXEMPTION
                for entry in evaluate(root).get("notBound", [])
            ),
        )

        # Mutual citation. Asserted WITHOUT --grandfather on purpose: excusing
        # would make this pass even unfixed, because writing the ledger does not
        # rewrite an audit. The claim that distinguishes the fix is that mutual
        # citation needs no excuse at all -- rebinding either audit must leave
        # the tree green, where before it stranded the other one.
        _make_audit(root, "cycle-c", "feedback/cycle-c.md", ["feedback/audits/cycle-b.audit.json"])
        _make_audit(root, "cycle-b", "feedback/cycle-b.md", ["feedback/audits/cycle-c.audit.json"])
        _make_audit(root, "cycle-c", "feedback/cycle-c.md", ["feedback/audits/cycle-b.audit.json"])
        check("two audits citing each other need no excuse at all", evaluate(root)["ok"])

        # The report binding is exempted on the same rule as evidence.
        _write(
            root,
            "feedback/audits/cycle-report.audit.json",
            json.dumps(
                {
                    "auditSchema": 1,
                    "report": "feedback/audits/cycle-a.audit.json",
                    "reportSha256": "0" * 64,
                    "findings": [],
                },
                indent=2,
            )
            + "\n",
        )
        r = evaluate(root)
        check(
            "an audit whose REPORT is an audit is exempt, not a violation",
            r["ok"]
            and any(
                entry["kind"] == "report" and entry["reason"] == AUDIT_EXEMPTION
                for entry in r.get("notBound", [])
            ),
        )
        os.remove(os.path.join(root, "feedback", "audits", "cycle-report.audit.json"))

        # The exemption must be NARROW: only *.audit.json under feedback/audits/.
        # A wholesale directory exclusion would quietly stop checking anything
        # else filed there.
        _write(root, "feedback/audits/notes.md", "notes\n")
        _make_audit(root, "cycle-d", "feedback/cycle-d.md", ["feedback/audits/notes.md"])
        grandfather(root, "selftest: before the narrow-exemption probe")
        _write(root, "feedback/audits/notes.md", "notes edited\n")
        r = evaluate(root)
        check(
            "a non-audit file under feedback/audits/ is still bound",
            not r["ok"]
            and any(v["locator"] == "file:feedback/audits/notes.md" for v in r["violations"]),
        )

        # And an ordinary file is untouched by any of this.
        _write(root, "src/thing.fs", "let a = 3\n")
        r = evaluate(root)
        check(
            "an ordinary bound file still fails when it changes",
            any(v["locator"] == "file:src/thing.fs" for v in r["violations"]),
        )
        grandfather(root, "selftest: audit convergence, final")
        check("--grandfather still settles the ordinary violations in one pass", evaluate(root)["ok"])

        # The invariant that was FALSE before the fix, and the reason the dead
        # end was so hard to read from the tool: --grandfather kept exiting 0
        # while `check` stayed red, so the remedy reported success four times
        # over without ever reaching green (rogue3#38).
        script = os.path.abspath(__file__)

        def cli(*argv: str) -> int:
            return subprocess.run(
                [sys.executable, script, "--root", root, *argv],
                capture_output=True,
                text=True,
            ).returncode

        _make_audit(root, "cycle-ledger", "feedback/cycle-ledger.md", [LEDGER_RELPATH])
        _write(root, "src/thing.fs", "let a = 4\n")
        settled = cli("--grandfather", "--reason", "selftest: exit-code invariant")
        check("--grandfather exiting 0 means check exits 0", settled == 0 and cli() == 0)
    finally:
        shutil.rmtree(root, ignore_errors=True)


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

        # 10. the digest rule matches FeedbackReportTool.sha256Text
        #     Golden vector for "a\nb\n", produced by the F# tool itself from
        #     CRLF input:
        #       printf 'a\r\nb\r\n' > f
        #       dotnet fsi .agents/skills/fs-gg-feedback-report/scripts/\
        #         feedback-tool.fsx -- digest f
        #     Anchoring to that constant, rather than to digest_text itself, is
        #     what makes this a cross-tool claim instead of a tautology.
        golden = "911169ddaaf146aff539f58c26c489af3b892dff0fe283c1c264c65ae5aa59a2"
        check("the digest rule matches a golden vector", digest_text(b"a\nb\n") == golden)
        crlf = os.path.join(root, "src", "crlf.fs")
        os.makedirs(os.path.dirname(crlf), exist_ok=True)
        with open(crlf, "wb") as handle:
            handle.write(b"a\r\nb\r\n")
        check("CRLF digests identically to LF", digest_file(crlf) == golden)
        with open(crlf, "wb") as handle:
            handle.write(b"a\rb\r")
        check("bare CR digests identically to LF", digest_file(crlf) == golden)
        with open(crlf, "wb") as handle:
            handle.write(b"\xef\xbb\xbfa\nb\n")
        check("a UTF-8 BOM is stripped, as File.ReadAllText strips it", digest_file(crlf) == golden)
        with open(crlf, "wb") as handle:
            handle.write(b"\x89PNG\r\n\x1a\n\xff\xfe binary")
        undecodable = None
        try:
            undecodable = digest_file(crlf)
        except UnicodeDecodeError:
            undecodable = None
        check(
            "a non-UTF-8 bound file digests instead of crashing",
            isinstance(undecodable, str) and len(undecodable) == 64,
        )
        os.remove(crlf)

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

        # 14. a malformed audit must FAIL, never bind nothing and pass
        def mangle(mutate) -> dict[str, Any]:
            """Write cycle-4 correctly, then break it, and evaluate."""
            _write(root, "src/mangle.fs", "let d = 1\n")
            _make_audit(root, "cycle-4", "feedback/cycle-4.md", ["src/mangle.fs"])
            path = os.path.join(root, "feedback", "audits", "cycle-4.audit.json")
            with open(path, encoding="utf-8") as handle:
                doc = json.load(handle)
            mutate(doc)
            _write(root, "feedback/audits/cycle-4.audit.json", json.dumps(doc, indent=2) + "\n")
            return evaluate(root)

        baseline_ok = evaluate(root)["ok"]

        def structural(mutate) -> bool:
            r = mangle(mutate)
            return not r["ok"] and any(v["kind"] == "structure" for v in r["violations"])

        check("a correctly formed extra audit keeps the tree green", baseline_ok and mangle(lambda d: d)["ok"])
        check(
            "a finding with checkedEvidence REMOVED fails",
            structural(lambda d: d["findings"][0].pop("checkedEvidence")),
        )
        check(
            "a finding with checkedEvidence RENAMED fails",
            structural(lambda d: d["findings"][0].update(
                {"checked_evidence": d["findings"][0].pop("checkedEvidence")})),
        )
        check(
            "checkedEvidence that is not an array fails",
            structural(lambda d: d["findings"][0].update({"checkedEvidence": {}})),
        )
        check("findings that is not an array fails", structural(lambda d: d.update({"findings": {}})))
        check("a missing findings key fails", structural(lambda d: d.pop("findings")))
        check("a missing report key fails", structural(lambda d: d.pop("report")))
        check(
            "an absolute file locator fails",
            structural(lambda d: d["findings"][0]["checkedEvidence"][0].update(
                {"locator": "file:/etc/passwd"})),
        )
        check(
            "a non-string locator fails",
            structural(lambda d: d["findings"][0]["checkedEvidence"][0].update({"locator": 7})),
        )
        # An escaping locator must never be silently skipped: that would let a
        # one-token edit hide a stale binding behind exit 0.
        outside = os.path.join(os.path.dirname(root), "audit-bindings-outside.txt")
        with open(outside, "w", encoding="utf-8", newline="\n") as handle:
            handle.write("outside the workspace\n")
        try:
            check(
                "a file: locator escaping the workspace fails",
                structural(lambda d: d["findings"][0]["checkedEvidence"][0].update(
                    {"locator": "file:../audit-bindings-outside.txt"})),
            )
            check(
                "an escaping report path fails",
                structural(lambda d: d.update({"report": "../audit-bindings-outside.txt"})),
            )
        finally:
            os.remove(outside)
        check(
            "an empty file: locator fails",
            structural(lambda d: d["findings"][0]["checkedEvidence"][0].update({"locator": "file:"})),
        )
        r = mangle(lambda d: d["findings"][0]["checkedEvidence"][0].pop("sha256"))
        check(
            "a file locator pinning no sha256 fails and cannot be excused",
            not r["ok"]
            and any("pins no sha256" in v["reason"] for v in r["violations"])
            and grandfather(root, "selftest: unpinned")["notExcusable"] != [],
        )
        check("an audit that is not valid JSON fails",
              (lambda: (_write(root, "feedback/audits/cycle-4.audit.json", "{not json\n"),
                        evaluate(root))[1])()["ok"] is False)
        check(
            "--grandfather refuses to bless a malformed audit",
            grandfather(root, "selftest: must not excuse structure")["notExcusable"] != []
            and not evaluate(root)["ok"],
        )
        os.remove(os.path.join(root, "feedback", "audits", "cycle-4.audit.json"))
        grandfather(root, "selftest: prune cycle-4")
        check("removing the malformed audit restores green", evaluate(root)["ok"])

        # 15. a hand-written exception missing a reason is rejected
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
        def ledger_rejected() -> bool:
            try:
                evaluate(root)
            except UsageError:
                return True
            return False

        check("an exception without a reason is rejected", ledger_rejected())
        # The previous fixture omitted boundSha256 too, so it passed even if
        # only that field were required. Each required field gets its own case.
        base_entry = {
            "audit": "feedback/audits/cycle-1.audit.json",
            "kind": "evidence",
            "locator": "file:src/thing.fs",
            "boundSha256": "0" * 64,
            "observedSha256": "1" * 64,
            "reason": "selftest",
        }
        for field in ("audit", "kind", "locator", "boundSha256", "observedSha256", "reason"):
            entry = {k: v for k, v in base_entry.items() if k != field}
            _write(
                root,
                LEDGER_RELPATH,
                json.dumps({"grandfatherSchema": 1, "entries": [entry]}, indent=2) + "\n",
            )
            check(f"a ledger entry missing '{field}' is rejected", ledger_rejected())

        # 16. the CLI surface: documented exit codes actually happen
        script = os.path.abspath(__file__)

        def cli(*argv: str) -> int:
            return subprocess.run(
                [sys.executable, script, "--root", root, *argv],
                capture_output=True,
                text=True,
            ).returncode

        check("a malformed ledger exits 2, not 1", cli() == 2)
        os.remove(ledger_path(root))
        check("--grandfather without --reason exits 2", cli("--grandfather") == 2)
        check("--reason without --grandfather exits 2", cli("--reason", "x") == 2)
        check("a nonexistent --root exits 2", _bad_root(script) == 2)
        check("--grandfather via the CLI exits 0", cli("--grandfather", "--reason", "selftest: cli") == 0)
        check("a clean tree exits 0", cli() == 0)
        _write(root, "src/thing.fs", "let a = 99\n")
        check("a stale binding exits 1", cli() == 1)
        check("--json still exits 1 on violations", cli("--json") == 1)

        # 17. the checker's own remedy surfaces cannot be bound (rogue3#38)
        selftest_exemptions(check)
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

    if args.reason and not args.grandfather:
        print("audit-bindings: --reason has no effect without --grandfather", file=sys.stderr)
        return 2

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
            for entry in outcome["notExcusable"]:
                print(
                    f"audit-bindings: NOT excused (repair the audit): {entry['audit']} "
                    f"{entry['locator']} -- {entry['reason']}",
                    file=sys.stderr,
                )
        # A malformed audit is not something the ledger can absorb, so the
        # rewrite does NOT report success while such a violation stands.
        return 1 if outcome["notExcusable"] else 0

    result = evaluate(root)
    if args.json:
        print(json.dumps(result, indent=2))
    else:
        report_text(result, sys.stdout)
    return 0 if result["ok"] else 1


def run(argv: list[str]) -> int:
    """main() with the input-error class mapped to its documented exit code."""
    try:
        return main(argv)
    except UsageError as error:
        print(f"audit-bindings: {error}", file=sys.stderr)
        return 2


if __name__ == "__main__":
    try:
        sys.exit(run(sys.argv[1:]))
    except SystemExit:
        raise
    except OSError as error:  # pragma: no cover - defensive
        print(f"audit-bindings: {error}", file=sys.stderr)
        sys.exit(2)
