# Quantified claims: name the revision you measured at

## The rule

> A figure produced by running something in this repository is not a fact about the repository. It
> is a fact about **one tree**. Quote the command that printed it and name the revision it was
> measured at, or do not quote the figure.

And its other half, which is the same mistake seen from the other side:

> **When a document and a tool disagree about a count, the document is usually counting and the tool
> is usually reporting.** Quote the tool's own output rather than describing it — and if no tool
> prints the number, that absence is itself worth a sentence.

The two are one rule. The first says *when* a number was true; the second says *what produced it*.
A figure missing either is a figure the next reader has to derive again before using any of it, and
"has to re-derive all of it" is the recorded cost both times this happened (`rogue3#81`).

## Where it applies

- **Item bodies.** The grammar below.
- **PR bodies and issue comments.** Same rule, same line. A comment adding a figure to an item that
  already carries one is the case that goes wrong most often — see *Pinning is necessary, not
  sufficient*.
- **Cycle reports.** Already covered elsewhere: the `fs-gg-feedback-report` finding shape has a
  required `Version:` field ("reproduced package/tool version and current version checked, or
  `n/a`") and requires each `Evidence:` locator to let another person inspect or reproduce the
  observation. That is this rule, already enforced by the report validator. Item bodies had no
  equivalent, and that asymmetry is what this page closes. `rogue3#89` carries the second half of
  the rule above into the same skill; this page and that item state one rule, not two.

## The grammar

Write a `Measured-at:` line as a top-level line in the body, beside `Paths:` and `Class:`:

```
Measured-at: <revision> — <the command that printed the figure>
```

One line per command. If a body quotes figures from three commands, it carries three lines.

**Required** whenever the body quotes a number produced by running something in this repository.
**Not required otherwise** — this must not become a field every item has to carry. An item that
quotes no measured figure carries no `Measured-at:` line, and that is correct, not an omission.

What counts as a revision depends on what the command read:

| the command reads | pin | example |
|---|---|---|
| the working tree, or anything in it | a commit sha | `Measured-at: 715bef9 — python3 scripts/check-audit-bindings.py --json` |
| the board (Projects, issues, claims) | a UTC timestamp, because **the board has no revision** | `Measured-at: board 2026-08-02T18:40Z — scripts/fsgg-coord ready --repo rogue3 --json` |
| an installed package or tool | the version string | `Measured-at: FS.GG.UI 0.21.1 — dotnet list package` |
| nothing — you counted it yourself | say so, in place of the command | `Measured-at: 14fd9eb — counted by hand from scripts/audit-binding-exceptions/` |

The last row is the point of the second half of the rule, not an escape from it. "Counted by hand"
is an honest and useful thing for a body to say; a hand count presented as a tool's output is not.

Check a sha before you write it: a commit on a branch that is later rebased away can end up on no
ref at all, and a pin nobody can resolve is worse than no pin.

```sh
git branch -a --contains <sha>     # non-empty, or the pin will not survive
```

## The check a reader runs

The pin exists so that a reader can settle the figure without asking the author:

```sh
git archive <revision> | tar -x -C <dir>
cd <dir> && <the quoted command>
```

Measure in that **clean export, not in a working tree**. Outputs written by your own earlier
commands are frequently gitignored, so they do not show up in `git status` and silently change what
the next command reports.

Read the exit code directly. `<cmd> | tail` reports `tail`'s exit status, not the command's.

## Pinning is necessary, not sufficient

A correctly pinned figure still rots — it just rots visibly. `rogue3#57`'s body pinned its sweep to
a `git archive` of `bfd1288`, which is exactly this discipline, and it still misled: the area was
rewritten before the item was implemented, and a later comment quoted a third figure from a third
base. Three pinned numbers sat in one item as though they were comparable
(`feedback/2026-08-02-Rogue3-17.md` §4.7).

So the pin carries two obligations beyond writing it:

1. **When you add a figure, reconcile the ones already there.** Re-derive the older figure at your
   revision, or strike it. Never leave figures from different trees side by side as if they were a
   series.
2. **A reader who finds the pinned revision is no longer in the base's history should treat the
   figure as unverified, not as wrong.** That distinction is the whole reason to write the pin: it
   separates *stale* from *disagrees*, which an unpinned figure cannot.

## Worked example

`rogue3#53`'s body argued its own severity from counts it did not pin: *"Twenty-two audits
currently pin **216** bindings"* and *"The ledger is now 72 entries"*. The same command, run in a
clean `git archive` export at each revision:

| revision | audits | bindings | how |
|---|---|---|---|
| not named (the `#53` body) | 22 | 216 | unknown — no command and no revision recorded |
| `715bef9` (when `#53` was implemented) | 29 | 271 | `python3 scripts/check-audit-bindings.py --json` |
| `14fd9eb` | 33 | 293 | same command |

Three trees, three answers, and nothing in the body to tell them apart. The direction of `#53`'s
argument survived — the ledger was *larger* than claimed, not smaller — so the fix did not depend on
the difference. The cost was that every figure had to be re-derived before any could be trusted, and
a reviewer reading the item at merge time saw numbers matching nothing.

Note also what the second claim counted. The tool reports `excused`; the body described "the
ledger". Those were the same number once and are not now — the ledger became a directory of
per-cycle files, and the tool's own output grew `superseded`, `dormant` and `obsolete` alongside
`excused`. A body quoting `excused` from the tool would have aged into a different number; a body
describing "the ledger" aged into a different *question*.

## Enforcement is social, not mechanical

Nothing computes this. `scripts/fsgg-coord lint` fails an item with no usable `Paths:` line and has
no opinion about `Measured-at:`; adding or omitting the line changes no gate's verdict, and no CI
target reads it.

What catches a missing or stale pin is a reader re-running the quoted command — which is what caught
both recorded instances. Writing the rule down makes that re-run cheap and makes its absence
visible; it does not make it automatic. A rule that presents itself as a gate is a failure this
repository files against often, and this is deliberately not one.
