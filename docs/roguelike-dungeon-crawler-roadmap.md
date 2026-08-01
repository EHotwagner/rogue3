# Hollow Depths — Milestone Roadmap

Source: https://github.com/FS-GG/FS.GG.Game/blob/main/docs/TestSpecs/Games/roguelike-dungeon-crawler.md

This local roadmap is the implementation ledger for the source specification. Milestones are sequential. Preserve milestone text and append concise merge, test, SDD, and feedback evidence when marking work complete.

**Legend:** 🟥 Not started · 🟨 In progress · 🟩 Done · ⬜ Deferred (post-v1)

### M0 — Scaffold & fixed-step loop
- 🟩 Project scaffold: `Model`/`Msg`/`update`/`view` skeleton (§7)
- 🟩 Fixed 120 Hz sim via `FixedStep.drainWith`, `MAX_STEPS = 5` guard, banked accumulator (§7.3, §13) — AC #8
- 🟩 `Rng` (splitmix64) seeded, `LayoutRng`/`DropRng` sub-streams via `Rng.split` (§13)
- 🟩 Logical 1280×720 coordinate system + world→screen transform (§6, §8)

Evidence (2026-08-01): focused Release 24/24 and full Release 71/71; `./fake.sh build -t Verify` green with five bounded-headless workloads; SDD verdict `readiness/001-m0-scaffold-fixed-step-loop/ship-verdict.json` is `shipReady` with 21 observed, non-synthetic obligations; cycle feedback is `feedback/2026-08-01-Rogue3.md`.

### M1 — Input & twin-stick control
- 🟩 `InputState` snapshot + `PressedThisTick` edge set `(currentKeys − previousKeys)` (§3, §7.3)
- 🟩 Keyboard/mouse + gamepad move & aim, fully decoupled (§3) — AC #9
- 🟩 Auto-repeat fire cadence + 8-way arrow-aim snap vs 360° analog aim (§3, §4.3)

Evidence (2026-08-01): focused Release M1 input 9/9 and full Release 80/80; `./fake.sh build -t Verify` green with 80/80 plus five authored bounded-headless workloads; SDD verdict `readiness/002-m1-input-twin-stick-control/ship-verdict.json` is `shipReady` with 26 observed, non-synthetic obligations; cycle feedback is `feedback/2026-08-01-Rogue3-2.md`. Keyboard and coordinate-bearing pointer samples traverse the pinned live shell host; the pure gamepad snapshot contract is green, while native gamepad polling remains a package-host release obligation because the pinned host exposes no gamepad seam.

### M2 — Movement, dodge & shots
- 🟩 Velocity lerp (`accel`/`friction`) + diagonal normalization, speed clamp (§4.1)
- 🟩 Axis-separated wall/obstacle sweep, circle hitbox `r = 13` (§4.1)
- 🟩 Dodge roll: i-frames, velocity impulse, `0.90 s` cooldown, fire lockout (§4.2)
- 🟩 Stat-derived shots (dmg/fireRate/shotSpeed/range/size) + velocity inheritance (§4.3)
- 🟩 Multishot `18°` spread fan centered on aim (§4.3) — AC #4
- 🟩 Shot lifetime/range, bounce, pierce & homing termination (§4.3) — AC #10

Evidence (2026-08-01): focused Release M2 11/11 and full Release 91/91; `./fake.sh build -t Verify` green with five authored runner-issued workloads and exact maximum-content scale (40 shots, 8 obstacles, 30 targets, 736 wall primitives, 2,400 homing considerations, multishot 3); final fresh-context performance critic supported on repair round 3; SDD verdict `readiness/003-m2-movement-dodge-shots/ship-verdict.json` is `shipReady` with 30 observed non-synthetic obligations; cycle feedback is `feedback/2026-08-01-Rogue3-3.md`.

### M3 — Combat, health & currency
- 🟩 Shot→enemy circle overlap: `dmg`, knockback, hit-flash, pierce decrement (§4.4)
- 🟩 Enemy/bullet→player damage with i-frame + `0.80 s` post-hit invuln gating (§4.4, §4.6) — AC #6
- 🟩 Half-heart health (red/soul/black), damage resolution & death at `0` (§4.6)
- 🟩 Player stats recompute: additive-then-multiplicative phases + clamps (§4.5) — AC #3
- 🟩 Coins/keys/bombs currencies (cap `99`), bomb drop/blast & shop purchase (§4.4, §4.7) — AC #11
- 🟩 Contact damage on overlap: `contactDmg`, `0.5 s` per-enemy re-tick cap, knockback `90 px/s` (§4.4)
- 🟩 `SpatialGrid.build 64.0` broadphase for shot↔enemy / bullet↔player queries (§13)
- 🟩 Heart types: soul/black stacking, black-heart depletion burst, 12-wide display cap, descent persistence (§4.6)
- 🟩 Bomb chain-detonation + currency cap-overflow waste (`99` cap) (§4.7, §4.4)

Evidence (2026-08-01): focused Release M3 9/9 and full Release/Verify 99/99; five runner-issued bounded-headless workloads retain exact M2 gates (40 shots, 8 obstacles, 30 targets, 736 wall primitives, 2,400 homing considerations, multishot 3) and add 30 enemies, 120 bullets, and 2,520 exact combat candidates including 240 bullet candidates; final fresh-context performance critic supported on the third repair round; SDD verdict `readiness/004-m3-combat-health-currency/ship-verdict.json` is `shipReady` with five observed non-synthetic obligations; cycle feedback is `feedback/2026-08-01-Rogue3-4.md`.

### M4 — Procedural floor generation
- 🟩 Seed derivation `floorSeed = split(runSeed, floorIndex)` on `LayoutRng` stream (§4.8, §13) — AC #2
- 🟩 Room budget + branching placement walk with bounded re-roll (§4.8)
- 🟩 Special-room assignment: boss/treasure/shop/secret on the placed graph (§4.8)
- 🟩 Room interior population by template + threat budget `6 + 2*floorIndex` (§4.8)
- 🟩 Door carving between orthogonally adjacent rooms (§4.8) — AC #1
- 🟩 Secret / super-secret reveal by bombing an adjacent wall; atomic door-graph update (§4.8, §13) — AC #14
- 🟩 Floor descent: trapdoor spawns on boss clear, `DescendFloor` regenerates next floor & carries player, drops room state (§7.3, §4.8)

Evidence (2026-08-01): focused Release M4 6/6 and full Release/Verify 105/105; six runner-issued bounded-headless workloads include production `DescendFloor` generation at the exact 20-room cap with p95/p99 and catch-up budgets green; SDD verdict `readiness/005-m4-procedural-floor-generation/ship-verdict.json` is `shipReady` with 10 observed non-synthetic obligations; cycle feedback is `feedback/2026-08-01-Rogue3-5.md`. Delivery is local-only because this checkout has no configured remote.

### M5 — Entities: enemies, bosses & rooms
- 🟩 Enemy roster + per-enemy state machines (e.g. Charger WindUp→Dash→Recover) (§5.2)
- 🟩 Boss phases & data-driven declarative bullet patterns (§5.3)
- 🟩 Room-clear gating: seal doors on entry, open + drop-roll on clear (§7.3) — AC #5
- 🟩 Weighted pickup/drop tables via `DropRng` sub-stream (§4.9)
- 🟩 Per-floor difficulty ramp: threat budget + enemy HP/bullet scaling (§6, §12)
- 🟩 Enemy behavior params: Brute ground-pound, bounded Grub split, Spitter/Turret/Caster/Fly patterns, enemy bullet base `180 px/s` (§5.2)
- 🟩 Obstacles: rock/tinted-rock/pot/spikes/pit collision, destructibles + drop tables via `DropRng`, spikes hazard, pit fly-over (§5.5, §4.1, §4.9)
- 🟩 Run item pool: treasure pedestal + boss floor-reward from `LayoutRng`, dupe-free per run (§4.11, §5.3) — AC #12
- 🟩 Shop room: item/consumable slots, `LayoutRng` pricing, key-locked items, no in-floor restock (§4.11, §4.7) — AC #11

Evidence (2026-08-01): full Release and Verify 123/123; six runner-issued bounded-headless workloads retain all prior exact gates and add 30 live M5 actors spanning eight kinds, 60 AI decisions/frame, five typed obstacles, three shop slots, one phase-three Maw emission, and eight source-specific boss projectiles; SDD verdict `readiness/006-m5-entities-bosses-rooms/ship-verdict.json` is `shipReady` with 13 observed non-synthetic obligations; cycle feedback is `feedback/2026-08-01-Rogue3-6.md`. Delivery is local-only because this checkout has no configured remote.

### M6 — Rendering & enemy symbology
- 🟩 Back-to-front layer draw order (background → HUD → overlays) (§8)
- 🟩 `Enemy → Token` ChannelMap in `FS.GG.Game.Render`, `Symbology.token` grammar (§8.1)
- 🟩 Legibility linter assertion pinned to the accepted `Size` channel (§8.1)
- 🟩 Pooled particles (cap `600`) + room-transition camera slide `0.35 s` (§8)

Evidence (2026-08-01): full Release 128/128; six runner-issued bounded-headless workloads green; the production-derived 37-element visual inventory and same-frame Token/Badge/Ring raster evidence are complete; the accepted linter result has exactly one `Warning Size` and zero errors; maximum-content evidence retains 600 particles, eight token encodings, eleven layers, and an active camera transition with p95 14.125 ms and p99 15.544 ms below budget; SDD verdict `readiness/007-m6-rendering-enemy-symbology/ship-verdict.json` is `shipReady` with 10 supported observed declarations and 20/20 ready obligations; cycle feedback is `feedback/2026-08-01-Rogue3-7.md`. Delivery is local-only because this checkout has no configured remote.

### M7 — UI, menus & stats
- 🟩 HUD: hearts row, currency, active-item charge meter, minimap, floor name (§9)
- 🟩 Adopt the generic FS.GG game shell (FS-GG/FS.GG.Rendering#991): main menu (title + Start/Config/Exit), Esc pause routing, Settings with screen resolution + fullscreen, and in-game key rebinding of the §3 controls, persisted — the game provides its name + key→command map + play update/view; the shell provides the rest, no bespoke menu system (§9.1)
- 🟩 Game-specific rows over the shell (run management, difficulty mode, volume/sound, screen shake) apply live + persist to `MetaProfile` (§9.1, §12, §13)
- 🟩 Stats & charts screen: KPI tiles + depth histogram + damage-per-floor line (§9.2)
- 🟩 Difficulty-mode scaling table (Easy/Normal/Hard) latched at `StartRun` (§12, §9.1) — AC #13

Evidence (2026-08-01): full Release 140/140 and focused M7 12/12; six governed bounded-headless workloads plus fail-closed menu/HUD/stats routes green; responsive HUD rasters at 1280×720 and 1920×1080 have non-overlapping anchors, and the stats raster shows four KPI tiles, five depth buckets, and distinct Dealt/Taken traces; exact implementation SHA `99a0c2d5458c1889e420b28b5941273359f51521` was accepted by independent functional and performance critics; SDD verdict `readiness/008-m7-ui-menus-stats/ship-verdict.json` is `shipReady` with 17/17 supported and observed obligations. Delivery is local-only because this checkout has no configured remote.

### M8 — Audio
- 🟩 `AudioEffect` cues per event, `Audio.interpret` → `AudioEvidence.Requested` (§10)
- 🟩 Per-context music loop (one track at a time), volume clamp `[0,1]` + mute (§10)

Evidence (2026-08-01): focused M8 5/5 and full Release/Verify 145/145; all 18 §10 cue IDs/volumes, ordered stop-before-loop replacements through the production shell, and clamp/mute requests are asserted at `AudioEvidence.Requested`; six unchanged-scale governed workloads pass, with maximum-content p95 15.9476 ms and p99 18.4669 ms; exact implementation SHA `b92e48e754368b4eafe57b3bb13e21235f956fe6` was accepted by independent functional and performance critics; SDD verdict `readiness/009-m8-audio/ship-verdict.json` is `shipReady` with 12/12 supported and observed obligations and zero synthetic/deferred/stale/missing evidence. Delivery is local-only because this checkout has no configured remote; device/speaker playback is not claimed.

### M9 — Win/loss & permadeath
- 🟩 Final-boss (Floor 6) defeat → `Victory` screen + unlock (§11)
- 🟩 Permadeath at `0` half-hearts → `GameOver`, run discarded (§11) — AC #7
- 🟩 Run-score tally + end-of-run meta-progression unlock evaluation (§11, §4.10)
- 🟩 `MetaProfile` JSON persistence: debounced, atomic temp-file+rename, load on boot (§13, §7.5)

Evidence (2026-08-01): focused M9/result-action Release 8/8 and full Release/Verify 153/153; an actual fixed-step player projectile defeats the floor-6 boss, fixed-step lethal health resolves GameOver, fixed-step draining stops at the first terminal step, and the production result tree visibly composes its score/stat/unlock summary with three bound actions to start a new run, retry the seed, or return to title. Both outcomes resolve once, discard transient run state, evaluate milestone unlocks, request terminal audio, and persist best-by-seed/lifetime profile facts. A unique system temporary directory proves latest-value debounce, sibling-temp atomic rename, cleanup, boot load, and safe malformed/version fallback without touching user data. Six governed workloads and four UI routes pass; batched production particles and a 720-frame maximum-content sample produce p95 13.9648 ms/p99 17.5014 ms, with three additional consecutive repetitions green, while the result route measures p95 0.3369 ms/p99 0.5735 ms at exactly 9 controls/3 actions/5 production-derived fields. UI evidence is regenerated, fail-closed, and hashed into critic input; event-driven persistence has an explicit non-frame disposition. SDD verdict `readiness/010-m9-win-loss-permadeath/ship-verdict.json` is `shipReady` with 9/9 observed obligations backed by a committed 153-pass TRX plus raw/BOM-stripped digest sidecar. Cycle feedback is `feedback/2026-08-01-Rogue3-10.md`. The accepted branch is published as PR #1 with an Actions-native Release-test gate; merge remains subject to exact-head critic confirmation and host acceptance.

### M10 — Acceptance & determinism
- 🟩 All 24 acceptance scenarios green (§14)
- 🟩 Procedural generation byte-identical for a seed (§14.1) — AC #1
- 🟩 Layout independent of combat RNG stream (§14.2) — AC #2
- 🟩 Shop/treasure/boss contents layout-deterministic & dupe-free (§14.12) — AC #12
- 🟩 Difficulty mode latches at `StartRun` and scales the sim (§14.13) — AC #13
- 🟩 Secret-room bomb-reveal updates the door graph atomically (§14.14) — AC #14
- 🟩 Seed + input-log replay is byte-identical given identical actions/timing (§13)

Evidence (2026-08-01): focused M10 Release 30/30 and full Release/Verify 183/183; all 24 §14 scenarios are one guarded Release list with one named production-driving test each, so a dropped scenario cannot pass green. "Byte-identical" is now a product-owned canonical structural encoder rather than `sprintf "%A"`, which truncates a collection after 100 elements — `[1..600]` and `[1..599] @ [999]` format to the identical 401-character string, and the maximum-content model carries 600 particles, 120 bullets and 40 shots; `PerformanceEvidence`'s model and per-frame message fingerprints moved onto the same encoder and all seven authored workload digests were re-derived, reviewed and copied. Seed plus an ordered input log replays through the production `update` to byte-identical bytes, while another seed or the same actions at different tick timings do not, and a non-canonical log is rejected. Two same-seed runs whose `DropRng` draws differ descend to byte-identical floors and, across a five-floor walk, offer identical dupe-free pedestal/shop/boss ids at identical prices. A bomb blast now reveals an adjacent secret inside the same fixed step, and the sweep walks every room asserting no door exists without its graph adjacency. Four production gaps the sweep exposed were implemented rather than stubbed: the detonation-driven reveal above (§14.14), `TraverseDoor` with durable room-clear and destroyed-obstacle state (§14.15), `UnlockDoor` with reciprocal `LockedKey` treasure doors (§14.16), and a dead actor that kept acting because cleanup read the legacy `Enemies` projection (§14.21). Seven bounded-headless workloads and four UI routes pass, all prior exact maximum-content gates retained (2,520 combat candidates, 736 wall queries, 2,400 homing considerations, 600 particles, 40 shots, 120 bullets, 30 actors, 60 decisions, 11 layers) with p95 6.7383 ms/p99 7.9070 ms in the committed artifact, and the added `secret-reveal` workload detonates exactly one staggered-fuse bomb per sampled fixed step at p95 4.5793 ms/p99 6.0677 ms with zero catch-up frames; percentiles are re-measured on every regeneration, so these are the committed candidate's samples rather than a fixed figure. SDD verdict `readiness/011-m10-acceptance-determinism/ship-verdict.json` is `shipReady` with 24/24 supported and observed obligations, zero synthetic/deferred/stale/missing, backed by a committed 183-pass TRX plus raw/BOM-stripped digest sidecar. Cycle feedback is `feedback/2026-08-01-Rogue3-11.md`. Merged as PR #2. Every deferral earlier milestones aimed at M10 — the M7/M8 acceptance sweep and the M9 replay/determinism work — is discharged here; the deferral ledger is empty.

### M11 — Playability & visual legibility
- 🟩 Room-to-room traversal is reachable from player input — a production key/pointer/proximity path dispatches `TraverseDoor`, not a test calling `update` directly (§14.15)
- 🟩 `UnlockDoor` is reachable from player input at a `LockedKey` door and spends a key (§14.16)
- 🟩 One door model: the floor graph's `FloorGeneration.DoorState` drives rendering; the parallel cosmetic `Entities.DoorState`/`M5Room.Doors` list is derived from it or removed
- 🟩 Every door renders at its own wall by `Direction` (N/E/S/W) rather than as an indexed strip at a fixed screen position, with a distinct visual per state including `LockedKey`, `BossDoor` and `HiddenWall`
- 🟩 Every gameplay object a player must act on is visually distinct and visible in the production frame — inventoried, rendered, and confirmed by looking at the frame
- 🟩 Render-and-look evidence: committed production-frame PNGs per relevant room/door state, visually inspected, with an independent visual-coverage critic
- 🟩 The starting room presents a real exit — its `M5Room.Doors` is populated from the floor graph instead of `[]`, so the room a player boots into is not a sealed box
- 🟩 `DescendFloor` is guarded by the state it depicts: it requires the room's trapdoor to exist and the player to be using it, rather than descending unconditionally from anywhere
- 🟩 Trapdoor reachability is provable end to end — the fixture is visible when present, and the boss-clear path that creates it (`Entities.bossCleared`) is reachable through doors a player can cross
- 🟩 A production journey proves boot → move → cross a door → return, through the real input route
- 🟩 Journey event vocabulary covers door traversal and trapdoor descent, so an unwired player action reports `JourneyDispatch.Unbound` instead of being inexpressible

Reason for this milestone (2026-08-01): the first attempt to actually play the game found no way out of the starting room, and the room is a sealed box by construction. `Model.fs:705-707` boots `M5Room` with `Doors=[]` and `Trapdoor=false`, so `Render.fs:292` draws no door and `Render.fs:316` draws no trapdoor. `TraverseDoor` and `UnlockDoor` are dispatched nowhere in `src/` — their only callers are `tests/Rogue3.Tests/M10AcceptanceDeterminismTests.fs`, which invokes `update` directly, so §14.15/§14.16 went green while the reducers stayed unreachable. `KeyChanged` only records into the input snapshot and dispatches nothing, so no key reaches any exit.

Doors are also drawn from a second, unrelated `Entities.DoorState = Open | LockedClear | BossSealed` list as a row of bars at a fixed `X=590+index*46, Y=48`, so they neither sit on their walls nor distinguish the floor graph's `LockedKey`/`HiddenWall` states.

Level progression passed its journeys because `JourneyEvent.Interact` maps to `DescendFloor` (`PerformanceEvidence.fs:1025`), a reducer with no preconditions — it checks neither trapdoor, nor cleared state, nor player position — dispatched from the same trapdoor-less starting room. The journey event vocabulary has no door event at all, so no `JourneyDispatch.Unbound` row could ever report the missing wiring, and the maximum-content scenario's `Interact → BeginM6RoomTransition` only sets `M6CameraTransition`, sliding the camera without changing `Floor.CurrentRoom`. M0–M10 proved simulation and evidence headlessly; no cycle ever rendered a frame and looked at it. This milestone closes the reachability and legibility gap that omission left.

Evidence (2026-08-01): focused M11 Release 16/16 and full Release/Verify 199/199. A player can now boot the game, see the room they are standing in, walk out of it and walk back: a scripted sequence of nothing but production `KeyChanged` and `Tick` messages moves the player into the starting room's north doorway, changes `Floor.CurrentRoom`, lands them at the reciprocal doorway `playerRadius + 4` inside the destination — outside the 14-deep sensor, so a crossing cannot re-trigger itself — and brings them home again. No test dispatches `TraverseDoor` to make that happen. The fixed step raises production `Msg` values and `advanceSim` folds them through `update` between steps, so there is exactly one traversal transition, `Replay.fs` needed no new entry kind, and a crossing replays from the input log alone. Walking into a `LockedKey` doorway holding one key opens both reciprocal records and spends exactly one; crossing afterwards never charges again; the same approach with no key leaves door state, room and currency untouched. Rendering reads the floor graph: every drawn door is one element per `FloorGeneration.Door` record of the current room, drawn in the doorway of the wall its `Direction` names, in six visually distinct presentations — `Open`, `LockedKey`, `BossDoor`, `HiddenWall`, and the combat and boss locks — and the room has walls for them to sit in, on the existing `FloorDecals` layer so the eleven-layer contract holds. The parallel cosmetic `Entities.DoorState` list survives only as the derived, index-aligned combat lock. `HiddenWall` stopped being a state nothing could produce: generation writes reciprocal hidden-wall doors and their graph adjacency for every pending secret and `revealSecret` flips them rather than growing a second door, so a player can see which wall to bomb and the whole-floor "no door without its graph adjacency" invariant still holds. `DescendFloor` is guarded by the state it depicts — the floor must record the `Trapdoor` fixture, the loaded room must agree, and the player must be standing on it — the trapdoor is drawn from that same predicate at the room centre, gains a distinct `TrapdoorReady` presentation with an `E DESCEND` prompt exactly when the guard would accept, and the descent cue is derived from the floor-index transition rather than from the message, so it is audible on the route a player takes and silent when a descent is refused. The boot model loads its start room through the same `loadM5Room` seam every other room uses. Trapdoor reachability is end to end: a route of crossable doors from the starting room to the boss room is found and walked, its first hop by keys and ticks, the boss falls, the floor records exactly one trapdoor, the frame draws it, and the interact key on it descends. Two runner-issued production journeys of `Start`, key edges and fixed ticks prove the crossing and the return from runner output rather than from a re-simulation. The journey's action slot had been instantiated with `unit` — a vocabulary that can express nothing, which is why the missing wiring was inexpressible; it is now a product-owned `PlayerAction` resolved against the live floor graph, and an unwired `CrossDoor` reports `JourneyDispatch.Unbound "cross-door-north"` and fails its receipt.

Two independent fresh-context critics reviewed the candidate and both returned findings, which is the milestone working rather than a footnote. The code critic measured that the `floor-descend` cue had become unreachable from the production route while a restaged test dispatched the message directly — this milestone's own defect class one layer down — that two restaged §14.18 assertions could no longer fail because entering a cleared room wipes the collections they seed, and that the FR-005 binding assertion could not fail at all because `Scene.group []` still yields a node. The visual critic, looking at the frames, found the HUD floor banner painted across the south doorway, `DoorBossDoor` drawn as a proper visual subset of `DoorBossSealed`, a trapdoor at 1.05:1 against the floor with no standing-on-it state, pickups drawn under the currency readout, and six frames whose minimap was a lie because they entered rooms by teleport rather than by crossing a door. All are fixed here, each with a regression test that fails without the fix; the remainder are filed. Rendering the frames also exposed a defect no test could have: making traversal reachable made the M6 camera slide reachable too, and it begins one full room away while nothing draws the room being left, so every door a player crossed would have blanked the screen for 0.35 seconds. Traversal no longer starts a slide, and rendering both rooms through one is filed as its own row.

Render-and-look evidence is sixteen committed production frames under `readiness/012-m11-playability-visual-legibility/frames/`: eight per relevant room and door state, and a contact sheet covering every remaining catalogued element, which is where the critic found two of the defects above. Seven bounded-headless workloads and four UI routes pass with every prior exact scale gate retained, `maximum-content` at p95 7.1233 ms/p99 8.6987 ms against a 16.67/25.0 ms budget with zero catch-up frames, a new `simulation.door-sensor-candidates` driver observed at exactly 8 per frame, and five new visual drivers at exactly one each; `scene.door-locked-clear` and `scene.door-boss-sealed` are knowingly demoted to non-performance because a room's lock seals every doorway at once and cannot co-exist with an open door in one frame. All eleven authored digests were re-derived, reviewed and copied twice — once for the implementation and once for the critics' fixes — and `main-menu`, the one UI route hashing neither `Model.fs` nor `Render.fs`, kept a byte-identical *definition digest* across both, which is the check that the digest set moved where the source moved and nowhere else. SDD verdict `readiness/012-m11-playability-visual-legibility/ship-verdict.json` is `shipReady` with 45/45 supported and observed obligations, zero synthetic or self-attested, and one accepted deferral. Cycle feedback is `feedback/2026-08-01-Rogue3-12.md`. Merged as PR #5.

### M12 — Audio assets for the cues the game already requests
- 🟥 Every sound and track id `AudioCues.fs` requests resolves to a real asset, so the cues the product already asks for are audible
- 🟥 A test fails when a requested cue id has no asset, so a silent cue cannot ship green again

Reason for this milestone (2026-08-01): launching the shipped build logs that `title-theme` did not resolve to an asset, and `assets/audio/` does not exist in the tree at all. `AudioCues.fs` resolves every id as `assets/audio/<id>.wav` and requests at least fourteen distinct ids — `shot-fire`, `shot-hit`, `enemy-death`, `player-hit`, `player-death`, `dodge-roll`, `bomb-explosion`, `boss-intro`, `door-lock`, `door-unlock`, `floor-descend`, plus `title-theme`, `floor-<n>-theme`, `shop-theme`, `boss-theme`, `game-over` and `victory` — so the whole cue set is silent, not the five ids first observed. The cue map and the request seam are correct and the host reports each miss rather than failing quietly; what is missing is the assets and a gate that notices. Deferred out of M11 (DEC-013) because it is an asset-availability fact rather than a playability or legibility defect.

### M13 — Room transition, pickups and world-space state
- 🟥 A room transition renders both the room being left and the room being entered, so a crossing reads as a slide rather than a blank screen (M11 removed the slide rather than ship the blank)
- 🟥 Obstacle drops carry a world position and can be walked onto, instead of being drawn as a fixed placeholder row
- 🟥 `RoomReward` and `ShopItem` are placed in the room rather than at fixed screen coordinates, and a shop slot shows its price and lock state
- 🟥 The drawn wall is a collider: the player cannot stand inside the wall band the renderer draws
- 🟥 Player i-frames, dodge, death and enemy telegraphs have a world-space visual, and the HUD is inventoried finer than one `HudScore` row

Reason for this milestone (2026-08-02): every row here was found by an independent visual-coverage critic looking at M11's production frames, and none of them is reachable by a requirements-derived test. `Render.cameraOffset` starts a slide one full room away while nothing draws the outgoing room; `M5ObstacleDrops` carries no position at all; `RoomReward`/`ShopItem` sit at fixed screen coordinates and overlap obstacles; the player's collision bound is the playfield rather than the drawn wall, so the player renders inside it; and `HudScore` is one catalogue row covering hearts, currency, charge, banner and the whole minimap, so deleting the boss-room minimap colour would keep the coverage audit complete.

### Stretch — deferred (post-v1)
- ⬜ Active items & charges fully fleshed out with the HUD charge meter (§15.1)
- ⬜ Item synergy graph — bespoke pairwise synergies (§15.2)
- ⬜ Sprite/animation atlas replacing primitive shapes (§15.3)
- ⬜ Daily-seed leaderboard with shareable seeds + online submission (§15.4)
- ⬜ More floors, bosses & final-floor branching path (§15.5)
- ⬜ Curse/blessing room modifiers altering a whole floor (§15.6)
- ⬜ Multiple playable characters with distinct starts (§15.7)
- ⬜ Local 2-player co-op twin-stick (§15.8)
- ⬜ Render interpolation between fixed sim steps (§15.9)
- ⬜ Mod/data-pack support — external item/enemy/template data (§15.10)
