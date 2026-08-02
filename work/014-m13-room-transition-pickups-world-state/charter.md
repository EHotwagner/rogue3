---
schemaVersion: 1
workId: 014-m13-room-transition-pickups-world-state
title: M13 Room Transition Pickups And World Space State
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

# M13 Room Transition Pickups And World Space State Charter

## Identity
- Work id: `014-m13-room-transition-pickups-world-state`
- Lifecycle stage: charter
- Status: chartered
- Close the five defects M11's committed production frames exposed
  (`readiness/012-m11-playability-visual-legibility/frames/`), tracked as board item
  `EHotwagner/rogue3#12`. Every one is a thing a player sees and no requirements-derived test can
  reach: a door crossing that renders an empty screen, pickups and shop stock parked at fixed
  coordinates instead of placed in the room, a drawn wall the player can stand inside, and
  player/enemy state with no world-space visual at all.

## Principles
- **The frame is the evidence.** Each row came from looking at a rendered frame, so each row is
  discharged by a committed frame that a human looked at, produced by `Render.toPng` over the
  production `View.view` projection — not by a scene-node count or an element-id assertion alone.
  A structural test that cannot fail when the screen is blank is not evidence for this milestone.
- **One geometry, two consumers.** The wall the renderer draws and the wall the player collides with
  must be the *same* value. A collider derived independently from the drawn shell is the defect one
  layer down: the two drift and the frame stops telling the truth about the simulation.
- **A visual is inventoried at the granularity at which it can rot.** `HudScore` is one catalogue row
  covering hearts, currency, charge, banner and the whole minimap, so deleting the boss-room minimap
  colour leaves the coverage audit complete. Split the row until the audit notices.
- **No second generation of world state.** Positioned pickups replace the positionless list; they do
  not sit beside it. `EHotwagner/rogue3#20` is already open against exactly that pattern in this
  product, and this work item must not add a third instance of it.
- **The camera contract is kept, not re-derived.** `Render.cameraOffset` begins one room away and
  settles to identity at 42 ticks; `M6RenderingEnemySymbologyTests` pins that. The fix is to draw the
  departed room, not to shorten the slide.
- M10 determinism, M11 playability and M12 audio-asset gates stay green. The canonical encoder is
  reflective, so every model change moves the workload definition digests; each moved digest is
  re-reviewed and re-declared rather than suppressed.

## Scope Boundaries
- In: the five M13 roadmap rows (`docs/roguelike-dungeon-crawler-roadmap.md` lines 155-159).
- In (consequential): a departed-room identity on the camera transition, so the renderer can know
  which room to draw behind the slide; a positioned floor-pickup type replacing the positionless
  `M5ObstacleDrops` element type, and the collection rule that makes walking onto one mean something;
  a room-owned placement function for shop slots and the reward pedestal; room wall slabs promoted
  out of the renderer into the model so the player sweep and the drawn shell share one value; new
  gameplay-visual inventory rows, their catalog entries and their performance cost drivers.
- In (consequential): the M13 render-and-look harness and its committed frames; refreshed
  performance evidence, intent and workload authorship declarations.
- Out: interpolating the slide's contents (enemies, particles and projectiles of the departed room).
  The departed room is drawn as its **shell** — floor, wall band, doors and trapdoor fixture — which
  is what fills the screen during a crossing. Re-simulating a room the player has left is out.
- Out: M14's scripted play-through agent and reachability audit, and M15's launch observability.
- Out: replacing the primitive-shape visual language with sprites (Stretch §15.3).
- Out: `src/Rogue3/Entities.fs`, `src/Rogue3/FloorGeneration.fs`, `src/Rogue3/Determinism.fs`,
  `src/Rogue3/Vec2.fs`, `src/Rogue3/Visibility.fs` and `src/Rogue3/Rogue3.fsproj`. The last three are
  the touch-set of the concurrently claimed `EHotwagner/rogue3#28`; the first three are not needed —
  every new type lands in `Model.fs` and no new compile item is added.

## Policy Pointers
- SDD policy comes from `.fsgg/sdd.yml` and `.fsgg/agents.yml`.
- Governance files are optional compatibility pointers and are not evaluated by this command.
- Honor constitution principles I, II, III, V, VI and VIII.
- Honor the M10 determinism contract in `src/Rogue3/Determinism.fs` (consumed, never edited).
- Honor the M6 camera contract asserted in `tests/Rogue3.Tests/M6RenderingEnemySymbologyTests.fs`
  lines 72-81 and the M11 one-door model documented in `src/Rogue3/Render.fs` lines 270-279.
- Honor the performance-first planning gate in `.agents/skills/pnext-item/references/performance-first.md`
  against the active typed intent `readiness/performance-intent.yml`.

## Deferrals Received
- M11 deferred all five rows out of `012-m11-playability-visual-legibility`, recording the reason in
  `src/Rogue3/Model.fs` lines 2060-2066 (the slide is not started because nothing draws the departed
  room), `src/Rogue3/Render.fs` lines 510-512 (`M5ObstacleDrops` carries no world position) and the
  roadmap's M13 reason paragraph. This work item discharges that deferral.

## Lifecycle Notes
- Tier 1: it changes the `Model` record's public field types, adds public functions to `Model` and
  `Render`, and changes the gameplay-visual inventory — the product's declared visual surface.
- The item's declared `Paths:` is incomplete for its own rows. `src/Rogue3/PerformanceEvidence.fs`
  fails closed when the performance cost-driver `VisualElement` set differs from
  `GameplayVisualInventory.all`, and both it and `src/Rogue3/EvidenceCommands.fs` construct
  `M5ObstacleDrops`. The extension was declared on the item before implementation
  (`EHotwagner/rogue3#12` comment) and is uncontended against the only other in-flight item.
- Expect every workload definition digest to move: `definitionDigest` folds in
  `Determinism.encode (workload.InitialState())`, and the encoder is reflective over the whole model.
- Next lifecycle action: `fsgg-sdd specify --work 014-m13-room-transition-pickups-world-state`.
