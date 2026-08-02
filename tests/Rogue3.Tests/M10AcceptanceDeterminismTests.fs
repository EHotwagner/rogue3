module Rogue3M10AcceptanceDeterminismTests

// M10: the source specification's §14 acceptance sweep, as ONE list with exactly one named test per
// numbered scenario, plus the §13 determinism and replay obligations.
//
// Every scenario body drives production code — `Model.update`, `FloorGeneration`, `Entities`,
// `Render` or the production interactive host. None of them re-implements a rule in the test. The
// guard test at the bottom fails if the list ever stops covering 1..24 exactly once, so a silently
// dropped scenario cannot leave the milestone green.

open System
open Expecto
open FS.GG.UI.Scene
open FS.GG.UI.Controls
open FS.GG.UI.KeyboardInput
open FS.GG.Game.Core
open Rogue3
open Rogue3.Geometry
open Rogue3.FloorGeneration
open Rogue3.Model

let private ticksFor seconds = int (seconds / fixedDt + 0.5)

let private enemyBullet id position damage : EnemyBullet =
    { Id = id; Position = position; Velocity = zero; Radius = 4.0; Damage = damage; Homing = 0.0; AgeTicks = 0 }

// Board item #20 deleted this file's `legacyOf` projection with the `Model.Enemy` type it produced.
// `Rogue3.Entities.EnemyActor` is now the only enemy representation, so a fixture that wants an
// enemy spawns one.

/// A fixture enemy the game can actually spawn: a real roster kind at a chosen position, with its
/// hit points overridden. `Grub` (radius 12, contact damage 1) is the default; every caller that
/// wants an enemy which cannot touch the player places it further than `playerRadius + 12 = 25`
/// away rather than zeroing a field that no longer exists.
let private fixtureEnemy id kind position hp : Rogue3.Entities.EnemyActor =
    { Rogue3.Entities.spawn 1 id kind position with HitPoints = hp }

let private grubAt id position hp = fixtureEnemy id Rogue3.Entities.EnemyKind.Grub position hp

let private startedRun seed = update (StartRun seed) initialModel |> fst

let private roomOfType kind (model: Model) =
    model.Floor.Rooms |> Map.toList |> List.find (fun (_, room) -> room.RoomType = kind) |> fst

/// M11: `DescendFloor` is guarded by the state it depicts, so reaching the next floor means doing
/// what a player must do — beat the floor's boss, which is what leaves the trapdoor fixture behind,
/// enter that room through the production seam, and stand on the trapdoor. Every scenario below that
/// descends stages it this way rather than descending out of a trapdoor-less room.
let private standOnTrapdoor (model: Model) =
    let bossId = roomOfType Boss model
    { model with Floor = FloorGeneration.clearBoss bossId model.Floor }
    |> update (EnterM5Room bossId)
    |> fst
    |> fun staged -> { staged with PlayerPosition = trapdoorCenter }

let private descendThroughTrapdoor model = model |> standOnTrapdoor |> update DescendFloor |> fst

/// Fixture item ids and shop prices reachable in one floor, in deterministic room order.
let private floorFixtureContents (model: Model) =
    model.Floor.Rooms
    |> Map.toList
    |> List.collect (fun (roomId, room) ->
        room.Fixtures
        |> List.collect (function
            | ItemPedestal item
            | BossReward item -> [ roomId, item.Id, 0 ]
            | ShopStock slots ->
                slots
                |> List.choose (fun slot ->
                    match slot.Offer with
                    | Rogue3.Entities.ShopOffer.Item item -> Some(roomId, item.Id, slot.Price)
                    | _ -> None)
            | _ -> []))

/// Advance ONLY the drop/AI stream, the way a longer fight would, without touching layout.
let private churnDropStream count (model: Model) =
    let mutable rng = model.DropRng

    for _ in 1..count do
        let struct (_, next) = Rng.nextInt 0 99 rng
        rng <- next

    { model with DropRng = rng }

// --------------------------------------------------------------------------------------------
// §14, one entry per numbered scenario.
// --------------------------------------------------------------------------------------------

let private scenarios: (int * string * (unit -> unit)) list =
    [ 1,
      "procedural generation is deterministic for a seed",
      fun () ->
          let a = generate 0xC0FFEEUL 1
          let b = generate 0xC0FFEEUL 1
          Expect.equal (Replay.floorBytes a.Floor) (Replay.floorBytes b.Floor) "a byte-for-byte serialization of the two floors is equal"

          let shape (floor: Floor) =
              floor.Rooms
              |> Map.toList
              |> List.map (fun (_, room) ->
                  room.Cell, room.RoomType, room.Doors, room.Fixtures, room.Interior.EnemyAnchors)

          Expect.equal a.Floor.Rooms.Count b.Floor.Rooms.Count "same room count"
          Expect.equal (shape a.Floor) (shape b.Floor) "same cells, RoomType per cell, special placement, doors and per-room enemy lists"
          Expect.notEqual (Replay.floorBytes (generate 0xC0FFEEUL 2).Floor) (Replay.floorBytes a.Floor) "another floor index encodes differently"
          Expect.notEqual (Replay.floorBytes (generate 0xC0FFEFUL 1).Floor) (Replay.floorBytes a.Floor) "another run seed encodes differently"

      2,
      "layout is independent of the combat RNG stream",
      fun () ->
          let fast = startedRun 0xC0FFEEUL

          let slow =
              let combatId = roomOfType Combat fast
              let entered = update (EnterM5Room combatId) fast |> fst

              entered.M5Enemies
              |> List.map _.Id
              |> List.fold (fun model enemyId -> update (DamageM5Enemy(enemyId, 9999.0)) model |> fst) entered

          Expect.notEqual slow.DropRng fast.DropRng "the slow run really did draw more from DropRng"
          Expect.equal slow.LayoutRng fast.LayoutRng "combat never advances the layout stream"

          let descend model = descendThroughTrapdoor model
          let fastFloor = (descend fast).Floor
          let slowFloor = (descend slow).Floor
          Expect.equal (Replay.floorBytes slowFloor) (Replay.floorBytes fastFloor) "the floor layout and enemy placement are identical across both runs"

          let anchors (floor: Floor) =
              floor.Rooms |> Map.toList |> List.map (fun (id, room) -> id, room.Interior.EnemyAnchors)

          Expect.equal (anchors slowFloor) (anchors fastFloor) "enemy placement is identical"

      3,
      "item stat modifiers stack additive-then-multiplicative and are order independent",
      fun () ->
          let lens = { Id = "cracked-lens"; Modifiers = [ { Stat = DamageStat; Kind = Add; Value = 1.0 } ] }
          let shard = { Id = "polyphemus-shard"; Modifiers = [ { Stat = DamageStat; Kind = Mul; Value = 1.0 } ] }
          Expect.floatClose Accuracy.high (recomputePlayerStats [ lens; shard ]).Damage 9.0 "(3.5 + 1.0) * 2.0"
          Expect.floatClose Accuracy.high (recomputePlayerStats [ shard; lens ]).Damage 9.0 "the reverse pickup order yields the same effective damage"

      4,
      "multishot and spread produce the right projectiles",
      fun () ->
          let stats = { basePlayerStats with Multishot = 3 }
          let shots = spawnShots 0 1 (vec2 400.0 300.0) zero (vec2 1.0 0.0) stats
          Expect.hasLength shots 3 "exactly three shots spawn"

          let degrees (shot: ShotSpawn) = atan2 shot.Direction.Vy shot.Direction.Vx * 180.0 / Math.PI

          List.zip (shots |> List.map degrees) [ -9.0; 0.0; 9.0 ]
          |> List.iter (fun (actual, expected) ->
              Expect.isLessThan (abs (actual - expected)) 0.01 $"direction {actual} is within 0.01 degrees of {expected}")

          shots
          |> List.iter (fun shot -> Expect.floatClose Accuracy.medium (magnitude shot.Velocity) stats.ShotSpeed "each shot carries the player's shotSpeed")

      5,
      "room-clear gating opens doors only when the last enemy dies",
      fun () ->
          let roster = [ for index in 0..3 -> Rogue3.Entities.spawn 1 (900 + index) Rogue3.Entities.EnemyKind.Maggot (vec2 (200.0 + float index * 60.0) 300.0) ]
          let liveIds = roster |> List.map _.Id

          let sealed' =
              { Rogue3.Entities.CombatRoom.IsBoss = false
                Rogue3.Entities.CombatRoom.Cleared = false
                Rogue3.Entities.CombatRoom.Doors = List.replicate 3 Rogue3.Entities.DoorState.Open
                Rogue3.Entities.CombatRoom.LiveEnemyIds = Set.ofList liveIds
                Rogue3.Entities.CombatRoom.Drop = None
                Rogue3.Entities.CombatRoom.Reward = None
                Rogue3.Entities.CombatRoom.Trapdoor = false }
              |> Rogue3.Entities.enterRoom liveIds

          Expect.isTrue (sealed'.Doors |> List.forall ((=) Rogue3.Entities.DoorState.LockedClear)) "entering an uncleared combat room seals every door to LockedClear"

          let model =
              { initialModel with
                  M5Enemies = roster
                  M5Room = sealed' }

          let partial' =
              liveIds
              |> List.take 3
              |> List.fold (fun current enemyId -> update (DamageM5Enemy(enemyId, 9999.0)) current |> fst) model

          Expect.isFalse partial'.M5Room.Cleared "fewer than all enemies dead leaves the room uncleared"
          Expect.isTrue (partial'.M5Room.Doors |> List.forall ((=) Rogue3.Entities.DoorState.LockedClear)) "all doors remain LockedClear and the player cannot exit"
          let beforeDrop = partial'.DropRng
          let cleared = update (DamageM5Enemy(List.last liveIds, 9999.0)) partial' |> fst
          Expect.isTrue cleared.M5Room.Cleared "the last enemy's death clears the room in the same transition"
          Expect.isTrue (cleared.M5Room.Doors |> List.forall ((=) Rogue3.Entities.DoorState.Open)) "all doors transition to Open"
          Expect.notEqual cleared.DropRng beforeDrop "a room-clear drop is rolled from DropRng"

      6,
      "damage applies and both invulnerability windows protect",
      fun () ->
          let source = add initialModel.PlayerPosition (vec2 10.0 0.0)
          let hit = update (Tick fixedDt) { initialModel with EnemyBullets = [ enemyBullet 1 source 1 ] } |> fst
          Expect.equal (totalHalfHearts hit.PlayerHealth) 5 "six half-hearts become five"
          Expect.equal hit.PostHitInvulnTicks postHitInvulnTicks "post-hit invulnerability is the documented 0.80 s"
          Expect.equal postHitInvulnTicks (ticksFor 0.80) "the documented window really is 0.80 s of fixed steps"
          Expect.notEqual hit.PlayerVelocity initialModel.PlayerVelocity "knockback is applied"

          let again = update (Tick fixedDt) { hit with EnemyBullets = [ enemyBullet 2 (add hit.PlayerPosition (vec2 10.0 0.0)) 1 ] } |> fst
          Expect.equal (totalHalfHearts again.PlayerHealth) 5 "a second bullet inside the window applies no further damage"

          let dodgeKeys = Set.singleton (ViewerKeyboard.toKeyId Space)
          let rolling = update (InputChanged { initialModel.Input.Current with Keys = dodgeKeys }) initialModel |> fst
          let rolled = update (Tick fixedDt) rolling |> fst
          Expect.isGreaterThan rolled.DodgeIFrameTicks 0 "the dodge roll opens its i-frame window"
          let duringRoll = update (Tick fixedDt) { rolled with EnemyBullets = [ enemyBullet 3 (add rolled.PlayerPosition (vec2 10.0 0.0)) 1 ] } |> fst
          Expect.equal (totalHalfHearts duringRoll.PlayerHealth) 6 "a bullet overlapping during the roll i-frames applies no damage"
          Expect.equal dodgeIFrameTicks (ticksFor 0.40) "the roll i-frame window really is 0.40 s"

      7,
      "permadeath ends the run and evaluates unlocks",
      fun () ->
          let started = startedRun 77UL

          let atOneHalfHeart =
              { started with
                  PlayerHealth = { started.PlayerHealth with RedHalfHearts = 1; SoulHalfHearts = 0; BlackHalfHearts = 0 }
                  RunStats = { started.RunStats with DepthReached = 3 }
                  EnemyBullets = [ enemyBullet 1 (add started.PlayerPosition (vec2 10.0 0.0)) 1 ] }

          Expect.equal (totalHalfHearts (fst (applyDamage 1 atOneHalfHeart.PlayerHealth))) 0 "a 1-damage hit takes the last half-heart to zero"
          let resolved = update (Tick fixedDt) atOneHalfHeart |> fst
          Expect.equal resolved.RunOutcome (Some RunOutcome.GameOver) "the step resolves to GameOver"
          Expect.isSome resolved.LastRunSummary "with a populated RunSummary"
          Expect.isFalse resolved.RunActive "the run is cleared on transition"
          Expect.isEmpty resolved.M5Enemies "transient run state is discarded"
          Expect.contains resolved.Profile.UnlockedItems "cracked-lens" "the unlock evaluator awards cracked-lens at bestFloor >= 3"

          let requests = profilePersistenceRequestsForTransition (Tick fixedDt) atOneHalfHeart resolved
          Expect.hasLength requests 1 "exactly one profile save is emitted for the terminal transition"

      8,
      "the fixed-timestep accumulator advances the simulation correctly",
      fun () ->
          let three = update (Tick 0.033) initialModel |> fst
          Expect.equal three.SimStepCount 3 "Tick 0.033 runs exactly three sim steps"
          Expect.floatClose Accuracy.medium three.SimAccumulator (0.033 - 3.0 / 120.0) "the accumulator holds the 0.008 s remainder"

          let four = update (Tick(1.0 / 30.0)) initialModel |> fst
          Expect.equal four.SimStepCount 4 "Tick (1/30) runs exactly four sim steps"
          Expect.floatClose Accuracy.medium four.SimAccumulator 0.0 "the remainder is zero within float epsilon"

          let stalled = update (Tick 1.0) initialModel |> fst
          Expect.equal stalled.SimStepCount maxSteps "a huge stall runs at most MAX_STEPS = 5 steps"
          Expect.isLessThan stalled.SimAccumulator fixedDt "the remainder is clamped, so there is no spiral of death"

      9,
      "twin-stick move and aim are decoupled",
      fun () ->
          let aimRight = add initialModel.PlayerPosition (vec2 200.0 0.0)

          let snapshot =
              { initialModel.Input.Current with
                  Keys = Set.singleton (ViewerKeyboard.toKeyId (Letter 'A'))
                  MousePosition = Some aimRight
                  MousePrimaryDown = true }

          let fired = update (InputChanged snapshot) initialModel |> fst |> update (Tick fixedDt) |> fst
          Expect.isLessThan fired.PlayerVelocity.Vx 0.0 "the player's velocity points left"
          let shot = fired.ShotSpawns |> List.head
          Expect.isGreaterThan shot.Direction.Vx 0.0 "the spawned shot travels right"
          Expect.floatClose Accuracy.medium shot.Velocity.Vx (shot.Speed + shotVelocityInheritance * fired.PlayerVelocity.Vx) "the shot inherits 0.25x the leftward velocity"
          Expect.isLessThan (shot.Velocity.Vx - shot.Speed) 0.0 "inheritance really subtracts from the rightward speed"

      10,
      "shot lifetime, range and pierce terminate projectiles",
      fun () ->
          let stats = basePlayerStats
          Expect.floatClose Accuracy.medium stats.ShotSpeed 420.0 "the fixture speed is 420"
          Expect.floatClose Accuracy.medium stats.Range 1.6 "the fixture range is 1.6 s"
          Expect.equal stats.Bounce 0 "bounce 0"
          Expect.equal stats.Pierce 0 "pierce 0"

          let bounds: Rect = { X = 0.0; Y = 0.0; Width = 1.0e9; Height = 1.0e9 }
          let start = spawnShots 0 1 (vec2 10.0 500.0) zero (vec2 1.0 0.0) stats

          let rec run steps shots =
              let stepped, _, _ = stepShots bounds [] [] shots
              if List.isEmpty stepped then steps, shots else run (steps + 1) stepped

          let steps, last = run 0 start
          Expect.equal steps (int (floor (stats.Range / fixedDt + 1e-9))) "the shot is destroyed when its age passes the range in seconds"
          Expect.floatClose Accuracy.low (last |> List.head |> _.DistanceTravelled) (stats.ShotSpeed * stats.Range) "it has travelled about 672 px"

          let narrow: Rect = { X = 0.0; Y = 0.0; Width = 20.0; Height = 1000.0 }
          let leaving, _, _ = stepShots narrow [] [] (spawnShots 0 1 (vec2 15.0 500.0) zero (vec2 1.0 0.0) stats)
          Expect.isEmpty leaving "a shot leaving the room bounds is destroyed earlier"

          let pierceStats = { basePlayerStats with Pierce = 2 }
          // `Grub` is radius 12 -- the radius this fixture used to hand-write -- and sits 247 units
          // from the player's start, well outside the 25-unit contact range its old ContactDamage = 0
          // stood in for.
          let target id = grubAt id (vec2 400.0 300.0) 1000.0
          let piercing = spawnShots 0 1 (vec2 400.0 300.0) zero (vec2 1.0 0.0) pierceStats
          Expect.equal (piercing |> List.head |> _.HitsRemaining) 3 "pierce 2 buys three enemy hits"

          let twoEnemies = update (Tick fixedDt) { initialModel with ShotSpawns = piercing; M5Enemies = [ target 1; target 2 ] } |> fst
          Expect.isNonEmpty twoEnemies.ShotSpawns "the shot survives its second enemy"
          let threeEnemies = update (Tick fixedDt) { initialModel with ShotSpawns = piercing; M5Enemies = [ target 1; target 2; target 3 ] } |> fst
          Expect.isEmpty threeEnemies.ShotSpawns "the shot is destroyed after hitting its third enemy"

      11,
      "currency and shop purchase spend, add and reject",
      fun () ->
          // Board item #20 removed the second shop. This acceptance scenario now runs through the
          // ONE shop message, `InteractM5Shop`, and the one reducer behind it. `Entities.purchase`
          // consumes a slot by emptying its offer rather than removing it from the list.
          let item = Rogue3.Entities.baseItems |> List.find (fun definition -> definition.Id = "cracked-lens")
          let slot: Rogue3.Entities.ShopSlot =
              { Id = 1; Offer = Rogue3.Entities.ShopOffer.Item item; Price = 7; KeyLocked = false }
          let rich = { initialModel with PlayerCurrency = { initialModel.PlayerCurrency with Coins = 10 }; M5ShopSlots = [ slot ] }
          let bought = update (InteractM5Shop 1) rich |> fst
          Expect.equal bought.PlayerCurrency.Coins 3 "ten coins minus a seven-coin item leaves three"
          Expect.equal bought.M5ShopSlots.Head.Offer Rogue3.Entities.ShopOffer.Empty "the shop slot is emptied"
          Expect.equal bought.RunStats.ItemsFound 1 "the purchase is recorded as an item found"
          // EHotwagner/rogue3#47 FIXED, through the MESSAGE rather than the reducer: this is the
          // acceptance scenario, so it buys via `InteractM5Shop` and proves the whole update path
          // grants the item and the recomputed stats.
          //
          // It does NOT prove player reachability, and must not be read as doing so — this scenario
          // dispatches the message. EHotwagner/rogue3#55 gave `InteractM5Shop` its production
          // dispatch site (`Model.playerRoomIntentsIn` raises it from an interact edge at a placed
          // shop slot), and the claim that a PLAYER can reach it is driven from `KeyChanged` + `Tick`
          // in `M14ItemGrantTests`, "Route 1b", alongside the pedestal and boss-reward routes.
          //
          // WHY THIS PARAGRAPH IS SHORTER THAN THE ONE IT REPLACES. The previous text counted the
          // `git grep` hits and concluded, correctly at the time, that none constructed the message.
          // It was prose, so when #55 wired the message nothing went red and the paragraph simply
          // became false. Reachability claims belong in assertions; this note now only says what this
          // scenario does and points at the file that owns the claim.
          Expect.equal (bought.PlayerItems |> List.map _.Id) [ "cracked-lens" ] "the purchased item is granted"
          Expect.equal bought.PlayerStats.Damage 2.625 "cracked-lens applies its x0.75 damage modifier"
          Expect.equal bought.PlayerStats.Multishot 2 "cracked-lens applies its +1 multishot modifier"
          Expect.equal bought.RunStats.ItemsFound bought.PlayerItems.Length "the counter and the inventory agree"

          let poor = { rich with PlayerCurrency = { rich.PlayerCurrency with Coins = 5 } }
          let rejected = update (InteractM5Shop 1) poor |> fst
          Expect.equal rejected.PlayerCurrency.Coins 5 "five coins are unchanged by a rejected purchase"
          Expect.equal rejected.M5ShopSlots.Head.Offer (Rogue3.Entities.ShopOffer.Item item) "the item remains in the shop"
          Expect.equal rejected.RunStats.ItemsFound 0 "and is not granted"

      12,
      "shop, treasure and boss contents are layout-deterministic and dupe-free",
      fun () ->
          let walkRun churn =
              let mutable model = churn (startedRun 0x5EEDUL)
              let mutable contents = floorFixtureContents model

              for _ in 1..4 do
                  model <- descendThroughTrapdoor model
                  contents <- contents @ floorFixtureContents model

              model, contents

          let fastModel, fast = walkRun id
          let slowModel, slow = walkRun (churnDropStream 500)
          Expect.notEqual slowModel.DropRng fastModel.DropRng "the two runs really did draw differently from DropRng"
          Expect.equal slow fast "both runs offer the identical item ids in identical rooms at identical prices"
          Expect.isNonEmpty fast "the walk actually collected fixture contents"
          let ids = fast |> List.map (fun (_, itemId, _) -> itemId)
          Expect.equal (List.distinct ids) ids "no item id appears twice across a single run's pedestals, shops and boss rewards"

      13,
      "difficulty latches at StartRun and scales the simulation",
      fun () ->
          let hard = difficultyScaling DifficultyMode.Hard
          Expect.floatClose Accuracy.high hard.EnemyHpScale 0.18 "Hard enemyHpScale is 0.18"
          Expect.floatClose Accuracy.high hard.PostHitInvulnSeconds 0.55 "Hard postHitInvuln is 0.55 s"
          Expect.equal hard.DropNothingWeight 55 "Hard dropNothingWeight is 55"

          let started = initialModel |> update (SetDifficulty DifficultyMode.Hard) |> fst |> update (StartRun 91UL) |> fst
          Expect.equal started.ActiveDifficulty (Some hard) "StartRun latches the settings difficulty into the run"

          let hit = update (Tick fixedDt) { started with EnemyBullets = [ enemyBullet 1 (add started.PlayerPosition (vec2 10.0 0.0)) 1 ] } |> fst
          Expect.equal hit.PostHitInvulnTicks (ticksFor hard.PostHitInvulnSeconds) "the latched invulnerability scales the simulation"

          let easyStart = initialModel |> update (SetDifficulty DifficultyMode.Easy) |> fst |> update (StartRun 91UL) |> fst
          let bossRoom model = update (EnterM5Room(roomOfType Boss model)) model |> fst
          let hardBoss = (bossRoom started).M5Boss |> Option.get
          let easyBoss = (bossRoom easyStart).M5Boss |> Option.get
          Expect.isGreaterThan hardBoss.HitPoints easyBoss.HitPoints "the latched hit-point scale reaches the live boss"

          let switched = update (SetDifficulty DifficultyMode.Easy) started |> fst
          Expect.equal switched.ActiveDifficulty (Some hard) "switching mid-run leaves the active run's scaling unchanged"
          Expect.equal switched.Profile.Settings.Difficulty DifficultyMode.Easy "the switch applies to the next StartRun only"

      14,
      "a bomb against an adjacent wall reveals the secret room in the same step",
      fun () ->
          let started = startedRun 0xC0FFEEUL
          Expect.isNonEmpty (started.Floor.PendingSecrets |> Map.toList) "the generated floor has a hidden secret to bomb into"
          let (struct (adjacent, secret)), _ = started.Floor.PendingSecrets |> Map.toList |> List.head
          let floor = { started.Floor with CurrentRoom = adjacent }
          let direction = roomDirection adjacent secret floor |> Option.get

          let armed =
              { started with
                  Floor = floor
                  Bombs = [ { Id = 1; Position = wallMidpoint direction; FuseTicks = 1 } ] }

          Expect.isTrue armed.Floor.Rooms.[secret].Hidden "the secret room starts hidden"
          let after = update (Tick fixedDt) armed |> fst
          Expect.isEmpty after.Bombs "the blast resolved inside this step"
          Expect.isFalse after.Floor.Rooms.[secret].Hidden "the secret room becomes enterable"
          Expect.contains after.Floor.Graph.[adjacent] secret "the forward adjacency is committed"
          Expect.contains after.Floor.Graph.[secret] adjacent "the reverse adjacency is committed"
          Expect.isTrue (after.Floor.Rooms.[adjacent].Doors |> List.exists (fun door -> door.ToRoom = secret && door.State = Open)) "a door is carved between the two cells"
          Expect.isTrue (after.Floor.Rooms.[secret].Doors |> List.exists (fun door -> door.ToRoom = adjacent && door.State = Open)) "and its reciprocal record"
          Expect.isFalse (Map.containsKey (struct (adjacent, secret)) after.Floor.PendingSecrets) "the pending secret is consumed"

          for KeyValue(roomId, room) in after.Floor.Rooms do
              for door in room.Doors do
                  Expect.contains after.Floor.Graph.[roomId] door.ToRoom $"room {roomId} has no door without its graph adjacency"

      15,
      "open door traversal is bidirectional and lands at the matching doorway",
      fun () ->
          let started = startedRun 0xC0FFEEUL

          let source, destination =
              started.Floor.Rooms
              |> Map.toList
              |> List.pick (fun (roomId, room) ->
                  room.Doors
                  |> List.tryPick (fun door ->
                      let reciprocal =
                          started.Floor.Rooms.[door.ToRoom].Doors
                          |> List.exists (fun back -> back.ToRoom = roomId && back.State = Open)

                      if door.State = Open && reciprocal then Some(roomId, door.ToRoom) else None))

          let inRoom = update (EnterM5Room source) { started with Floor = { started.Floor with CurrentRoom = source } } |> fst
          let potId = inRoom.M5Obstacles |> List.find (fun obstacle -> obstacle.Kind = Rogue3.Entities.ObstacleKind.Pot) |> _.Id
          let broken = update (DamageM5Obstacle(potId, 99)) inRoom |> fst
          Expect.isFalse (broken.M5Obstacles |> List.exists (fun obstacle -> obstacle.Id = potId)) "the pot is destroyed while the player is in the room"

          let fought =
              broken.M5Enemies
              |> List.map _.Id
              |> List.fold (fun current enemyId -> update (DamageM5Enemy(enemyId, 9999.0)) current |> fst) broken

          let direction = roomDirection source destination fought.Floor |> Option.get
          let crossed = update (TraverseDoor destination) fought |> fst
          Expect.equal crossed.Floor.CurrentRoom destination "CurrentRoom becomes the destination"
          Expect.isTrue crossed.Floor.Rooms.[destination].Visited "the destination is marked visited"
          Expect.contains crossed.Floor.MapRevealed destination "and revealed"

          let landing =
              match direction with
              | East -> crossed.PlayerPosition.Vx, playerRadius + 4.0
              | West -> crossed.PlayerPosition.Vx, playfieldWidth - playerRadius - 4.0
              | South -> crossed.PlayerPosition.Vy, playerRadius + 4.0
              | North -> crossed.PlayerPosition.Vy, playfieldHeight - playerRadius - 4.0

          Expect.floatClose Accuracy.medium (fst landing) (snd landing) "the player enters at the reciprocal doorway"
          Expect.isGreaterThan (magnitude (sub crossed.PlayerPosition (wallMidpoint direction))) playerRadius "and does not overlap the transition trigger it came from"

          let returned = update (TraverseDoor source) crossed |> fst
          Expect.equal returned.Floor.CurrentRoom source "the return crossing re-enters the first room"
          Expect.isTrue returned.Floor.Rooms.[source].Cleared "its cleared state is preserved"
          Expect.isEmpty returned.M5Enemies "a cleared room does not repopulate"
          Expect.isFalse (returned.M5Obstacles |> List.exists (fun obstacle -> obstacle.Id = potId)) "the destroyed obstacle stays destroyed"
          Expect.equal returned.DropRng crossed.DropRng "and its clear drop is never rolled a second time"

      16,
      "locked doors consume exactly one key and stay unlocked",
      fun () ->
          let started = startedRun 0xC0FFEEUL
          let treasure = roomOfType Treasure started
          let neighbour = started.Floor.Graph.[treasure] |> List.head

          let atDoor =
              { started with
                  Floor = { started.Floor with CurrentRoom = neighbour }
                  PlayerCurrency = { started.PlayerCurrency with Keys = 1 } }

          let locked (floor: Floor) fromRoom toRoom =
              floor.Rooms.[fromRoom].Doors |> List.exists (fun door -> door.ToRoom = toRoom && door.State = LockedKey)

          Expect.isTrue (locked atDoor.Floor neighbour treasure) "the approach record starts LockedKey"
          Expect.isTrue (locked atDoor.Floor treasure neighbour) "and so does its reciprocal record"

          let blocked = update (TraverseDoor treasure) atDoor |> fst
          Expect.equal blocked.Floor.CurrentRoom neighbour "a locked door cannot be traversed"

          let unlocked = update (UnlockDoor treasure) atDoor |> fst
          Expect.equal unlocked.PlayerCurrency.Keys 0 "keys become zero"

          let isOpen (floor: Floor) fromRoom toRoom =
              floor.Rooms.[fromRoom].Doors |> List.exists (fun door -> door.ToRoom = toRoom && door.State = Open)

          Expect.isTrue (isOpen unlocked.Floor neighbour treasure) "the approach record becomes Open"
          Expect.isTrue (isOpen unlocked.Floor treasure neighbour) "and so does its reciprocal record"
          let entered = update (TraverseDoor treasure) unlocked |> fst
          Expect.equal entered.Floor.CurrentRoom treasure "traversal is now allowed"

          let reEntered = update (UnlockDoor neighbour) { entered with PlayerCurrency = { entered.PlayerCurrency with Keys = 1 } } |> fst
          Expect.equal reEntered.PlayerCurrency.Keys 1 "re-entering never consumes another key"

          let keyless = { atDoor with PlayerCurrency = { atDoor.PlayerCurrency with Keys = 0 } }
          let refused = update (UnlockDoor treasure) keyless |> fst
          Expect.equal refused.Floor keyless.Floor "with zero keys neither door state nor room changes"
          Expect.equal refused.PlayerCurrency keyless.PlayerCurrency "and no currency moves"

      17,
      "the boss door seals the encounter and opens only on boss death",
      fun () ->
          let started = startedRun 0xC0FFEEUL
          let bossId = roomOfType Boss started
          let entered = update (EnterM5Room bossId) started |> fst
          Expect.isNonEmpty entered.M5Room.Doors "the boss room has exits"
          Expect.isTrue (entered.M5Room.Doors |> List.forall ((=) Rogue3.Entities.DoorState.BossSealed)) "every exit becomes BossSealed on activation"
          Expect.isSome entered.M5Boss "the boss is live"
          Expect.isNonEmpty entered.M5Enemies "the encounter has ordinary adds"

          let addsDead =
              entered.M5Enemies
              |> List.map _.Id
              |> List.fold (fun current enemyId -> update (DamageM5Enemy(enemyId, 9999.0)) current |> fst) entered

          Expect.isSome addsDead.M5Boss "the boss is still alive"
          Expect.isTrue (addsDead.M5Room.Doors |> List.forall ((=) Rogue3.Entities.DoorState.BossSealed)) "exits stay sealed while the boss lives"

          let defeated = update (DamageM5Boss 99999.0) addsDead |> fst
          Expect.isNone defeated.M5Boss "the boss dies"
          Expect.isTrue (defeated.M5Room.Doors |> List.forall ((=) Rogue3.Entities.DoorState.Open)) "exits open"
          Expect.isSome defeated.M5Room.Reward "exactly one reward pedestal"
          Expect.isTrue defeated.M5Room.Trapdoor "and one trapdoor spawn"
          Expect.hasLength (defeated.Floor.Rooms.[bossId].Fixtures |> List.filter ((=) Trapdoor)) 1 "the floor records exactly one trapdoor"

      18,
      "trapdoor descent carries run state and replaces floor state",
      fun () ->
          let started = startedRun 0xC0FFEEUL

          // M11: descend from the room that actually depicts a trapdoor — the cleared boss room —
          // standing on it, which is now the only state `DescendFloor` accepts. The seeded room-local
          // collections are applied AFTER that staging, because `EnterM5Room` on a cleared room
          // replaces `M5Enemies`/`M5Obstacles`/`M5ShopSlots`/`EnemyBullets` with the room's own
          // (empty) contents — which would leave `Expect.isEmpty descended.M5Enemies` unable to fail.
          let loaded =
              { standOnTrapdoor started with
                  PlayerItems = [ { Id = "carried"; Modifiers = [] } ]
                  PlayerHealth = { started.PlayerHealth with SoulHalfHearts = 2 }
                  PlayerCurrency = { Coins = 12; Keys = 3; Bombs = 4 }
                  M5Enemies = [ Rogue3.Entities.spawn 1 4242 Rogue3.Entities.EnemyKind.Grub (vec2 300.0 300.0) ]
                  EnemyBullets = [ enemyBullet 1 zero 1 ]
                  Bombs = [ { Id = 1; Position = zero; FuseTicks = 5 } ]
                  M5ShopSlots = [ { Id = 1; Offer = Rogue3.Entities.ShopOffer.Consumable Rogue3.Entities.PickupKind.Key; Price = 1; KeyLocked = false } ]
                  M5Obstacles = [ Rogue3.Entities.obstacle Rogue3.Entities.ObstacleKind.Rock 1 |> Rogue3.Entities.obstacleAt (vec2 200.0 200.0) ] }

          Expect.isNonEmpty loaded.M5Enemies "the pre-descend state really carries live actors"
          Expect.isNonEmpty loaded.M5Obstacles "and room obstacles"
          Expect.isNonEmpty loaded.M5ShopSlots "and shop stock"
          Expect.isNonEmpty loaded.EnemyBullets "and bullets"

          let descended = update DescendFloor loaded |> fst
          Expect.equal descended.FloorIndex 2 "FloorIndex increments"
          Expect.notEqual descended.Floor.Seed started.Floor.Seed "the next seeded floor is generated"
          Expect.equal descended.Floor.CurrentRoom 0 "the player starts in its START room"
          Expect.equal descended.Floor.Rooms.[0].RoomType Start "which really is the START room"
          Expect.equal (descended.PlayerItems |> List.map _.Id) [ "carried" ] "items persist"
          Expect.equal descended.PlayerStats loaded.PlayerStats "stats persist"
          Expect.equal descended.PlayerHealth loaded.PlayerHealth "health persists"
          Expect.equal descended.PlayerCurrency { Coins = 12; Keys = 3; Bombs = 4 } "coins, keys and bombs persist"
          Expect.isEmpty descended.M5Enemies "live actors do not"
          Expect.isEmpty descended.EnemyBullets "nor bullets"
          Expect.isEmpty descended.Bombs "nor bombs"
          Expect.isEmpty descended.M5ShopSlots "nor shop stock"
          Expect.isEmpty descended.M5Obstacles "nor room obstacles"
          Expect.isTrue (descended.Floor.Rooms |> Map.forall (fun _ room -> room.Cleared = (room.RoomType = Start || room.RoomType = Treasure || room.RoomType = Shop))) "room-clear state does not carry across the descent"

      19,
      "bomb spend, fuse, blast and chain detonation resolve exactly once",
      fun () ->
          let bombKey = Set.singleton (ViewerKeyboard.toKeyId (Letter 'Q'))
          let twoBombs = { initialModel with PlayerCurrency = { initialModel.PlayerCurrency with Bombs = 2 } }
          Expect.isEmpty twoBombs.Bombs "no active bomb"
          let pressed = update (InputChanged { twoBombs.Input.Current with Keys = bombKey }) twoBombs |> fst |> update (Tick fixedDt) |> fst
          Expect.hasLength pressed.Bombs 1 "one bomb entity spawns"
          Expect.equal pressed.PlayerCurrency.Bombs 1 "currency becomes one"

          let held = update (Tick fixedDt) pressed |> fst
          Expect.hasLength held.Bombs 1 "holding the key does not spawn a second bomb"

          let target id hp = grubAt id (vec2 700.0 390.0) hp

          let chain =
              { initialModel with
                  PlayerPosition = vec2 100.0 100.0
                  Bombs = [ { Id = 1; Position = vec2 700.0 390.0; FuseTicks = 1 }; { Id = 2; Position = vec2 740.0 390.0; FuseTicks = 400 } ]
                  M5Enemies = [ target 10 200.0 ] }

          let blasted = update (Tick fixedDt) chain |> fst
          Expect.isEmpty blasted.Bombs "the second bomb inside the blast detonates that step"
          Expect.floatClose Accuracy.high (blasted.M5Enemies |> List.head |> _.HitPoints) 120.0 "each explosion applies its damage exactly once"
          let later = update (Tick(fixedDt * 4.0)) blasted |> fst
          Expect.floatClose Accuracy.high (later.M5Enemies |> List.head |> _.HitPoints) 120.0 "neither bomb can explode again on a later step"

      20,
      "pickup caps and health ordering follow the resource rules",
      fun () ->
          Expect.equal (addCurrency 3 98) 99 "a +3 pickup at 98 caps at 99"

          let capped = { Coins = addCurrency 3 98; Keys = addCurrency 3 98; Bombs = addCurrency 3 98 }
          Expect.equal capped { Coins = 99; Keys = 99; Bombs = 99 } "every currency caps at 99"

          let mixed = { RedContainers = 3; RedHalfHearts = 6; SoulHalfHearts = 2; BlackHalfHearts = 2 }
          let afterBlack, bursts = applyDamage 2 mixed
          Expect.equal afterBlack.BlackHalfHearts 0 "black hearts are consumed first"
          Expect.equal afterBlack.SoulHalfHearts 2 "soul hearts are untouched while black remains"
          Expect.equal afterBlack.RedHalfHearts 6 "and red is untouched"
          Expect.equal bursts 1 "one depleted black heart emits one burst"
          let afterSoul, _ = applyDamage 2 afterBlack
          Expect.equal afterSoul.SoulHalfHearts 0 "soul is consumed before red"
          Expect.equal afterSoul.RedHalfHearts 6 "red is still untouched"
          let afterRed, _ = applyDamage 2 afterSoul
          Expect.equal afterRed.RedHalfHearts 4 "red is consumed last"

          let enemy = grubAt 1 (vec2 900.0 300.0) 50.0

          let burst =
              update
                  (Tick fixedDt)
                  { initialModel with
                      PlayerHealth = { initialModel.PlayerHealth with BlackHalfHearts = 2 }
                      M5Enemies = [ enemy ]
                      EnemyBullets = [ enemyBullet 1 (add initialModel.PlayerPosition (vec2 10.0 0.0)) 2 ] }
              |> fst

          Expect.equal burst.BlackHeartBursts 1 "the production step counts the depleted black heart"
          Expect.floatClose Accuracy.high (burst.M5Enemies |> List.head |> _.HitPoints) 40.0 "and emits one 10-damage room burst"

          let full = { RedContainers = 12; RedHalfHearts = 24; SoulHalfHearts = 0; BlackHalfHearts = 0 }
          Expect.equal (addTemporaryHearts 6 6 full) full "temporary-heart pickups never exceed the documented cap"
          Expect.equal (healRed 99 full) full "healing never exceeds the red container cap"
          Expect.equal (addRedContainer full).RedContainers 12 "container pickups cap at twelve"

      21,
      "enemy and boss state machines respect cadence and ownership",
      fun () ->
          let kinds =
              [ Rogue3.Entities.EnemyKind.Grub; Rogue3.Entities.EnemyKind.Maggot; Rogue3.Entities.EnemyKind.Spitter
                Rogue3.Entities.EnemyKind.Fly; Rogue3.Entities.EnemyKind.Charger; Rogue3.Entities.EnemyKind.Turret
                Rogue3.Entities.EnemyKind.Caster; Rogue3.Entities.EnemyKind.Brute ]

          let roster = kinds |> List.mapi (fun index kind -> Rogue3.Entities.spawn 1 (600 + index) kind (vec2 (200.0 + float index * 40.0) 320.0))

          let live =
              { initialModel with
                  PlayerPosition = vec2 400.0 320.0
                  M5Enemies = roster }

          let stepped = update (Tick fixedDt) live |> fst
          Expect.equal (stepped.M5AiDecisions - live.M5AiDecisions) roster.Length "every live actor takes exactly one decision per fixed step"

          let charger = Rogue3.Entities.spawn 1 700 Rogue3.Entities.EnemyKind.Charger (vec2 380.0 320.0)

          let chargerModel =
              { initialModel with
                  PlayerPosition = vec2 400.0 320.0
                  M5Enemies = [ charger ] }

          let phases =
              [ 1..ticksFor 3.0 ]
              |> List.scan (fun model _ -> update (Tick fixedDt) model |> fst) chargerModel
              |> List.map (fun model -> model.M5Enemies |> List.tryHead |> Option.map _.State)
              |> List.choose id
              |> List.distinct

          Expect.isGreaterThan phases.Length 1 "the Charger crosses its documented wind-up/dash/recover transitions at its configured cadence"

          // The bullet is placed exactly on the actor, so this assertion still fails if enemy
          // bullets ever start hitting enemies: the actor is the only enemy representation now, and
          // 30.0 is its full hit points.
          let victim = grubAt 800 (vec2 500.0 320.0) 30.0
          let friendlyFire = update (Tick fixedDt) { initialModel with M5Enemies = [ victim ]; EnemyBullets = [ enemyBullet 1 (vec2 500.0 320.0) 3 ] } |> fst
          Expect.floatClose Accuracy.high (friendlyFire.M5Enemies |> List.head |> _.HitPoints) 30.0 "enemy bullets cannot damage enemies"

          let ownShots = spawnShots 0 1 initialModel.PlayerPosition zero (vec2 1.0 0.0) basePlayerStats
          let selfHarm = update (Tick fixedDt) { initialModel with ShotSpawns = ownShots } |> fst
          Expect.equal (totalHalfHearts selfHarm.PlayerHealth) 6 "player shots cannot damage the player"

          let turret = Rogue3.Entities.spawn 1 900 Rogue3.Entities.EnemyKind.Turret (vec2 500.0 320.0)
          let dead = { turret with HitPoints = 0.0 }

          let afterDeath =
              [ 1..ticksFor 3.0 ]
              |> List.fold
                  (fun model _ -> update (Tick fixedDt) model |> fst)
                  { initialModel with PlayerPosition = vec2 520.0 320.0; M5Enemies = [ dead ] }

          Expect.isEmpty afterDeath.EnemyBullets "a dead actor emits no later attack"

      22,
      "obstacles use distinct collision and destruction rules",
      fun () ->
          let started = startedRun 0xC0FFEEUL
          let entered = update (EnterM5Room(roomOfType Combat started)) started |> fst

          Expect.equal
              (entered.M5Obstacles |> List.map _.Kind |> Set.ofList)
              (Set.ofList
                  [ Rogue3.Entities.ObstacleKind.Rock; Rogue3.Entities.ObstacleKind.TintedRock
                    Rogue3.Entities.ObstacleKind.Pot; Rogue3.Entities.ObstacleKind.Spikes
                    Rogue3.Entities.ObstacleKind.Pit ])
              "the generated room instantiates every obstacle kind"

          Expect.isTrue (Rogue3.Entities.blocksMovement Rogue3.Entities.MovementClass.Grounded Rogue3.Entities.ObstacleKind.Pit) "a pit blocks grounded movement"
          Expect.isFalse (Rogue3.Entities.blocksMovement Rogue3.Entities.MovementClass.Flying Rogue3.Entities.ObstacleKind.Pit) "but not flying movement"
          Expect.isFalse (Rogue3.Entities.blocksShots Rogue3.Entities.ObstacleKind.Pit) "and does not block shots"
          Expect.isTrue (Rogue3.Entities.blocksShots Rogue3.Entities.ObstacleKind.Rock) "a rock blocks shots"
          Expect.equal (Rogue3.Entities.spikeDamage Rogue3.Entities.ObstacleKind.Spikes) 1 "spikes deal contact damage"

          let spike = Rogue3.Entities.obstacle Rogue3.Entities.ObstacleKind.Spikes 702 |> Rogue3.Entities.obstacleAt initialModel.PlayerPosition
          let hurt = update (Tick fixedDt) { initialModel with M5Obstacles = [ spike ] } |> fst
          Expect.equal (totalHalfHearts hurt.PlayerHealth) 5 "standing on spikes hurts"

          let rock = Rogue3.Entities.obstacle Rogue3.Entities.ObstacleKind.Rock 704
          let inert = update (DamageM5Obstacle(704, 999)) { initialModel with M5Obstacles = [ rock ] } |> fst
          Expect.hasLength inert.M5Obstacles 1 "a rock is indestructible"
          Expect.equal inert.DropRng initialModel.DropRng "and rolls nothing"

          // Two blast resolutions reaching the same pot inside ONE fixed step must not double-destroy
          // or double-roll it: the chained second bomb sees a floor the first one already changed.
          let pot = Rogue3.Entities.obstacle Rogue3.Entities.ObstacleKind.Pot 703 |> Rogue3.Entities.obstacleAt (vec2 700.0 390.0)

          let doubled =
              update
                  (Tick fixedDt)
                  { initialModel with
                      PlayerPosition = vec2 100.0 100.0
                      M5Obstacles = [ pot ]
                      Bombs = [ { Id = 1; Position = vec2 700.0 390.0; FuseTicks = 1 }; { Id = 2; Position = vec2 740.0 390.0; FuseTicks = 400 } ] }
              |> fst

          Expect.isEmpty doubled.Bombs "both bombs resolved in the same step"
          Expect.isEmpty doubled.M5Obstacles "the pot is destroyed exactly once"
          Expect.isLessThanOrEqual doubled.M5ObstacleDrops.Length 1 "and rolled at most one drop"

          let again = update (DamageM5Obstacle(703, 99)) doubled |> fst
          Expect.equal again.DropRng doubled.DropRng "a destroyed obstacle cannot draw again"

      23,
      "fire cadence, roll commitment, bounce and homing terminate safely",
      fun () ->
          let aim = add initialModel.PlayerPosition (vec2 200.0 0.0)
          let firing = { initialModel.Input.Current with MousePosition = Some aim; MousePrimaryDown = true }
          let held = update (InputChanged firing) initialModel |> fst

          let afterOneSecond =
              [ 1..ticksFor 1.0 ]
              |> List.fold (fun model _ -> update (Tick fixedDt) model |> fst) held

          let expectedShots = int (floor (basePlayerStats.FireRate * 1.0)) + 1
          Expect.equal afterOneSecond.TotalShotSpawns expectedShots "held fire spawns only at the effective fire-rate cadence"

          let dodgeKeys = Set.singleton (ViewerKeyboard.toKeyId Space)
          let rollStart = update (InputChanged { firing with Keys = dodgeKeys }) initialModel |> fst |> update (Tick fixedDt) |> fst
          Expect.equal rollStart.TotalShotSpawns 0 "no shot spawns during roll commitment"
          Expect.isGreaterThan rollStart.DodgeRollTicks 0 "the roll is committed"

          let room: Rect = { X = 0.0; Y = 0.0; Width = 1000.0; Height = 600.0 }
          let wall: Rect = { X = 0.0; Y = 0.0; Width = 20.0; Height = 600.0 }

          let intoWall bounce =
              let mutable shots = spawnShots 0 1 (vec2 30.0 300.0) zero (vec2 -1.0 0.0) { basePlayerStats with Bounce = bounce }

              for _ in 1..4 do
                  let stepped, _, _ = stepShots room [ wall ] [] shots
                  shots <- stepped

              shots

          let bounced = intoWall 1
          Expect.hasLength bounced 1 "a shot with one bounce survives its first wall contact"
          Expect.equal (bounced |> List.head |> _.BouncesRemaining) 0 "the bounce counter decrements once per contact"
          Expect.isGreaterThan (bounced |> List.head |> _.Velocity.Vx) 0.0 "and the contact reflected it"
          Expect.isEmpty (intoWall 0) "a spent bounce budget removes the shot at the wall"

          let homingStats = { basePlayerStats with Homing = 1.0 }
          let homingShot = spawnShots 0 1 (vec2 400.0 300.0) zero (vec2 1.0 0.0) homingStats
          let targets = [ { Id = 1; Position = vec2 400.0 100.0 } ]
          let steered, _, _ = stepShots { X = 0.0; Y = 0.0; Width = 1280.0; Height = 720.0 } [] targets homingShot
          let before = homingShot |> List.head |> _.Velocity
          let after = steered |> List.head |> _.Velocity
          let turn = abs (atan2 after.Vy after.Vx - atan2 before.Vy before.Vx)
          Expect.isLessThanOrEqual turn (homingStats.Homing * 2.0 * Math.PI * fixedDt + 1e-9) "homing turns no faster than its cap"

          let rec expire steps shots =
              let stepped, _, _ = stepShots { X = 0.0; Y = 0.0; Width = 1.0e9; Height = 1.0e9 } [] targets shots
              if List.isEmpty stepped then steps else expire (steps + 1) stepped

          Expect.isGreaterThan (expire 0 (spawnShots 0 1 (vec2 400.0 300.0) zero (vec2 1.0 0.0) homingStats)) 0 "range expiry still removes every homing shot"

      24,
      "pause and restart isolate all simulation state",
      fun () ->
          let host = EvidenceCommands.interactiveHost
          let menu = host.Init() |> fst
          let playing = host.Update (EvidenceCommands.StartFreshRun 4242UL) menu |> fst
          Expect.equal playing.Shell.Screen Rogue3.GameShell.Playing "the run is live"

          let enemy = { grubAt 1 (vec2 900.0 300.0) 50.0 with Velocity = vec2 20.0 0.0 }

          let busy =
              { playing with
                  Play =
                      { playing.Play with
                          M5Enemies = [ enemy ]
                          EnemyBullets = [ { enemyBullet 1 (vec2 300.0 300.0) 1 with Velocity = vec2 100.0 0.0 } ]
                          Bombs = [ { Id = 1; Position = vec2 700.0 390.0; FuseTicks = 60 } ]
                          DodgeCooldownTicks = 40
                          FloorNameTicks = 100
                          M6CameraTransition = Some { Direction = RoomSlideDirection.East; ElapsedTicks = 3; FromRoom = 0 }
                          ShotSpawns = spawnShots 0 1 (vec2 400.0 300.0) zero (vec2 1.0 0.0) basePlayerStats } }

          let escape = host.MapKey ViewerKey.Escape true |> Option.get
          let paused = host.Update escape busy |> fst
          Expect.equal paused.Shell.Screen Rogue3.GameShell.Paused "Esc pauses through the production host"
          let frozen = Replay.modelBytes paused.Play
          let ticked = host.Update (EvidenceCommands.PlayDispatch(Tick(fixedDt * 8.0))) paused |> fst
          Expect.equal (Replay.modelBytes ticked.Play) frozen "no enemy, bullet, fuse, cooldown, banner or room transition advances while paused"

          let resumed = host.Update escape ticked |> fst
          let advanced = host.Update (EvidenceCommands.PlayDispatch(Tick(fixedDt * 8.0))) resumed |> fst
          Expect.notEqual (Replay.modelBytes advanced.Play) frozen "and the same tick advances the simulation once resumed"

          let terminal = host.Update (EvidenceCommands.PlayDispatch(CompleteRunStats(false, Some DeathCause.Trap))) advanced |> fst
          Expect.equal terminal.Play.RunOutcome (Some RunOutcome.GameOver) "the run ends"
          let restarted = host.Update (EvidenceCommands.PlayDispatch(StartRun 4242UL)) terminal |> fst
          let fresh = update (StartRun 4242UL) initialModel |> fst
          let comparable (model: Model) = { model with Profile = defaultMetaProfile; LastRunSummary = None }
          Expect.equal (Replay.modelBytes (comparable restarted.Play)) (Replay.modelBytes (comparable fresh)) "all run, floor, entity, input and accumulator state equals a fresh run for that seed"
          Expect.notEqual restarted.Play.Profile defaultMetaProfile "while only the MetaProfile persists" ]

// --------------------------------------------------------------------------------------------
// §13 determinism and replay.
// --------------------------------------------------------------------------------------------

/// One authored run: actions and their exact timing in a single ordered stream.
let private authoredLog =
    let key letter down = KeyChanged(ViewerKeyboard.toKeyId (Letter letter), down)

    [ yield Started
      for round in 0..9 do
          yield key 'D' true
          yield PointerChanged(vec2 (700.0 + float round * 10.0) 360.0, Some true)
          yield Tick(1.0 / 60.0)
          yield key 'D' false
          yield key 'W' true
          yield Tick 0.033
          yield key 'W' false
          yield Tick(1.0 / 120.0) ]
    |> List.mapi (fun index message -> { Replay.Sequence = index; Replay.Message = message })

let private determinismTests =
    [ testCase "the canonical encoding sees a divergence that sprintf %A cannot" (fun _ ->
          let long = [ 1..600 ]
          let diverged = [ 1..599 ] @ [ 999 ]
          Expect.equal (sprintf "%A" long) (sprintf "%A" diverged) "%A truncates after 100 elements, so the two lists format identically"
          Expect.notEqual (Determinism.encode long) (Determinism.encode diverged) "the canonical encoding is not length limited"
          Expect.notEqual (Determinism.digest long) (Determinism.digest diverged) "and neither is its digest")

      testCase "the canonical encoding is stable, total and value-determined" (fun _ ->
          Expect.equal (Determinism.encode initialModel) (Determinism.encode initialModel) "encoding the same model twice is byte identical"
          Expect.equal (Determinism.encode (initialModelForSeed 5UL)) (Determinism.encode (initialModelForSeed 5UL)) "two independently built models of one seed encode identically"
          Expect.notEqual (Determinism.encode (initialModelForSeed 5UL)) (Determinism.encode (initialModelForSeed 6UL)) "different seeds encode differently"
          Expect.equal (Determinism.encode (Set.ofList [ 3; 1; 2 ])) (Determinism.encode (Set.ofList [ 2; 3; 1 ])) "set insertion order is not observable"
          Expect.equal (Determinism.encode (Map.ofList [ 2, "b"; 1, "a" ])) (Determinism.encode (Map.ofList [ 1, "a"; 2, "b" ])) "map insertion order is not observable"
          Expect.notEqual (Determinism.encode nan) (Determinism.encode 0.0) "special floats stay distinguishable")

      testCase "seed plus input log replays byte-identically" (fun _ ->
          let first = Replay.replay 0xC0FFEEUL authoredLog
          let second = Replay.replay 0xC0FFEEUL authoredLog
          Expect.equal (Replay.modelBytes second) (Replay.modelBytes first) "two independent replays of one seed and log produce byte-identical models"
          Expect.equal (snd (Replay.record 0xC0FFEEUL authoredLog)) (Replay.modelBytes first) "recording and replaying agree"
          Expect.isGreaterThan first.SimStepCount 0 "the replay really advanced the simulation"
          Expect.notEqual (Replay.modelBytes (Replay.replay 0xC0FFEFUL authoredLog)) (Replay.modelBytes first) "another seed replays to a different run")

      testCase "identical actions with different timing replay to a different run" (fun _ ->
          let retimed =
              authoredLog
              |> List.map (fun entry ->
                  match entry.Message with
                  | Tick dt -> { entry with Replay.Message = Tick(dt * 2.0) }
                  | _ -> entry)

          Expect.notEqual (Replay.modelBytes (Replay.replay 0xC0FFEEUL retimed)) (Replay.modelBytes (Replay.replay 0xC0FFEEUL authoredLog)) "timing is part of the replay contract, not a free variable")

      testCase "a log whose sequence numbers are not unique and increasing is rejected" (fun _ ->
          let duplicated = authoredLog |> List.map (fun entry -> { entry with Replay.Sequence = 0 })
          let reordered = authoredLog |> List.rev
          Expect.isFalse (Replay.isCanonical duplicated) "duplicate sequence numbers are not canonical"
          Expect.isFalse (Replay.isCanonical reordered) "a reordered log is not canonical"
          Expect.isTrue (Replay.isCanonical authoredLog) "the authored log is canonical"
          Expect.throwsT<ArgumentException> (fun () -> Replay.replay 0xC0FFEEUL duplicated |> ignore) "replaying a non-canonical log raises instead of silently reordering") ]

[<Tests>]
let m10AcceptanceDeterminismTests =
    testList
        "M10 acceptance and determinism"
        [ yield!
              (scenarios
               |> List.map (fun (number, title, body) -> testCase (sprintf "AC%02d %s" number title) (fun _ -> body ())))
          yield
              testCase "the sweep covers source-specification scenarios 1 through 24 exactly once" (fun _ ->
                  let numbers = scenarios |> List.map (fun (number, _, _) -> number)
                  Expect.equal (List.sort numbers) [ 1..24 ] "every §14 acceptance scenario has exactly one named test"
                  Expect.equal (List.distinct numbers).Length numbers.Length "and no scenario is claimed twice"

                  let titles = scenarios |> List.map (fun (_, title, _) -> title)
                  Expect.equal (List.distinct titles).Length titles.Length "each scenario has a distinct name")
          yield! determinismTests ]
