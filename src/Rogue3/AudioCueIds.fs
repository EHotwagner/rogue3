/// M12 (013-m12-audio-assets, DEC-001): the SINGLE declaration of every audio cue id this product
/// requests. Three consumers read it and none of them keeps a second list:
///
///   1. `AudioCues.fs` builds every `SoundId`/`TrackId` from these values, so the cue map cannot
///      request an id that is not declared here.
///   2. `scripts/generate-audio-assets.fsx` loops over `sounds`/`tracks` to write `assets/audio/`,
///      so the asset set cannot drift from the cue map.
///   3. `tests/Rogue3.Tests/M12AudioAssetTests.fs` enumerates the same lists to assert every id
///      resolves, so the guard cannot pass while the product is silent.
///
/// WHY IT IS ITS OWN FILE, and why it opens nothing. `AudioCues.fs` opens FS.GG.Audio.Core,
/// FS.GG.Audio.Host, Rogue3.Geometry and Rogue3.Model, so it cannot be `#load`ed by a standalone
/// `dotnet fsi` script without resolving the whole package graph and model compile order. A
/// generator that cannot read the declaration re-lists the ids by hand — and a hand-written list
/// that drifts from `AudioCues.fs` is exactly the defect M12 exists to close. This module depends on
/// nothing (not even `Vec2`), compiles first in `Rogue3.fsproj`, and is one `#load` line for a script.
///
/// ADD A CUE: add its value here, add it to `sounds` or `tracks`, use the value in `AudioCues.fs`,
/// and re-run `dotnet fsi scripts/generate-audio-assets.fsx`. Skip any of those and the M12 guard
/// reds — an inline `sfx "new-id"` literal in `AudioCues.fs` fails the drift scan, and a declared id
/// with no asset fails resolution.
module Rogue3.AudioCueIds

// ── Sounds ───────────────────────────────────────────────────────────────────────────────────
// The seven fixed-step `AudioEvent` cases.

let shotFire = "shot-fire"
let shotHit = "shot-hit"
let enemyDeath = "enemy-death"
let playerHit = "player-hit"
let playerDeath = "player-death"
let dodgeRoll = "dodge-roll"
let bombExplosion = "bomb-explosion"

// Acquisition. `PickupKind` maps many-to-one: Coin1/Coin3 share `pickupCoin`, HalfRedHeart/SoulHeart
// share `pickupHeart`, and `Nothing` has no cue at all — which is why the id count is not the
// `PickupKind` case count.

let pickupCoin = "pickup-coin"
let pickupKey = "pickup-key"
let pickupBomb = "pickup-bomb"
let pickupHeart = "pickup-heart"
let itemPickup = "item-pickup"

// State-derived cues: recovered by diffing the before/after model rather than keyed to a `Msg`.

let doorLock = "door-lock"
let doorUnlock = "door-unlock"
let floorDescend = "floor-descend"
let bossIntro = "boss-intro"
let bossPhase = "boss-phase"
let bossDeath = "boss-death"

// ── Tracks ───────────────────────────────────────────────────────────────────────────────────

let titleTheme = "title-theme"
let shopTheme = "shop-theme"
let bossTheme = "boss-theme"
let gameOverTheme = "game-over"
let victoryTheme = "victory"

/// How many `floor-<n>-theme` loops the declaration carries. Six, because the product's own victory
/// condition hard-codes floor six as the last one (`Model.fs`: `model.FloorIndex=6 && model.M5Boss.IsSome`
/// finishes the run), so a run visits floors 1..6.
[<Literal>]
let floorThemeCount = 6

/// The parameterized track family, and the reason it is a function rather than six more literals.
///
/// TOTAL BY CONSTRUCTION. `AudioCues` used to interpolate `floor-{max 1 index}-theme` straight from
/// an unbounded `int`, so "six floors" was a fact about `Model.fs` that the cue map merely believed.
/// Clamping into `[1, floorThemeCount]` removes the drift: every integer — reachable, unreachable, or
/// negative — names a declared track, so no floor index can ever request an asset that does not
/// exist. A seventh floor would reuse floor six's loop, which is audible and deliberate rather than
/// silent and accidental.
let floorTheme (index: int) =
    let clamped = max 1 (min floorThemeCount index)
    $"floor-{clamped}-theme"

/// Every sound id the product can request. Built from the values above, never re-typed.
let sounds =
    [ shotFire; shotHit; enemyDeath; playerHit; playerDeath; dodgeRoll; bombExplosion
      pickupCoin; pickupKey; pickupBomb; pickupHeart; itemPickup
      doorLock; doorUnlock; floorDescend; bossIntro; bossPhase; bossDeath ]

/// Every track id the product can request, including the whole `floorTheme` range.
let tracks =
    [ titleTheme
      yield! [ for index in 1 .. floorThemeCount -> floorTheme index ]
      shopTheme; bossTheme; gameOverTheme; victoryTheme ]

/// Every cue id, sounds first. One asset must exist per entry.
let all = sounds @ tracks
