---
schemaVersion: 1
workId: 012-m11-playability-visual-legibility
title: M11 Playability and Visual Legibility
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

# M11 Playability and Visual Legibility Charter

## Identity
- Make Hollow Depths playable and legible: a player who boots the game can see the room they are in, see its exits, walk through one, come back, and see and use the trapdoor that takes them down a floor. A human launched the shipped build and reported "still cant leave. no doors visible"; this work item closes that gap and the visual-legibility gap behind it.

## Principles
- A row is done only when the behaviour is reachable from the production input route. Asserting a reducer by calling `update` directly does not discharge a reachability claim.
- One door model. The floor graph (`FloorGeneration.Door`, carrying `Direction` and `DoorState`) is the single source of door truth; anything else that describes a door is derived from it or deleted.
- Every gameplay object a player must act on is visible in the production frame, and the proof is a rendered frame a human looked at — not a node count.
- The journey event vocabulary must be able to *express* every user-facing action, so an unwired action reports `JourneyDispatch.Unbound` instead of being inexpressible.
- M10 determinism, the canonical encoder, replay and the 24 acceptance scenarios stay green. Digests invalidated by a model or floor-shape change are re-derived and reviewed, never suppressed.

## Scope Boundaries
- In: the eleven M11 roadmap rows — production-input-reachable door traversal and key-door unlocking; a single door model driving rendering; per-wall directional door rendering with a distinct visual per `DoorState`; a complete and visually confirmed gameplay-visual inventory; committed render-and-look PNGs with an independent visual-coverage critic; a starting room with real exits; a `DescendFloor` guarded by the trapdoor it depicts; end-to-end trapdoor reachability; a boot-to-cross-a-door-and-return production journey; and journey events for door traversal and trapdoor descent.
- In (consequential): room wall rendering, because a door cannot be drawn "at its own wall" when no wall is drawn, and the starting room currently renders as an unbounded void.
- Out: audio asset resolution failures observed at launch (`title-theme`, `floor-1-theme`, `dodge-roll`, `player-hit`, `bomb-explosion` resolve to `None`) — reported as a finding and routed as separate roadmap work.
- Out: every "Stretch — deferred (post-v1)" roadmap row (§15), including the sprite/animation atlas that would replace primitive shapes.

## Policy Pointers
- Honor constitution principles I, II, IV, V, VI and VIII; source specification sections 4.8, 12, 13 and 14, in particular §14.15 (door traversal) and §14.16 (key doors).
- Honor the M10 determinism contract in `src/Rogue3/Determinism.fs` and the replay contract in `src/Rogue3/Replay.fs`.

## Lifecycle Notes
- Tier 1 product behaviour work: it changes the production render surface, the floor door graph, the fixed-step input route and the journey event vocabulary, so signatures, tests, digests and evidence move together.
- Model-shape and floor-shape changes invalidate authored workload and UI-route digests. Expect to re-derive, review and copy them as M10 did.
- Next lifecycle action: `fsgg-sdd specify --work 012-m11-playability-visual-legibility`.
