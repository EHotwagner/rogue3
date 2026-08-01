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

Evidence (2026-08-01): focused M9/result-action Release 7/7 and full Release/Verify 152/152; an actual fixed-step player projectile defeats the floor-6 boss, fixed-step lethal health resolves GameOver, terminal ticks freeze, and three bound result controls start a new run, retry the seed, or return to title. Both outcomes resolve once to visible scored summaries, discard transient run state, evaluate milestone unlocks, request terminal audio, and persist best-by-seed/lifetime profile facts. A unique system temporary directory proves two queued profiles debounce to one latest-value write, sibling temp creation plus atomic rename, no leftover temp, supported boot load, and absent/malformed/unsupported-version fallback without touching user data. Six governed workloads and three UI routes pass (maximum-content p95 14.4875 ms, p99 16.2378 ms), with explicit terminal-overlay and event-driven-persistence dispositions; SDD verdict `readiness/010-m9-win-loss-permadeath/ship-verdict.json` is `shipReady` with 9/9 observed obligations backed by the committed final TRX. Cycle feedback is `feedback/2026-08-01-Rogue3-10.md`. Delivery is local-only because this checkout has no configured remote.

### M10 — Acceptance & determinism
- 🟥 All 24 acceptance scenarios green (§14)
- 🟥 Procedural generation byte-identical for a seed (§14.1) — AC #1
- 🟥 Layout independent of combat RNG stream (§14.2) — AC #2
- 🟥 Shop/treasure/boss contents layout-deterministic & dupe-free (§14.12) — AC #12
- 🟥 Difficulty mode latches at `StartRun` and scales the sim (§14.13) — AC #13
- 🟥 Secret-room bomb-reveal updates the door graph atomically (§14.14) — AC #14
- 🟥 Seed + input-log replay is byte-identical given identical actions/timing (§13)

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
