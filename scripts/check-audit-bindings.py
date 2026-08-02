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
       python3 scripts/check-audit-bindings.py --grandfather --reason "<why>"
     which rewrites the ledger from the current violations and prunes obsolete
     entries.  The diff is the record of what was excused and why.
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

NOT BOUND: the excuse ledger
----------------------------

Remedy (1) WRITES the exceptions ledger.  So a binding whose target is the
ledger itself has no fixed point, and the gate was asking for the impossible
(rogue3#38): an audit that cites `scripts/audit-binding-exceptions.json` --
reasonable when the finding is *about* the ledger -- goes stale the moment
anything is excused.  Excusing that binding writes the ledger, which changes the
ledger, which invalidates the excuse just written.  Reproduced at d8d0024: four
consecutive `--grandfather` runs produced four distinct ledger digests and
`check` still exited 1.

So a citation onto the ledger is NOT checked.  It is not silently dropped:
`evaluate` returns it under `notBound`, the text report names it, `--json`
carries it and counts it, and the summary and verdict lines both say how many
citations were not bound -- so a reader can always tell the difference between
"this citation is exempt" and "the checker missed it".

NOT BOUND: the derived readiness roll-ups
-----------------------------------------

The OTHER exemption (rogue3#56).  `readiness/evidence-graph.md`,
`readiness/performance-evidence.json` and `readiness/m7-ui-performance.json` are
outputs of the merge gate over inputs no checkout can reproduce -- the graph
enumerates a tree that is partly regenerable output, and the other two carry
measured timings, allocations and digests over them.  rogue3#56 removed all three
from the index, so an audit that cited one of them pinned a sha256 over a run
output.

Unlike an ordinary deleted file, the ledger cannot settle these, because they do
not observe the same thing twice: in a checkout that has run the gate the file is
PRESENT with churning bytes, and in one that has not it is ABSENT.  An exception
pins exactly one observed digest, so an entry written from a developer's tree
fails in CI and an entry written from CI fails on the developer's tree.  Measured
on the FIRST commit of rogue3#56 -- db5ee2e, which untracked the artifacts and left
this checker unchanged: `check` reported 22 stale bindings in a worktree that had
just run the gate, and the four bindings onto `readiness/evidence-graph.md` were
among the FRESH ones, because the publication rule had restored the exact bytes the
file carried while tracked.  Fresh in the very checkout where CI was about to read
`<missing>`.  (With this exemption in place the same tree reports 17 stale and 19
not bound.  22 is the pre-fix number and belongs to that commit, not to the
finished candidate -- said explicitly because an unattributed measurement in a
comment is how the previous cycle's figures went stale.)

So these citations are reported, not checked, through the same `notBound` channel
as the ledger.  The set is ENUMERATED in `DERIVED_ROLLUP_RELPATHS` and each member
must ALSO be declared in `.gitignore`; a CITED path the constant calls derived
while `.gitignore` does not declare it is a structural violation, not a silent
pass, and a `!` negation withdraws the declaration exactly as deleting the line
does.  That coupling catches a rule removed or negated.  It does NOT catch a
RE-TRACKED artifact: `.gitignore` has no effect on a file already in the index, and
nothing here consults git -- a checker that asked git what is tracked would exempt
everything in an export with no repository, and a check that can vanish is the
failure mode this file exists to catch.  The index check lives in CI instead, in
`.github/workflows/verify.yml` under "Derived roll-ups stay out of the index".

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
    "at ONE observed digest: change the file again and it fails again. Adding "
    "an entry here is the PREFERRED remedy; rebinding a MERGED audit rewrites "
    "what that audit records its critic as having verified (see 7e71d71) and "
    "is right only for an audit you re-verified yourself."
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


# The ONE path this checker must write in order to clear a violation. Binding it
# is a fixed-point equation with no fixed point -- see the module docstring and
# rogue3#38.
LEDGER_EXEMPTION = (
    "this is the exceptions ledger itself: the only place an excuse can live, so "
    "excusing a binding on it rewrites it and invalidates the excuse just written"
)

GITIGNORE_RELPATH = ".gitignore"

DERIVED_EXEMPTION = (
    "this is a readiness ROLL-UP the repository deliberately does not track "
    "(rogue3#56): the bytes are a run output, so no checkout can be asked to hold "
    "the digest a merged audit pinned over them"
)

# rogue3#56. Each of these is written by the merge gate over inputs that are not
# reproducible from a checkout -- the evidence graph enumerates a partly-untracked
# tree, and the other three carry measured timings, allocations and digests over
# them. They were removed from the index in that item, so a digest binding onto one
# of them can never go fresh again and, worse, does not even give the SAME answer
# twice: in a checkout that has run the gate the file is present with churning
# bytes, and in one that has not it is absent entirely. An excuse pins exactly one
# observed digest, so the ledger cannot settle a path that observes two different
# things depending on who is looking. They are reported instead, through the same
# `notBound` channel the ledger uses.
DERIVED_ROLLUP_RELPATHS: dict[str, str] = {
    "readiness/evidence-graph.md": DERIVED_EXEMPTION,
    "readiness/performance-evidence.json": DERIVED_EXEMPTION,
    "readiness/m7-ui-performance.json": DERIVED_EXEMPTION,
}


def gitignore_declarations(root: str) -> set[str]:
    """The paths the workspace `.gitignore` declares ignored, as written.

    Comments and blanks are dropped, and a `!path` NEGATION removes `path` from the
    result rather than being returned as a literal. Without that, negating a rule --
    one `!` line, which un-ignores the artifact and is what a reader would reach for
    to put it back -- would leave the positive rule in the set and the exemption
    standing over a file the repository has resumed treating as ordinary.

    This matches whole lines only. It is a declaration reader, not a gitignore
    matcher: it answers "does this file say this exact path is ignored", which is
    the question `derived_exemptions` asks, and it deliberately does not try to
    resolve globs, directory rules or precedence.

    Where it and git DISAGREE, it disagrees conservatively, and that is chosen
    rather than accidental. Real gitignore precedence is order-dependent and
    last-match-wins, while this is order-independent set subtraction, so
    `!readiness/x` followed by `readiness/x` is IGNORED by git and NOT DECLARED
    here -- verified both ways on this tree. The result is that the exemption is
    withdrawn and a structural violation is raised over a file git really does
    ignore: the checker goes on checking a binding it could have skipped, which
    costs a stale binding and one obvious fix (drop the stray `!`). The opposite
    error -- exempting a path the repository has quietly resumed treating as
    ordinary -- is the one that loses evidence silently, so the ambiguity is
    resolved towards more checking every time.
    """
    path = os.path.join(root, GITIGNORE_RELPATH)
    if not os.path.isfile(path):
        return set()
    with open(path, "rb") as handle:
        raw = handle.read().decode("utf-8-sig", errors="replace")
    raw = raw.replace("\r\n", "\n").replace("\r", "\n")

    positive: set[str] = set()
    negated: set[str] = set()
    for line in raw.split("\n"):
        line = line.strip()
        if not line or line.startswith("#"):
            continue
        if line.startswith("!"):
            negated.add(line[1:].strip())
        else:
            positive.add(line)
    return positive - negated


def derived_exemptions(root: str) -> tuple[dict[str, str], list[str]]:
    """The derived roll-ups this workspace actually declares, and those it does not.

    The exemption is keyed on `.gitignore` rather than on this list alone, because
    a hard-coded list is a hole that widens silently: anyone could add a path to it
    and a stale binding would stop being reported. Requiring the repository to say
    the same thing in the file that DECLARES it a run output means the two statements
    have to agree, and a path that stops being ignored -- by deletion or by an `!`
    negation -- stops being exempt and is named as a structural violation instead of
    quietly keeping its pass.

    What this does NOT do, stated because the obvious reading is wrong: it does not
    detect that the artifact has been RE-TRACKED. A `.gitignore` rule has no effect
    on a file already in the index, so `git add -f` puts the bytes back under version
    control with every line here unchanged and this exemption still standing. That
    check cannot live here, because it needs the index -- and a checker that shelled
    out to git would answer "nothing is tracked" in an export with no repository and
    exempt every binding in the gate, a vanishing check being the failure mode one
    level up from the one this whole file exists to catch. It lives in CI instead:
    `.github/workflows/verify.yml`, "Derived roll-ups stay out of the index".
    """
    declared = gitignore_declarations(root)
    live = {rel: why for rel, why in DERIVED_ROLLUP_RELPATHS.items() if rel in declared}
    undeclared = sorted(rel for rel in DERIVED_ROLLUP_RELPATHS if rel not in declared)
    return live, undeclared


def exemption(rel: str, derived: dict[str, str] | None = None) -> str | None:
    """Why `rel` cannot be bound, or None when it is an ordinary file.

    Takes the WORKSPACE-RELATIVE path derived from the RESOLVED location, not
    the locator text, so `file:feedback/../scripts/audit-binding-exceptions.json`
    is recognised as the ledger too. A textual match on the locator would let a
    one-token rewrite reintroduce the unsatisfiable binding this exempts.

    Two exemptions, both ENUMERATED paths rather than a shape: the ledger, and the
    derived readiness roll-ups `derived` names (see `derived_exemptions`, which
    decides that set from `.gitignore` and not from a bare constant). In particular
    a citation onto another `*.audit.json` is still NOT exempt: see the module
    docstring for why the audit-to-audit exemption is wrong. `derived` defaults to
    empty, so a caller that forgets it under-exempts -- which is a loud stale
    binding, never a silent pass.
    """
    if rel == LEDGER_RELPATH:
        return LEDGER_EXEMPTION
    if derived and rel in derived:
        return derived[rel]
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
    derived, undeclared_derived = derived_exemptions(root)

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

    # Every `file:` path any audit resolves to, so the `.gitignore` coupling below can
    # be raised only where it can actually matter.
    cited_rels: set[str] = set()

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
                resolved_rel = _rel(root_real, resolved)
                cited_rels.add(resolved_rel)
                why = exemption(resolved_rel, derived)
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
                resolved_rel = _rel(root_real, resolved)
                cited_rels.add(resolved_rel)
                why = exemption(resolved_rel, derived)
                if why is not None:
                    note_exempt(audit_rel, "evidence", locator, rel, why)
                    continue
                bindings.append(
                    Binding(
                        audit_rel, "evidence", f"file:{rel}", rel, resolved,
                        _sha(check.get("sha256")),
                    )
                )

    # rogue3#56: the constant and `.gitignore` must say the same thing about a path
    # some audit actually cites. Raised only for a CITED path, because that is the
    # only place the disagreement can change a verdict -- raising it for a path no
    # audit mentions would fail every workspace that does not happen to carry this
    # repository's ignore rules, including a fresh scaffold, and a gate that is red
    # everywhere is a gate nobody runs. It is structural for the same reason a
    # malformed audit is: it pins no digest, so the ledger has nothing to excuse it
    # against. Bounded route to green in both directions -- put the ignore rule back,
    # or drop the path from DERIVED_ROLLUP_RELPATHS and let its bindings be checked.
    for rel in undeclared_derived:
        if rel in cited_rels:
            bad(
                "<check-audit-bindings.py>",
                f"file:{rel}",
                f"DERIVED_ROLLUP_RELPATHS calls this a run output, but {GITIGNORE_RELPATH} "
                "does not declare it, so the repository may be tracking bytes this checker "
                "was written to stop checking. Restore the ignore rule, or remove the path "
                "from DERIVED_ROLLUP_RELPATHS",
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

    not_bound = result.get("notBoundCitations", [])

    # The not-bound count belongs on the summary line: without it, four
    # citations simply vanish between `main` and a branch and a reader diffing
    # CI logs sees the binding count drop with no accounting.
    print(
        "audit-bindings: {audits} audits, {bindings} bindings, {fresh} fresh, "
        "{excused} explicitly excused, {notBoundCount} not bound".format(
            notBoundCount=len(not_bound), **result
        ),
        file=stream,
    )

    if not_bound:
        print(
            f"\naudit-bindings: {len(not_bound)} citation(s) NOT BOUND -- the repository "
            "cannot hold\nthe bytes these pin, so they are reported rather than checked "
            "(reason per group):",
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
                for entry in evaluate(root).get("notBoundCitations", [])
            ),
        )

        # The exemption is decided on the RESOLVED path, so a traversing locator
        # cannot smuggle the unsatisfiable binding back in.
        _make_audit(root, "cycle-dots", "feedback/cycle-dots.md", ["feedback/../" + LEDGER_RELPATH])
        check(
            "a traversing locator onto the ledger is exempt too",
            any(
                entry["reason"] == LEDGER_EXEMPTION and ".." in entry["locator"]
                for entry in evaluate(root).get("notBoundCitations", [])
            ),
        )
        grandfather(root, "selftest: ledger convergence, after traversal")
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
        # This is why an audit-to-audit exemption is unnecessary: excusing writes
        # only the ledger, which is exempt, so there is nothing left to chase.
        grandfather(root, "selftest: audit-to-audit settles via the ledger")
        first = digest_file(ledger_path(root))
        check("an audit-to-audit violation settles in one --grandfather pass", evaluate(root)["ok"])
        grandfather(root, "selftest: audit-to-audit, second pass")
        check(
            "and stays settled -- the ledger is byte-identical on the next pass",
            evaluate(root)["ok"] and digest_file(ledger_path(root)) == first,
        )

        # Mutual citation A<->B, the shape called unsatisfiable. Rebind A, which
        # strands B; one pass settles it, and it stays settled.
        _make_audit(root, "cycle-c", "feedback/cycle-c.md", ["feedback/audits/cycle-b.audit.json"])
        _make_audit(root, "cycle-b", "feedback/cycle-b.md", ["feedback/audits/cycle-c.audit.json"])
        _make_audit(root, "cycle-c", "feedback/cycle-c.md", ["feedback/audits/cycle-b.audit.json"])
        check("mutual citation is red before it is excused", not evaluate(root)["ok"])
        grandfather(root, "selftest: mutual citation")
        settled_ledger = digest_file(ledger_path(root))
        grandfather(root, "selftest: mutual citation, again")
        check(
            "two audits citing each other converge in one pass and stay converged",
            evaluate(root)["ok"] and digest_file(ledger_path(root)) == settled_ledger,
        )

        # A cited audit that does not EXIST must still fail. The exemption fires
        # before the existence check, so exempting audits would turn a dangling
        # or typo'd cross-reference silently green.
        _make_audit(root, "cycle-missing", "feedback/cycle-missing.md", ["feedback/audits/cycle-a.audit.json"])
        grandfather(root, "selftest: before the dangling-reference probe")
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
        _make_audit(real, "cycle-ledger", "feedback/cycle-ledger.md", [LEDGER_RELPATH])
        _write(real, "src/thing.fs", "let a = 2\n")

        grandfather(link, "selftest: symlinked root, first pass")
        through_link = evaluate(link)
        check(
            "the ledger citation is exempt through a symlinked root",
            any(
                entry["locator"] == f"file:{LEDGER_RELPATH}"
                for entry in through_link.get("notBoundCitations", [])
            ),
        )
        before = digest_file(ledger_path(real))
        grandfather(link, "selftest: symlinked root, second pass")
        check(
            "a symlinked root converges exactly as a real one does",
            evaluate(link)["ok"] and digest_file(ledger_path(real)) == before,
        )
    finally:
        shutil.rmtree(outer, ignore_errors=True)

    selftest_derived_rollups(check)


def selftest_derived_rollups(check) -> None:
    """rogue3#56: a binding onto an untracked readiness roll-up is reported, not checked.

    The claim these cases defend is narrow and easy to over-apply: the exemption is
    an ENUMERATED set that `.gitignore` has to agree with, and it must give the SAME
    verdict whether or not the checkout happens to hold the run output. Cases that
    only proved "the path is exempt" would still pass if someone widened it to all
    of `readiness/`, or dropped the `.gitignore` coupling, or let the verdict depend
    on whether the file was on disk -- so each of those is a case of its own.
    """
    derived = sorted(DERIVED_ROLLUP_RELPATHS)[0]
    other_derived = sorted(DERIVED_ROLLUP_RELPATHS)[1]
    root = tempfile.mkdtemp(prefix="audit-bindings-selftest-derived-")
    try:
        _write(root, ".gitignore", "bin/\n" + "".join(f"{rel}\n" for rel in DERIVED_ROLLUP_RELPATHS))
        _empty_ledger(root)
        # The roll-up EXISTS while the audit is made, exactly as it does in a
        # checkout that just ran the gate, so the audit pins a real digest.
        _write(root, derived, "sensed: 1\n")
        _write(root, "readiness/performance-intent.yml", "performanceIntent:\n  id: PI\n")
        _make_audit(root, "cycle-derived", "feedback/cycle-derived.md", [derived, "readiness/performance-intent.yml"])

        present = evaluate(root)
        check(
            "a citation onto a declared derived roll-up is reported as not bound",
            any(
                entry["locator"] == f"file:{derived}" and entry["reason"] == DERIVED_EXEMPTION
                for entry in present.get("notBoundCitations", [])
            ),
        )
        check(
            "an ordinary readiness file beside it is still CHECKED, not swept up",
            any(b.path == "readiness/performance-intent.yml" for b in collect_bindings(root)[0]),
        )
        check("the tree with a present derived roll-up is green", present["ok"])

        # The same commit, seen from a checkout that never ran the gate. The verdict
        # must not move -- that divergence is the reason the ledger cannot settle
        # these, and a rule that only handled one of the two states would be worse
        # than no rule, because it would be green exactly where it was tested.
        os.remove(os.path.join(root, *derived.split("/")))
        absent = evaluate(root)
        check(
            "the same citation is reported identically when the roll-up is ABSENT",
            absent["ok"]
            and [entry["locator"] for entry in absent.get("notBoundCitations", [])]
            == [entry["locator"] for entry in present.get("notBoundCitations", [])],
        )

        # Changing the bytes is what a gate run does. It must not matter either.
        _write(root, derived, "sensed: 2\n")
        check("changed bytes on a derived roll-up are still not a violation", evaluate(root)["ok"])

        # Resolved-path rule, as for the ledger: a traversing locator is the same file.
        _make_audit(root, "cycle-dots", "feedback/cycle-dots.md", ["feedback/../" + derived])
        check(
            "a traversing locator onto a derived roll-up is exempt too",
            any(
                entry["reason"] == DERIVED_EXEMPTION and ".." in entry["locator"]
                for entry in evaluate(root).get("notBoundCitations", [])
            ),
        )

        # A near-miss must NOT be exempt: the set is exact paths, not prefixes.
        _write(root, derived + ".bak", "sensed: 1\n")
        _make_audit(root, "cycle-bak", "feedback/cycle-bak.md", [derived + ".bak"])
        _write(root, derived + ".bak", "sensed: 9\n")
        near = evaluate(root)
        check(
            "a path that merely RESEMBLES a derived roll-up is checked and can go stale",
            not near["ok"] and any(v["locator"] == f"file:{derived}.bak" for v in near["violations"]),
        )
        grandfather(root, "selftest: derived roll-ups, clearing the near-miss")
        check("the near-miss settles through the ledger like any other file", evaluate(root)["ok"])
    finally:
        shutil.rmtree(root, ignore_errors=True)

    # --- the .gitignore coupling: the constant alone must not exempt anything ---
    root = tempfile.mkdtemp(prefix="audit-bindings-selftest-derived-ignore-")
    try:
        # Every derived path declared EXCEPT one. The odd one out is the case: the
        # constant still names it, so if the exemption read the constant alone it
        # would stay exempt and the repository could track it with nothing checking.
        _write(
            root,
            ".gitignore",
            "".join(f"{rel}\n" for rel in DERIVED_ROLLUP_RELPATHS if rel != other_derived),
        )
        _empty_ledger(root)
        _write(root, other_derived, "measured: 1\n")
        _make_audit(root, "cycle-tracked", "feedback/cycle-tracked.md", [other_derived])

        live, undeclared = derived_exemptions(root)
        check("a derived path missing from .gitignore is not in the live exempt set", other_derived not in live)
        check("...and is named as undeclared", undeclared == [other_derived])

        r = evaluate(root)
        check(
            "an undeclared derived path is a STRUCTURAL violation, not a silent pass",
            not r["ok"]
            and any(
                v["kind"] == "structure" and v["locator"] == f"file:{other_derived}"
                for v in r["violations"]
            ),
        )
        check(
            "...and its citation is checked again rather than reported as not bound",
            all(
                entry["locator"] != f"file:{other_derived}"
                for entry in r.get("notBoundCitations", [])
            ),
        )

        # The structural violation must not be excusable, or the coupling is
        # decorative: --grandfather would paper over a re-tracked artifact.
        grandfather(root, "selftest: derived roll-ups, undeclared path")
        check(
            "--grandfather cannot excuse an undeclared derived path",
            not evaluate(root)["ok"],
        )

        # Declaring it again is the bounded route to green, and it is the ONLY
        # change required.
        _write(root, ".gitignore", "".join(f"{rel}\n" for rel in DERIVED_ROLLUP_RELPATHS))
        check("restoring the .gitignore line clears it", evaluate(root)["ok"])

        # A commented-out ignore rule is not a declaration. Both halves matter, and
        # the second is the one with teeth: asserting only that the path is
        # undeclared would pass even with the comment filter removed, because
        # "# readiness/x" never equals "readiness/x" under an equality test. The
        # filter earns its place only if NOTHING comment-shaped survives into the
        # declaration set, so that is what is asserted.
        _write(
            root,
            ".gitignore",
            "".join(
                (f"# {rel}\n" if rel == other_derived else f"{rel}\n")
                for rel in DERIVED_ROLLUP_RELPATHS
            ),
        )
        check("a commented-out ignore rule does not declare the path", not evaluate(root)["ok"])
        check(
            "...and no comment line reaches the declaration set at all",
            not any(line.startswith("#") for line in gitignore_declarations(root)),
        )

        # An `!` NEGATION withdraws the declaration. This is the one-line mutant: the
        # positive rule is still present and still says the path is ignored, but git
        # no longer ignores it, and neither may this checker.
        _write(
            root,
            ".gitignore",
            "".join(f"{rel}\n" for rel in DERIVED_ROLLUP_RELPATHS) + f"!{other_derived}\n",
        )
        check(
            "an ! negation removes the path from the declaration set",
            other_derived not in gitignore_declarations(root),
        )
        negated = evaluate(root)
        check(
            "a negated declaration withdraws the exemption and is a structural violation",
            not negated["ok"]
            and any(
                v["kind"] == "structure" and v["locator"] == f"file:{other_derived}"
                for v in negated["violations"]
            ),
        )
        check(
            "a negation does not disturb the OTHER declared roll-ups",
            all(rel in gitignore_declarations(root) for rel in DERIVED_ROLLUP_RELPATHS if rel != other_derived),
        )

        # And a workspace with no .gitignore at all exempts NOTHING -- the default
        # direction of every unknown here is "keep checking".
        os.remove(os.path.join(root, ".gitignore"))
        live, undeclared = derived_exemptions(root)
        check(
            "a workspace with no .gitignore exempts no derived path",
            not live and undeclared == sorted(DERIVED_ROLLUP_RELPATHS),
        )
        check(
            "exemption() called without the derived set never exempts a roll-up",
            exemption(derived) is None and exemption(derived, {}) is None,
        )
        check(
            "the ledger exemption does not depend on the derived set",
            exemption(LEDGER_RELPATH) == LEDGER_EXEMPTION,
        )
    finally:
        shutil.rmtree(root, ignore_errors=True)

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
            ("feedback/audits/notes.md", "a non-audit file under feedback/audits/ is still bound"),
        ):
            _write(root, rel, "one\n")
            _make_audit(root, "probe", "feedback/probe.md", [rel])
            grandfather(root, "selftest: before the narrowness probe")
            _write(root, rel, "two\n")
            r = evaluate(root)
            check(label, not r["ok"] and any(v["locator"] == f"file:{rel}" for v in r["violations"]))
            grandfather(root, "selftest: after the narrowness probe")

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
