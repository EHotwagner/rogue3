---
schemaVersion: 1
workId: 014-m13-room-transition-pickups-world-state
title: M13 Room Transition Pickups And World Space State
stage: specify
changeTier: tier1
status: specified
publicOrToolFacingImpact: true
---

# M13 Room Transition Pickups And World Space State Specification

Prose status: specified

## User Value
A player sees the game they are playing. Crossing a door slides one room off the screen and the next one on, instead of blanking for 0.35 s. A pickup that falls out of a smashed pot lies where the pot was and is collected by walking onto it, instead of appearing in a fixed row under the currency readout and never being collectable. Shop stock and the boss reward sit on the floor of the room, clear of the furniture, and a shop slot says what it costs and whether it needs a key. The wall the player can see is the wall the player stops at. And the states that decide whether a player lives — invulnerability, the dodge roll, being down, and an enemy winding up to hit them — are visible in the world instead of being invisible model fields.

## Scope
- SB-001: The five M13 roadmap rows (`docs/roguelike-dungeon-crawler-roadmap.md` lines 155-159), each discharged by a committed production frame a human looked at, in addition to its behavioural test.
- SB-002: A departed-room identity carried by the camera transition is in scope as a consequence of SB-001: the renderer cannot draw the room being left without being told which room that was.
- SB-003: Replacing the element type of `Model.M5ObstacleDrops` with a positioned floor-pickup record is in scope, and the positionless list is removed rather than kept beside it.
- SB-004: Promoting the room wall band out of `Render` and into `Model` is in scope as a consequence of the collider row: the drawn shell and the collider must be one value, not two agreeing ones.
- SB-005: New gameplay-visual inventory rows, their catalog entries and their performance cost drivers are in scope, because the product fails closed when those three sets disagree.
- SB-006: `src/Rogue3/PerformanceEvidence.fs` and `src/Rogue3/EvidenceCommands.fs` are in the effective touch-set even though the board item did not declare them; the extension was declared on `EHotwagner/rogue3#12` before implementation.

## Non-Goals
- SB-007: Simulating or interpolating the departed room's contents through the slide is out. The departed room is drawn as its shell — floor, wall band, doors and trapdoor fixture. Its enemies are dead by construction (a sealed doorway refuses the crossing), and its projectiles and particles are discarded on room entry today and stay discarded.
- SB-008: Changing the M6 camera contract is out. `Render.cameraOffset` still begins one full room away and settles to identity at exactly 42 ticks.
- SB-009: M14 (scripted play-through agent, reachability audit) and M15 (launch observability) are out.
- SB-010: Sprites, animation atlases and any change to the primitive-shape visual language are out (Stretch §15.3).
- SB-011: `src/Rogue3/Entities.fs`, `src/Rogue3/FloorGeneration.fs`, `src/Rogue3/Determinism.fs`, `src/Rogue3/Vec2.fs`, `src/Rogue3/Visibility.fs` and `src/Rogue3/Rogue3.fsproj` are out of the touch-set. No new compile item is added.
- SB-012: No regression of M6 layer ordering, M8 audio cues, M10 determinism and replay, M11 playability or M12 audio assets.

## User Stories
- US-001 (P1): As a player, when I walk through a door I watch the room I am leaving slide off the screen while the room I am entering slides on, so I can tell which way I went.
- US-002 (P1): As a player, when I smash a pot and it drops a coin, the coin lies where the pot was and I pick it up by walking over it.
- US-003 (P1): As a player, when I stand in a shop I can see what each slot costs and which slot needs a key, and the stock is on the floor of the room rather than on top of the furniture.
- US-004 (P1): As a player, I stop at the wall I can see; I never end up standing inside the stone band the game draws.
- US-005 (P1): As a player, I can see when I am invulnerable, when I am rolling, when I am down, and when an enemy is winding up to hit me — without reading a HUD number for any of it.
- US-006 (P2): As a maintainer, if I delete the boss-room minimap colour or the heart glyphs, the visual coverage audit goes red, instead of staying complete because the whole HUD is one catalogue row.

## Acceptance Scenarios
- AC-001 [US-001] [FR-001]: Given a player standing in a doorway of a room with an open door, when the production fixed step raises the crossing, then the resulting model carries an active camera transition whose direction is the direction crossed and whose departed-room id is the room the player was standing in.
- AC-002 [US-001] [FR-002]: Given a model with an active camera transition at any tick strictly before 42, when the production view is projected, then it contains both the entered room's world content and a departed-room shell, and the two are exactly one playfield width or height apart along the slide axis; and given a model with no camera transition, no departed-room shell is drawn at all.
- AC-003 [US-001] [FR-003]: Given the M6 camera contract, when `Render.cameraOffset` is evaluated at tick 0, at tick 41 and at tick 42 of an east slide, then it is `vec2 playfieldWidth 0.0`, non-zero, and `zero` respectively — unchanged by this work item.
- AC-004 [US-002] [FR-004]: Given a destructible obstacle that rolls a drop when it is destroyed, when it is destroyed through the production reducer, then the resulting drop carries the destroyed obstacle's world position, and `Model.M5ObstacleDrops` has no positionless representation left in the product.
- AC-005 [US-002] [FR-005]: Given a floor pickup lying in the room, when the player is driven onto it with movement keys and ticks alone, then the pickup is removed from the model and its effect is applied exactly once — coins, keys and bombs credited against the 99 cap, half-red and soul hearts added to health — and driving over the same spot again credits nothing.
- AC-006 [US-002] [FR-006]: Given floor pickups in a room, when the production renderer projects them, then each is drawn at its own world position, no two share a position, and every one is inside the wall band and clear of every HUD region.
- AC-007 [US-003] [FR-007]: Given every room of every generated floor a run can reach, when the shop slots and the reward pedestal are placed, then each placed position is inside the wall band, clear of every doorway opening plus its apron, clear of the trapdoor footprint, and does not overlap any obstacle in that room.
- AC-008 [US-003] [FR-008]: Given a generated three-slot shop, when it is rendered, then each slot's drawn scene carries its price as text and a distinct lock mark exactly when `KeyLocked` is true, and a slot whose offer is `Empty` is visually distinct from one that still has stock.
- AC-009 [US-004] [FR-009]: Given the room wall slabs the renderer draws, when the player is driven at each of the four walls with movement keys and ticks alone until the velocity settles, then the player circle never intersects any wall slab.
- AC-010 [US-004] [FR-010]: Given the same room, when the player is driven at a doorway with an open door, then the crossing still fires — the wall band must not seal the openings it is drawn with — and the existing M11 traversal, key-door and descent routes stay green.
- AC-011 [US-004] [FR-011]: Given the drawn wall shell and the player's collider, when both are computed for the same model, then they are derived from one exported function, so a change to one cannot leave the other behind.
- AC-012 [US-005] [FR-012]: Given a player with post-hit invulnerability, a player mid dodge-roll, a downed player, and an enemy in a wind-up state, when each is projected through the production renderer, then each emits its own named world-space element positioned on the actor it describes, and no two of them are byte-identical scenes.
- AC-013 [US-006] [FR-013]: Given the gameplay-visual inventory, when it is enumerated, then the HUD is represented by separate hearts, currency, active-charge, minimap and floor-banner rows rather than one `HudScore` row, and the composed HUD scene handed to the viewer is unchanged.
- AC-014 [US-006] [FR-014]: Given every new gameplay-visual element, when the coverage gate and the performance cost-driver gate run, then the runtime inventory, the committed catalog and the performance driver set agree exactly, and each new element's representative scene is distinct from every other element's.
- AC-015 [US-001] [FR-015]: Given the M13 render-and-look harness, when it is run against the built product, then it writes committed production frames for a mid-crossing slide, positioned pickups, a priced and locked shop with its reward, the wall-clamped player, and the four world-space state visuals; each frame is produced by `Render.toPng` over `View.view` and is inspected by a human before ship.
- AC-016 [FR-016]: Given the full Release suite and `Verify`, when they run against the exact candidate, then every M6, M8, M10, M11 and M12 obligation stays green, every workload's p95/p99/scene-node/catch-up budget holds, and every moved workload definition digest is re-declared with the value the run emits.

## Functional Requirements
- FR-001: `Model.M6CameraTransition` MUST carry the id of the room the crossing departed, and the production `TraverseDoor` transition MUST start the slide rather than suppress it. (covers AC-001)
- FR-002: The production view MUST draw the departed room's shell — floor background, wall band, door presentations and trapdoor fixture — one playfield away along the slide axis for the whole life of the transition, and MUST draw nothing extra when no transition is active. (covers AC-002)
- FR-003: `Render.cameraOffset` MUST keep the M6 contract: one full playfield offset at tick 0, non-zero before settle, `zero` at 42 ticks. (covers AC-003)
- FR-004: `Model.M5ObstacleDrops` MUST hold positioned floor pickups whose position is the world position of the obstacle that dropped them; no positionless drop representation may remain in the product. (covers AC-004)
- FR-005: Walking the player onto a floor pickup through the production input route MUST remove it and apply its effect exactly once, honoring the 99 currency cap and the health rules already in the product. (covers AC-005)
- FR-006: The renderer MUST draw each floor pickup at its own world position, inside the wall band and clear of every HUD region. (covers AC-006)
- FR-007: Shop slots and the reward pedestal MUST be placed by a room-owned, deterministic placement function whose results are inside the wall band, clear of every doorway plus apron, clear of the trapdoor footprint, and non-overlapping with the room's obstacles, for every room of every reachable floor. (covers AC-007)
- FR-008: A rendered shop slot MUST show its price and MUST carry a distinct mark when it is key-locked, and an emptied slot MUST be visually distinct from a stocked one. (covers AC-008)
- FR-009: The room wall band MUST be a collider for the player: no reachable player position may intersect a drawn wall slab. (covers AC-009)
- FR-010: The wall collider MUST leave every doorway opening passable, so traversal, key-door unlocking and descent stay reachable through the input route. (covers AC-010)
- FR-011: The drawn wall shell and the player's wall collider MUST both be derived from one exported geometry function, not from two independently maintained descriptions. (covers AC-011)
- FR-012: Player invulnerability, the dodge roll, the downed state and an enemy wind-up MUST each have their own named world-space gameplay-visual element, drawn at the actor it describes. (covers AC-012)
- FR-013: The HUD MUST be inventoried as separate hearts, currency, active-charge, minimap and floor-banner elements, while the composed HUD scene the viewer receives stays byte-identical to today's. (covers AC-013)
- FR-014: The runtime gameplay-visual inventory, the committed element-visual catalog and the performance cost-driver visual set MUST agree exactly, and every element's representative scene MUST be distinct from every other element's. (covers AC-014)
- FR-015: A committed render-and-look harness MUST produce production frames for every row of this work item through `Render.toPng` over `View.view`, and those frames MUST be inspected before ship. (covers AC-015)
- FR-016: The full Release suite, the `Verify` route and the typed performance evidence MUST be green against the exact candidate, with every moved workload definition digest re-declared from the emitted value. (covers AC-016)

## Ambiguities
- AMB-001: How much of the departed room to draw — the full room including its obstacles, or its shell.
- AMB-002: What "the player cannot stand inside the wall band" means at a doorway whose door is locked, hidden or combat-sealed.
- AMB-003: Whether the pickup collection rule also applies to the room-clear drop (`M5Room.Drop`) and the shop stock, or only to obstacle drops.
- AMB-004: Whether the floor banner is a required cost driver in a steady-state workload.

## Public Or Tool-Facing Impact
- `Model.M6CameraTransition` gains a field; `Model.M5ObstacleDrops` changes element type. Both are public record surfaces of the product's model, so this is a tier-1 change and the canonical determinism encoding of every model moves with it.
- New public functions on `Model` (room wall slabs, pickup placement) and on `Render` (departed-room shell).
- `GameplayVisualInventory.GameplayVisualElement` gains cases and loses `HudScore` — the product's declared visual surface.
- Every workload definition digest in `readiness/performance-evidence.json` and `readiness/performance-intent.yml` moves, because `definitionDigest` folds in `Determinism.encode` of the workload's initial model.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd clarify --work 014-m13-room-transition-pickups-world-state`.
- The four ambiguities above are blocking and are resolved in `clarifications.md` before `plan`.
