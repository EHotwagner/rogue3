# Hollow Depths — roadmap completion report

Date: 2026-08-01
Roadmap: `docs/roguelike-dungeon-crawler-roadmap.md`
Source specification: `FS-GG/FS.GG.Game` → `docs/TestSpecs/Games/roguelike-dungeon-crawler.md`
Driver: `work-roadmap`, one disposable worker per milestone
Final `main`: `14eec68`

Every non-deferred milestone, M0 through M10, is 🟩 on `main`. The ten Stretch rows remain ⬜ deferred
(post-v1) by the roadmap's own legend and were never in scope. The deferral ledger the earlier
milestones accumulated — the M7/M8 acceptance sweep and the M9 replay/determinism work, all aimed at
M10 — is discharged and empty.

## 1. Per-milestone record

| M | Milestone | Landed | SDD work item | Verdict | Obligations | Cycle report | Ckpts | Findings |
|---|---|---|---|---|---|---|---|---|
| M0 | Scaffold & fixed-step loop | `befd803` | `001-m0-scaffold-fixed-step-loop` | shipReady | 21/21 | `2026-08-01-Rogue3.md` | 4 | 2 |
| M1 | Input & twin-stick control | `1bb4652` | `002-m1-input-twin-stick-control` | shipReady | 26/26 | `-2.md` | 4 | 4 |
| M2 | Movement, dodge & shots | `5e56d20` | `003-m2-movement-dodge-shots` | shipReady | 30/30 | `-3.md` | 4 | 3 |
| M3 | Combat, health & currency | `ed31018` | `004-m3-combat-health-currency` | shipReady | 5/5 | `-4.md` | 4 | 3 |
| M4 | Procedural floor generation | `7aaa1b2`, `be4e732` | `005-m4-procedural-floor-generation` | shipReady | 10/10 | `-5.md` | 4 | 3 |
| M5 | Entities: enemies, bosses & rooms | `f8895d4` | `006-m5-entities-bosses-rooms` | shipReady | 13/13 | `-6.md` | 5 | 4 |
| M6 | Rendering & enemy symbology | `4fc6e2c`…`ebccb4a` | `007-m6-rendering-enemy-symbology` | shipReady | 10/10 | `-7.md` | 5 | 5 |
| M7 | UI, menus & stats | `b3f0a80`…`9767d17` | `008-m7-ui-menus-stats` | shipReady | 17/17 | `-8.md` | 5 | 5 |
| M8 | Audio | `ef2b620`…`571bfb6` | `009-m8-audio` | shipReady | 12/12 | `-9.md` | 1 | 1 |
| M9 | Win/loss & permadeath | **PR #1** → `ea0dfc1` | `010-m9-win-loss-permadeath` | shipReady | 9/9 | `-10.md` | 10 | 1 |
| M10 | Acceptance & determinism | **PR #2** → `14eec68` | `011-m10-acceptance-determinism` | shipReady | 24/24 | `-11.md` | 7 | 8 |

Totals: **11 milestones, 11 `shipReady` verdicts, 177 observed obligations, 0 self-attested, 0
synthetic/deferred/stale/missing, 53 checkpoints, 39 findings.**

Cycle reports are under `feedback/`, audits under `feedback/audits/`, checkpoint JSONL under
`feedback/checkpoints/roadmap-roguelike-dungeon-crawler-m<N>-<slug>.jsonl`. SDD artefacts are under
`work/<id>/` (charter, spec, clarifications, checklist, plan, tasks, evidence) and `readiness/<id>/`
(analysis, verify, ship verdict).

### The publication boundary, and why only two milestones have PRs

M0–M8 landed as direct commits because **the repository had no public remote until partway through
M9**. That is the single largest deviation from the roadmap's intended discipline, and it is not
cosmetic: for seven consecutive cycles the workers correctly reported that local ship readiness could
not produce remote acceptance evidence, and could do nothing about it (§3 below). M9 published the
remote, opened PR #1, and added the Actions-native Release-test workflow that supplies the durable
exact-head check the merge gate needs. M10 is therefore the first milestone that ran the intended
shape end to end: branch → PR → green required check on the exact head → review → merge.

Both PRs merged green on their exact final head. PR #2's `Release tests` run is bound to `fad5319`,
which is the head that merged — not an earlier commit.

## 2. What each milestone actually proved

**M0–M2** established the spine: a 120 Hz fixed-step sim through `FixedStep.drainWith` with a
`MAX_STEPS = 5` guard and banked accumulator (AC #8), splitmix64 `Rng` with `LayoutRng`/`DropRng`
sub-streams via `Rng.split`, a logical 1280×720 space, and twin-stick input decoupled across
keyboard/mouse and gamepad (AC #9) with a `PressedThisTick` edge set.

**M3–M5** built the game: combat, health and currency; procedural floor generation; then enemies,
bosses and room types. M4's `be4e732` is worth noting — a follow-up commit that rebound the floor
performance receipt to a *representative* route after the first receipt was found to measure an
unrepresentative one.

**M6–M8** built what the player sees and hears. All three needed repair rounds (M6 took five commits,
M7 four, M8 four), and in each case the repair was driven by an independent review that rejected
evidence rather than code: sidecar-to-runtime gaps, held-input and scale evidence gaps, audio
transition coverage.

**M9** delivered win/loss and permadeath — an idempotent `finishRun` reducer shared by death and
victory, `MetaProfile` JSON persistence with debounced atomic temp-file+rename, and the first real
PR.

**M10** was the largest single cycle and changed a load-bearing product decision. The worker
established by measurement that **`sprintf "%A"` cannot serve as a determinism golden**: `%A`
truncates a collection after 100 elements, so `sprintf "%A" [1..600]` and
`sprintf "%A" ([1..599] @ [999])` are the *same* 401-character string, while the maximum-content model
carries 600 particles. That blind spot had been sitting in `PerformanceEvidence`'s model and
per-frame message fingerprints since M2/M3. It was replaced with a product-owned canonical structural
encoder (`src/Rogue3/Determinism.fs`), and all seven workload digests plus three UI-route digests were
re-derived, reviewed and copied.

The scenario-indexed acceptance sweep then exposed four *production* gaps that milestone-scoped suites
had never touched, and all four were implemented rather than stubbed: detonation-driven same-step
secret reveal (§14.14), `TraverseDoor` with durable room-clear and destroyed-obstacle state (§14.15),
`UnlockDoor` with reciprocal `LockedKey` treasure doors (§14.16), and a dead actor that kept taking
turns because cleanup read the legacy `Enemies` projection that combat resolution empties first
(§14.21). Release/Verify went 153 → **183/183**, focused M10 30/30.

## 3. Recurring root causes, by owner

**FS.GG.SDD / local-versus-remote acceptance evidence — 7 consecutive cycles (M1 §4.4, M2 §4.3,
M3 §4.3, M4 §4.3, M5 §4.3, M6 §4.3, M7 §4.4).** The most-repeated finding on the whole roadmap. Local
`ship` reports readiness but structurally cannot supply the remote acceptance evidence a merge gate
wants, so every worker restated it and none could close it. **Resolved at M9** by publishing the remote
and adding the Actions workflow. Worth recording as a pattern: a finding that recurs seven times
without any worker being able to act on it is an environment gap, not a quality signal, and the
per-cycle machinery had no way to say so — each cycle honestly re-filed it as `accepted recurrence`.

**Evidence that does not traverse the production route — M2 §4.1, M3 §4.2, M4 §4.2, M5 §4.2,
M6 §4.2, M7 §4.3, plus M9 §4.1 and M10 §4.7.** Owner: this product's evidence emitters. The shape
repeats with different subjects — a baseline workload digest that excluded helper-backed
representative state; population counts that did not prove broadphase query pressure; a persistence
claim that asserted `PersistenceEvidence.Requested` instead of the host backend's actual files. Every
instance was caught by an independent reviewer, never by a green suite. **This is the roadmap's most
valuable repeated lesson: on this project, a green test suite has never once caught an evidence
defect; a fresh-context critic reading the exact commit has caught them all.**

**FS.GG skill guidance, review-limit contradiction — M5 §4.4, M6 §4.5 (duplicate). STILL OPEN.**
Installed `work-roadmap` guidance delegates to `pnext-item`, which caps the same critic at three repair
rounds and forbids a fourth; the installed files encode no override for a complex milestone, so a
worker can read the nested limit as terminal even when the host grants a different one. Filed against
FS.GG `work-roadmap` and `pnext` skill guidance. Nothing in this repo can fix it.

**FS.GG.SDD stage diagnostics — M10 §4.5, §4.6.** The `plan` stage silently reclaimed an authored
`## Performance Intent` section and emitted two diagnostics, neither naming the reclaimed section; and
`evidence` cannot cite its own work item's ship verdict. Both cost an authoring round.

**FS.GG guidance gap on determinism encoding — M10 §4.2.** Neither `fs-gg-game-core` nor
`fs-gg-testing` names an encoding or its truncation limit for byte-identical comparison, which is the
upstream half of the `%A` defect. The product half is owned by Rogue3 and was fixed here.

## 4. Checkpoint disposition ledger

All 53 checkpoints are dispositioned. Each was synthesized into its own cycle's §4 findings, §7
workarounds-still-in-tree, or §8 friction-and-cost, and the report↔audit↔cycle binding is machine
validated (§6). By recorded kind:

| Kind | Count | Disposition |
|---|---|---|
| `positive-pattern` | 23 | Positive patterns; promoted in §5 below |
| `orchestration` | 9 | Structured findings against host/worker orchestration |
| `friction` | 7 | Structured findings, avoidable cost recorded per cycle |
| `quality-gap` | 7 | Structured findings, all closed inside their own cycle |
| `defect` | 3 | Structured findings; the `%A` truncation defect is the significant one |
| `capability-gap` | 2 | Structured findings routed to FS.GG owners |
| `documentation` | 2 | One fixed in-cycle; the review-limit contradiction remains open (§3) |

By phase: `implementation-test-evidence` 16, `verify-ship-pr` 15, `lifecycle-authoring` 11,
`onboarding-first-build` 6, plus 5 under earlier cycles' phase names
(`scaffold-onboarding`, `implementation`, `verification`, `independent-review-repair-1`,
`independent-review-repair-2`).

No checkpoint was deduplicated against an existing tracker issue, for the reason in §7: the tracker is
empty.

## 5. Positive patterns worth promoting

- **Fresh-context critics on the exact commit.** They caught every evidence defect on this roadmap.
  M10's critic found nine factual errors in the first draft of that cycle's report and then *retracted
  one of its own claims* on re-verification — it had asserted all four UI-route `definitionDigest`s
  changed; a clean-tree re-run showed zero changed. A critic that corrects itself is worth more than
  one that only accuses.
- **Exact structural counters over population counts.** "2,520 combat candidates, 736 wall queries,
  2,400 homing considerations, 600 particles, 40 shots, 120 bullets" is falsifiable; "maximum content"
  is not. This is what made workload underrepresentation detectable at all.
- **Scenario-indexed acceptance sweeps.** M10's 24 §14 scenarios as one guarded Release list with one
  named production-driving test each — so a dropped scenario cannot pass green — found four production
  bugs that ten milestone-scoped suites had missed.
- **Canonical whole-state encoding.** It turns "nothing advanced" into a single assertion instead of a
  field-by-field comparison that silently omits the field that broke.
- **Raster evidence for visual semantics** (M7 §4.4): rendering a frame and looking at it exposed chart
  semantics that structural tests could not see.
- **Every field must have a runtime consumer** (M7 §4.2): difficulty and stats tables became
  trustworthy only once nothing was displayed that nothing produced.

## 6. Verification performed by the host

Not the workers' word — re-run against merged paths on `main`:

- PR #2 `MERGED` at 2026-08-01T20:04:02Z, merge commit `14eec68`; `Release tests` SUCCESS bound to
  `fad5319`, the exact merged head.
- All seven M10 roadmap rows 🟩 with an `Evidence (2026-08-01)` paragraph in the M0–M9 style.
- `readiness/011-m10-acceptance-determinism/ship-verdict.json` → `shipReady`, 24/24 supported and
  observed, zero blocking findings.
- **All 11 cycles** pass all three fail-closed validators — `validate-checkpoints`,
  `validate --audit`, and `validate-feedback-state.py` with the four required phases. 11/11 PASS,
  every exit code 0. Envelope counts match JSONL line counts in every cycle.

## 7. What a human should look at

1. **`feedback/2026-08-01-Rogue3-11.md` §4.7 — committed performance evidence is not reproducible
   from unchanged sources, and it is dispositioned `issue` with no issue filed.** Two `Verify` runs on
   one commit change `p50/p95/p99` and `allocatedBytes` (inherent measurement variance) and, via the
   assembly MVID in `compositionAuthority`, all seven `receiptDigest` values plus `inputDigest` and
   `artifactDigest` (deterministic-identity churn). Authored `definitionDigest`s are stable, which is
   the part that gates review. Only the churn half is fixable by stabilizing anything. This bit the
   M10 worker concretely — the roadmap and an evidence note quoted percentiles from an earlier
   regeneration than the committed one and had to be corrected. **The routing needs a decision**: this
   repository's issue tracker is enabled and empty, so the cycle report is the only artifact carrying
   it. Owner is split between FS.GG.Game.Harness (`JourneyReceipt.compositionAuthority`) and this
   product's evidence emitter.
2. **The review-limit guidance contradiction (§3) remains open** against FS.GG skill guidance, twice
   filed, unfixable from here.
3. **Two workarounds are still in the tree**, declared in M10 §7: `Determinism.writeStructured`'s total
   fallback for unrecognized leaf types, and the `state.placed-bombs` driver declaring 48 against a
   product cap of 99.
4. **No issues were filed anywhere, in any cycle**, and no other repo needed changes. Every finding
   routed to an FS.GG owner lives only in a cycle report in this repository. If those owners are meant
   to see them, something has to carry them across.
5. **`stash@{0}` is still present** and was deliberately left intact — the pre-M10 prior-art draft.
   M10 adopted more of it than "shape" suggests (module name, `InputLogEntry`, the three accessor
   names, the fold through `Model.update`, the sequence guard) and discarded only its `%A` encoder. It
   can be dropped whenever you like.

## 8. Process defect in this run, owned by the host

The `work-roadmap` host handed the M10 worker a prior-art brief asserting that the draft `Replay.fs`
called an `initialModelForSeed` that was not defined anywhere. **That was wrong** — it is defined at
`src/Rogue3/Model.fs:668` and resolves normally. The worker's first checkpoint of the cycle records
the cost: a verification detour before the draft could be judged on its actual defect, which was its
`%A` encoding choice. The handoff's *other* flag — that `%A` as a determinism golden needed validating
rather than adopting — was correct and turned out to be the cycle's most consequential finding.

The lesson is narrow and worth keeping: an unverified claim in a handoff brief costs the receiving
worker real time and is indistinguishable, on arrival, from a verified one. Handoff claims should
carry their evidence or be marked unverified.

A second, smaller one: this run began under `work-board`, which found the coordination board wired
(`FS-GG`/`Coordination`) but holding **zero rows for this repository** — the engine reports no board
row names `rogue3`, and `gh issue list` returns nothing. The board-driven loop had nothing to schedule.
The markdown roadmap was the real ledger, so the run switched to `work-roadmap`. A workspace whose
`FSGG_COORD_*` env points at the org default board without ever having rows there looks board-capable
and is not.

## 9. Roll-up

- **Milestones:** 11 of 11 non-deferred complete; 10 Stretch rows remain deferred by design.
- **PRs:** 2 (#1 M9, #2 M10). M0–M8 predate the remote.
- **SDD:** 11/11 `shipReady`, 177/177 observed obligations, 0 self-attested.
- **Tests:** Release/Verify 183/183 on `main`; seven bounded-headless workloads and four UI routes
  green.
- **Feedback:** 11/11 cycles validated, 53 checkpoints dispositioned, 39 findings.
- **Open follow-ups:** 3 — the §4.7 evidence-reproducibility routing decision, the FS.GG review-limit
  guidance contradiction, and the two declared in-tree workarounds.
- **Aggregate avoidable cost:** dominated by evidence rework, not by code. Repair rounds clustered in
  M6 (5 commits), M7 (4), M8 (4), M9 (7) and M10 (7), and in nearly every case the repair was to
  evidence or its binding rather than to gameplay. The single largest avoidable cost across the
  roadmap was the seven-cycle restatement of a finding no worker could act on.
- **Coverage gap:** native-window play, packaging, and package upgrades were never exercised in any
  cycle. Every claim in this repository is a headless one.
