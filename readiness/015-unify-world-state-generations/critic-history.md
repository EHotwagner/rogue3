# 015 — independent critic history

Two fresh-context critics reviewed the candidate at `2cafc5d` (the pre-rebase twin of `9a24cf6`).
Neither authored the change, the checkpoints or the lifecycle artifacts. Both were given only the
repository, the board item and a rubric; neither was given the author's rationale. Both changed what
ships.

`git diff 2cafc5d..<final>` over the four production files was empty at the time both critics
reported, so every line reference and mutation verdict below was measured against the code that
merged.

## Critic A — code, with mutation licence

Ran **38 mutations**, one at a time, each followed by a full `dotnet test`, each reverted. Also built
`d8d0024` into a scratch tree and ran identical fixed-step scenarios against both libraries.

**19 of 38 mutations passed green**, which is the finding. Four of them were acted on:

| Mutation | Was | Now |
|---|---|---|
| `actorRadius ≡ 64.0` (also 12.0, 1.0) | green | **caught** — roster radius/contact-damage derivation test with a Brute-vs-Fly overlap |
| `aliveAfter` (or `aliveBefore`) without its `HitPoints > 0.0` filter — silences all death audio | green | **caught** — `EnemyDied` emission test |
| `shotPassThrough` built at 32×32 (the structural no-op this change exists to have found) | green | **caught** — Pit-versus-Rock corridor test |
| no test covered a bomb **kill** at all | — | **added** — bomb-kill timing test |
| bombs damage a zero-hit-point corpse | green | **accepted**: `max 0.0` leaves a corpse at zero, so the mutation is a provable no-op |

**Critic A's blocking behavioural finding.** `AC-008`'s preservation claim was false for bomb and
black-heart-burst kills. At `d8d0024` that damage was written to the legacy `Enemies` list alone and
reached the actors through an `hpById` re-sync that ran *after* `stepM5Entities`'s cleanup fold, so
such a kill resolved one fixed step late: the corpse took one extra AI decision and its drop roll,
kill credit and room clear all landed a step later. Measured on both builds for one Grub-and-bomb
scenario — `d8d0024` credits the kill on step 2, the candidate on step 1; the death-cue count is one
in both. The consequence is that the `DropRng` draw order shifts whenever such a kill coincides with
another draw in the same step. `AC-008`/`FR-008` were amended to declare it, `CA-003` carries the
measurement, and the new bomb-kill test guards it.

Critic A also confirmed independently: the §14.21 mutation fails exactly the two tests the author
claimed; the `Obstacles` cache could never actually go stale in production (its five assignment sites
were paired 1:1 with the `M5Obstacles` ones), so the derivation is equivalent; `Entities.spawn` is
the only `EnemyActor` construction site; no absolute determinism golden exists to be invalidated;
and the performance re-declaration reproduces byte-for-byte.

## Critic B — evidence and lifecycle

Reproduced the merge gate from a clean `git archive` export and reported true exit codes; verified
the 42 stale audit bindings spanned exactly the 14 audit-bound files the diff touches and revealed no
unaccounted file; re-ran the performance evidence and confirmed every `definitionDigest`, every
`maximumExpected` and every observed cost value reproduces, with only per-run receipts and timings
moving; and checked the touch-set widening timestamps against file mtimes.

**Critic B's blocking finding, which Critic A found independently.** Shop coverage *was* deleted.
`purchaseM5ShopSlot` debits, empties the offer and bumps `RunStats.ItemsFound` — it never appends to
`PlayerItems` and never calls `recomputePlayerStats`. Four assertions were dropped or weakened, one
of them keeping its original message string while the asserted property changed. After the removal
`PlayerItems` has no production writer and `recomputePlayerStats` has no production caller. The gap
is pre-existing and player-facing; the removed dead reducer concealed it. Filed at root cause as
`EHotwagner/rogue3#47`, recorded as `DEF-003`/`SB-014`, and pinned by a characterization assertion in
both shop tests. This is the one place `CA-009`'s rewrite-never-delete rule was not met, and it is
now an admission in the record rather than a silent deferral.

**Critic B's other accepted findings.**

- Four evidence entries asserted the audit-binding gate passed and the excuse ledger carried entries.
  Neither was true when written. Rewritten after the ledger was actually written and both gate
  commands verified at exit 0.
- Three deferrals were recorded as `kind: verification` / `result: pass`, each citing a Release TRX
  that cannot witness "an issue was filed". Converted to `kind: deferral` / `result: deferred` with
  rationale, owner, scope and later-lifecycle visibility. The ship verdict now reads 50 observed and
  4 deferred rather than 51 observed.
- `EV037` miscounted its own diff (three/four instead of five/three). Corrected.
- Only `M10` asserted its descent fixtures were non-empty before the descent; `M4` carried the
  explanatory comment without the check. The assertion was added to `M4`.
- The Release TRX and this file were untracked, so a reviewer could not see the run. Both are now
  force-added past `.gitignore`, matching the `014` precedent.
- `EV044` misquoted the tool's own output. Corrected.

**Recorded and not acted on.** Critic B judged the new `maximum-content` enemy fixture "no more
spawnable than the one it replaced" — the actors are co-located on the player and carry 10000 hit
points. That is true and is now stated plainly in `EV018` and in the source comment: what changed is
that the fixture's enemies have the radii, contact damage and kinds the game ships, and the workload
measures 2100 combat candidates where it measured 2520 — strictly less, disclosed rather than
absorbed. Critic B also noted that `state.live-enemies` and `state.m5-enemies` now measure the same
population under this fixture; that is true and is left as a known limit of the workload rather than
repaired by inventing a second population.
