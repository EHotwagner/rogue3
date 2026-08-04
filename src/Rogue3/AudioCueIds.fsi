// The DECLARED public surface of this module (EHotwagner/rogue3#96, constitution principle III:
// "Public Surface Is Declared, Not Incidental"). A binding absent from this file is private to
// the implementation and is NOT product API -- adding one here is a contracted (Tier 1) change.
module Rogue3.AudioCueIds

val shotFire: string

val shotHit: string

val enemyDeath: string

val playerHit: string

val playerDeath: string

val dodgeRoll: string

val bombExplosion: string

val pickupCoin: string

val pickupKey: string

val pickupBomb: string

val pickupHeart: string

val itemPickup: string

val doorLock: string

val doorUnlock: string

val floorDescend: string

val bossIntro: string

val bossPhase: string

val bossDeath: string

val titleTheme: string

val shopTheme: string

val bossTheme: string

val gameOverTheme: string

val victoryTheme: string

/// How many `floor-<n>-theme` loops the declaration carries. Six, because the product's own victory
/// condition hard-codes floor six as the last one (`Model.fs`: `model.FloorIndex=6 && model.Boss.IsSome`
/// finishes the run), so a run visits floors 1..6.
[<Literal>]
val floorThemeCount: int = 6

/// The parameterized track family, and the reason it is a function rather than six more literals.
///
/// TOTAL BY CONSTRUCTION. `AudioCues` used to interpolate `floor-{max 1 index}-theme` straight from
/// an unbounded `int`, so "six floors" was a fact about `Model.fs` that the cue map merely believed.
/// Clamping into `[1, floorThemeCount]` removes the drift: every integer — reachable, unreachable, or
/// negative — names a declared track, so no floor index can ever request an asset that does not
/// exist. A seventh floor would reuse floor six's loop, which is audible and deliberate rather than
/// silent and accidental.
val floorTheme: index: int -> string

/// Every sound id the product can request. Built from the values above, never re-typed.
val sounds: string list

/// Every track id the product can request, including the whole `floorTheme` range.
val tracks: string list

/// Every cue id, sounds first. One asset must exist per entry.
val all: string list
