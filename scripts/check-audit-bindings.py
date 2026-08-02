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
Exceptions that no longer correspond to a violation are reported as obsolete.
They do NOT fail the check -- see APPLYING AND DORMANT ENTRIES below for why
that stopped being safe once the ledger became one file per cycle, and for what
replaced it.

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
their union.

`--grandfather --cycle <id>` writes exactly ONE path,
`scripts/audit-binding-exceptions/<id>.json`, and DELETES NOTHING.  That is a
hard rule, not a description of the common case: a remedy that can touch another
cycle's file is the shared write this exists to remove, and a remedy that can
DELETE one can strand a merged audit citing it with no way back -- the rogue3#38
shape, and worse, because the gate would stay green while the validator stayed
red.  A ledger file, once written, is never removed by this tool; an emptied one
is left holding `"entries": []`.

It was a single shared file until rogue3#53.  Because remedy (1) is the only
non-destructive remedy, and because the heavily-cited files are exactly the ones
work lands in, every concurrent worker that touched a bound file was funnelled
onto that one path -- while no board item declared it, so nothing could sequence
them.  Three workers in one bounded fan-out collided on it and negotiated an
append order by hand.  Per-cycle files remove the shared path from the remedy:
two cycles editing the same bound file now write two different files.

APPLYING AND DORMANT ENTRIES
----------------------------

The union has to converge no matter which order two cycles land in, so an entry
is matched by its binding key AND the digest it observed, and a stale binding is
excused when ANY entry in the union observes the digest the file actually has.
Every entry is therefore in one of two states:

  * APPLYING -- its binding is stale and it observes the digest the file has.
                It excuses that binding.
  * DORMANT  -- it excuses nothing right now.  Reported and counted, NEVER
                fatal.  Two shapes, reported separately because they mean
                different things:
                  SUPERSEDED -- the binding is still stale, but the file is at a
                    digest this entry did not observe.  Another entry may be
                    excusing it, or the binding may be failing.
                  OBSOLETE -- the binding is not stale at all: the file went
                    back to the bytes the audit pins, or the audit was rebound
                    or removed.

Two things about DORMANT are deliberate and are weakenings of what this gate did
before rogue3#53.  Both are stated here rather than left to be discovered.

FIRST: an obsolete entry used to FAIL the check, on the ground that it stopped
the ledger rotting the way the digests did.  It cannot fail it now.  With one
shared file, the worker who saw the failure could always clear it.  With one file
per cycle, an obsolete entry usually sits in a MERGED cycle's file, and a fatal
condition that only a finished cycle can clear is a gate with no bounded route to
green.  Rot is instead made loud and attributable: the count is on the summary
line and on the verdict line, every dormant entry is listed with the cycle file
it came from, and `--grandfather --cycle <id>` drops the dormant entries in
`<id>.json` -- so a cycle that runs the remedy always commits a clean file of its
own, and nothing accumulates in a file anyone is still writing.

SECOND: the digests a binding tolerates now only ever GROW.  Under one shared
file, `--grandfather` replaced the entry for a binding, so the digest an earlier
cycle excused was forgotten.  Under the union, that entry is still there and
still applies if the file returns to that digest.  So reverting a merged change
can be green under a closed cycle's recorded reason rather than demanding a fresh
one.  That is the price of order-independence and it is paid knowingly: two
cycles' entries must both survive the merge, or the verdict depends on which
landed second.  What is NOT weakened is the property the ledger exists for -- a
digest NO cycle ever excused still fails, so editing an excused file again fails
again, exactly as before.  The excuse that fires is a diffable line in the tree
naming its cycle, its digest and its reason; it is a reviewed permission, just
not one reviewed this week.

The single file the ledger used to be, `scripts/audit-binding-exceptions.json`,
is a FROZEN ARCHIVE: still read, still honoured, never written by anything here.
Its entries are the record of what earlier cycles excused and why, and four
merged audits cite the path, so deleting it would both discard that record and
break evidence this repository already accepted.

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
    "(scripts/check-audit-bindings.py) written by ONE cycle: "
    "`--grandfather --cycle <id>` writes this file and nothing else, ever. Each "
    "entry excuses ONE stale binding at ONE observed digest, so a digest no "
    "cycle ever excused still fails. The checker evaluates the UNION of every "
    "file in this directory, so two concurrent cycles excuse the same binding "
    "without sharing a path -- and because both entries survive the merge, the "
    "digests a binding tolerates only ever GROW: returning a file to a digest "
    "some earlier cycle excused is green under that cycle's recorded reason. An "
    "entry that excuses nothing right now is reported as dormant, never as a "
    "failure, and only the cycle that owns this file prunes it. Adding an entry "
    "here is the PREFERRED remedy; rebinding a MERGED audit rewrites what that "
    "audit records its critic as having verified (see 7e71d71) and is right "
    "only for an audit you re-verified yourself."
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


LEDGER_REPAIR = (
    "repair the JSON, or DELETE the file -- a ledger file is exempt from binding, so "
    "deleting it turns its excuses back into ordinary violations you can excuse again"
)


def _load_ledger_file(root: str, relpath: str) -> list[dict[str, str]]:
    """Every entry in one ledger file, each tagged with the file it came from.

    A file this cannot read is a UsageError (exit 2, "input the checker cannot
    interpret"), not a violation, and it stops `--grandfather` too. That is a
    hand-repair route rather than a one-command one, and it is left that way
    deliberately: the alternative is a remedy that silently discards a cycle's
    recorded excuses because their file did not parse. Every message therefore
    names both repairs.
    """
    path = ledger_path(root, relpath)
    with open(path, "rb") as handle:
        try:
            doc = json.loads(handle.read().decode("utf-8-sig"))
        except (ValueError, UnicodeDecodeError) as exc:
            raise UsageError(f"{relpath}: not readable as JSON: {exc} -- {LEDGER_REPAIR}")
    if not isinstance(doc, dict):
        raise UsageError(f"{relpath}: root must be a JSON object -- {LEDGER_REPAIR}")

    # No `cycle` field: the FILENAME is the cycle, and a second copy of it inside
    # the file could only ever disagree. A redundant field that can disagree buys
    # provenance nothing -- every report line already names the file an entry came
    # from -- and costs a hard-stop state, because renaming a file to fix a typo
    # would then brick the remedy that has to repair it.

    entries = doc.get("entries")
    if entries is None:
        entries = []
    if not isinstance(entries, list):
        raise UsageError(
            f"{relpath}: 'entries' must be a list, found {_typename(entries)} -- a ledger whose "
            f"entries cannot be read excuses nothing and would pass silently. {LEDGER_REPAIR}"
        )

    out: list[dict[str, str]] = []
    seen: set[tuple[str, str, str, str]] = set()
    for index, entry in enumerate(entries):
        if not isinstance(entry, dict):
            raise UsageError(f"{relpath}: entry {index} must be an object -- {LEDGER_REPAIR}")
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
    silently ignored, or an excuse would be written and never applied. Every
    candidate is put through `exemption` itself rather than through a second copy
    of its rule, so "a path exempt from binding" and "a path read as an
    exception" are the same set by construction rather than by agreement.
    """
    found: list[str] = []
    if os.path.isfile(ledger_path(root, LEGACY_LEDGER_RELPATH)):
        found.append(LEGACY_LEDGER_RELPATH)
    directory = ledger_path(root, LEDGER_DIR)
    if os.path.isdir(directory):
        nested: list[str] = []
        root_real = os.path.realpath(root)
        for current, _dirs, names in os.walk(directory):
            for name in names:
                if not name.endswith(LEDGER_SUFFIX):
                    continue
                # Relativise the REALPATH against the REALPATH'd root, exactly as
                # `exemption`'s call site does. Relativising the symlinked form
                # instead would let a symlinked ledger directory be READ here and
                # not EXEMPT there -- the two must decide the same paths, or an
                # excuse could live somewhere a citation is still bound.
                rel = _rel(root_real, os.path.realpath(os.path.join(current, name)))
                if exemption(rel) is not None:
                    nested.append(rel)
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


def write_ledger(root: str, relpath: str, entries: list[dict[str, str]]) -> None:
    """Write ONE cycle's ledger file. The only writer in this module.

    Callers must never point this at a path the running cycle does not own: see
    the hard rule in the module docstring.
    """
    ordered = [
        {field: entry[field] for field in ENTRY_FIELDS}
        for entry in sorted(entries, key=_entry_sort_key)
    ]
    doc: dict[str, Any] = {
        "grandfatherSchema": LEDGER_SCHEMA,
        "note": LEDGER_NOTE,
        "entries": ordered,
    }
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
    excused_bindings: list[Binding] = []
    applying: list[dict[str, str]] = []
    superseded: list[dict[str, str]] = []
    violations: list[dict[str, Any]] = list(malformed)
    live_keys: set[tuple[str, str, str, str]] = set()

    for binding in bindings:
        if binding.bound is None:
            violations.append(_violation(binding, "audit pins no sha256 for this file locator"))
            continue
        if binding.fresh:
            fresh.append(binding)
            continue

        candidates = grouped.get(binding.key)
        if candidates:
            # The binding is live and stale, so an entry for it is either
            # applying or superseded -- never obsolete, whatever else is true.
            live_keys.add(binding.key)
            matching = [e for e in candidates if e["observedSha256"] == binding.observed]
            others = [e for e in candidates if e["observedSha256"] != binding.observed]
            superseded.extend(dict(entry) for entry in others)
            if matching:
                # Excused by whichever cycle observed the digest the file
                # actually has, whichever cycle that is and whenever it ran --
                # see APPLYING AND DORMANT ENTRIES in the module docstring.
                excused_bindings.append(binding)
                applying.extend(dict(entry) for entry in matching)
                continue
            # No entry observes the current bytes, so an excuse still pins ONE
            # digest and an edit to a digest nobody excused fails, as before.
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
        (dict(entry) for entry in entries if entry_key(entry) not in live_keys),
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
        # BINDINGS excused, not entries -- so `fresh + excused + violations`
        # accounts for every binding even when two cycles' files happen to hold
        # byte-identical entries for one of them.
        "excused": len(excused_bindings),
        "excusedEntries": applying,
        # DORMANT: an entry that excuses nothing right now. Never a violation --
        # an obsolete entry usually sits in a MERGED cycle's file, and a fatal
        # condition only a finished cycle could clear is a gate with no bounded
        # route to green. Counted and listed instead, so a tolerance nobody can
        # see cannot be mistaken for a hole. See the module docstring.
        "superseded": len(superseded),
        "supersededEntries": sorted(superseded, key=_entry_sort_key),
        "obsolete": len(obsolete),
        "dormant": len(superseded) + len(obsolete),
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
        "ok": not violations,
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

    Writes exactly one path, `scripts/audit-binding-exceptions/<cycle>.json`, and
    touches NOTHING else -- no other cycle's file, not the frozen archive, no
    deletions. That is what stops two concurrent cycles colliding on the excuse
    ledger (rogue3#53), and it is unconditional rather than usual: a remedy with
    even a rare cross-cycle write is a path no item can declare, and one that
    could delete a file a merged audit cites would strand that audit for good.

    Another cycle's entries are read and honoured but never copied here and never
    re-worded: their reason text is that cycle's record, not this one's to
    rewrite. Dormant entries in OTHER files are left exactly where they are; they
    fail nothing, and the cycle that owns them prunes them by running this.

    Reusing a cycle id ADOPTS the file of that name: entries still applying are
    carried forward verbatim, dormant ones are dropped. That is right when a cycle
    re-runs its own remedy and wrong when an id is reused by accident, which
    nothing here can tell apart -- so the count of entries this run inherited is
    reported and the CLI prints it.
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
    # Nothing to say and nothing already said: a cycle that excuses nothing must
    # not leave an empty file behind just for having run the command. A file that
    # ALREADY exists is rewritten even when it empties, because this tool never
    # deletes a ledger file -- something may be citing it.
    existed = os.path.isfile(ledger_path(root, own_relpath))
    if entries or existed:
        write_ledger(root, own_relpath, entries)

    kept = {entry_key(entry) for entry in entries}
    after = evaluate(root)
    return {
        "written": own_relpath if (entries or existed) else None,
        "cycle": cycle,
        "entries": len(entries),
        # Entries that were in <cycle>.json before this run: 0 for a fresh cycle,
        # non-zero when this id is being re-run or reused.
        "adopted": len(previous),
        "carriedForward": sum(
            1
            for entry in entries
            if _without_source(previous.get(entry_key(entry))) == _without_source(entry)
        ),
        "pruned": len([key for key in previous if key not in kept]),
        # Dormant entries in files this cycle does not own, listed so the reader
        # knows they exist and were deliberately left alone rather than missed.
        "dormantElsewhere": sorted(
            {
                entry["sourceFile"]
                for entry in after["supersededEntries"] + after["obsoleteExceptions"]
                if entry["sourceFile"] != own_relpath
            }
        ),
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
    # CI logs sees the binding count drop with no accounting. The dormant count
    # is on it for the same reason -- it is a tolerance, and a tolerance nobody
    # can see is indistinguishable from a hole.
    print(
        "audit-bindings: {audits} audits, {bindings} bindings, {fresh} fresh, "
        "{excused} explicitly excused, {notBoundCount} not bound; "
        "{dormant} dormant exception(s) ({superseded} superseded, {obsolete} obsolete) "
        "over {ledgerFileCount} ledger file(s)".format(
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
            f"\naudit-bindings: {len(superseded)} SUPERSEDED EXCEPTION(S) -- an excuse for a "
            "binding that is\nstill stale, pinned at a digest the file no longer has. Another "
            "entry may be excusing\nthat binding, or it may be failing above. Never a failure "
            "in itself:",
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
            f"\naudit-bindings: {len(obsolete)} OBSOLETE EXCEPTION(S) -- these excuse nothing: "
            "the binding is\nnot stale at all. Never a failure. The cycle that owns the file "
            "prunes them by running\nthe remedy for its own id; nobody else may touch it:",
            file=stream,
        )
        for entry in obsolete:
            print(
                f"    {entry['sourceFile']}  {entry['audit']}  {entry['locator']}",
                file=stream,
            )

    if violations:
        print(
            "\naudit-bindings: fix by REBINDING the audit -- recompute each stale sha256 so it\n"
            "pins the bytes that now exist (feedback-tool.fsx has no rebind subcommand; use\n"
            "`-- digest <file>` per file) -- or by excusing each one EXPLICITLY:\n"
            "    python3 scripts/check-audit-bindings.py --grandfather \\\n"
            '        --cycle <cycle-id> --reason "<why>"\n'
            f"then commit {LEDGER_DIR}/<cycle-id>.json. That is the ONLY path the remedy writes,\n"
            "and it is yours: a concurrent cycle writes its own and the two never conflict. An\n"
            "exception is pinned to one observed digest, so the next change to the same file\n"
            "fails again -- run this LAST.",
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
            # Same directory name somewhere else entirely. A SUBSTRING match
            # rather than a prefix would exempt this.
            (
                "vendor/scripts/audit-binding-exceptions/x.json",
                "the ledger directory name in another directory is still bound",
            ),
            # The gate compares case-sensitively on every platform, and the F#
            # validator is required to agree with it. Case-folding either side
            # would exempt a path the other still binds.
            (
                "scripts/Audit-Binding-Exceptions/x.json",
                "a CASE variant of the ledger directory is still bound",
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

        # The ledger DIRECTORY itself is not a file, so a citation onto it is an
        # ordinary missing-file violation. Exempting the bare directory path
        # would turn a typo'd locator silently green.
        _make_audit(root, "bare-dir", "feedback/bare-dir.md", ["src/thing.fs"])
        _write(
            root,
            "feedback/audits/bare-dir.audit.json",
            json.dumps(
                {
                    "auditSchema": 1,
                    "report": "feedback/bare-dir.md",
                    "reportSha256": digest_file(os.path.join(root, "feedback", "bare-dir.md")),
                    "findings": [
                        {
                            "id": "§4.1",
                            "checkedEvidence": [
                                {
                                    "locator": f"file:{LEDGER_DIR}",
                                    "result": "verified",
                                    "sha256": "0" * 64,
                                }
                            ],
                        }
                    ],
                },
                indent=2,
            )
            + "\n",
        )
        r = evaluate(root)
        check(
            "a citation onto the bare ledger DIRECTORY is bound, not exempt",
            any(v["locator"] == f"file:{LEDGER_DIR}" for v in r["violations"])
            and not any(c["locator"] == f"file:{LEDGER_DIR}" for c in r["notBoundCitations"]),
        )
        os.remove(os.path.join(root, "feedback", "audits", "bare-dir.audit.json"))
        grandfather(root, SELFTEST_CYCLE, "selftest: after the bare-directory probe")

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


def _entries_or_missing(root: str, relpath: str) -> list[dict[str, str]] | None:
    """A ledger file's entries, or None when it does not exist.

    So a case asserting "this file still holds nothing" FAILS when the file was
    deleted instead of crashing on the open -- a crash still exits non-zero, but
    it reports no case name, and the name is what tells a reader which claim
    broke.
    """
    if not os.path.isfile(ledger_path(root, relpath)):
        return None
    return _load_ledger_file(root, relpath)


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
            and outcome["adopted"] == 0,
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

        # The same claim where it can actually be violated: with the file back at
        # ALPHA's digest, alpha's entry is APPLYING, so a naive rewrite that took
        # every excusing entry rather than its own would copy alpha's reason into
        # beta's file. The case above cannot catch that -- it runs at a third
        # digest, where there is nothing to copy.
        _write(root, "src/thing.fs", "let a = 2\n")
        grandfather(root, "item-beta", "selftest: beta re-runs while ALPHA is excusing")
        check(
            "a file emptied by its own cycle stays, because something may cite it",
            os.path.isfile(ledger_path(root, beta)),
        )
        check(
            "a rewrite does not adopt another cycle's APPLYING entry",
            evaluate(root)["ok"]
            and _entries_or_missing(root, beta) == []
            and [entry["reason"] for entry in _entries_or_missing(root, alpha)]
            == ["selftest: alpha excused it"],
        )
    finally:
        shutil.rmtree(root, ignore_errors=True)

    # --- NOTHING but this cycle's own file is ever written -------------------
    #
    # The hard rule. A remedy that touches a foreign ledger file is a path no
    # item can declare -- rogue3#53 verbatim -- and one that can DELETE a file a
    # merged audit cites strands that audit with no way back, which is rogue3#38
    # at a new address and worse, because the gate would stay green while the
    # feedback validator stayed red.
    root = _stale_binding_tree("audit-bindings-selftest-no-foreign-write-")
    try:
        grandfather(root, "item-alpha", "selftest: alpha excused it")
        alpha = cycle_ledger_relpath("item-alpha")
        # An audit whose finding is ABOUT alpha's excuse cites alpha's file. The
        # kit's SKILL.md teaches exactly this citation.
        _make_audit(root, "cycle-cites-alpha", "feedback/cycle-cites-alpha.md", [alpha])
        # Restoring the bytes the audit pins makes alpha's entry excuse nothing.
        _write(root, "src/thing.fs", "let a = 1\n")
        stranded = evaluate(root)
        check(
            "an obsolete entry is reported, and is NOT a failure",
            stranded["ok"]
            and len(stranded["obsoleteExceptions"]) == 1
            and stranded["obsoleteExceptions"][0]["sourceFile"] == alpha
            and stranded["dormant"] == 1,
        )

        before = {rel: digest_file(ledger_path(root, rel)) for rel in ledger_files(root)}
        outcome = grandfather(root, "item-beta", "selftest: beta must not touch alpha's file")
        check(
            "another cycle's obsolete entry is left exactly where it is",
            digest_file(ledger_path(root, alpha)) == before[alpha],
        )
        check(
            "a cited cycle file is never deleted by another cycle's remedy",
            os.path.isfile(ledger_path(root, alpha)) and evaluate(root)["ok"],
        )
        check(
            "the dormant entries left alone are named, never silently skipped",
            outcome["dormantElsewhere"] == [alpha],
        )
        check(
            "a cycle with nothing to excuse writes no file at all",
            outcome["written"] is None
            and not os.path.exists(ledger_path(root, cycle_ledger_relpath("item-beta"))),
        )
        # And the cycle that OWNS the dormant entry prunes it by running its own
        # remedy -- the only route, and one command.
        pruned = grandfather(root, "item-alpha", "selftest: alpha prunes its own dormant entry")
        check(
            "the owning cycle prunes its own dormant entry in one command",
            pruned["checkOk"]
            and pruned["pruned"] == 1
            and _load_ledger_file(root, alpha) == []
            and os.path.isfile(ledger_path(root, alpha)),
        )

        # An EMPTY foreign file is the one most tempting to tidy away, and the
        # one whose deletion strands a merged audit for good: `cycle-cites-alpha`
        # above cites this exact path. Another cycle's remedy must leave it.
        _write(root, "src/thing.fs", "let a = 5\n")
        grandfather(root, "item-beta", "selftest: beta runs beside an EMPTY foreign file")
        check(
            "an EMPTY foreign ledger file is not tidied away by another cycle",
            os.path.isfile(ledger_path(root, alpha)) and evaluate(root)["ok"],
        )
    finally:
        shutil.rmtree(root, ignore_errors=True)

    # --- the frozen archive is never written, in any state -------------------
    root = _stale_binding_tree("audit-bindings-selftest-archive-")
    try:
        result = evaluate(root)
        violation = result["violations"][0]
        archive_note = "FROZEN ARCHIVE. Do not add entries here."
        _write(
            root,
            LEGACY_LEDGER_RELPATH,
            json.dumps(
                {
                    "grandfatherSchema": 1,
                    "note": archive_note,
                    "entries": [
                        {
                            "audit": violation["audit"],
                            "kind": violation["kind"],
                            "locator": violation["locator"],
                            "boundSha256": violation["boundSha256"],
                            "observedSha256": violation["observedSha256"],
                            "reason": "selftest: excused before the migration",
                        }
                    ],
                },
                indent=2,
            )
            + "\n",
        )
        archive_before = digest_file(ledger_path(root, LEGACY_LEDGER_RELPATH))
        # Retire the archive's only entry, so a prune would have emptied and
        # deleted the file, taking its schema and its note with it.
        _write(root, "src/thing.fs", "let a = 1\n")
        after_revert = evaluate(root)
        check(
            "an obsolete entry in the ARCHIVE is reported and is not a failure",
            after_revert["ok"]
            and [e["sourceFile"] for e in after_revert["obsoleteExceptions"]]
            == [LEGACY_LEDGER_RELPATH],
        )
        grandfather(root, "item-alpha", "selftest: must not touch the archive")
        check(
            "--grandfather never writes the frozen archive, even to prune it",
            os.path.isfile(ledger_path(root, LEGACY_LEDGER_RELPATH))
            and digest_file(ledger_path(root, LEGACY_LEDGER_RELPATH)) == archive_before,
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

        # Renaming a ledger file is an ordinary thing to do -- a cycle-id typo,
        # a board renumber -- and must not brick the tool. It cannot, because the
        # filename IS the cycle and nothing inside the file restates it.
        renamed = f"{LEDGER_DIR}/item-alpha-renamed.json"
        os.replace(ledger_path(root, alpha), ledger_path(root, renamed))
        check(
            "renaming a ledger file changes nothing about the verdict",
            evaluate(root)["ok"] and evaluate(root)["ledgerFiles"] == [renamed],
        )
        os.replace(ledger_path(root, renamed), ledger_path(root, alpha))

        entry = {k: v for k, v in entries[0].items() if k != "sourceFile"}

        def rejected(doc: Any) -> bool:
            _write(root, alpha, json.dumps(doc, indent=2) + "\n")
            try:
                evaluate(root)
            except UsageError:
                return True
            return False

        # Two entries for the same binding at the same digest in ONE file is a
        # broken write, not the concurrency the union absorbs.
        check(
            "a duplicate entry within one cycle file is rejected",
            rejected({"grandfatherSchema": LEDGER_SCHEMA, "entries": [entry, dict(entry)]}),
        )
        # A ledger whose shape cannot be read excuses nothing and would otherwise
        # pass silently -- the same failure the audit-shape checks exist for.
        check(
            "a ledger whose 'entries' is not a list is rejected",
            rejected({"grandfatherSchema": LEDGER_SCHEMA, "entries": {"a": entry}}),
        )
        check(
            "a ledger whose root is not an object is rejected",
            rejected([entry]),
        )
        check(
            "a ledger entry that is not an object is rejected",
            rejected({"grandfatherSchema": LEDGER_SCHEMA, "entries": ["not an object"]}),
        )
        # Every one of those messages must name the way out, or a corrupt ledger
        # file is a dead end the reader has to guess their way off.
        for doc in ("{not json\n",):
            _write(root, alpha, doc)
            message = ""
            try:
                evaluate(root)
            except UsageError as error:
                message = str(error)
            check(
                "an unreadable ledger file names both repairs",
                "DELETE the file" in message and "repair the JSON" in message,
            )
    finally:
        shutil.rmtree(root, ignore_errors=True)

    # --- the ledger's write shape is canonical, so a diff is a real diff ------
    root = _stale_binding_tree("audit-bindings-selftest-write-shape-")
    try:
        _write(root, "src/b.fs", "let b = 1\n")
        _write(root, "src/a.fs", "let a = 1\n")
        _make_audit(root, "cycle-2", "feedback/cycle-2.md", ["src/b.fs", "src/a.fs"])
        _write(root, "src/b.fs", "let b = 2\n")
        _write(root, "src/a.fs", "let a = 2\n")
        grandfather(root, "item-alpha", "selftest: write shape")
        alpha = cycle_ledger_relpath("item-alpha")
        written = _load_ledger_file(root, alpha)
        # Fed in REVERSED, so insertion order and canonical order differ: reading
        # back whatever order it was given would pass a check that only asserts
        # the file agrees with itself.
        write_ledger(root, alpha, list(reversed(written)))
        reread = _load_ledger_file(root, alpha)
        check(
            "entries are written in canonical key order, not insertion order",
            len(reread) > 1
            and [_entry_sort_key(e) for e in reread]
            == sorted(_entry_sort_key(e) for e in written),
        )
        # `carriedForward` is reported, so it has to be true: a re-run that
        # changes nothing must report every entry as carried, not as rewritten.
        again = grandfather(root, "item-alpha", "selftest: a different reason entirely")
        check(
            "an unchanged re-run carries every entry forward verbatim",
            again["carriedForward"] == len(written)
            and again["adopted"] == len(written)
            and all(
                entry["reason"] == "selftest: write shape"
                for entry in _load_ledger_file(root, alpha)
            ),
        )
        # A deleted bound file is excusable, but only by an entry that observed
        # its ABSENCE -- an entry pinned at some earlier digest must not excuse
        # a file that is now gone, and one pinned at <missing> must not excuse a
        # file that came back.
        os.remove(os.path.join(root, "src", "a.fs"))
        deleted = evaluate(root)
        check(
            "an entry pinned at a digest does NOT excuse the file's disappearance",
            not deleted["ok"]
            and any(v["observedSha256"] == MISSING for v in deleted["violations"]),
        )
        grandfather(root, "item-alpha", "selftest: excuse the deletion")
        check("a deleted bound file is excused at <missing>", evaluate(root)["ok"])
        _write(root, "src/a.fs", "let a = 3\n")
        check(
            "an entry that observed <missing> does not excuse a file that came back",
            not evaluate(root)["ok"],
        )
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

        # 5. restoring the bytes makes the binding fresh, and the exception
        #    obsolete: reported, counted, and NOT a failure -- an obsolete entry
        #    usually sits in a merged cycle's file, and a fatal condition only a
        #    finished cycle could clear is a gate with no bounded route to green.
        _write(root, "src/thing.fs", "let a = 1\n")
        r = evaluate(root)
        check(
            "an exception that no longer excuses anything is obsolete, and is reported",
            len(r["obsoleteExceptions"]) == 1 and not r["violations"],
        )
        check("an obsolete exception does not fail the check", r["ok"] and r["dormant"] == 1)

        # 6. --grandfather prunes obsolete entries out of its OWN file
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
            len(r["obsoleteExceptions"]) == 1 and not r["violations"],
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
            if outcome["written"] is None:
                print(
                    "audit-bindings: nothing to excuse; no exceptions file written for "
                    f"{outcome['cycle']}."
                )
            else:
                print(
                    f"audit-bindings: wrote {outcome['written']} with {outcome['entries']} "
                    f"exception(s); pruned {outcome['pruned']}."
                )
            if outcome["adopted"]:
                print(
                    f"audit-bindings: {outcome['adopted']} exception(s) were already in that file "
                    f"-- this run ADOPTED {outcome['cycle']}'s file. Correct when this cycle is "
                    "re-running its own remedy; check the diff if the id was reused."
                )
            if outcome["dormantElsewhere"]:
                print(
                    "audit-bindings: dormant exception(s) remain in "
                    + ", ".join(outcome["dormantElsewhere"])
                    + " -- left untouched deliberately: this command writes no file but its own."
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
        # The remedy reports the VERDICT, not the fact that it wrote a file
        # (rogue3#38, feedback/2026-08-02-Rogue3-9.md 4.2). `checkOk` is the whole
        # verdict: anything the rewrite could not clear -- a malformed audit, an
        # audit pinning no digest -- is still a violation when it re-evaluates,
        # so a second `and not notExcusable` term would be unreachable and would
        # read as a guard that is doing work.
        return 0 if outcome["checkOk"] else 1

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
