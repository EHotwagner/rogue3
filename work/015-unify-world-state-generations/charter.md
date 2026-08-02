---
schemaVersion: 1
workId: 015-unify-world-state-generations
title: Unify The Two World State Generations In Model
stage: charter
changeTier: tier1
status: chartered
policyPointers:
  - .fsgg/sdd.yml
  - .fsgg/agents.yml
  - .fsgg/policy.yml
  - .fsgg/capabilities.yml
  - .fsgg/tooling.yml
---

# Unify The Two World State Generations In Model Charter

## Identity
- Work id: `015-unify-world-state-generations`
- Lifecycle stage: charter
- Status: chartered
- Close board item `EHotwagner/rogue3#20`: the `Model` record carries a pre-M5 generation of world
  state (`Enemies`, `Obstacles`, `ShopSlots`) beside the M5+ generation (`M5Enemies`, `M5Obstacles`,
  `M5ShopSlots`) with no rule about which is authoritative. Three pairs, six fields, two shop types,
  two shop messages and two shop reducers. The pattern has already shipped one defect: M11's §14.21
  dead actor kept taking turns because cleanup read the legacy `Enemies` projection, which shot
  resolution empties first.
- `work/014-m13-room-transition-pickups-world-state/charter.md` already named this item by number
  under its "No second generation of world state" principle. This work item discharges it.

## Principles
- **One representation per fact, enforced by construction rather than by assertion.** The item's
  acceptance permits either removing a legacy field or stating an invariant with a test that fails
  when the two disagree. An invariant is a second-best: it detects divergence after the fact, and the
  shipped defect was exactly a divergence nobody detected. Every one of the three pairs is closed by
  **removal**, so there is no second value left to disagree.
- **A projection with no extra state becomes a function, not a field.** `Obstacles` is recomputed
  from `M5Obstacles` by the identical filter-and-map expression at four separate sites
  (`Model.fs` 1457, 1584, 1689, and the boot value). It stores nothing `M5Obstacles` does not already
  determine, so it collapses to one named derivation and the field disappears.
- **A projection that carries real state moves that state onto the authoritative type.** The legacy
  `Enemy` record is not purely derived: `Velocity`, `LastContactTick` and `HitFlashTicks` are written
  by combat and read back by combat, and `EnemyActor` has nowhere to put them. `Radius` and
  `ContactDamage` are already pure functions of `Kind` through `Entities.definition`. So the three
  stateful fields move onto `EnemyActor` and the two derived ones are read from the definition.
- **Behaviour is preserved except where the two generations disagreed.** The one place they
  provably disagree is the §14.21 mechanism: `resolveShotCombat` drops zero-hit-point entries from
  `Enemies` while leaving them in `M5Enemies`. With one list there is nothing to drop; deaths stay
  resolved by `stepM5Entities`'s existing cleanup, which is what rolls the drop, credits the kill and
  clears the room. Dropping dead actors inside `resolveShotCombat` instead would destroy the drop
  roll, so the unified list keeps zero-hit-point actors until that cleanup runs.
- **A removal must not quietly delete a tested behaviour.** Enemy knockback (`Velocity`) and the hit
  flash (`HitFlashTicks`) are asserted by `M3CombatHealthCurrencyTests` and are written but never
  integrated or rendered. They are carried onto `EnemyActor` unchanged and the observation is filed at
  root cause, rather than deleted under cover of a refactor.
- **Deleting a reducer must not delete its coverage.** `purchaseShopSlot` and `InteractShop` go, but
  the affordability, rejection and descent-clearing assertions written against them are re-pointed at
  `purchaseM5ShopSlot`/`InteractM5Shop`, not removed. A shrinking test count is the failure mode here.
- **Every moved digest is re-reviewed, never suppressed.** `Determinism.encode` is reflective over
  the whole `Model`, so removing three fields moves all seven workload definition digests and the
  performance intent. Each is re-declared from a measured run, following M13.
- M10 determinism, M11 playability, M12 audio-asset and M13 room-transition gates stay green, and
  the suite does not shrink.

## Scope Boundaries
- In: removal of `Model.Enemies`, `Model.Obstacles`, `Model.ShopSlots`, the `Model.ShopSlot` and
  `Model.ShopCost` types, the `Model.Enemy` type, the `InteractShop` message and the
  `purchaseShopSlot` reducer.
- In (consequential): `Velocity`, `LastContactTick` and `HitFlashTicks` on
  `Rogue3.Entities.EnemyActor` and their initialisation in `Entities.spawn`; one named blocking-rect
  derivation over `M5Obstacles` in `Model`; the re-pointing of every affected test fixture and
  assertion; the `maximum-content` performance fixture, the `state.static-obstacles`,
  `state.live-enemies` and `state.m5-obstacles` cost-driver declarations, the seven workload
  definition digests and `readiness/performance-intent.yml`'s `maximumExpectedScale`.
- Out: **renaming the surviving `M5`-prefixed fields.** `M5Enemies`, `M5Obstacles` and `M5ShopSlots`
  are read by `src/Rogue3/Render.fs`, `src/Rogue3/EvidenceCommands.fs` and
  `src/Rogue3/GameplayVisualInventory.fs`, none of which is in this item's touch-set and all of which
  prior audits bind. A rename is pure churn across three audit-bound files for no behavioural gain
  and would collide with the concurrently claimed `EHotwagner/rogue3#38`. The prefix is a naming
  wart once the older generation is gone; it is not a second generation.
- Out: integrating enemy `Velocity` so knockback moves an enemy, and rendering `HitFlashTicks`.
  Both are gameplay changes, filed rather than made.
- Out: `Model.EnemyBullets`/`Model.EnemyBullet`, `Model.Bombs`, `Model.HomingTargets` and the Pong
  paddle residue. `EnemyBullets` has no M5 twin — it is the only enemy-bullet representation, not a
  legacy generation. The paddle residue is `EHotwagner/rogue3#18`, a separate open item.
- Out: `src/Rogue3/Render.fs`, `src/Rogue3/EvidenceCommands.fs`, `src/Rogue3/FloorGeneration.fs`,
  `src/Rogue3/GameplayVisualInventory.fs`, `src/Rogue3/Determinism.fs` and `src/Rogue3/Rogue3.fsproj`.
  A whole-tree survey found zero references to any removed field, type, message or reducer in any of
  them; they are already entirely on the M5 generation. No compile item is added or removed.
- Out: `scripts/check-audit-bindings.py` and `.github/workflows/audit-bindings.yml`, the touch-set of
  the concurrently claimed `EHotwagner/rogue3#38`.

## Policy Pointers
- SDD policy comes from `.fsgg/sdd.yml` and `.fsgg/agents.yml`.
- Governance files are optional compatibility pointers and are not evaluated by this command.
- Honor constitution principles I, II, III, V, VI and VIII.
- Honor the M10 determinism contract in `src/Rogue3/Determinism.fs` (consumed, never edited).
- Honor the §14.21 dead-actor rule documented at `src/Rogue3/Model.fs` lines 1693-1698; this work
  item removes the projection that rule had to defend against.
- Honor the performance-first planning gate in
  `.agents/skills/pnext-item/references/performance-first.md` against the active typed intent
  `readiness/performance-intent.yml`.

## Deferrals Received
- `work/014-m13-room-transition-pickups-world-state/charter.md` recorded "No second generation of
  world state" as a principle and named `EHotwagner/rogue3#20` as already open against the pattern.
  M13 avoided adding a third instance; this work item removes the first.

## Lifecycle Notes
- Tier 1: it removes public fields, a public type, a public union case and a public function from
  `Model`, and adds public fields to `Rogue3.Entities.EnemyActor`.
- The item's declared `Paths:` omitted `src/Rogue3/PerformanceEvidence.fs`, which constructs 30
  `Model.Enemy` records and writes `Obstacles=`/`Enemies=` into the maximum-content fixture, and
  reads both fields as cost-driver scale. The touch-set was widened on the item before any edit
  (`scripts/fsgg-coord set-paths rogue3#20`), and the widened set collides with
  `EHotwagner/rogue3#38` on `scripts/audit-binding-exceptions.json` alone; that collision is being
  sequenced with its holder rather than edited around.
- Expect every workload definition digest to move: `definitionDigest` folds in
  `Determinism.encode (workload.InitialState())` and the encoder is reflective over the whole model.
  `MaximumExpected` is checked for **exact** equality in `maximum-content`
  (`src/Rogue3/PerformanceEvidence.fs` line 1827), so a changed fixture shape forces a re-declaration
  rather than passing under a ceiling.
- Next lifecycle action: `fsgg-sdd specify --work 015-unify-world-state-generations`.
