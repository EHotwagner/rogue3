---
schemaVersion: 1
workId: 011-m10-acceptance-determinism
title: M10 Acceptance Determinism
stage: specify
changeTier: tier1
status: specified
publicOrToolFacingImpact: true
---

# M10 Acceptance Determinism Specification

Prose status: specified

## User Value
A player can trust that a Hollow Depths seed reproduces the same dungeon and the same run however they play it, and every behaviour the source specification promises is demonstrably green on the production route.

## Scope
- SB-001: All 24 source-specification acceptance scenarios (§14) proven against the production `Model.update` route, seeded generation byte-identity (§14.1), layout/combat RNG stream independence (§14.2), layout-deterministic dupe-free fixture contents (§14.12), difficulty latching at `StartRun` (§14.13), atomic bomb-driven secret reveal (§14.14), seed-plus-input-log replay byte-identity (§13), and the production door-traversal and key-door seams §14.15/§14.16 require.

## Non-Goals
- SB-002: Every "Stretch — deferred (post-v1)" row (§15) stays out, including render interpolation, online leaderboards, mid-run saves, additional characters, item synergy graphs, and sprite atlases.

## User Stories
- US-001 (P1): As a player, I get the same dungeon and the same fixture contents from a seed no matter how fast or slow I fight through it.
- US-002 (P1): As a player, every behaviour the game promises — movement, combat, rooms, doors, bombs, pickups, difficulty, pause and restart — actually holds when I play it.
- US-003 (P1): As a player sharing a seed, a recorded run replays to exactly the same final state from the seed plus my ordered actions and timing.

## Acceptance Scenarios
- AC-001 [US-002] [FR-001]: Given the 24 source-specification acceptance scenarios, when the Release suite runs, then each scenario has a named test that drives production model, floor-generation, or shell functions and every one of the 24 passes.
- AC-002 [US-001] [FR-002]: Given `runSeed = 0xC0FFEE` and `floorIndex = 1`, when the floor is generated twice independently, then the canonical byte encodings of the two floors are equal, and a floor from any other seed or index encodes differently.
- AC-003 [US-001] [FR-003]: Given two runs on one seed whose combat draws differ in count, when each run's floor is inspected, then room graph, room types, fixtures and enemy anchors are identical while the drop stream has advanced differently.
- AC-004 [US-001] [FR-004]: Given two same-seed runs with different combat draws, when treasure pedestals, shop slots and boss rewards are generated across a whole run, then item ids, positions and prices are identical between runs and no item id repeats within one run.
- AC-005 [US-002] [FR-005]: Given Hard is selected in settings, when `StartRun` fires, then the run latches `EnemyHpScale = 0.18`, `PostHitInvulnSeconds = 0.55` and `DropNothingWeight = 55` and the simulation scales by them, and a later difficulty switch leaves the active run's scaling untouched.
- AC-006 [US-002] [FR-006]: Given a bomb whose blast covers a wall adjacent to a hidden secret room, when the fixed step that detonates it resolves, then that same step carves reciprocal doors, clears the hidden flag, commits both graph adjacencies and drops the pending secret, with no state in which a door exists without its adjacency.
- AC-007 [US-003] [FR-007]: Given a seed and an ordered input log of production messages with their exact tick timings, when the log is replayed through the production update function, then the canonical byte encoding of the final model equals the original run's, and a log whose sequence numbers are not unique and increasing is rejected.
- AC-008 [US-002] [FR-008]: Given adjacent rooms with reciprocal open doors and a key-locked door, when the player traverses and unlocks through production messages, then traversal lands at the reciprocal doorway and preserves the departed room's state, and unlocking spends exactly one key, opens both door records, and is never charged twice.

## Functional Requirements
- FR-001: The Release test suite MUST contain one named, production-driving test per source-specification acceptance scenario 1 through 24, and all 24 MUST pass. (covers AC-001)
- FR-002: The product MUST expose a canonical byte encoding of a generated floor that is equal for equal seed and floor index, differs for any other seed or index, and does not truncate large collections. (covers AC-002)
- FR-003: Floor layout, room types, fixtures and enemy anchors MUST depend only on the layout stream, so runs on one seed whose drop-stream draws differ still produce identical floors. (covers AC-003)
- FR-004: Treasure, shop and boss-reward contents MUST be drawn on the layout stream and MUST be dupe-free across a whole run's pedestals, shops and boss rewards. (covers AC-004)
- FR-005: `StartRun` MUST latch the settings difficulty into the run and the simulation MUST read the latched scaling for enemy hit points, post-hit invulnerability and drop weighting, with mid-run difficulty changes applying only to the next run. (covers AC-005)
- FR-006: A bomb blast that resolves against a wall adjacent to a hidden secret room MUST reveal it inside the same fixed step, updating doors, hidden flag, graph adjacency and pending-secret set as one transition. (covers AC-006)
- FR-007: The product MUST replay a seed plus an ordered input log through the production update function to a byte-identical canonical final encoding, and MUST reject a log whose sequence numbers are not unique and strictly increasing. (covers AC-007)
- FR-008: Production messages MUST traverse an open or boss door to the reciprocal doorway while preserving the departed room's cleared and fixture state, and MUST unlock a key-locked door by spending exactly one key and opening both reciprocal door records. (covers AC-008)

## Ambiguities
No material ambiguities recorded.

## Public Or Tool-Facing Impact
- Adds a product-owned canonical determinism encoding and replay module plus two new production messages; no framework package API changes.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd clarify --work 011-m10-acceptance-determinism`.
