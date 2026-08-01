---
schemaVersion: 1
workId: 012-m11-playability-visual-legibility
title: M11 Playability Visual Legibility
stage: plan
changeTier: tier1
status: planned
sourceSpec: work/012-m11-playability-visual-legibility/spec.md
sourceClarifications: work/012-m11-playability-visual-legibility/clarifications.md
sourceChecklist: work/012-m11-playability-visual-legibility/checklist.md
publicOrToolFacingImpact: true
---

# M11 Playability Visual Legibility Plan

Prose status: planned

## Source Snapshot
- spec: work/012-m11-playability-visual-legibility/spec.md sha256:c69689bb6122e990bf3ffce861390d0ae9a267ba6ba79be5bb565de49daf6236 schemaVersion:1
- clarifications: work/012-m11-playability-visual-legibility/clarifications.md sha256:9b9dc4f7aba42d4721490bbc736cc62f73e6a94d9c10b88656f67d9ed72241d1 schemaVersion:1
- checklist: work/012-m11-playability-visual-legibility/checklist.md sha256:9551602e27b70a319e14a885801d7628c7c89fe374c8380fd87a83cf352e445b schemaVersion:1

## Plan Scope
- Work item 012-m11-playability-visual-legibility is planned from the current specification, clarification, and checklist facts.
- Requirement count: 11.
- Clarification decision count: 13.
- Checklist result count: 12.
- The defect is a broken chain, not a broken reducer. `FloorGeneration` already knows every door, its direction and its state; `tryTraverseDoor` and `tryUnlockDoor` already perform the right transitions; `TraverseDoor`/`UnlockDoor` already call them. What is missing is anything in the fixed step that raises those messages from player input, a renderer that reads the floor graph instead of a cosmetic parallel list, and a boot model that loads the room the player stands in. The plan closes the chain and adds no second copy of any transition.

## Plan Decisions
- PD-001 [AC-001] [FR-001] [DEC-001] [DEC-002] complete: Add a pure `playerRoomIntents pressedThisTick model : Msg list` to `Model.fs`, evaluated on the stepped model after `stepSimWithInput`. When the player's centre enters the doorway sensor of a usable door of the current room it yields `TraverseDoor door.ToRoom`. `advanceSim` gains a `dispatch: Msg -> Model -> Model` parameter and folds those messages through `update` between fixed steps; `update` becomes `let rec` and supplies itself as the dispatcher. Exactly one traversal transition exists, so `Replay.fs` is untouched and a crossing replays from `KeyChanged` plus `Tick` alone.
- PD-002 [AC-001] [FR-001] [DEC-001] complete: Add the doorway geometry to `Model.fs` — `doorwayHalfSpan = 56.0`, `doorwaySensorDepth = 14.0`, and `doorwaySensorContains direction position`. The existing arrival clearance `playerRadius + 4.0` stays, so an arrival at 17 units from the wall lies outside a 14-deep sensor and a crossing cannot immediately re-trigger.
- PD-003 [AC-002] [FR-002] [DEC-003] complete: The same sensor scan yields `UnlockDoor door.ToRoom` when the door state is `LockedKey` and the player holds at least one key, so walking into a key door with a key opens it and spends exactly one key, and walking into it with none changes nothing.
- PD-004 [AC-003] [FR-003] [DEC-006] complete: Rewrite the door half of `Render.renderedElementsIn` to read `model.Floor.Rooms.[model.Floor.CurrentRoom].Doors` and zip it with the derived combat-lock projection `model.M5Room.Doors`, which `loadM5Room` already builds one-per-graph-door in the same order. Document `M5Room.Doors` in `Entities.fs` and `Model.fs` as the derived combat-lock projection it is, not a second door model.
- PD-005 [AC-004] [FR-004] [DEC-006] [DEC-007] complete: Draw each door in the doorway rect of the wall its `Direction` names, choosing among six visually distinct elements — `DoorOpen`, `DoorLockedKey`, `DoorBossDoor`, `DoorHiddenWall`, `DoorLockedClear` and `DoorBossSealed` — with `HiddenWall` winning over the combat lock and the combat lock winning over `Open`. Add a `RoomWalls` element on the existing `FloorDecals` layer that draws the four walls with a gap at each doorway, so no `RenderLayer` case is added and the eleven-layer contract holds.
- PD-006 [AC-004] [FR-004] [DEC-004] complete: Make `HiddenWall` a generated state. `FloorGeneration.generateWithPool` writes a reciprocal `HiddenWall` door pair and the reciprocal graph edges for every pending secret adjacency, and `revealSecret` flips those existing records to `Open` instead of prepending a second pair. `tryTraverseDoor` already accepts only `Open` and `BossDoor`, so a hidden wall is visible and impassable until a bomb reveals it, and a player can finally see which wall to bomb.
- PD-007 [AC-005] [FR-005] complete: Extend `GameplayVisualInventory` with `RoomWalls`, `DoorLockedKey`, `DoorBossDoor` and `DoorHiddenWall`, their handles, and evidence models built from real floor rooms rather than hand-written door lists, and add the matching rows to `tests/Rogue3.Tests/element-visuals.catalog` so the coverage audit stays complete.
- PD-008 [AC-006] [FR-006] complete: Add a committed render-and-look harness that rasterises the production frame with `Render.toPng` for the starting room, a four-state door room, a combat-sealed room, a boss-sealed room and a cleared boss room with its trapdoor, writing to `readiness/012-m11-playability-visual-legibility/frames/`, and obtain an independent visual-coverage critic verdict on the exact candidate persisted outside the authored tree.
- PD-009 [AC-007] [FR-007] [DEC-010] complete: Load the starting room at boot. `initialModel` becomes `initialModelForSeed 0xC0FFEEUL |> loadM5Room 0` and `StartRun` does the same, so `M5Room` is derived from the floor graph through the seam every other room uses instead of being hand-written empty at `Model.fs:705-707`.
- PD-010 [AC-008] [FR-008] [DEC-005] complete: Guard `DescendFloor` on the current floor room carrying the `Trapdoor` fixture, the loaded `M5Room.Trapdoor` agreeing, and the player's centre lying inside the trapdoor bounds; otherwise it returns the model unchanged. Guard the `floor-descend` audio cue on the floor index actually changing so a refused descent is silent.
- PD-011 [AC-009] [FR-009] [DEC-007] complete: Derive `M5Room.Trapdoor` from the room's `Trapdoor` fixture in `loadM5Room` and draw the trapdoor at the room centre, so the fixture is visible whenever the floor records it rather than only when a boss died in this session. Add the interact-edge descent intent to `playerRoomIntents`, dispatching `DescendFloor` then `EnterM5Room 0`, and prove the boss room is reachable from the start room by crossing doors.
- PD-012 [AC-010] [FR-010] [DEC-008] complete: Add a runner-issued production journey that boots the shipped model, moves with real key events, crosses a door into another room and crosses back, asserting `Floor.CurrentRoom` changed in both directions and that every issued event mapped.
- PD-013 [AC-011] [FR-011] [DEC-008] complete: Instantiate the journey's `'menu` type parameter with a product-owned `PlayerAction` vocabulary — `CrossDoor`, `UnlockKeyDoor`, `UseTrapdoor`, `BurstParticles` — resolved against the live `model.Floor` in `MapEvent`, returning `JourneyDispatch.Unbound` naming the action for any scenario that does not bind it, and issue an unbound action in at least one script so the arm is not dead code.
- PD-014 [DEC-009] [DEC-011] [DEC-012] complete: Restage the five unguarded `DescendFloor` call sites and the `floor-generation` workload to the route a player must now take, keep `DescendFloor`'s existing "replace every room-local collection" contract so the scenario-18 assertions keep their meaning, and re-derive, review and copy all seven workload digests and all four UI-route digests.
- PD-015 [DEC-013] acceptedDeferral: DEC-013 defers the unresolved launch audio assets (`title-theme`, `floor-1-theme`, `dodge-roll`, `player-hit`, `bomb-explosion`) to separate roadmap work. They are an asset-availability fact the host already reports correctly, not a playability or legibility defect, and pulling them in would widen a milestone that already carries eleven rows.
- PD-016 [CR-012] acceptedDeferral: The checklist review row carrying DEC-013 forward, so evidence must show the deferral was recorded and routed rather than silently dropped.

## Contract Impact
- PC-001 [PD-001] [PD-002] [PD-003] [PD-010] [PD-011] product contract: `update` becomes recursive, `advanceSim` gains a dispatcher parameter, `Model` gains the `TotalDoorSensorQueries` counter, and `DescendFloor` gains a precondition. No framework package API changes.
- PC-002 [PD-004] [PD-005] [PD-006] [PD-007] product contract: `FloorGeneration.generate` emits additional `HiddenWall` doors and graph edges, `Render.renderedElements` gains four elements and loses the fixed-strip door layout, and the gameplay-visual inventory and catalog grow by four rows.
- PC-003 [PD-012] [PD-013] product contract: The production journey's action vocabulary becomes a product-owned `PlayerAction` type instead of `unit`.
- framework: FS.GG.Game.Harness#Journey.runScriptWithIdentity
- framework: FS.GG.UI.Symbology.Render#Render.toPng

## Verification Obligations
- VO-001 [PD-001] [PD-002] [PD-003] [PD-009] [PD-010] [PD-011] [PD-012] [PC-001] semanticTest: A focused Release M11 list proves crossing, returning, unlocking and descending from scripted `KeyChanged`/`Tick` messages only, proves a crossing does not re-trigger on arrival, and proves a refused descent changes nothing.
- VO-002 [PD-004] [PD-005] [PD-006] [PD-007] [PD-008] [PC-002] semanticTest: The visual-coverage gate is complete over the grown inventory, every door state renders on its own wall with a distinct scene digest, and committed production-frame PNGs were rendered and visually inspected with an independent critic verdict.
- VO-003 [PD-013] [PD-014] [PC-003] semanticTest: A full Release `Test`/`Verify` run with regenerated bounded-headless workload evidence and UI-route evidence at the exact candidate, including the unbound-action arm reporting itself.

## Performance Intent
No performance intent is declared for this work item.

## Migration Posture
- PM-001 [PC-001] [PC-002] [PC-003] additive: The counter, the new elements and the new journey vocabulary are additive, and the generated floor gains doors rather than losing them. Saved profiles are untouched. Every digest that moves fails closed until re-derived, reviewed and copied rather than migrating silently.

## Generated View Impact
- GV-001 [PD-001] [PD-008] [PD-014] workModel: `readiness/012-m11-playability-visual-legibility/` refreshes from current plan sources or reports staleGeneratedView, the M11 ship verdict is committed, `readiness/performance-evidence.json`, `readiness/performance-intent.yml`, `readiness/m7-ui-performance.json` and `readiness/performance-critic-request.json` are regenerated at the exact candidate, and the render-and-look frames are committed under `readiness/012-m11-playability-visual-legibility/frames/`.

## Accepted Deferrals
- DEC-013 acceptedDeferral: The unresolved launch audio assets are routed as separate roadmap work rather than pulled into a milestone that already carries eleven rows. The host already reports them correctly, and they are an asset-availability fact, not a playability or legibility defect.
- CR-012 acceptedDeferral: The checklist row carrying DEC-013, kept visible so evidence must show the deferral was recorded and routed rather than silently dropped.

## Planning Findings
No blocking planning findings recorded.

## Advisory Notes
- Optional Governance pointers remain compatibility facts only.
- The lesson this item encodes: M10 proved reducers correct while leaving them unreachable, because coverage was measured over messages the product defines rather than actions a player can take. Every row here is discharged from the production input route, and PD-013 exists so the next gap of this kind reports itself as `JourneyDispatch.Unbound` instead of being inexpressible.
- `JourneyEvent` is a closed nine-case DU owned by FS.GG.Game.Harness, so a `CrossDoor` case cannot be added. The product's action vocabulary lives in the `'menu` type parameter, which the product had instantiated with `unit` — a slot that can express exactly one value and therefore nothing.
- The play screen renders through a bare canvas, so `ControlRenderResult.BoundIds` cannot guard an in-game affordance. `JourneyDispatch.Unbound` is the keyboard-route analogue and is the reason both rows exist.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd tasks --work 012-m11-playability-visual-legibility`.
