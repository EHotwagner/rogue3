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
- 🟥 Velocity lerp (`accel`/`friction`) + diagonal normalization, speed clamp (§4.1)
- 🟥 Axis-separated wall/obstacle sweep, circle hitbox `r = 13` (§4.1)
- 🟥 Dodge roll: i-frames, velocity impulse, `0.90 s` cooldown, fire lockout (§4.2)
- 🟥 Stat-derived shots (dmg/fireRate/shotSpeed/range/size) + velocity inheritance (§4.3)
- 🟥 Multishot `18°` spread fan centered on aim (§4.3) — AC #4
- 🟥 Shot lifetime/range, bounce, pierce & homing termination (§4.3) — AC #10

### M3 — Combat, health & currency
- 🟥 Shot→enemy circle overlap: `dmg`, knockback, hit-flash, pierce decrement (§4.4)
- 🟥 Enemy/bullet→player damage with i-frame + `0.80 s` post-hit invuln gating (§4.4, §4.6) — AC #6
- 🟥 Half-heart health (red/soul/black), damage resolution & death at `0` (§4.6)
- 🟥 Player stats recompute: additive-then-multiplicative phases + clamps (§4.5) — AC #3
- 🟥 Coins/keys/bombs currencies (cap `99`), bomb drop/blast & shop purchase (§4.4, §4.7) — AC #11
- 🟥 Contact damage on overlap: `contactDmg`, `0.5 s` per-enemy re-tick cap, knockback `90 px/s` (§4.4)
- 🟥 `SpatialGrid.build 64.0` broadphase for shot↔enemy / bullet↔player queries (§13)
- 🟥 Heart types: soul/black stacking, black-heart depletion burst, 12-wide display cap, descent persistence (§4.6)
- 🟥 Bomb chain-detonation + currency cap-overflow waste (`99` cap) (§4.7, §4.4)

### M4 — Procedural floor generation
- 🟥 Seed derivation `floorSeed = split(runSeed, floorIndex)` on `LayoutRng` stream (§4.8, §13) — AC #2
- 🟥 Room budget + branching placement walk with bounded re-roll (§4.8)
- 🟥 Special-room assignment: boss/treasure/shop/secret on the placed graph (§4.8)
- 🟥 Room interior population by template + threat budget `6 + 2*floorIndex` (§4.8)
- 🟥 Door carving between orthogonally adjacent rooms (§4.8) — AC #1
- 🟥 Secret / super-secret reveal by bombing an adjacent wall; atomic door-graph update (§4.8, §13) — AC #14
- 🟥 Floor descent: trapdoor spawns on boss clear, `DescendFloor` regenerates next floor & carries player, drops room state (§7.3, §4.8)

### M5 — Entities: enemies, bosses & rooms
- 🟥 Enemy roster + per-enemy state machines (e.g. Charger WindUp→Dash→Recover) (§5.2)
- 🟥 Boss phases & data-driven declarative bullet patterns (§5.3)
- 🟥 Room-clear gating: seal doors on entry, open + drop-roll on clear (§7.3) — AC #5
- 🟥 Weighted pickup/drop tables via `DropRng` sub-stream (§4.9)
- 🟥 Per-floor difficulty ramp: threat budget + enemy HP/bullet scaling (§6, §12)
- 🟥 Enemy behavior params: Brute ground-pound, bounded Grub split, Spitter/Turret/Caster/Fly patterns, enemy bullet base `180 px/s` (§5.2)
- 🟥 Obstacles: rock/tinted-rock/pot/spikes/pit collision, destructibles + drop tables via `DropRng`, spikes hazard, pit fly-over (§5.5, §4.1, §4.9)
- 🟥 Run item pool: treasure pedestal + boss floor-reward from `LayoutRng`, dupe-free per run (§4.11, §5.3) — AC #12
- 🟥 Shop room: item/consumable slots, `LayoutRng` pricing, key-locked items, no in-floor restock (§4.11, §4.7) — AC #11

### M6 — Rendering & enemy symbology
- 🟥 Back-to-front layer draw order (background → HUD → overlays) (§8)
- 🟥 `Enemy → Token` ChannelMap in `FS.GG.Game.Render`, `Symbology.token` grammar (§8.1)
- 🟥 Legibility linter assertion pinned to the accepted `Size` channel (§8.1)
- 🟥 Pooled particles (cap `600`) + room-transition camera slide `0.35 s` (§8)

### M7 — UI, menus & stats
- 🟥 HUD: hearts row, currency, active-item charge meter, minimap, floor name (§9)
- 🟥 Adopt the generic FS.GG game shell (FS-GG/FS.GG.Rendering#991): main menu (title + Start/Config/Exit), Esc pause routing, Settings with screen resolution + fullscreen, and in-game key rebinding of the §3 controls, persisted — the game provides its name + key→command map + play update/view; the shell provides the rest, no bespoke menu system (§9.1)
- 🟥 Game-specific rows over the shell (run management, difficulty mode, volume/sound, screen shake) apply live + persist to `MetaProfile` (§9.1, §12, §13)
- 🟥 Stats & charts screen: KPI tiles + depth histogram + damage-per-floor line (§9.2)
- 🟥 Difficulty-mode scaling table (Easy/Normal/Hard) latched at `StartRun` (§12, §9.1) — AC #13

### M8 — Audio
- 🟥 `AudioEffect` cues per event, `Audio.interpret` → `AudioEvidence.Requested` (§10)
- 🟥 Per-context music loop (one track at a time), volume clamp `[0,1]` + mute (§10)

### M9 — Win/loss & permadeath
- 🟥 Final-boss (Floor 6) defeat → `Victory` screen + unlock (§11)
- 🟥 Permadeath at `0` half-hearts → `GameOver`, run discarded (§11) — AC #7
- 🟥 Run-score tally + end-of-run meta-progression unlock evaluation (§11, §4.10)
- 🟥 `MetaProfile` JSON persistence: debounced, atomic temp-file+rename, load on boot (§13, §7.5)

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
