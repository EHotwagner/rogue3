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

  1. Excuse it explicitly:
       python3 scripts/check-audit-bindings.py --grandfather \
           --cycle <cycle-id> --reason "<why>"
     which rewrites YOUR CYCLE'S ledger file from the current violations and
     prunes entries in it that excuse nothing.  The diff is the record of what
     was excused and why.
  2. Rebind the audit, so it pins the bytes that now exist -- ONLY when the
     audit is this cycle's own and you re-verified it yourself.  Rebinding a
     MERGED audit rewrites what that audit records its critic as having
     verified, which is the practice commit 7e71d71 established the precedent
     against and which rogue3#38 exists to eliminate.  NOTE: feedback-tool.fsx
     has no `rebind` subcommand -- it offers `digest <file>` for one hash at a
     time, so this means recomputing and pasting each `sha256` by hand.

Excusing is preferred and is also the only remedy with tooling, so expect (1) to
be the common path.  The ledger's growth is a known weakness, not the intended
design.

ONE LEDGER FILE PER CYCLE
-------------------------

The ledger is a DIRECTORY, `scripts/audit-binding-exceptions/`, holding one
`<cycle-id>.json` per cycle.  The checker reads every file in it and evaluates
their union; `--grandfather --cycle <id>` writes exactly ONE of them.

It was a single shared file until rogue3#53.  Because remedy (1) is the only
non-destructive remedy, and because the heavily-cited files are exactly the ones
work lands in, every concurrent worker that touched a bound file was funnelled
onto that one path -- while no board item declared it, so nothing could sequence
them.  Three workers in one bounded fan-out collided on it and negotiated an
append order by hand.  Per-cycle files remove the shared path from the routine
remedy: two cycles editing the same bound file now write two different files.

That alone is not enough, because the union still has to converge no matter
which order the two land in.  So an entry is matched by its binding key AND the
digest it observed, and a stale binding is excused when ANY entry in the union
observes the digest the file actually has:

  * excused    -- some entry for this binding observes the current digest.
  * SUPERSEDED -- an entry for a binding another entry excuses, pinned at a
                  digest the file no longer has.  Reported, never fatal.  This
                  is what makes the merge of two cycles order-independent: the
                  loser's excuse becomes a record of what it excused, not a
                  failure.  Its reason text is still the reviewable line saying
                  somebody excused that binding and why.
  * OBSOLETE   -- an entry whose binding is not stale at all (the file went back
                  to the bytes the audit pins, or the audit was rebound or
                  removed).  Still fatal, still pruned by `--grandfather`, so
                  the ledger cannot rot the way the digests did.

Supersession does NOT weaken "an exception excuses one observed digest".  Edit
an excused file again and no entry observes the new digest, so it fails again --
exactly as before.  What changed is only that a superseded entry is no longer
mistaken for rot.

The single file the ledger used to be, `scripts/audit-binding-exceptions.json`,
is a FROZEN ARCHIVE: still read, still honoured, never written by the remedy.
Its entries are the record of what earlier cycles excused and why, and four
merged audits cite the path, so deleting it would both discard that record and
break evidence this repository already accepted.  The one thing that can still
write it is a prune of an entry that excuses nothing -- see OBSOLETE above --
and `--grandfather` names every foreign file it prunes.

NOT BOUND: the excuse ledger
----------------------------

Remedy (1) WRITES the exceptions ledger.  So a binding whose target is the
ledger itself has no fixed point, and the gate was asking for the impossible
(rogue3#38): an audit that cites a ledger file -- reasonable when the finding is
*about* the ledger -- goes stale the moment anything is excused.  Excusing that
binding writes the ledger, which changes the ledger, which invalidates the
excuse just written.  Reproduced at d8d0024: four consecutive `--grandfather`
runs produced four distinct ledger digests and `check` still exited 1.

The exemption therefore covers the whole ledger directory, not one file: with
per-cycle files, `scripts/audit-binding-exceptions/<id>.json` is now the file an
excuse lands in, so exempting only the legacy path would reintroduce rogue3#38
under a new name on the first cycle whose audit cites its own excuse.  The
legacy path stays exempt because merged audits still cite it.

So a citation onto any ledger file is NOT checked.  It is not silently dropped:
`evaluate` returns it under `notBound`, the text report names it, `--json`
carries it and counts it, and the summary and verdict lines both say how many
citations were not bound -- so a reader can always tell the difference between
"this citation is exempt" and "the checker missed it".

Why an audit citing ANOTHER audit is NOT exempt
-----------------------------------------------

It looks like the same shape and it is not.  The gate's rule is "fresh OR
excused", and excusing writes only the ledger -- never an audit.  So once the
ledger is exempt, a stale binding onto another `*.audit.json` settles through
remedy (1) in a single pass, and stays settled.  Verified over this tree by
rebinding a merged audit that another audit cites -- the exact case that
stranded feedback/audits/2026-08-02-Rogue3-6.audit.json in M13 -- then running
`--grandfather` three times: green after the first pass, one ledger digest
across all three.  Mutual citation A<->B settles the same way.

Exempting audits as well would therefore buy nothing about convergence, and it
would cost the only check in the repository that notices an edit to a merged
audit -- the very event rogue3#38's history is a record of.  It is left checked
deliberately.  The cost is one ledger entry per audit rebind, which is the point:
that entry is a diffable, reasoned line saying somebody edited merged evidence
and why.

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
import re
import shutil
import subprocess
import sys
import tempfile
from typing import Any

AUDIT_GLOB_DIR = "feedback/audits"
AUDIT_SUFFIX = ".audit.json"

# The ledger directory: one `<cycle-id>.json` per cycle, all read, exactly one
# written by any single `--grandfather` run. rogue3#53.
LEDGER_DIR = "scripts/audit-binding-exceptions"
LEDGER_SUFFIX = ".json"

# The single shared file the ledger was until rogue3#53, now a frozen archive:
# still READ so earlier cycles' excuses keep working, still EXEMPT because
# merged audits cite it, never written by the remedy.
LEGACY_LEDGER_RELPATH = "scripts/audit-binding-exceptions.json"

LEDGER_SCHEMA = 2
MISSING = "<missing>"

# A cycle id becomes a FILENAME, so it is validated rather than trusted: no
# separators, no traversal, no leading dot, no surprises across filesystems.
# Matches the board's `item-<number>-<slug>` convention without requiring it.
CYCLE_ID_PATTERN = re.compile(r"^[a-z0-9][a-z0-9._-]{0,99}$")

LEDGER_NOTE = (
    "Explicit exceptions to the audit-binding check "
    "(scripts/check-audit-bindings.py) written by ONE cycle. Each entry excuses "
    "ONE stale binding at ONE observed digest: change the file again and it "
    "fails again. The checker evaluates the UNION of every file in this "
    "directory, so two concurrent cycles excuse the same binding without "
    "sharing a path; whichever digest the file ends up with wins and the other "
    "entry is reported as superseded, not as rot. Adding an entry here is the "
    "PREFERRED remedy; rebinding a MERGED audit rewrites what that audit "
    "records its critic as having verified (see 7e71d71) and is right only for "
    "an audit you re-verified yourself."
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


# The paths this checker must write in order to clear a violation. Binding one is
# a fixed-point equation with no fixed point -- see the module docstring and
# rogue3#38.
LEDGER_EXEMPTION = (
    "this is the exceptions ledger itself: the only place an excuse can live, so "
    "excusing a binding on it rewrites it and invalidates the excuse just written"
)


def exemption(rel: str) -> str | None:
    """Why `rel` cannot be bound, or None when it is an ordinary file.

    Takes the WORKSPACE-RELATIVE path derived from the RESOLVED location, not
    the locator text, so `file:feedback/../scripts/audit-binding-exceptions.json`
    is recognised as the ledger too. A textual match on the locator would let a
    one-token rewrite reintroduce the unsatisfiable binding this exempts. The
    resolved path is also why the directory test cannot be escaped: a locator
    like `scripts/audit-binding-exceptions/../../secret.json` resolves outside
    the directory and is bound normally.

    Deliberately the ledger and NOTHING else. The directory prefix is not a
    widening -- it is the same one exemption, following the ledger from one file
    to one file per cycle (rogue3#53). In particular a citation onto another
    `*.audit.json` is NOT exempt: see the module docstring for why the obvious
    second exemption is wrong. Neither is a same-suffix neighbour of the
    directory: `scripts/audit-binding-exceptions.json.bak` and
    `scripts/audit-binding-exceptionsX.json` are ordinary files, which is why
    this compares against `LEDGER_DIR + "/"` rather than a bare `startswith`.
    """
    if rel == LEGACY_LEDGER_RELPATH:
        return LEDGER_EXEMPTION
    if rel.startswith(LEDGER_DIR + "/") and rel.endswith(LEDGER_SUFFIX):
        return LEDGER_EXEMPTION
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

    # `resolve_inside` returns a REALPATH. Relativising it against a `root` that
    # still contains a symlinked component yields `../real/...`, which matches no
    # exemption -- so the whole exemption silently switches off and rogue3#38
    # comes back. Both sides must be realpath'd. `_rel(root, audit_abs)` below is
    # unaffected: its argument is built from `root` and never realpath'd.
    root_real = os.path.realpath(root)

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
                why = exemption(_rel(root_real, resolved))
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
                why = exemption(_rel(root_real, resolved))
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


ENTRY_FIELDS = ("audit", "kind", "locator", "boundSha256", "observedSha256", "reason")


def cycle_ledger_relpath(cycle: str) -> str:
    return f"{LEDGER_DIR}/{cycle}{LEDGER_SUFFIX}"


def ledger_path(root: str, relpath: str) -> str:
    return os.path.join(root, *relpath.split("/"))


def validate_cycle(cycle: str) -> str:
    """The cycle id, or a UsageError naming why it cannot become a filename.

    A cycle id is turned straight into a path, so an unvalidated one is a path
    traversal: `--cycle ../../etc/x` would have `--grandfather` write outside
    the ledger directory -- and outside the workspace -- while reporting that it
    wrote an exception.
    """
    cleaned = (cycle or "").strip()
    if not CYCLE_ID_PATTERN.match(cleaned):
        raise UsageError(
            f"--cycle {cycle!r} is not a usable ledger filename: use lowercase letters, digits, "
            "'.', '-' and '_' only, starting with a letter or digit (the board's convention is "
            "item-<issue-number>-<slug>)"
        )
    return cleaned


def _load_ledger_file(root: str, relpath: str) -> list[dict[str, str]]:
    """Every entry in one ledger file, each tagged with the file it came from."""
    path = ledger_path(root, relpath)
    with open(path, "rb") as handle:
        try:
            doc = json.loads(handle.read().decode("utf-8-sig"))
        except (ValueError, UnicodeDecodeError) as exc:
            raise UsageError(f"{relpath}: not readable as JSON: {exc}")
    if not isinstance(doc, dict):
        raise UsageError(f"{relpath}: root must be a JSON object")

    # A `cycle` that disagrees with the filename would make the ledger's own
    # provenance a lie -- and `--grandfather --cycle X` writes by filename, so
    # the disagreement would never be repaired by using the tool.
    declared = doc.get("cycle")
    if declared is not None:
        expected = os.path.basename(relpath)[: -len(LEDGER_SUFFIX)]
        if not isinstance(declared, str) or declared.strip() != expected:
            raise UsageError(
                f"{relpath}: 'cycle' is {declared!r} but the filename says {expected!r}"
            )

    entries = doc.get("entries")
    if entries is None:
        entries = []
    if not isinstance(entries, list):
        raise UsageError(f"{relpath}: 'entries' must be a list")

    out: list[dict[str, str]] = []
    seen: set[tuple[str, str, str, str]] = set()
    for index, entry in enumerate(entries):
        if not isinstance(entry, dict):
            raise UsageError(f"{relpath}: entry {index} must be an object")
        missing = [field for field in ENTRY_FIELDS if not entry.get(field)]
        if missing:
            raise UsageError(
                f"{relpath}: entry {index} is missing required field(s): {', '.join(missing)}"
            )
        normalized = {field: str(entry[field]) for field in ENTRY_FIELDS}
        key = entry_key(normalized)
        # Duplicates WITHIN one file are still an error: one cycle excusing the
        # same binding at the same digest twice is a broken write, not the
        # concurrency the union is there to absorb.
        if key in seen:
            raise UsageError(f"{relpath}: duplicate entry for {key[0]} {key[2]}")
        seen.add(key)
        normalized["sourceFile"] = relpath
        out.append(normalized)
    return out


def ledger_files(root: str) -> list[str]:
    """Every ledger file, legacy first, then the per-cycle files in path order.

    Walks rather than lists: a ledger file filed in a subdirectory must not be
    silently ignored, or an excuse would be written and never applied. The walk
    matches `exemption`, which exempts `*.json` at any depth under the
    directory, so no path is exempt from binding yet invisible as an exception.
    """
    found: list[str] = []
    if os.path.isfile(ledger_path(root, LEGACY_LEDGER_RELPATH)):
        found.append(LEGACY_LEDGER_RELPATH)
    directory = ledger_path(root, LEDGER_DIR)
    if os.path.isdir(directory):
        nested: list[str] = []
        for current, _dirs, names in os.walk(directory):
            for name in names:
                if name.endswith(LEDGER_SUFFIX):
                    nested.append(_rel(root, os.path.join(current, name)))
        found.extend(sorted(nested))
    return found


def entry_key(entry: dict[str, str]) -> tuple[str, str, str, str]:
    return (entry["audit"], entry["kind"], entry["locator"], entry["boundSha256"])


def load_ledger(root: str) -> list[dict[str, str]]:
    """The union of every ledger file, in stable order.

    A LIST, not a dict keyed by binding: two cycles legitimately hold entries
    for the same binding at different observed digests, and collapsing them
    would make the verdict depend on which cycle's file was read last -- that is
    the merge order-dependence this layout exists to remove.
    """
    entries: list[dict[str, str]] = []
    for relpath in ledger_files(root):
        entries.extend(_load_ledger_file(root, relpath))
    return entries


def group_by_key(entries: list[dict[str, str]]) -> dict[tuple[str, str, str, str], list[dict[str, str]]]:
    grouped: dict[tuple[str, str, str, str], list[dict[str, str]]] = {}
    for entry in entries:
        grouped.setdefault(entry_key(entry), []).append(entry)
    return grouped


def _entry_sort_key(entry: dict[str, str]) -> tuple[str, str, str, str, str]:
    return entry_key(entry) + (entry.get("observedSha256", ""),)


def read_ledger_note(root: str, relpath: str) -> str | None:
    """The `note` a ledger file already carries, or None when it has none."""
    path = ledger_path(root, relpath)
    if not os.path.isfile(path):
        return None
    with open(path, "rb") as handle:
        try:
            doc = json.loads(handle.read().decode("utf-8-sig"))
        except (ValueError, UnicodeDecodeError):
            return None
    if isinstance(doc, dict) and isinstance(doc.get("note"), str):
        return doc["note"]
    return None


def write_ledger(
    root: str,
    relpath: str,
    entries: list[dict[str, str]],
    cycle: str | None,
    note: str | None = None,
) -> None:
    ordered = [
        {field: entry[field] for field in ENTRY_FIELDS}
        for entry in sorted(entries, key=_entry_sort_key)
    ]
    doc: dict[str, Any] = {"grandfatherSchema": LEDGER_SCHEMA}
    if cycle is not None:
        doc["cycle"] = cycle
    # A file this run did not author keeps the note it already carried. Pruning
    # a dead entry out of the frozen archive must not also replace the paragraph
    # explaining that it IS the frozen archive.
    doc["note"] = LEDGER_NOTE if note is None else note
    doc["entries"] = ordered
    path = ledger_path(root, relpath)
    os.makedirs(os.path.dirname(path), exist_ok=True)
    # Temp + rename: an interrupted rewrite must not truncate a ledger file,
    # which is the only record of what that cycle excused.
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
    entries = load_ledger(root)
    grouped = group_by_key(entries)

    fresh: list[Binding] = []
    excused: list[dict[str, str]] = []
    superseded: list[dict[str, str]] = []
    violations: list[dict[str, Any]] = list(malformed)
    used_keys: set[tuple[str, str, str, str]] = set()

    for binding in bindings:
        if binding.bound is None:
            violations.append(_violation(binding, "audit pins no sha256 for this file locator"))
            continue
        if binding.fresh:
            fresh.append(binding)
            continue

        candidates = grouped.get(binding.key)
        if candidates:
            # The binding is live and stale, so every entry for it is doing its
            # job or has been overtaken -- neither is rot, and neither is
            # obsolete.
            used_keys.add(binding.key)
            matching = [e for e in candidates if e["observedSha256"] == binding.observed]
            stale_entries = [e for e in candidates if e["observedSha256"] != binding.observed]
            if matching:
                # Excused by whichever cycle observed the digest the file
                # actually has. The rest are superseded: a record of what
                # another cycle excused, not a failure -- see the module
                # docstring.
                excused.extend(dict(entry) for entry in matching)
                superseded.extend(dict(entry) for entry in stale_entries)
                continue
            # No entry observes the current bytes, so an excuse still pins ONE
            # digest and a further edit fails again, exactly as before.
            superseded.extend(dict(entry) for entry in stale_entries)
            observed_before = ", ".join(sorted({_short(e["observedSha256"]) for e in candidates}))
            violations.append(
                _violation(
                    binding,
                    "file changed again since it was excused "
                    f"(ledger excused {observed_before}, file is now "
                    f"{_short(binding.observed)})",
                )
            )
            continue

        if binding.actual is None:
            violations.append(_violation(binding, "bound file does not exist"))
        else:
            violations.append(_violation(binding, "file changed since the audit bound it"))

    obsolete = sorted(
        (dict(entry) for entry in entries if entry_key(entry) not in used_keys),
        key=_entry_sort_key,
    )

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
        # An entry for a live stale binding pinned at a digest the file no
        # longer has. Never a violation: this is how the union of two cycles'
        # ledgers converges regardless of merge order (rogue3#53). Counted and
        # listed so it is visible rather than an invisible tolerance.
        "superseded": len(superseded),
        "supersededEntries": sorted(superseded, key=_entry_sort_key),
        "violations": violations,
        "obsoleteExceptions": obsolete,
        # Which files the union was read from, so a reader can tell an empty
        # ledger from an unread one.
        "ledgerFiles": ledger_files(root),
        # Citations onto the excuse ledger: reported, not checked, never a
        # violation. Present so exemption is auditable rather than an invisible
        # hole -- see the module docstring and rogue3#38. The scalar sits beside
        # the other scalars so a dashboard built from them cannot miss it.
        "notBound": len(exempt),
        "notBoundCitations": exempt,
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


def grandfather(root: str, cycle: str, reason: str) -> dict[str, Any]:
    """Rewrite ONE cycle's ledger file from the current violations.

    The routine remedy writes exactly one path, `scripts/audit-binding-
    exceptions/<cycle>.json`, which is what stops two concurrent cycles from
    colliding on the excuse ledger (rogue3#53). Another cycle's entries are read
    and honoured but never copied into this file and never re-worded: their
    reason text is that cycle's record, not this one's to rewrite.

    The ONE exception is pruning: an OBSOLETE entry excuses nothing anywhere and
    is a hard failure wherever it lives, so leaving foreign obsolete entries in
    place would be a red gate with no single-command route to green -- the
    rogue3#38 shape. Those are deleted, and the files they were deleted from are
    named in the result so the cross-cycle write is never silent.
    """
    cycle = validate_cycle(cycle)
    own_relpath = cycle_ledger_relpath(cycle)
    previous = {
        entry_key(entry): entry
        for entry in load_ledger(root)
        if entry["sourceFile"] == own_relpath
    }
    result = evaluate(root)

    # Entries that still excuse a live violation are carried forward verbatim;
    # only newly stale bindings pick up the new --reason. Anything in THIS file
    # that excuses nothing -- obsolete or superseded -- is simply not
    # re-emitted, which is the prune and which is what makes a second
    # --grandfather run after a further edit cost one command, not a growing
    # pile of dead entries.
    entries: list[dict[str, str]] = [
        dict(entry)
        for entry in result["excusedEntries"]
        if entry["sourceFile"] == own_relpath
    ]
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
                "sourceFile": own_relpath,
            }
        )
    write_ledger(root, own_relpath, entries, cycle)

    foreign_pruned = _prune_obsolete_elsewhere(root, own_relpath, result["obsoleteExceptions"])

    kept = {entry_key(entry) for entry in entries}
    after = evaluate(root)
    return {
        "written": own_relpath,
        "cycle": cycle,
        "entries": len(entries),
        "carriedForward": sum(
            1
            for entry in entries
            if _without_source(previous.get(entry_key(entry))) == _without_source(entry)
        ),
        "pruned": len([key for key in previous if key not in kept]),
        "prunedElsewhere": foreign_pruned,
        "notExcusable": unexcusable,
        # rogue3#38's second lesson, and feedback/2026-08-02-Rogue3-9.md §11.3:
        # the remedy must report the VERDICT, not the write. Exiting 0 while the
        # gate stayed red is how the dead end went unread for four passes.
        "checkOk": after["ok"],
    }


def _without_source(entry: dict[str, str] | None) -> dict[str, str] | None:
    if entry is None:
        return None
    return {field: entry[field] for field in ENTRY_FIELDS}


def _prune_obsolete_elsewhere(
    root: str, own_relpath: str, obsolete: list[dict[str, str]]
) -> list[dict[str, Any]]:
    """Delete obsolete entries from ledger files this cycle does not own.

    A file left with no entries is removed rather than kept as an empty husk, so
    a finished cycle that excused nothing leaves no path behind.
    """
    by_file: dict[str, set[tuple[str, str, str, str, str]]] = {}
    for entry in obsolete:
        source = entry["sourceFile"]
        if source == own_relpath:
            continue  # already dropped by the rewrite above
        by_file.setdefault(source, set()).add(_entry_sort_key(entry))

    pruned: list[dict[str, Any]] = []
    for relpath in sorted(by_file):
        remaining = [
            entry
            for entry in _load_ledger_file(root, relpath)
            if _entry_sort_key(entry) not in by_file[relpath]
        ]
        removed = len(by_file[relpath])
        if remaining:
            declared = os.path.basename(relpath)[: -len(LEDGER_SUFFIX)]
            write_ledger(
                root,
                relpath,
                remaining,
                declared if relpath != LEGACY_LEDGER_RELPATH else None,
                note=read_ledger_note(root, relpath),
            )
        else:
            os.remove(ledger_path(root, relpath))
        pruned.append({"file": relpath, "removed": removed, "deleted": not remaining})
    return pruned


# --------------------------------------------------------------------------
# reporting
# --------------------------------------------------------------------------


def report_text(result: dict[str, Any], stream) -> None:
    malformed = [v for v in result["violations"] if v["kind"] == "structure"]
    violations = [v for v in result["violations"] if v["kind"] != "structure"]
    obsolete = result["obsoleteExceptions"]
    superseded = result.get("supersededEntries", [])

    not_bound = result.get("notBoundCitations", [])

    # The not-bound count belongs on the summary line: without it, four
    # citations simply vanish between `main` and a branch and a reader diffing
    # CI logs sees the binding count drop with no accounting. The superseded
    # count is on it for the same reason -- it is a tolerance, and a tolerance
    # nobody can see is indistinguishable from a hole.
    print(
        "audit-bindings: {audits} audits, {bindings} bindings, {fresh} fresh, "
        "{excused} explicitly excused, {superseded} superseded, "
        "{notBoundCount} not bound, over {ledgerFileCount} ledger file(s)".format(
            notBoundCount=len(not_bound),
            ledgerFileCount=len(result.get("ledgerFiles", [])),
            **result,
        ),
        file=stream,
    )

    if not_bound:
        print(
            f"\naudit-bindings: {len(not_bound)} citation(s) NOT BOUND -- a citation onto "
            "the excuse\nledger can never be satisfied, so it is reported rather than checked:",
            file=stream,
        )
        # Reason as a group HEADER above its entries. Printing it once below the
        # group, indented deeper than the entries, reads as though it belonged
        # to the last line only -- and every other block here prints its reason
        # attached to the entry it explains.
        by_reason: dict[str, list[dict[str, Any]]] = {}
        for entry in not_bound:
            by_reason.setdefault(entry["reason"], []).append(entry)
        for reason in sorted(by_reason):
            print(f"\n  {reason}:", file=stream)
            for entry in by_reason[reason]:
                print(f"    {entry['audit']}  {entry['locator']}", file=stream)

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

    if superseded:
        print(
            f"\naudit-bindings: {len(superseded)} SUPERSEDED EXCEPTION(S) -- another cycle's "
            "excuse for a\nbinding that is excused at the digest the file now has. Reported, "
            "not a failure:",
            file=stream,
        )
        for entry in superseded:
            print(
                f"    {entry['sourceFile']}  {entry['audit']}  {entry['locator']}"
                f"  (observed {_short(entry['observedSha256'])})",
                file=stream,
            )

    if obsolete:
        print(
            f"\naudit-bindings: {len(obsolete)} OBSOLETE EXCEPTION(S) under {LEDGER_DIR}/ "
            "-- these no longer excuse anything and must be pruned:",
            file=stream,
        )
        for entry in obsolete:
            print(
                f"    {entry['sourceFile']}  {entry['audit']}  {entry['locator']}",
                file=stream,
            )

    if violations or obsolete:
        print(
            "\naudit-bindings: fix by REBINDING the audit -- recompute each stale sha256 so it\n"
            "pins the bytes that now exist (feedback-tool.fsx has no rebind subcommand; use\n"
            "`-- digest <file>` per file) -- or by excusing each one EXPLICITLY:\n"
            "    python3 scripts/check-audit-bindings.py --grandfather \\\n"
            '        --cycle <cycle-id> --reason "<why>"\n'
            f"then commit {LEDGER_DIR}/<cycle-id>.json. That file is yours: a concurrent cycle\n"
            "writes its own and the two never conflict. An exception is pinned to one observed\n"
            "digest, so the next change to the same file fails again -- run this LAST.",
            file=stream,
        )
    elif not malformed:
        # The verdict must carry the exempt count. "every audit binding is fresh
        # or explicitly excused" is true only because the exempt citations
        # stopped counting as bindings, and this is the line a skimmer reads.
        if not_bound:
            print(
                "audit-bindings: OK -- every CHECKED binding is fresh or explicitly excused; "
                f"{len(not_bound)} citation(s) were not checked (listed above).",
                file=stream,
            )
        else:
            print(
                "audit-bindings: OK -- every audit binding is fresh or explicitly excused.",
                file=stream,
            )


# --------------------------------------------------------------------------
# self-test
# --------------------------------------------------------------------------


# The cycle the self-test writes as. Every case that used to assert over "the
# ledger" now asserts over THIS cycle's file; the cases that are specifically
# about two cycles name their own.
SELFTEST_CYCLE = "selftest-cycle"
SELFTEST_LEDGER = cycle_ledger_relpath(SELFTEST_CYCLE)


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
        SELFTEST_LEDGER,
        json.dumps(
            {
                "grandfatherSchema": LEDGER_SCHEMA,
                "cycle": SELFTEST_CYCLE,
                "note": LEDGER_NOTE,
                "entries": [],
            },
            indent=2,
        )
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
        _make_audit(root, "cycle-ledger", "feedback/cycle-ledger.md", [SELFTEST_LEDGER])

        # Give --grandfather real work, so it must write the ledger.
        _write(root, "src/thing.fs", "let a = 2\n")
        grandfather(root, SELFTEST_CYCLE, "selftest: ledger convergence, first pass")
        first = digest_file(ledger_path(root, SELFTEST_LEDGER))
        grandfather(root, SELFTEST_CYCLE, "selftest: ledger convergence, second pass")
        second = digest_file(ledger_path(root, SELFTEST_LEDGER))
        check("an audit citing the ledger converges after two --grandfather runs", evaluate(root)["ok"])
        check("the second --grandfather run leaves the ledger byte-identical", first == second)
        check(
            "the ledger citation is reported as not bound, not silently dropped",
            any(
                entry["locator"] == f"file:{SELFTEST_LEDGER}" and entry["reason"] == LEDGER_EXEMPTION
                for entry in evaluate(root).get("notBoundCitations", [])
            ),
        )

        # The exemption is decided on the RESOLVED path, so a traversing locator
        # cannot smuggle the unsatisfiable binding back in.
        _make_audit(root, "cycle-dots", "feedback/cycle-dots.md", ["feedback/../" + SELFTEST_LEDGER])
        check(
            "a traversing locator onto the ledger is exempt too",
            any(
                entry["reason"] == LEDGER_EXEMPTION and ".." in entry["locator"]
                for entry in evaluate(root).get("notBoundCitations", [])
            ),
        )
        grandfather(root, SELFTEST_CYCLE, "selftest: ledger convergence, after traversal")
        check("the tree is still green after the traversing citation", evaluate(root)["ok"])
    finally:
        shutil.rmtree(root, ignore_errors=True)

    # --- an audit citing another audit is CHECKED, and settles via the ledger --
    #
    # This is the exemption that was NOT made, so these cases guard the decision
    # rather than a mechanism. If someone later exempts *.audit.json, the first
    # two go red.
    root = tempfile.mkdtemp(prefix="audit-bindings-selftest-audit-")
    try:
        _write(root, "src/thing.fs", "let a = 1\n")
        _empty_ledger(root)
        _make_audit(root, "cycle-a", "feedback/cycle-a.md", ["src/thing.fs"])
        # A finding ABOUT cycle-a's audit cites that audit.
        _make_audit(root, "cycle-b", "feedback/cycle-b.md", ["feedback/audits/cycle-a.audit.json"])

        # Rebinding cycle-a rewrites the very bytes cycle-b binds. That MUST be
        # reported -- it is the only signal in the repository that a merged
        # audit was edited.
        _write(root, "src/thing.fs", "let a = 2\n")
        _make_audit(root, "cycle-a", "feedback/cycle-a.md", ["src/thing.fs"])
        r = evaluate(root)
        check(
            "editing an audit that another audit cites is still DETECTED",
            not r["ok"]
            and any(v["locator"] == "file:feedback/audits/cycle-a.audit.json" for v in r["violations"]),
        )
        check(
            "an audit citing another audit is never treated as not bound",
            not r.get("notBoundCitations", []),
        )

        # ...and it settles through the ledger in ONE pass and stays settled.
        # This is why the second exemption is unnecessary: excusing writes only
        # the ledger, which is exempt, so there is nothing left to chase.
        grandfather(root, SELFTEST_CYCLE, "selftest: audit-to-audit settles via the ledger")
        first = digest_file(ledger_path(root, SELFTEST_LEDGER))
        check("an audit-to-audit violation settles in one --grandfather pass", evaluate(root)["ok"])
        grandfather(root, SELFTEST_CYCLE, "selftest: audit-to-audit, second pass")
        check(
            "and stays settled -- the ledger is byte-identical on the next pass",
            evaluate(root)["ok"] and digest_file(ledger_path(root, SELFTEST_LEDGER)) == first,
        )

        # Mutual citation A<->B, the shape called unsatisfiable. Rebind A, which
        # strands B; one pass settles it, and it stays settled.
        _make_audit(root, "cycle-c", "feedback/cycle-c.md", ["feedback/audits/cycle-b.audit.json"])
        _make_audit(root, "cycle-b", "feedback/cycle-b.md", ["feedback/audits/cycle-c.audit.json"])
        _make_audit(root, "cycle-c", "feedback/cycle-c.md", ["feedback/audits/cycle-b.audit.json"])
        check("mutual citation is red before it is excused", not evaluate(root)["ok"])
        grandfather(root, SELFTEST_CYCLE, "selftest: mutual citation")
        settled_ledger = digest_file(ledger_path(root, SELFTEST_LEDGER))
        grandfather(root, SELFTEST_CYCLE, "selftest: mutual citation, again")
        check(
            "two audits citing each other converge in one pass and stay converged",
            evaluate(root)["ok"] and digest_file(ledger_path(root, SELFTEST_LEDGER)) == settled_ledger,
        )

        # A cited audit that does not EXIST must still fail. The exemption fires
        # before the existence check, so exempting audits would turn a dangling
        # or typo'd cross-reference silently green.
        _make_audit(root, "cycle-missing", "feedback/cycle-missing.md", ["feedback/audits/cycle-a.audit.json"])
        grandfather(root, SELFTEST_CYCLE, "selftest: before the dangling-reference probe")
        os.remove(os.path.join(root, "feedback", "audits", "cycle-a.audit.json"))
        r = evaluate(root)
        check(
            "a cited audit that does not exist still fails",
            not r["ok"]
            and any(
                v["locator"] == "file:feedback/audits/cycle-a.audit.json"
                and v["observedSha256"] == MISSING
                for v in r["violations"]
            ),
        )

        # An audit whose REPORT is another audit must not bypass the report
        # binding -- a one-token `report` rewrite would otherwise skip
        # reportSha256 entirely.
        _write(
            root,
            "feedback/audits/cycle-report.audit.json",
            json.dumps(
                {
                    "auditSchema": 1,
                    "report": "feedback/audits/cycle-b.audit.json",
                    "reportSha256": "0" * 64,
                    "findings": [],
                },
                indent=2,
            )
            + "\n",
        )
        r = evaluate(root)
        check(
            "an audit whose REPORT is another audit does not bypass the report binding",
            not r["ok"] and any(v["kind"] == "report" for v in r["violations"]),
        )
        os.remove(os.path.join(root, "feedback", "audits", "cycle-report.audit.json"))
    finally:
        shutil.rmtree(root, ignore_errors=True)

    # --- the exemption must survive a symlinked root -------------------------
    #
    # `resolve_inside` realpaths; `root` may not be realpath'd by the caller. If
    # the two are relativised against each other the exemption silently switches
    # off and rogue3#38 returns. mkdtemp() is a real path on Linux, so this case
    # builds the symlink explicitly rather than trusting $TMPDIR to be one.
    outer = tempfile.mkdtemp(prefix="audit-bindings-selftest-link-")
    try:
        real = os.path.join(outer, "real")
        link = os.path.join(outer, "link")
        os.makedirs(real, exist_ok=True)
        os.symlink(real, link)
        _write(real, "src/thing.fs", "let a = 1\n")
        _empty_ledger(real)
        _make_audit(real, "cycle-1", "feedback/cycle-1.md", ["src/thing.fs"])
        _make_audit(real, "cycle-ledger", "feedback/cycle-ledger.md", [SELFTEST_LEDGER])
        _write(real, "src/thing.fs", "let a = 2\n")

        grandfather(link, SELFTEST_CYCLE, "selftest: symlinked root, first pass")
        through_link = evaluate(link)
        check(
            "the ledger citation is exempt through a symlinked root",
            any(
                entry["locator"] == f"file:{SELFTEST_LEDGER}"
                for entry in through_link.get("notBoundCitations", [])
            ),
        )
        before = digest_file(ledger_path(real, SELFTEST_LEDGER))
        grandfather(link, SELFTEST_CYCLE, "selftest: symlinked root, second pass")
        check(
            "a symlinked root converges exactly as a real one does",
            evaluate(link)["ok"] and digest_file(ledger_path(real, SELFTEST_LEDGER)) == before,
        )
    finally:
        shutil.rmtree(outer, ignore_errors=True)

    # --- ordinary files, and the exit-code invariant -------------------------
    root = tempfile.mkdtemp(prefix="audit-bindings-selftest-ordinary-")
    try:
        _write(root, "src/thing.fs", "let a = 1\n")
        _empty_ledger(root)
        _make_audit(root, "cycle-1", "feedback/cycle-1.md", ["src/thing.fs"])

        # The exemption must be NARROW: only the ledger, by exact path. Neither a
        # sibling directory nor a same-suffix neighbour may inherit it.
        for rel, label in (
            ("scripts/audit-binding-exceptions.json.bak", "a neighbour of the ledger is still bound"),
            ("scripts/audit-binding-exceptionsX.json", "a near-miss ledger name is still bound"),
            # The directory prefix must be a PATH prefix. A bare `startswith`
            # would exempt every one of these, which is how one exemption
            # becomes a class nobody chose.
            (
                "scripts/audit-binding-exceptions-other/x.json",
                "a directory whose name merely starts with the ledger's is still bound",
            ),
            (
                "scripts/audit-binding-exceptions/notes.md",
                "a non-.json file inside the ledger directory is still bound",
            ),
            ("feedback/audits/notes.md", "a non-audit file under feedback/audits/ is still bound"),
        ):
            _write(root, rel, "one\n")
            _make_audit(root, "probe", "feedback/probe.md", [rel])
            grandfather(root, SELFTEST_CYCLE, "selftest: before the narrowness probe")
            _write(root, rel, "two\n")
            r = evaluate(root)
            check(label, not r["ok"] and any(v["locator"] == f"file:{rel}" for v in r["violations"]))
            grandfather(root, SELFTEST_CYCLE, "selftest: after the narrowness probe")

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

        _make_audit(root, "cycle-ledger", "feedback/cycle-ledger.md", [SELFTEST_LEDGER])
        _write(root, "src/thing.fs", "let a = 4\n")
        settled = cli(
            "--grandfather", "--cycle", SELFTEST_CYCLE, "--reason", "selftest: exit-code invariant"
        )
        check("--grandfather exiting 0 means check exits 0", settled == 0 and cli() == 0)
    finally:
        shutil.rmtree(root, ignore_errors=True)


def _stale_binding_tree(prefix: str) -> str:
    """A tree with exactly one stale binding on `src/thing.fs`."""
    root = tempfile.mkdtemp(prefix=prefix)
    _write(root, "src/thing.fs", "let a = 1\n")
    _make_audit(root, "cycle-1", "feedback/cycle-1.md", ["src/thing.fs"])
    _write(root, "src/thing.fs", "let a = 2\n")
    return root


def selftest_per_cycle(check) -> None:
    """rogue3#53: the excuse ledger must not be a path two cycles share.

    Every case here is about the UNION of several cycle files. The single-file
    cases above already cover what one cycle sees; these cover what two see, and
    what happens when they land in either order.
    """
    # --- two cycles never write the same path --------------------------------
    root = _stale_binding_tree("audit-bindings-selftest-two-cycles-")
    try:
        grandfather(root, "item-alpha", "selftest: alpha excused it")
        alpha = cycle_ledger_relpath("item-alpha")
        check(
            "a cycle writes its own file and no shared one",
            os.path.isfile(ledger_path(root, alpha))
            and not os.path.exists(ledger_path(root, LEGACY_LEDGER_RELPATH))
            and evaluate(root)["ok"],
        )

        # A second cycle, arriving at a DIFFERENT digest for the same binding --
        # exactly what two workers editing one bound file produce.
        alpha_before = digest_file(ledger_path(root, alpha))
        _write(root, "src/thing.fs", "let a = 3\n")
        outcome = grandfather(root, "item-beta", "selftest: beta excused it")
        beta = cycle_ledger_relpath("item-beta")
        check(
            "a second cycle excusing the same binding writes only its own file",
            outcome["written"] == beta
            and digest_file(ledger_path(root, alpha)) == alpha_before
            and outcome["prunedElsewhere"] == [],
        )
        result = evaluate(root)
        check(
            "the union is green with one cycle excusing and the other superseded",
            result["ok"] and result["excused"] == 1 and result["superseded"] == 1,
        )
        check(
            "the superseded entry names the cycle file it came from",
            result["supersededEntries"][0]["sourceFile"] == alpha,
        )
        check(
            "a superseded entry is not counted as obsolete",
            not result["obsoleteExceptions"],
        )

        # ORDER INDEPENDENCE. Whichever cycle's digest the file ends up at, the
        # union is green -- that is the property that lets the two land in
        # either order. Flipping the file back to alpha's bytes swaps the roles.
        _write(root, "src/thing.fs", "let a = 2\n")
        flipped = evaluate(root)
        check(
            "the union is green whichever cycle's digest the file ends at",
            flipped["ok"]
            and flipped["excused"] == 1
            and flipped["supersededEntries"][0]["sourceFile"] == beta,
        )

        # And a THIRD digest neither cycle observed still fails: supersession
        # does not weaken "an exception excuses one observed digest".
        _write(root, "src/thing.fs", "let a = 4\n")
        third = evaluate(root)
        check(
            "a digest no entry observed still fails, with both entries superseded",
            not third["ok"]
            and len(third["violations"]) == 1
            and "changed again" in third["violations"][0]["reason"]
            and third["superseded"] == 2,
        )

        # A cycle re-excusing after a further edit rewrites ITS file only, and
        # does not adopt the other cycle's entry -- the reason text in another
        # cycle's file is that cycle's record.
        beta_entries_before = len(_load_ledger_file(root, beta))
        grandfather(root, "item-beta", "selftest: beta re-excused it")
        check(
            "re-excusing replaces this cycle's entry rather than accumulating",
            evaluate(root)["ok"] and len(_load_ledger_file(root, beta)) == beta_entries_before,
        )
        check(
            "re-excusing never copies another cycle's entry into this cycle's file",
            all(
                entry["reason"].startswith("selftest: beta")
                for entry in _load_ledger_file(root, beta)
            )
            and len(_load_ledger_file(root, alpha)) == 1,
        )
    finally:
        shutil.rmtree(root, ignore_errors=True)

    # --- obsolete entries anywhere still fail, and still have a one-command
    #     route to green: the rogue3#38 shape must not come back ---------------
    root = _stale_binding_tree("audit-bindings-selftest-obsolete-")
    try:
        grandfather(root, "item-alpha", "selftest: alpha excused it")
        alpha = cycle_ledger_relpath("item-alpha")
        # Restoring the bytes the audit pins makes alpha's entry excuse nothing.
        _write(root, "src/thing.fs", "let a = 1\n")
        stranded = evaluate(root)
        check(
            "an obsolete entry in ANOTHER cycle's file is still a failure",
            not stranded["ok"]
            and len(stranded["obsoleteExceptions"]) == 1
            and stranded["obsoleteExceptions"][0]["sourceFile"] == alpha,
        )
        outcome = grandfather(root, "item-beta", "selftest: beta cleans up")
        check(
            "one --grandfather run by a DIFFERENT cycle reaches green",
            outcome["checkOk"] and evaluate(root)["ok"],
        )
        check(
            "the foreign prune is reported, never silent",
            outcome["prunedElsewhere"] == [{"file": alpha, "removed": 1, "deleted": True}],
        )
        check(
            "a cycle file emptied by pruning is removed, not left as a husk",
            not os.path.exists(ledger_path(root, alpha)),
        )
    finally:
        shutil.rmtree(root, ignore_errors=True)

    # --- pruning a file this cycle does not own keeps that file's own note ----
    #
    # The frozen archive's note is the paragraph saying it IS the frozen archive.
    # A prune that replaced it with the generic per-cycle note would delete the
    # only instruction telling the next worker not to append there.
    root = _stale_binding_tree("audit-bindings-selftest-foreign-note-")
    try:
        grandfather(root, "item-alpha", "selftest: alpha excused it")
        alpha = cycle_ledger_relpath("item-alpha")
        kept = [dict(entry) for entry in _load_ledger_file(root, alpha)]
        # A second, independent binding so the file is not emptied by the prune.
        _write(root, "src/other.fs", "let o = 1\n")
        _make_audit(root, "cycle-2", "feedback/cycle-2.md", ["src/other.fs"])
        _write(root, "src/other.fs", "let o = 2\n")
        grandfather(root, "item-alpha", "selftest: alpha excused both")
        write_ledger(
            root,
            alpha,
            _load_ledger_file(root, alpha),
            "item-alpha",
            note="ALPHA'S OWN NOTE, which a foreign prune must not replace.",
        )
        # Retire exactly one of alpha's two entries, so item-beta must rewrite
        # alpha's file rather than delete it.
        _write(root, "src/thing.fs", "let a = 1\n")
        grandfather(root, "item-beta", "selftest: beta prunes one of alpha's entries")
        check(
            "a foreign prune keeps the pruned file's own note",
            evaluate(root)["ok"]
            and read_ledger_note(root, alpha) == "ALPHA'S OWN NOTE, which a foreign prune must not replace."
            and len(_load_ledger_file(root, alpha)) == len(kept),
        )
    finally:
        shutil.rmtree(root, ignore_errors=True)

    # --- the remedy reports the VERDICT, not the write -----------------------
    root = _stale_binding_tree("audit-bindings-selftest-verdict-")
    try:
        # A malformed audit cannot be excused, so the rewrite cannot reach green
        # -- and must say so rather than reporting the successful write.
        _write(root, "feedback/audits/broken.audit.json", "{not json\n")
        outcome = grandfather(root, "item-alpha", "selftest: cannot clear this")
        check(
            "--grandfather reports checkOk=False when it cannot reach green",
            not outcome["checkOk"] and not evaluate(root)["ok"],
        )
        script = os.path.abspath(__file__)
        exit_code = subprocess.run(
            [
                sys.executable, script, "--root", root,
                "--grandfather", "--cycle", "item-alpha", "--reason", "selftest: verdict",
            ],
            capture_output=True,
            text=True,
        ).returncode
        check("--grandfather exits 1 when the check would still be red", exit_code == 1)
    finally:
        shutil.rmtree(root, ignore_errors=True)

    # --- a cycle id becomes a filename, so it is validated -------------------
    root = _stale_binding_tree("audit-bindings-selftest-cycle-id-")
    try:
        for bad_id in ("../escape", "a/b", "/abs", "", " ", ".hidden", "Upper", "x" * 200):
            rejected = False
            try:
                grandfather(root, bad_id, "selftest: must be rejected")
            except UsageError:
                rejected = True
            check(f"the cycle id {bad_id!r} is rejected", rejected)
        check(
            "a rejected cycle id writes nothing at all",
            not os.path.isdir(ledger_path(root, LEDGER_DIR))
            and not os.path.exists(os.path.join(os.path.dirname(root), "escape.json")),
        )
        check(
            "an ordinary board cycle id is accepted",
            grandfather(root, "item-53-per-cycle-ledger", "selftest: accepted")["checkOk"],
        )
    finally:
        shutil.rmtree(root, ignore_errors=True)

    # --- the legacy single file is still read, and still exempt --------------
    root = _stale_binding_tree("audit-bindings-selftest-legacy-")
    try:
        # Hand-write the pre-rogue3#53 shape: one shared file, no `cycle` key.
        result = evaluate(root)
        violation = result["violations"][0]
        _write(
            root,
            LEGACY_LEDGER_RELPATH,
            json.dumps(
                {
                    "grandfatherSchema": 1,
                    "entries": [
                        {
                            "audit": violation["audit"],
                            "kind": violation["kind"],
                            "locator": violation["locator"],
                            "boundSha256": violation["boundSha256"],
                            "observedSha256": violation["observedSha256"],
                            "reason": "selftest: written before the migration",
                        }
                    ],
                },
                indent=2,
            )
            + "\n",
        )
        migrating = evaluate(root)
        check(
            "an entry in the legacy single file still excuses",
            migrating["ok"] and migrating["ledgerFiles"] == [LEGACY_LEDGER_RELPATH],
        )
        _make_audit(root, "cycle-legacy", "feedback/cycle-legacy.md", [LEGACY_LEDGER_RELPATH])
        check(
            "a citation onto the legacy ledger path is still exempt",
            any(
                entry["locator"] == f"file:{LEGACY_LEDGER_RELPATH}"
                for entry in evaluate(root)["notBoundCitations"]
            ),
        )
        # The new remedy reads the legacy file and leaves it byte-identical: a
        # cycle arriving mid-migration excuses into its own file, and the old
        # entry becomes a superseded record rather than something to rewrite.
        _write(root, "src/thing.fs", "let a = 5\n")
        legacy_before = digest_file(ledger_path(root, LEGACY_LEDGER_RELPATH))
        grandfather(root, "item-alpha", "selftest: after the migration")
        after = evaluate(root)
        check(
            "--grandfather never writes the legacy path",
            after["ok"]
            and digest_file(ledger_path(root, LEGACY_LEDGER_RELPATH)) == legacy_before
            and any(
                entry["sourceFile"] == LEGACY_LEDGER_RELPATH
                for entry in after["supersededEntries"]
            ),
        )
    finally:
        shutil.rmtree(root, ignore_errors=True)

    # --- a citation onto a PER-CYCLE file is exempt too ----------------------
    #
    # This is rogue3#38 at the path the excuse now actually lands in. If only the
    # legacy path were exempt, the first cycle whose audit cites its own excuse
    # would reproduce the dead end under a new name.
    root = _stale_binding_tree("audit-bindings-selftest-cycle-citation-")
    try:
        own = cycle_ledger_relpath("item-alpha")
        grandfather(root, "item-alpha", "selftest: seed the file")
        _make_audit(root, "cycle-about-ledger", "feedback/cycle-about-ledger.md", [own])
        _write(root, "src/thing.fs", "let a = 6\n")
        grandfather(root, "item-alpha", "selftest: per-cycle convergence, first pass")
        first = digest_file(ledger_path(root, own))
        grandfather(root, "item-alpha", "selftest: per-cycle convergence, second pass")
        check(
            "an audit citing a per-cycle ledger file converges in one pass",
            evaluate(root)["ok"] and digest_file(ledger_path(root, own)) == first,
        )
        check(
            "the per-cycle citation is reported as not bound, not silently dropped",
            any(
                entry["locator"] == f"file:{own}" and entry["reason"] == LEDGER_EXEMPTION
                for entry in evaluate(root)["notBoundCitations"]
            ),
        )
        # Resolved-path, not locator text: a traversing locator must not smuggle
        # the unsatisfiable binding back in at the new path either.
        _make_audit(root, "cycle-dots", "feedback/cycle-dots.md", ["feedback/../" + own])
        check(
            "a traversing locator onto a per-cycle file is exempt too",
            any(
                entry["reason"] == LEDGER_EXEMPTION and ".." in entry["locator"]
                for entry in evaluate(root)["notBoundCitations"]
            ),
        )
    finally:
        shutil.rmtree(root, ignore_errors=True)

    # --- ledger files are found wherever they are, and must not lie ----------
    root = _stale_binding_tree("audit-bindings-selftest-ledger-shape-")
    try:
        grandfather(root, "item-alpha", "selftest: seed")
        alpha = cycle_ledger_relpath("item-alpha")
        entries = _load_ledger_file(root, alpha)

        # A file in a subdirectory is exempt from binding, so it must also be
        # READ -- otherwise an excuse could be written where nothing applies it.
        nested = f"{LEDGER_DIR}/nested/item-alpha.json"
        os.makedirs(os.path.dirname(ledger_path(root, nested)), exist_ok=True)
        os.replace(ledger_path(root, alpha), ledger_path(root, nested))
        check(
            "a ledger file in a subdirectory is still read",
            evaluate(root)["ok"] and evaluate(root)["ledgerFiles"] == [nested],
        )
        os.replace(ledger_path(root, nested), ledger_path(root, alpha))

        # A `cycle` that disagrees with the filename is a lie about provenance,
        # and `--grandfather --cycle` writes by filename, so it would never be
        # repaired by using the tool.
        _write(
            root,
            alpha,
            json.dumps(
                {
                    "grandfatherSchema": LEDGER_SCHEMA,
                    "cycle": "item-somebody-else",
                    "entries": [{k: v for k, v in entries[0].items() if k != "sourceFile"}],
                },
                indent=2,
            )
            + "\n",
        )
        mismatched = False
        try:
            evaluate(root)
        except UsageError:
            mismatched = True
        check("a cycle field disagreeing with the filename is rejected", mismatched)

        # Two entries for the same binding at the same digest in ONE file is a
        # broken write, not the concurrency the union absorbs.
        entry = {k: v for k, v in entries[0].items() if k != "sourceFile"}
        _write(
            root,
            alpha,
            json.dumps(
                {"grandfatherSchema": LEDGER_SCHEMA, "entries": [entry, dict(entry)]}, indent=2
            )
            + "\n",
        )
        duplicated = False
        try:
            evaluate(root)
        except UsageError:
            duplicated = True
        check("a duplicate entry within one cycle file is rejected", duplicated)
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
        grandfather(root, SELFTEST_CYCLE, "selftest: excused")
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
        grandfather(root, SELFTEST_CYCLE, "selftest: prune")
        r = evaluate(root)
        check("--grandfather prunes obsolete entries", r["ok"] and not r["obsoleteExceptions"])
        check("the pruned ledger holds no entries", load_ledger(root) == [])

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
        grandfather(root, SELFTEST_CYCLE, "selftest: determinism")
        with open(ledger_path(root, SELFTEST_LEDGER), "rb") as handle:
            first = handle.read()
        grandfather(root, SELFTEST_CYCLE, "selftest: determinism")
        with open(ledger_path(root, SELFTEST_LEDGER), "rb") as handle:
            second = handle.read()
        check("the ledger is byte-stable across reruns", first == second)

        # 13. rebinding an audit retires its exception instead of reusing it
        _write(root, "src/rebind.fs", "let c = 1\n")
        _make_audit(root, "cycle-3", "feedback/cycle-3.md", ["src/rebind.fs"])
        _write(root, "src/rebind.fs", "let c = 2\n")
        grandfather(root, SELFTEST_CYCLE, "selftest: excused before rebind")
        before = len(load_ledger(root))
        check("the pre-rebind edit is excused", evaluate(root)["ok"])
        _make_audit(root, "cycle-3", "feedback/cycle-3.md", ["src/rebind.fs"])
        r = evaluate(root)
        check(
            "rebinding retires the exception instead of reusing it",
            not r["ok"] and len(r["obsoleteExceptions"]) == 1 and not r["violations"],
        )
        grandfather(root, SELFTEST_CYCLE, "selftest: prune after rebind")
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
            and grandfather(root, SELFTEST_CYCLE, "selftest: unpinned")["notExcusable"] != [],
        )
        check("an audit that is not valid JSON fails",
              (lambda: (_write(root, "feedback/audits/cycle-4.audit.json", "{not json\n"),
                        evaluate(root))[1])()["ok"] is False)
        check(
            "--grandfather refuses to bless a malformed audit",
            grandfather(root, SELFTEST_CYCLE, "selftest: must not excuse structure")["notExcusable"] != []
            and not evaluate(root)["ok"],
        )
        os.remove(os.path.join(root, "feedback", "audits", "cycle-4.audit.json"))
        grandfather(root, SELFTEST_CYCLE, "selftest: prune cycle-4")
        check("removing the malformed audit restores green", evaluate(root)["ok"])

        # 15. a hand-written exception missing a reason is rejected
        _write(
            root,
            SELFTEST_LEDGER,
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
                SELFTEST_LEDGER,
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
        os.remove(ledger_path(root, SELFTEST_LEDGER))
        check("--grandfather without --reason exits 2", cli("--grandfather") == 2)
        check("--reason without --grandfather exits 2", cli("--reason", "x") == 2)
        check("--cycle without --grandfather exits 2", cli("--cycle", SELFTEST_CYCLE) == 2)
        # Without --cycle the remedy would have to pick a default path, and a
        # default path is the shared file rogue3#53 exists to remove.
        check(
            "--grandfather without --cycle exits 2",
            cli("--grandfather", "--reason", "selftest: no cycle") == 2,
        )
        check("a nonexistent --root exits 2", _bad_root(script) == 2)
        check(
            "--grandfather via the CLI exits 0",
            cli("--grandfather", "--cycle", SELFTEST_CYCLE, "--reason", "selftest: cli") == 0,
        )
        check("a clean tree exits 0", cli() == 0)
        _write(root, "src/thing.fs", "let a = 99\n")
        check("a stale binding exits 1", cli() == 1)
        check("--json still exits 1 on violations", cli("--json") == 1)

        # 17. the checker's own remedy surfaces cannot be bound (rogue3#38)
        selftest_exemptions(check)

        # 18. one ledger file per cycle, and the union of them (rogue3#53)
        selftest_per_cycle(check)
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
        help="rewrite THIS CYCLE'S exceptions file from the current violations and prune entries "
        "in it that excuse nothing",
    )
    parser.add_argument(
        "--cycle",
        default=None,
        help="the cycle id owning the exceptions file to write "
        f"({LEDGER_DIR}/<cycle-id>.json); required with --grandfather",
    )
    parser.add_argument("--reason", default=None, help="justification recorded with --grandfather")
    parser.add_argument("--selftest", action="store_true", help="exercise the checker against a temporary tree")
    args = parser.parse_args(argv)

    if args.selftest:
        return selftest()

    if args.reason and not args.grandfather:
        print("audit-bindings: --reason has no effect without --grandfather", file=sys.stderr)
        return 2
    if args.cycle and not args.grandfather:
        print("audit-bindings: --cycle has no effect without --grandfather", file=sys.stderr)
        return 2

    root = os.path.abspath(args.root or default_root())
    if not os.path.isdir(root):
        print(f"audit-bindings: not a directory: {root}", file=sys.stderr)
        return 2

    if args.grandfather:
        if not args.reason or not args.reason.strip():
            print("audit-bindings: --grandfather requires --reason", file=sys.stderr)
            return 2
        if not args.cycle or not args.cycle.strip():
            # Deliberately not defaulted. A default would put two concurrent
            # cycles back on one path, which is the whole of rogue3#53.
            print(
                "audit-bindings: --grandfather requires --cycle <cycle-id> -- each cycle writes "
                f"its own {LEDGER_DIR}/<cycle-id>.json so concurrent cycles never collide",
                file=sys.stderr,
            )
            return 2
        outcome = grandfather(root, args.cycle, args.reason.strip())
        if args.json:
            print(json.dumps(outcome, indent=2))
        else:
            print(
                f"audit-bindings: wrote {outcome['written']} with {outcome['entries']} "
                f"exception(s); pruned {outcome['pruned']}."
            )
            for pruned in outcome["prunedElsewhere"]:
                print(
                    f"audit-bindings: pruned {pruned['removed']} obsolete exception(s) from "
                    f"{pruned['file']}"
                    + (" and removed the now-empty file" if pruned["deleted"] else "")
                    + " -- that file belongs to another cycle; review the deletion."
                )
            for entry in outcome["notExcusable"]:
                print(
                    f"audit-bindings: NOT excused (repair the audit): {entry['audit']} "
                    f"{entry['locator']} -- {entry['reason']}",
                    file=sys.stderr,
                )
            if not outcome["checkOk"]:
                print(
                    "audit-bindings: the check is STILL RED after this rewrite -- run the check "
                    "for the reason.",
                    file=sys.stderr,
                )
        # A malformed audit is not something the ledger can absorb, and neither
        # is anything else the rewrite failed to clear, so the remedy reports
        # the VERDICT rather than the fact that it wrote a file (rogue3#38,
        # feedback/2026-08-02-Rogue3-9.md 4.2).
        return 0 if outcome["checkOk"] and not outcome["notExcusable"] else 1

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
