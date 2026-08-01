---
schemaVersion: 1
workId: 012-m11-playability-visual-legibility
title: M11 Playability Visual Legibility
stage: clarify
changeTier: tier1
status: clarified
sourceSpec: work/012-m11-playability-visual-legibility/spec.md
publicOrToolFacingImpact: true
---

# M11 Playability Visual Legibility Clarifications

## Source Specification
- work/012-m11-playability-visual-legibility/spec.md

## Clarification Questions
- CQ-001 [AMB-001]: What player input crosses a door, given `KeyChanged` only records into the input snapshot and no key reaches any exit?
- CQ-002 [AMB-004]: Should the production input route dispatch the `TraverseDoor`/`UnlockDoor`/`DescendFloor` messages, or call the reducer bodies directly?
- CQ-003 [AMB-002]: Is `HiddenWall` ever produced by floor generation, and if not, how can a `HiddenWall` visual be exercised by anything a player can reach?
- CQ-004 [AMB-003]: What exactly does "the player is using the trapdoor" mean for the `DescendFloor` guard?
- CQ-005: How can doors be drawn at their own walls when the door vocabularies are disjoint and `M5Room.Doors` carries no direction?
- CQ-006: What does guarding `DescendFloor` break, and how is each break discharged without weakening M10?
- CQ-007: The `JourneyEvent` DU is owned by FS.GG.Game.Harness and cannot gain a `CrossDoor` case. How does the journey vocabulary come to cover door traversal and trapdoor descent?
- CQ-008: Does this change touch a measured production performance route, and which digests move?

## Answers
- CA-001: Walking into the doorway. The rebindable keymap already binds `E` to an `active` ("Use active / interact") command at `EvidenceCommands.fs:338` and routes it to `CommandChanged "active"`, but `resolveInput` reads neither `active` nor any door state, so no key has ever reached a door. Movement input already reaches the fixed step; proximity to a doorway is therefore the shortest real path from a key a player presses to a room change.
- CA-002: Dispatch the messages. `Replay.fs` folds the production `update` over an ordered log of `KeyChanged`/`Tick` messages, and its stated invariant is that "there is no replay-only transition to drift away from the shipped one". A second copy of the traversal transition inside the step would be exactly that. Dispatching also keeps the M10 §14.15/§14.16 message tests valid rather than orphaned.
- CA-003: `HiddenWall` is currently produced nowhere. Grep over `src/` finds it only in the `DoorState` declaration at `FloorGeneration.fs:6`. A visual for a state generation never emits could only ever be exercised by a hand-built fixture — the same "prove it with a fixture, it is unreachable in play" defect this milestone exists to close. So generation must emit it.
- CA-004: The current room carries the `Trapdoor` fixture, the loaded room state agrees (`M5Room.Trapdoor`), the player's centre lies inside the trapdoor's drawn bounds, and the interact edge fires. `DescendFloor` today checks none of trapdoor, cleared state or position (`Model.fs:1917-1939`).
- CA-005: `FloorGeneration.Door` already carries `Direction` and the four-state `DoorState`; `Entities.DoorState` (`Open | LockedClear | BossSealed`) carries neither and exists only to express the combat lock. `loadM5Room` already builds `M5Room.Doors` one entry per floor-graph door, in the same order (`Model.fs:1370`), so the two lists are already index-aligned — the renderer just never used the graph half.
- CA-006: Five sites drive `DescendFloor` from a room with no trapdoor — `M4ProceduralFloorTests.fs:67`, `M10AcceptanceDeterminismTests.fs:109`, `:316`, `:515`, and `M8AudioTests.fs:89` — plus the `floor-generation` performance workload (`PerformanceEvidence.fs:1306-1307`), whose runner journey terminates on `FloorIndex >= 9`. Each drives the production message from an unstaged model, which is precisely the defect: they were passing because the reducer was unguarded.
- CA-007: `JourneyEvent<'key,'pointer,'menu,'effectResult>` is closed at nine cases, but `'menu` is a product-owned type parameter that the product currently instantiates with `unit` (`PerformanceEvidence.fs:1007`), so the action slot can express exactly one value and therefore nothing. `MapEvent` also receives the model, so a product action can be resolved against the live floor graph.
- CA-008: Yes. `Model.fs` and `Render.fs` bytes are hashed directly into three and one of the four UI-route digests respectively (`EvidenceCommands.fs:1366-1371`), and `Determinism.encode model` plus the runner receipt digests feed all seven workload definition digests (`PerformanceEvidence.fs:675-700`). Every one of the eleven authored digests moves.

## Decisions
- DEC-001 [AMB:AMB-001] [CQ-001] [CA-001]: Crossing a door is proximity-driven. Each door occupies a doorway sensor on the wall its `Direction` names, spanning 56 logical units either side of the wall midpoint and 14 units inward. A player whose centre enters the sensor of a usable door crosses it. No extra key is required, so the shortest reachable route is the movement input a player already has.
- DEC-002 [AMB:AMB-004] [CQ-002] [CA-002]: The fixed step raises production `Msg` values and `advanceSim` folds them through `update` between fixed steps. `update` becomes `let rec`, and `advanceSim` takes the dispatcher as a parameter, so there is exactly one traversal transition, replay needs no change, and every M10 door test keeps driving the same reducer the player now reaches.
- DEC-003 [CQ-001]: Unlocking is the same gesture. A player whose centre enters the sensor of a `LockedKey` door while holding at least one key raises `UnlockDoor`; the door pair opens and one key is spent. Traversal then happens on a later step, because the sensor test for traversal requires an already-usable door. With no key the approach changes nothing.
- DEC-004 [AMB:AMB-002] [CQ-003] [CA-003]: Floor generation emits `HiddenWall`. For every pending secret adjacency it writes a reciprocal `HiddenWall` door pair and the reciprocal graph edges, and `FloorGeneration.revealSecret` flips those existing records to `Open` instead of prepending a second door pair. `tryTraverseDoor` already accepts only `Open` and `BossDoor`, so a hidden wall is visible and impassable until a bomb reveals it. This also makes the secret mechanic playable: a player can now see which wall to bomb.
- DEC-005 [AMB:AMB-003] [CQ-004] [CA-004]: `DescendFloor` is guarded inside the reducer: the current floor room must carry the `Trapdoor` fixture, the loaded `M5Room.Trapdoor` must agree, and the player's centre must lie inside the trapdoor's drawn bounds. The production route additionally requires the interact edge — the `E` key or the rebindable `active` command — so standing on a trapdoor never descends by accident.
- DEC-006 [CQ-005] [CA-005]: One door model. `FloorGeneration.Door` is the only source of a door's existence, direction and state. `M5Room.Doors` survives only as the derived combat-lock projection, index-aligned with the current room's `Doors`, and is documented as such. Rendering zips the two and draws each door on its own wall, choosing among six distinct elements: `DoorOpen`, `DoorLockedKey`, `DoorBossDoor`, `DoorHiddenWall`, `DoorLockedClear` (combat lock) and `DoorBossSealed` (boss lock). The combat lock wins over `Open`; `HiddenWall` wins over the combat lock, because a wall does not become a sealed door when enemies are alive.
- DEC-007 [CQ-005]: The trapdoor is drawn at the room centre, and `loadM5Room` derives `M5Room.Trapdoor` from the room's `Trapdoor` fixture, so the fixture is visible whenever the floor records it rather than only when a boss died in this session. Room walls are drawn as one `RoomWalls` element on the existing `FloorDecals` layer; no render layer is added, so the eleven-layer contract holds.
- DEC-008 [CQ-007] [CA-007]: The product instantiates the journey's `'menu` slot with a product-owned `PlayerAction` vocabulary — `CrossDoor`, `UnlockKeyDoor`, `UseTrapdoor`, `BurstParticles` — and resolves each against the live floor graph in `MapEvent`. A scenario that does not bind an action returns `JourneyDispatch.Unbound` naming it. At least one script issues an unbound action so the arm is not dead code, and the boot-to-return journey issues the bound ones.
- DEC-009 [CQ-006] [CA-006]: `DescendFloor` keeps its existing "replace every room-local collection" contract, and the production route dispatches `EnterM5Room 0` after it, so the player lands in a loaded start room without changing what the M10 scenario-18 assertions mean. The five unstaged `DescendFloor` call sites are restaged to clear the boss room, enter it and stand the player on the trapdoor before descending — which is what the acceptance scenarios describe and what a player must now do.
- DEC-010 [CQ-006]: The boot model loads the starting room through the same `loadM5Room` seam every other room uses, so `M5Room` is derived from the floor graph rather than hand-written empty, and `StartRun` does the same. `finishRun`'s discarded post-run state stays raw: it is a discarded model behind a result overlay, not a room a player stands in.
- DEC-011 [CQ-006]: The `floor-generation` workload is re-authored to stage a guarded descent per sampled frame using production messages only, and its runner journey boots a cleared boss room with the player on the trapdoor and issues the interact edge, so the measured route is the one a player actually takes.
- DEC-012 [CQ-008] [CA-008]: All seven workload digests and all four UI-route digests are re-derived, reviewed and copied, exactly as M10 did. The audio cue for `DescendFloor` is guarded on the floor index actually changing, so a refused descent is silent.

## Accepted Deferrals
- DEC-013: The unresolved audio assets observed at launch (`title-theme`, `floor-1-theme`, `dodge-roll`, `player-hit`, `bomb-explosion` all resolve to `None`) are deferred out of this milestone and reported as a finding for separate roadmap work. They are an asset-availability fact, not a playability or legibility defect, and the host is reporting them correctly.

## Remaining Ambiguity
None. Every recorded question has a decision, and AMB-001 through AMB-004 are resolved by DEC-001/DEC-003, DEC-004, DEC-005 and DEC-002 respectively.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd checklist --work 012-m11-playability-visual-legibility`.
