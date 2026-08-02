# M13 critic history

Three independent fresh-context critics reviewed work item `014-m13-room-transition-pickups-world-state`
(board item `EHotwagner/rogue3#12`, PR #37). None authored what it reviewed.

This file exists because the cycle's feedback report quoted process facts — how many mutations were
applied, how many rooms a sweep covered, what a critic disproved — that were otherwise reconstructible
only from the author's narration. An actionability critic flagged exactly that, and it was right to.
Precedent for committing this: `readiness/006-m5-entities-bosses-rooms/implementation-critic-history.md`
and `feedback/2026-08-01-Rogue3-7-assets/critic-history.md`.

Findings are reproduced as the critics reported them. Where a figure was produced by a throwaway
script under `/tmp` that is not part of the repository, it is marked **(not re-derivable from this
tree)** so a reader knows which numbers rest on a transcript rather than on a committed artifact.

---

## Critic 1 — code (mutation testing)

Method: a harness applied a textual revert to production source, rebuilt, ran the whole Expecto
assembly, recorded which tests went red, and reverted. **(harness not re-derivable from this tree)**

Eighteen mutations were applied. **Sixteen went red. Two stayed green**, and both were real gaps:

| Mutation | Result |
|---|---|
| Drop `M6CameraTransition = Some {…FromRoom = departed}` from `TraverseDoor` | RED: FR-001 |
| `departedRoomScene` → `None` | RED: FR-002 + 2 M11/coverage tests |
| `roomShellScene` → floor rect only | RED: FR-002 |
| `departedRoomStep East` → half a playfield | RED: FR-002 |
| Drop `Position=obstacle.Position` at both drop sites → constant | RED: FR-004 |
| `collectFloorPickups spiked` → `spiked` | RED: FR-005 |
| `Coin3` credits 1 coin | RED: both FR-005 tests |
| Pickups back to the M11 indexed strip | RED: FR-006 |
| `placeRoomFixtures` → M11 fixed row | RED: FR-007 |
| `shopSlotScene` drops the price/lock label | RED: FR-008 |
| Player sweeps `model.Obstacles` only (pre-M13) | RED: FR-009 |
| `hasDoor` → always false (walls seal doorways) | RED: FR-010, FR-011 + 4 M11 tests |
| Renderer draws `roomWallSlabsFor Set.empty` (drift) | RED: FR-011 |
| Remove the four world-space state visuals | RED: FR-012, FR-014 + 3 coverage tests |
| Reorder `hudRegionScenes` | RED: FR-013 |
| HUD back to one element | RED: FR-013, FR-014 + 3 coverage tests |
| **Shop + reward back to the M11 fixed screen coordinates (`520+i*90,160` / `620,450`)** | **GREEN — 224/224** |
| **`TotalFloorPickupCandidates` stops counting** | **GREEN — 224/224** |

### Findings

1. **(Medium) Nothing tested that the renderer consumes `placeRoomFixtures`.** FR-007 exercised the
   function in isolation; no test asserted the `ShopItem`/`RoomReward` elements are drawn at those
   positions. The milestone's third row had no regression guard at all.
2. **(Low) The new cost counter was unguarded** by `dotnet test`; the exact-equality driver only bites
   when the performance-evidence command runs.
3. **(High) A `HiddenWall` opened a collider gap in a wall drawn as solid stone.**
   `roomWallSlabsFor` opened a `2 × doorwayHalfSpan` gap for every wall carrying a door of *any*
   state, but `Render.doorScene` draws `DoorHiddenWall` as a full-depth rectangle in the same `stone`
   colour, its own comment reading *"It reads as WALL, not door"*.
   Counterexample (seed `0xC0FFEE`, floor 1, room 1, south hidden wall) **(not re-derivable from this
   tree — produced by a `/tmp` probe script)**:
   ```
   final player position = (638.78, 707.00); inside a collider slab = false
   penetration of the player's disc into the DRAWN 24-unit stone band = 24.00 units
   still CurrentRoom 1 (did not cross) = true
   ```
   The count of hidden-wall doors on floors 1–6 of that seed — 46 — **is** re-derivable from the built
   assembly. FR-009 could not see the defect: it tested only the boot room and asserted only against
   `roomWallSlabs`, which by construction excludes the gap.
4. **(Medium) Uncollected pickups were destroyed by any room change.** `loadM5Room` set
   `M5ObstacleDrops=[]` while `FloorGeneration.recordDestroyedObstacle` is durable, so the pot never
   returned to re-drop:
   ```
   smashing every obstacle -> 1 drops: [(Coin1, (370.0, 480.0))]
   after crossing to room 1: drops carried = 0 ; coins = 0
   returning to room 0:      drops = 0 ; obstacles standing = 3 ; destroyed recorded = 2
   ```
5. **(Low) `RunStats.CoinsCollected` was over-credited past the 99 cap**, and that stat feeds
   `runScore`: at the cap, collecting `Coin3` gave `Coins` delta 0 and `CoinsCollected` delta 3.
6. **(Low) `PlayerDodgeRoll` was drawn for a dead player** while `PlayerInvulnerable` correctly gated
   on `Alive`, so dying mid-roll drew the speed trail and the "down" cross on one frame.
7. **(Low, latent) `DescendFloor` left a live transition and uncollected drops** pointing into a
   regenerated floor; room ids are reused across floors, so a stale id resolves to a *different* room.
   Not reachable by play (42 ticks ≈ 84 px of travel versus ≥317 px from any doorway to the trapdoor).
8. **(Low, latent) The placement fallback could duplicate a point and ignored its own clearance rule**
   **(not re-derivable from this tree)**:
   ```
   count=20 on an empty room -> 20 positions, 18 distinct
      [17] (1228.0, 520.0)   [18] (1228.0, 520.0)   [19] (1228.0, 520.0)
   ```
9. **(Low, test quality) FR-013's "byte-identical" assertion was a tautology** — it compared
   `Render.hudSceneForSize` against `hudRegionScenes |> collect |> group`, which is literally that
   function's definition. It could not fail for any implementation.
10. **(High) FR-015 asserted files that were `.gitignore`d and absent from the commits.** A fresh
    clone of the branch contained only `ship-verdict.json` under `readiness/014-…/`.
11. **(Low) The wall is a collider for the player but not for shots** — `shotWalls` is still
    `model.Obstacles` and shots bounce on the raw `roomBounds`, so a shot flies 24 units into the
    drawn stone before bouncing. The asymmetry is owned by a comment in `Model.fs`; no test records it.

### What the critic DISPROVED

These are as valuable as the defects and are recorded so the report's claims are not one-sided.
All were produced by scripts under `/tmp` and are **not re-derivable from this tree**:

- **The HUD split really is byte-identical to pre-M13.** The critic built a worktree at the merge base
  `6b4ed07`, compiled it, and hashed `SceneCodec.export(...).CanonicalBytes` from *both* assemblies for
  two non-trivial models: `4B7939B7…047AD0` (2134 B, 25 nodes) and `3B5030BF…165341`, identical on both
  sides. The shipped test asserts a 25-node count rather than byte-identity, so this conclusion rests
  on the critic's transcript, not on a committed artifact.
- **No pickup is put out of reach by the new wall collider.** A sweep of 16,860 obstacles over 41 seeds
  × 6 floors found 0 drops unreachable at `playerRadius + floorPickupRadius = 25`.
- **`placeRoomFixtures` is total and deterministic**, and its fallback never fires in production: 1,331
  fixture-bearing rooms over 121 seeds × 6 floors, 0 placements violating `placementAccepts`.
- **No double-collection, no credit-without-removal, no drop lost to `finishRun`/`StartRun`.**
- **Boot, `DescendFloor` landing, and a 460 px/s dodge into all four corners** all stop at
  `wallThickness + playerRadius` with no slab penetration.
- **`TotalFloorPickupCandidates` counts what its `ScaleSource` says**: 6 drops × 2 fixed steps → 12.

### Context the critic surfaced, not introduced by M13

`InteractM5Shop` is dispatched from nothing in the input path — `grep -rn "InteractM5Shop" src/` finds
only the `Msg` case, the reducer and an audio cue. M13 gives shop stock a placed plinth and a price or
keyhole label, but there is still no production route by which a player buys anything. Recorded here
for M14's reachability audit.

---

## Critic 2 — visual (rendered frames)

Method: opened all twelve committed frames, cropped and pixel-sampled ambiguous regions, and rendered
roughly thirty further states of its own under `/tmp` — crossings sampled every few ticks, all four
walls, empty and unaffordable shop slots, real reducer-produced drops, HUD occlusion, telegraph over
obstacles. Its re-render of the crossing at tick 0 came out byte-identical to the shipped frame, so
the frames are reproducible.

### Findings that changed the candidate

1. **`04-positioned-drops` overwrote the fact it existed to prove.** The harness discarded the model's
   drops and substituted `Position = vec2 (240.0 + index * 150.0) (300.0 + (index % 2) * 130.0)`. The
   mechanic was correct — the production reducer put a `Coin1` at `(370.0, 480.0)`, exactly the
   TintedRock's own anchor — but the frame did not show it. **This is re-derivable:** the harness now
   prints both lines on every run.
2. **`10-player-down` published a dead player at full health.** The fixture `{ boot with
   PlayerLifeState = Dead }` never touches `PlayerHealth`, so the frame showed a downed avatar under
   three full red hearts — a state play cannot reach, with the two life-or-death indicators
   contradicting each other.
3. **The crossing samples did not read as one motion.** Tick 0 is the departed room alone (correct —
   the entered room is exactly one playfield away, which is M6's contract) but static and symmetric,
   carrying no directional cue.
4. **Pickup legibility.** Sampled against the floor `rgb(27,19,32)`: bomb `rgb(43,43,43)` effectively
   invisible; coin `rgb(245,197,66)`, coin-3 `rgb(255,225,92)` and key `rgb(217,177,74)` three
   near-identical yellows, so a key that gates doors and locked shop slots read as loose change.
5. **Every shop consumable drew as one light-cyan disc** `rgb(132,208,236)` r=11 — practically the
   player's own body `rgb(126,227,255)` r=13 — and gave no indication of what the slot was selling.
6. **`ACTIVE n/m` was drawn straight through the charge dial it names**, bisecting the arc.
7. **The telegraph sat on `FloorDecals`, below Obstacles and Pickups**, so a charge lane crossing a
   rock was visually cut in half; and the demonstration frame aimed the Fly's dive away from the player.

### Findings recorded but not fixed here

- **The departed room is a hollow shell for the whole 0.35 s** — floor, wall band, doors and trapdoor
  only. Filed as **#35** with the midpoint frame as its reproduction.
- **The player arrives inside the entered room's door panel.** `doorwayClearance = 17` while a panel
  extends `wallThickness + doorApron = 42` into the room, so a crossing lands the player among the
  bars of a doorway that seals behind them. This is M11's arrival geometry, made visible by drawing
  the slide; it is *not* a wall-band violation, because the doorway gap is deliberate (DEC-002).
- **The minimap panel is translucent and drawn over the top-right of the playfield**, and HUD text is
  drawn over the top-left with no backing panel; an enemy parked under either is camouflaged and a
  pickup under the minimap is invisible. Pre-existing M7/M11.
- **South doorways overflow the canvas** by roughly 18 px of apron, clipped at y=720; north doors are
  complete, so the pair is asymmetric. Pre-existing M11.
- An emptied shop slot barely registers, and an unaffordable price is drawn in the same yellow as an
  affordable one.
- The minimap has no legend, and two near-identical yellow/orange cell fills carry different meanings.

### Verified good

- Wall containment measured flush: `y = 37.00` at the north wall (`wallThickness` 24 + `playerRadius`
  13) and `y = 683` at the south (`720 − 24 − 13`). The north figure is printed by the committed
  harness on every run; the south figure came from a `/tmp` render **(not re-derivable from this tree)**.
- Shop and reward placement genuinely clear of the furniture, checked at 3× zoom, with prices and the
  key-lock plate legible — a real improvement on M11's `13-shop-and-reward`, where the first slot was
  drawn straight through the pot.
- Invulnerability ring, dodge trail and telegraph arrow all read, and compose legibly when combined.

---

## Critic 3 — report actionability

Reviewed `feedback/2026-08-02-Rogue3-6.md` in two passes (cold read, then evidence verification).
Dispositions: §4.1 actionable, §4.2 actionable, §4.3 **duplicate**, §4.4 **incomplete**, §4.5
actionable, §4.6 actionable, §4.7 positive-pattern with unsupported specifics.

It independently reproduced §4.1 in a clean `git archive origin/main` sandbox — four `--grandfather`
runs, four distinct ledger digests, check exiting 1 after every one, then the `1 OBSOLETE EXCEPTION(S)`
failure on the rebind route — and recomputed the 46 hidden-wall doors exactly from the built assembly.

It found four errors in the report, all corrected before the audit was written:

1. **§4.3's recurrence claim was inverted.** The report claimed prior reports wrongly attributed the
   `plan` section loss to `--accept-upstream` and that the plain command was a new narrowing.
   `feedback/2026-08-02-Rogue3-2.md` §4.2 already withdraws that inference in its own text, and
   `-11` §4.5 and `-12` §4.6 both record the plain command. It is a fifth occurrence with no new fact,
   and the wrong attribution being corrected was the author's own `plan.md` line.
2. **§9's headline latency figures were two commits stale** — quoted from the pre-critic-fix run
   rather than from the committed artifact.
3. **§1's rebase target was wrong** (`562e905`; the merge base is `855a204`).
4. **§9 listed generated readiness views as committed** when they are untracked — §4.2's own point
   turned against §9.

It also flagged that this cycle committed no critic history while two earlier cycles did, which is why
this file exists.
