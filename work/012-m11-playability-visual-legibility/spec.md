---
schemaVersion: 1
workId: 012-m11-playability-visual-legibility
title: M11 Playability Visual Legibility
stage: specify
changeTier: tier1
status: specified
publicOrToolFacingImpact: true
---

# M11 Playability Visual Legibility Specification

Prose status: specified

## User Value
A player can boot Hollow Depths, see the room they are standing in and its exits, walk through a door into the next room and back again, spend a key on a locked door, and see and use the trapdoor that takes them down a floor. Before this work the starting room was a sealed, featureless box: its door list was empty, no door or wall was drawn, and no key reached any exit.

## Scope
- SB-001: The eleven M11 roadmap rows - production-input-reachable door traversal and key-door unlocking, one door model driving per-wall directional rendering with a distinct visual per door state including LockedKey, BossDoor and HiddenWall, a complete and visually confirmed gameplay-visual inventory with committed render-and-look evidence and an independent visual-coverage critic, a starting room whose exits come from the floor graph, a trapdoor-guarded DescendFloor, an end-to-end trapdoor reachability proof, a boot-to-cross-a-door-and-return production journey, and journey events for door traversal and trapdoor descent.
- SB-002: Room wall rendering is in scope as a consequence of SB-001: a door cannot be drawn at its own wall when no wall is drawn, and the room currently renders as an unbounded void.

## Non-Goals
- SB-003: Every Stretch deferred post-v1 row is out, including the sprite and animation atlas that would replace primitive shapes, render interpolation, and item synergy graphs.
- SB-004: The audio assets that fail to resolve at launch are out; they are reported as a finding and routed as separate roadmap work.
- SB-005: No regression of M10: the canonical determinism encoder, replay, and the 24 acceptance scenarios stay green. Digests invalidated by a model or floor shape change are re-derived and reviewed, never suppressed.

## User Stories
- US-001 (P1): As a player who just booted the game, I can see the room I am in - its walls, its exits, and the objects in it - and I can tell an open door from a locked one.
- US-002 (P1): As a player, I can walk into a doorway and end up in the next room, and walk back the way I came.
- US-003 (P1): As a player holding a key, I can open a key-locked door by walking into it, and it costs me exactly one key.
- US-004 (P1): As a player who has beaten the floor boss, I can see the trapdoor that appeared and descend by standing on it, and I cannot descend from anywhere else.
- US-005 (P2): As a maintainer, an action a player can take that nobody wired up shows up as an unbound journey event instead of being inexpressible.

## Acceptance Scenarios
- AC-001 [US-002] [FR-001]: Given the booted production model, when a scripted sequence of production KeyChanged and Tick messages walks the player into a doorway of the starting room, then Floor.CurrentRoom changes to the room behind that door and the player stands at the reciprocal doorway, with no test invoking TraverseDoor directly.
- AC-002 [US-003] [FR-002]: Given the player holds one key and stands in a room with a LockedKey door, when production key and tick messages walk the player into that doorway, then both reciprocal door records become Open and exactly one key is spent, and repeating the approach with zero keys changes nothing.
- AC-003 [US-001] [FR-003]: Given any loaded room, when the production frame is rendered, then every drawn door is derived from that room's FloorGeneration.Door records, and no rendered door is produced from a door description the floor graph does not contain.
- AC-004 [US-001] [FR-004]: Given a room with doors on more than one wall, when the production frame is rendered, then each door is drawn on the wall its Direction names, and each of Open, LockedKey, BossDoor and HiddenWall plus the combat-sealed presentation produces a visually distinct element with its own stable handle.
- AC-005 [US-001] [FR-005]: Given the production-owned gameplay-visual inventory, when the coverage gate runs, then every declared gameplay element resolves to a handle that representative production rendering exercises, the catalog agrees, and the audit is complete with no missing, stale, unbound, unobserved or unsupported-hidden element.
- AC-006 [US-001] [FR-006]: Given the shipped renderer, when the render-and-look evidence is produced, then a committed PNG exists for each relevant room and door state, each was visually inspected, and an independent visual-coverage critic recorded a verdict outside the authored tree.
- AC-007 [US-001] [FR-007]: Given the model a player boots into, when the starting room is inspected, then its room state carries one entry per floor-graph door of the current room rather than an empty list, and the rendered frame shows those exits.
- AC-008 [US-004] [FR-008]: Given a room with no trapdoor fixture, or a player not standing on the trapdoor, when DescendFloor is applied, then the floor index does not change; and given the trapdoor fixture exists and the player is standing on it, then the floor index advances by one.
- AC-009 [US-004] [FR-009]: Given a freshly generated floor, when the player crosses doors from the starting room to the boss room and defeats the boss through the production route, then the boss room gains exactly one trapdoor fixture, the trapdoor is drawn in the production frame, and the player can descend by standing on it.
- AC-010 [US-002] [FR-010]: Given the production journey runner, when the boot-to-return journey script is issued, then the run receipt shows the player booted, moved, crossed a door into another room and crossed back, with every issued event mapped to a production message and none unbound.
- AC-011 [US-005] [FR-011]: Given the journey event vocabulary, when a door-traversal or trapdoor-descent event is issued to a scenario that does not bind it, then the run reports JourneyDispatch.Unbound naming that event rather than the event being inexpressible.

## Functional Requirements
- FR-001: The production input route MUST be able to cross a door: player proximity to a usable doorway plus production movement input MUST produce the same room transition the TraverseDoor message performs, reached from KeyChanged and Tick alone. (covers AC-001)
- FR-002: The production input route MUST be able to unlock a LockedKey door, spending exactly one key, opening both reciprocal door records, and never charging twice or charging when no key is held. (covers AC-002)
- FR-003: Rendering MUST derive every door it draws from the current room's FloorGeneration.Door records; any parallel door description MUST be derived from those records or removed. (covers AC-003)
- FR-004: Each door MUST be drawn on the room wall its Direction names, and each door state - Open, LockedKey, BossDoor, HiddenWall, and the combat-sealed presentation - MUST produce a visually distinct rendered element with its own stable handle. (covers AC-004)
- FR-005: Every gameplay object a player must act on MUST be declared in the production-owned gameplay-visual inventory, resolve to a handle exercised by representative production rendering, and pass the coverage audit as complete. (covers AC-005)
- FR-006: The work MUST commit production-frame PNGs covering each relevant room and door state, record that they were visually inspected, and obtain an independent visual-coverage critic verdict persisted outside the authored tree. (covers AC-006)
- FR-007: The model a player boots into MUST populate the current room's state from the floor graph, so the starting room presents its real exits instead of an empty door list. (covers AC-007)
- FR-008: DescendFloor MUST require the current room to carry a trapdoor fixture and the player to be standing on that trapdoor, and MUST leave the model unchanged otherwise. (covers AC-008)
- FR-009: The trapdoor MUST be reachable end to end: the boss room that creates it MUST be reachable by crossing doors from the starting room, the fixture MUST be drawn whenever it is present, and descending MUST be possible from it. (covers AC-009)
- FR-010: A runner-issued production journey MUST prove boot, move, cross a door, and return through the real input route, with every issued event mapped to a production message. (covers AC-010)
- FR-011: The journey event vocabulary MUST express door traversal and trapdoor descent as distinct events, so a scenario that does not bind them reports JourneyDispatch.Unbound for that event. (covers AC-011)

## Ambiguities
- AMB-001: Whether crossing a door is triggered by walking into the doorway, by a dedicated interact key, or by both.
- AMB-002: Whether the HiddenWall door state is produced by floor generation for still-hidden secret rooms, or remains a state only the renderer and its evidence fixtures exercise.
- AMB-003: What "the player is using the trapdoor" means precisely for the DescendFloor guard - standing within the fixture's drawn bounds, or a separate confirm input.
- AMB-004: Whether the production input route reaching a door transition literally dispatches the TraverseDoor message, or calls the shared reducer body the message calls.

## Public Or Tool-Facing Impact
- Extends the product-owned journey event vocabulary and the gameplay-visual inventory, changes the rendered element set and the floor door graph, and adds fixed-step door handling. No framework package API changes. Authored workload and UI-route digests move and are re-derived.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd clarify --work 012-m11-playability-visual-legibility`.
