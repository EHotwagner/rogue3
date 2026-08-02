# Quantified claims: name a revision, and make it one that survives

## The rule

> A figure produced by running something in this repository is not a fact about the repository. It
> is a fact about **one tree**. Quote the command that printed it and name the revision it was
> measured at — and that revision must be **reachable from `main`**, or name the content instead.

And its other half, which is the same mistake seen from the other side:

> **When a document and a tool disagree about a count, the document is usually counting and the tool
> is usually reporting.** Quote the tool's own output rather than describing it — and if no tool
> prints the number, that absence is itself worth a sentence.

The two are one rule. The first says *when* a number was true; the second says *what produced it*. A
figure missing either is a figure the next reader has to derive again before using any of it, and
"had to re-derive all of it before using any of it" is the recorded cost every time this has
happened here (`rogue3#81`).

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
  the rule above into that same skill; this page and that item state one rule on two surfaces, so a
  reader who finds either should expect the other.

## The grammar

Write a `Measured-at:` line as a top-level line in the body, beside `Paths:` and `Class:`:

```
Measured-at: <revision-or-digest> — <the command that printed the figure>
```

One line per command. If a body quotes figures from three commands, it carries three lines.

**Required** whenever the body quotes a number produced by running something in this repository.
**Not required otherwise** — this must not become a field every item has to carry. An item that
quotes no measured figure carries no `Measured-at:` line, and that is correct, not an omission.

What to pin depends on what the command read:

| the command reads | pin | example |
|---|---|---|
| the tree, at a commit already on `main` | the commit sha | `Measured-at: 715bef9 — python3 scripts/check-audit-bindings.py --json` |
| the tree, at work **not yet merged** | the **content digest** of the file the claim is about — see below | `Measured-at: build.fsx blob 50e64333 — ./fake.sh build -t TemplateDrift` |
| the board (Projects, issues, claims) | a UTC timestamp, because **the board has no revision** | `Measured-at: board 2026-08-02T18:40Z — scripts/fsgg-coord ready --repo rogue3 --json` |
| an installed package or tool | the version string | `Measured-at: FS.GG.UI 0.21.1 — dotnet list package` |
| nothing — you counted it yourself | say so, in place of the command | `Measured-at: d2c4e2f — counted by hand from scripts/audit-binding-exceptions/` |

The last row is the point of the second half of the rule, not an escape from it. "Counted by hand"
is an honest and useful thing for a body to say; a hand count presented as a tool's output is not.

## A commit sha is only a pin if it is reachable

Writing *a* sha is not enough. The sha has to still resolve for the person reading you, and in this
repository most of them will not.

**Check before you write it.** The right question is whether the commit is in `main`'s history, not
whether some ref somewhere mentions it:

```sh
git fetch origin
git merge-base --is-ancestor <sha> origin/main   # exit 0 = citable; exit 1 = do not cite it
```

**`git cat-file` is not this check, and neither is your own clone.** An orphaned commit stays in
your local object store long after it is on no ref at all, so it keeps resolving for *you* and for
nobody else. Measured here on `rogue3#77`'s cited `b689137`:

| check | result |
|---|---|
| `git cat-file -t b689137` | `commit` — it resolves locally |
| `git branch -a --contains b689137` | **empty** — it is on no branch, local or remote |
| `git merge-base --is-ancestor b689137 origin/main` | **exit 1** — not in `main`'s history |
| `git ls-remote origin \| grep -c b689137` | **0** — a fresh clone never receives it |

A rebase orphaned it. The first line is why the author did not notice.

**Your own branch's sha is not a pin either — it is an expiry date.** This repository squash- or
rebase-merges, so a feature-branch commit no longer lands on `main` at all. Measured at `d2c4e2f`,
which is a fixed endpoint rather than a moving ref — `git rev-list --merges --count d2c4e2f` → **13**
merge commits on `main`; `4fc6993` is itself the thirteenth and last of them, so **12** precede it;
and `git rev-list --merges --count 4fc6993..d2c4e2f` → **0** over the `git rev-list --count
4fc6993..d2c4e2f` → **10** commits added since. Every commit in that span is single-parent.

(Those counts are written against `d2c4e2f` rather than `origin/main` deliberately. Against
`origin/main` the same commands answer differently as soon as anything merges — including the change
that added this page — which would make this paragraph break its own rule in the act of stating it.)

`rogue3#77`'s replacement citation, `be74e52`, is reachable from its own
feature branch and from nothing else (`git ls-remote origin | grep -c be74e52` → 0), which makes it
the same defect with the timer reset: it will become unresolvable *the moment its own work merges*.
That is not a risk, it is a certainty with a delay.

## When the measurement exists only on unmerged work, pin the content

This is the common case — the tree you measured at is often *only* your branch — and it has a clean
answer. Cite **content, not a commit**. A content digest is ref-independent, checkable by any reader
against their own tree, and survives any merge strategy:

```sh
git rev-parse HEAD:<path>            # the git blob id of that file's exact contents
git show HEAD:<path> | sha256sum     # or a plain sha256, if you prefer a non-git digest
```

The property that makes this work is that a squash merge destroys the commit and keeps the content.
Measured across `rogue3#62`, which merged as `d2c4e2f`:

| | value |
|---|---|
| `git rev-parse item-62-…` (branch tip) | `f3122c01…` |
| `git merge-base --is-ancestor` that tip `origin/main` | **exit 1** — the commit did not survive |
| `git rev-parse item-62-…:build.fsx` | `50e64333bb634ec38a0ddc98bd5fbff633f87ca0` |
| `git rev-parse d2c4e2f:build.fsx` | `50e64333bb634ec38a0ddc98bd5fbff633f87ca0` — **identical** |

The commit id died in the merge; the blob id did not move. So a claim pinned to `build.fsx blob
50e64333` stayed checkable across exactly the event that broke the commit citation, and a reader can
settle it with one command against whatever tree they have.

Pin the digest of **the file the claim is about**, not of the whole tree. A claim about a mutant
surviving in one source file is pinned by that file's digest; a tree-wide sha would go stale on any
unrelated edit and tell the reader nothing about whether the claim still holds.

### When the figure has no single file — pin the merge base and state the delta

Some figures are counts over the whole tree, and this page's own worked example is one:
`check-audit-bindings.py` counts every audit and every bound file, so there is no "file the claim is
about" to digest. For those, neither row above works on its own. Do this instead:

1. **Pin the merge base**, which is on `main` and therefore reachable:
   `git merge-base HEAD origin/main`. Quote the figure measured *there*.
2. **State what your branch does to it** — either "this branch does not change the measured inputs",
   which a reader can check, or the delta and the digests of the files that cause it.

So an item measuring 34 audits on an unmerged branch writes `Measured-at: <merge-base> — python3
scripts/check-audit-bindings.py --json`, plus one sentence on what the branch adds. That keeps the
pin resolvable forever and still tells the reader what the branch's own tree would say. Quoting only
the branch figure is the case this whole page exists to stop, and it is not rescued by a digest.

## Never label a pin with a moving description

A pin can be perfectly good and still be wrapped in a phrase that rots. Write the sha; do **not**
write what the sha currently *is*.

> "re-checked against the current head `77f567d`"

That sentence is falsified by its own next commit — including, and this is the trap, by the commit
that records the review which corrected it. It went stale three times in one item this way, each
time repaired by bumping the sha, each repair immediately falsified by the act of committing it.

The fix is not to pin harder. It is to **delete the moving words**:

> "re-checked against `77f567d`"

Now it is permanently true. The sha alone is a fixed pin and needs no relative label; "the current
head", "the latest commit", "the tip of this branch", "as of now" and "the merge base" all name
something that moves, and add nothing a bare sha does not already say.

This is a different failure from the branch-sha problem above, and it cannot be fixed the same way.
A branch sha rots because the *commit* stops being reachable, which pinning to `main` solves. A
self-referential label rots because the *description* stops being accurate while the sha stays
perfectly valid — so there is nothing to re-pin, and the only remedy is to not write the description.

The same applies to any figure introduced as "currently", "now" or "at present". `rogue3#53`'s
"**The ledger is now 72 entries**" was wrong for exactly this reason before it was ever stale: the
word doing the damage was `now`.

## The check a reader runs

For a commit pin, the reader settles the figure without asking the author:

```sh
git archive <revision> | tar -x -C <dir>
cd <dir> && <the quoted command>
```

Measure in that **clean export, not in a working tree**. Outputs written by your own earlier
commands are frequently gitignored, so they do not appear in `git status` and silently change what
the next command reports.

Read the exit code directly. `<cmd> | tail` reports `tail`'s exit status, not the command's.

For a content pin, the reader runs `git rev-parse <ref>:<path>` (or `sha256sum`) against their own
tree and compares one string.

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

`rogue3#53`'s body argued its own severity from counts it did not pin: *"Twenty-two audits currently
pin **216** bindings"* and *"The ledger is now 72 entries"*. The same command, run in a clean
`git archive` export at each revision:

| revision | audits | bindings |
|---|---|---|
| not named (the `#53` body) | 22 | 216 |
| `715bef9` — when `#53` was implemented | 29 | 271 |
| `14fd9eb` | 33 | 293 |
| `d2c4e2f` | 34 | 298 |

All rows but the first from `python3 scripts/check-audit-bindings.py --json`. Four trees, four
answers, and nothing in the body to tell them apart. The last two are **adjacent commits** —
`git rev-list --count 14fd9eb..d2c4e2f` → 1, and `d2c4e2f`'s sole parent is `14fd9eb` — measured
hours apart while *this page* was being written. That is the honest scale of the problem: one merge
moved the figure, so it moves faster than the item that quotes it.

The direction of `#53`'s argument survived — the ledger was *larger* than claimed, not smaller — so
the fix did not depend on the difference. The cost was that every figure had to be re-derived before
any could be trusted, and a reviewer reading the item at merge time saw numbers matching nothing.

Note also what the second claim counted. The tool reports `excused`; the body described "the
ledger". Those were the same number once and are not now — the ledger became a directory of
per-cycle files, and the tool's output grew `superseded`, `dormant` and `obsolete` alongside
`excused`. A body quoting `excused` from the tool would have aged into a different number; a body
describing "the ledger" aged into a different *question*.

## Enforcement is social, not mechanical

Nothing computes this. `scripts/fsgg-coord lint` fails an item with no usable `Paths:` line and has
no opinion about `Measured-at:`; adding or omitting the line changes no gate's verdict, and no CI
target reads it.

What catches a missing, unreachable or stale pin is a reader re-running the quoted command — which
is what caught every recorded instance, including the two `rogue3#77` citations above. Writing the
rule down makes that re-run cheap and its absence visible; it does not make it automatic. A rule
that presents itself as a gate is a failure this repository files against often, and this is
deliberately not one.
