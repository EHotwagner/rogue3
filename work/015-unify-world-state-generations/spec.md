---
schemaVersion: 1
workId: 015-unify-world-state-generations
title: Unify World State Generations
stage: specify
changeTier: tier1
status: specified
publicOrToolFacingImpact: true
---

# Unify World State Generations Specification

Prose status: specified

## User Value
A maintainer reading `Model` finds exactly one representation of the enemies in a room, one of the obstacles, and one of the shop stock. Today there are two of each, kept in step by hand at six separate assignment sites and by nothing else, and the product has already shipped a defect from it: a dead actor kept taking turns because cleanup read the legacy `Enemies` projection, which shot resolution empties one step earlier. The failure mode is not that a value is wrong; it is that a reader picks the wrong one of two values that are both present, and nothing says which was meant. Removing the older generation removes the choice. A player sees no change at all — which is the point, and is what the evidence has to show.

## Scope
- SB-001: The three legacy/M5 pairs named on `EHotwagner/rogue3#20` — `Enemies`/`M5Enemies`, `Obstacles`/`M5Obstacles`, `ShopSlots`/`M5ShopSlots` — are each closed by removing the legacy field. The item's acceptance also permits a stated invariant with a test that fails on disagreement; that option is deliberately not taken, because an invariant only detects a divergence that removal makes unrepresentable.
- SB-002: The whole legacy shop surface goes with `ShopSlots`: the `Model.ShopSlot` and `Model.ShopCost` types, the `InteractShop` message, the `purchaseShopSlot` reducer and its `update` dispatch. `EHotwagner/rogue3#20` names this explicitly, and `InteractShop` has zero production dispatch sites.
- SB-003: Moving `Velocity`, `LastContactTick` and `HitFlashTicks` onto `Rogue3.Entities.EnemyActor` is in scope as a consequence of SB-001: those three are real combat state that the legacy `Enemy` record carried and `EnemyActor` has nowhere to put, so `Enemies` cannot be removed without them. `Radius` and `ContactDamage` are not moved — they are already total functions of `Kind` through `Entities.definition`.
- SB-004: One named blocking-rect derivation over `M5Obstacles` in `Model` is in scope, replacing the four copies of the same filter-and-map expression that today keep the `Obstacles` cache current.
- SB-005: `src/Rogue3/PerformanceEvidence.fs` is in the effective touch-set although the board item did not declare it: it constructs 30 `Model.Enemy` records and eight legacy obstacle rects for the `maximum-content` fixture and reads both fields as cost-driver scale. The extension was declared on the item before implementation.
- SB-006: Re-pointing every affected test fixture and assertion is in scope. An assertion written against a removed field is rewritten against the surviving one; it is not deleted.

## Non-Goals
- SB-007: Renaming the surviving `M5Enemies`, `M5Obstacles` and `M5ShopSlots` fields is out. They are read by `src/Rogue3/Render.fs`, `src/Rogue3/EvidenceCommands.fs` and `src/Rogue3/GameplayVisualInventory.fs`, none of which is in the touch-set and all of which prior audits bind. The prefix is a naming wart once the older generation is gone, not a second generation.
- SB-008: Integrating enemy `Velocity` so knockback moves an enemy, and drawing `HitFlashTicks`, are out. Both are written by combat and read by no integrator and no renderer today, and both are asserted by `M3CombatHealthCurrencyTests`. They are carried across unchanged and the observation is filed, not acted on under cover of a refactor.
- SB-009: `Model.EnemyBullets`, `Model.Bombs` and `Model.HomingTargets` are out. None has an M5 twin, so none is a second generation of anything; `EnemyBullets` is the only enemy-bullet representation the product has.
- SB-014: Making the shop actually grant the item it charges for is out. `purchaseM5ShopSlot` debits, empties the offer and bumps `RunStats.ItemsFound` without touching `PlayerItems`; the pre-M5 reducer this item removes did grant the item but had zero production dispatch sites, so the capability was never reachable. Closing it needs stat modifiers on `Entities.ItemDefinition`, a design decision this item has no standing to make. Filed as `EHotwagner/rogue3#47` and pinned by a characterization assertion.
- SB-010: The Pong paddle residue in `Model` is out — that is `EHotwagner/rogue3#18`, a separate open item with its own holder.
- SB-011: `src/Rogue3/Render.fs`, `src/Rogue3/EvidenceCommands.fs`, `src/Rogue3/FloorGeneration.fs`, `src/Rogue3/GameplayVisualInventory.fs`, `src/Rogue3/Determinism.fs`, `src/Rogue3/AudioCues.fs` and `src/Rogue3/Rogue3.fsproj` are out of the touch-set. A whole-tree survey found zero references to any removed field, type, message or reducer in any of them. No compile item is added or removed.
- SB-012: `scripts/check-audit-bindings.py` and `.github/workflows/audit-bindings.yml` are out — the touch-set of the concurrently claimed `EHotwagner/rogue3#38`.
- SB-013: No regression of M6 layer ordering and camera contract, M8 audio cues, M10 determinism and replay, M11 playability, M12 audio assets or M13 room transition and pickups.

## User Stories
- US-001 (P1): As a maintainer, when I ask "where do this room's enemies live", `Model` gives me one answer, so I cannot write cleanup against a projection that a different step already emptied.
- US-002 (P1): As a maintainer, when I ask "which obstacles block the player", I find one derivation with no stored copy, so no assignment site can forget to refresh a cache.
- US-003 (P1): As a maintainer, when I ask "how does a player buy something", there is one shop type, one message and one reducer, so I cannot wire a screen to the dead half.
- US-004 (P1): As a player, nothing changes: the same enemies take the same damage on the same tick, drop the same items, and the same walls stop me.
- US-005 (P2): As a maintainer, the tests that used to prove enemy damage, knockback, hit flash, bomb chains, friendly fire, obstacle blocking, shop affordability and descent clearing still prove exactly those things, against the surviving representation.

## Acceptance Scenarios
- AC-001 [US-001] [FR-001]: Given the product source tree, when it is searched for the removed surface, then `Model.Enemies`, `Model.Obstacles`, `Model.ShopSlots`, `Model.Enemy`, `Model.ShopSlot`, `Model.ShopCost`, `Msg.InteractShop` and `purchaseShopSlot` are absent from every file under `src/`, and the product still compiles.
- AC-002 [US-001] [FR-002]: Given an enemy actor reduced to zero hit points by a shot, when the production fixed step completes, then the actor is removed exactly once through the same cleanup that rolls its drop and credits its kill, and no representation of it survives the step — the §14.21 mechanism has no second list to survive in.
- AC-003 [US-001] [FR-003]: Given `Rogue3.Entities.EnemyActor`, when an enemy is hit by a shot, then its knockback velocity, its hit flash and its contact re-tick are recorded on the actor itself, and a shot cannot damage the same enemy twice.
- AC-004 [US-002] [FR-004]: Given a room whose obstacles include a grounded-blocking kind and a non-blocking kind, when the player is driven at each through the production input route, then the player is stopped by the blocking one and passes through the non-blocking one, and the rect set that stopped them is produced by one named derivation over `M5Obstacles` with no stored field beside it.
- AC-005 [US-002] [FR-005]: Given an obstacle destroyed by a bomb or by direct damage, when the reducer returns, then what the player next sweeps reflects the destruction, and no reducer in the product assigns a separate blocking-rect cache.
- AC-006 [US-003] [FR-006]: Given a player with enough coins and a stocked M5 shop slot, when `InteractM5Shop` is dispatched through the production `update`, then the purchase is accepted, the currency is debited and the item is recorded; and given a player without enough coins, the purchase is rejected and the slot keeps its stock.
- AC-007 [US-003] [FR-007]: Given a run descending a floor, when `DescendFloor` completes, then the room-local collections it clears are the surviving ones — enemies, obstacles and shop stock — and each is observably non-empty immediately before the descent, so the assertion can fail.
- AC-008 [US-004] [FR-008]: Given the pre-change and post-change products driven by the same seed through the production `update`, when both are stepped, then every gameplay-observable value the suite asserts — hit points, currency, health, kills, drops, room clear, player position — is unchanged, with one declared exception: a kill dealt by a **bomb blast or a black-heart burst** is now resolved in the step the damage lands rather than one step later, because that damage used to reach the actor list through a re-sync that ran after the cleanup fold. That is the same §14.21 defect class the item exists to close, it shifts the drop-stream draw order for such kills, and it is a stated consequence rather than a preserved behaviour. The canonical determinism encoding is expected to differ, because three fields left the record.
- AC-009 [US-005] [FR-009]: Given the full test suite, when it runs against the candidate, then it passes with at least the 228 tests present at `d8d0024`, and every assertion that referenced a removed field asserts the same property against the surviving one.
- AC-010 [FR-010]: Given the `maximum-content` performance workload, when it runs against the candidate, then every cost driver whose population changed shape is re-declared to the value the run emits — `MaximumExpected` is compared for exact equality in that workload — and no driver is retired to avoid a mismatch.
- AC-011 [FR-011]: Given the seven workloads, when the performance route runs against the exact candidate, then every moved workload definition digest is re-declared from the emitted value in both `src/Rogue3/PerformanceEvidence.fs` and `readiness/performance-intent.yml`, and every p95, p99, scene-node and catch-up budget holds.
- AC-012 [FR-012]: Given that the merge gate reproduces locally, when `dotnet test -c Release` and `python3 scripts/check-audit-bindings.py --selftest` then `python3 scripts/check-audit-bindings.py` run against the exact candidate, then all three exit 0, with each exit code read directly rather than through a pipe.

## Functional Requirements
- FR-001: The pre-M5 world-state generation MUST NOT exist in `src/` after this change: the `Enemies`, `Obstacles` and `ShopSlots` fields, the `Enemy`, `ShopSlot` and `ShopCost` types, the `InteractShop` case and the `purchaseShopSlot` reducer are all removed. (Stories: US-001; Acceptance: AC-001)
- FR-002: Enemy death MUST be resolved from the single surviving actor list by the existing `stepM5Entities` cleanup, and shot resolution MUST NOT drop zero-hit-point actors, so the drop roll, kill credit and room clear each happen exactly once. (Stories: US-001; Acceptance: AC-002)
- FR-003: `Rogue3.Entities.EnemyActor` MUST carry the combat state the legacy record carried and the actor lacked — knockback `Velocity`, `LastContactTick` and `HitFlashTicks` — initialised by `Entities.spawn`; `Radius` and `ContactDamage` MUST be read from `Entities.definition` rather than stored. (Stories: US-001; Acceptance: AC-003)
- FR-004: The player's blocking-rect set MUST be produced by exactly one named derivation over `M5Obstacles`, with no stored field beside it. (Stories: US-002; Acceptance: AC-004)
- FR-005: No reducer may carry a separate blocking-rect cache assignment; destroying an obstacle MUST change what the player sweeps by changing `M5Obstacles` alone. (Stories: US-002; Acceptance: AC-005)
- FR-006: The M5 shop MUST be the only shop — one type, one message, one reducer — and the affordability and rejection properties previously asserted against `purchaseShopSlot` MUST be asserted against it. (Stories: US-003; Acceptance: AC-006)
- FR-007: `DescendFloor` MUST clear the surviving room-local collections, and the test proving it MUST seed them observably non-empty first. (Stories: US-003; Acceptance: AC-007)
- FR-008: Gameplay behaviour MUST be preserved — damage, knockback, hit flash, contact re-tick, bomb chains, friendly-fire immunity, black-heart bursts, obstacle blocking, spike damage, drops, kill accounting and room clear all behave as they did at `d8d0024` — EXCEPT that a bomb or burst kill resolves one fixed step earlier, which MUST be declared and guarded by a test rather than discovered. (Stories: US-004; Acceptance: AC-008)
- FR-009: The suite MUST NOT shrink; every assertion against a removed field is rewritten, not deleted. (Stories: US-005; Acceptance: AC-009)
- FR-010: Every performance cost driver whose measured population changed MUST be re-declared to its measured value with an accurate `ScaleSource`; none may be retired to avoid a mismatch. (Stories: US-005; Acceptance: AC-010)
- FR-011: Every moved workload definition digest MUST be re-declared from the emitted value in both the source declaration and the typed intent, and every timing and structural budget MUST hold. (Stories: US-005; Acceptance: AC-011)
- FR-012: The two real CI commands MUST be reproduced locally against the exact candidate with their true exit codes captured. (Stories: US-005; Acceptance: AC-012)

## Ambiguities
- AMB-001: Whether `Obstacles` should become a derivation or be kept with a stated invariant and a disagreement test, given that the item's acceptance permits either.
- AMB-002: Whether the legacy `Enemy` record's `Velocity` — written by shot knockback and read by no integrator — should be carried onto `EnemyActor` or dropped as dead state.
- AMB-003: Whether removing `Enemies` changes when a dead actor leaves the model, and if so whether that is a behaviour change or the closure of the §14.21 defect.
- AMB-004: Whether the `state.static-obstacles` cost driver survives the removal of the field it names, or is retired as a duplicate of `state.m5-obstacles`.
- AMB-005: Whether the surviving fields should lose their `M5` prefix in the same change.

## Public Or Tool-Facing Impact
- `Model` loses three fields, three types, one `Msg` case and one public function. `Rogue3.Entities.EnemyActor` gains three fields. Both are public record surfaces of the product's model, so this is a tier-1 change.
- The canonical determinism encoding of every model moves, because `Determinism.encode` is reflective over the whole record. Every workload definition digest in `readiness/performance-evidence.json` and `readiness/performance-intent.yml` moves with it.
- `Rogue3.Entities.spawn` keeps its signature, and every `EnemyActor` construction outside it goes through `spawn` or a `with` expression, so no caller outside the touch-set breaks.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd clarify --work 015-unify-world-state-generations`.
- AMB-001 through AMB-005 are blocking and are resolved in `clarifications.md` before `plan`.
