module Rogue3.Tests.M3CombatHealthCurrencyTests

open Expecto
open FS.GG.UI.KeyboardInput
open Rogue3.Geometry
open Rogue3.Model
open Rogue3.PerformanceEvidence

let private tick model = stepSim model
let private enemy id position hp contact =
    { Id = id; Position = position; Velocity = zero; Radius = 12.0; HitPoints = hp
      ContactDamage = contact; LastContactTick = None; HitFlashTicks = 0 }
let private bullet id position damage = { Id = id; Position = position; Radius = 5.0; Damage = damage }

[<Tests>]
let statTests =
    testList "M3 deterministic stats" [
        testCase "AC3 additive phase precedes multiplicative phase independent of pickup ordering" <| fun _ ->
            let add = { Id = "cracked-lens"; Modifiers = [ { Stat = DamageStat; Kind = Add; Value = 1.0 } ] }
            let mul = { Id = "polyphemus-shard"; Modifiers = [ { Stat = DamageStat; Kind = Mul; Value = 1.0 } ] }
            Expect.equal (recomputePlayerStats [ add; mul ]).Damage 9.0 "add then multiply"
            Expect.equal (recomputePlayerStats [ mul; add ]).Damage 9.0 "phase order is stable across pickup order"
            let clamped =
                { Id = "hostile"; Modifiers =
                    [ { Stat = DamageStat; Kind = Add; Value = -999.0 }
                      { Stat = ShotSpeedStat; Kind = Add; Value = 9999.0 }
                      { Stat = MultishotStat; Kind = Add; Value = 99.0 }
                      { Stat = SpeedMultiplierStat; Kind = Add; Value = -99.0 } ] }
                |> List.singleton |> recomputePlayerStats
            Expect.equal clamped.Damage 0.5 "damage clamp"
            Expect.equal clamped.ShotSpeed 900.0 "shot speed clamp"
            Expect.equal clamped.Multishot 12 "multishot clamp"
            Expect.equal clamped.SpeedMultiplier -0.5 "speed multiplier clamp"
    ]

[<Tests>]
let combatTests =
    testList "M3 combat" [
        testCase "shot overlap uses the grid and applies damage knockback flash and pierce once" <| fun _ ->
            let stats = { basePlayerStats with Pierce = 1 }
            let shot = spawnShots 0 1 (vec2 100.0 100.0) zero (vec2 1.0 0.0) stats |> List.exactlyOne
            let model = { initialModel with ShotSpawns = [ shot ]; Enemies = [ enemy 7 (vec2 104.0 100.0) 10.0 0 ] }
            let hit = tick model
            let target = hit.Enemies |> List.exactlyOne
            Expect.equal target.HitPoints 6.5 "snapshot damage is applied"
            Expect.isGreaterThan target.Velocity.Vx 0.0 "knockback follows shot velocity"
            Expect.isGreaterThan target.HitFlashTicks 0 "hit flash starts"
            Expect.equal hit.ShotSpawns.Head.HitsRemaining 1 "pierce budget decrements"
            Expect.contains hit.ShotSpawns.Head.HitEnemyIds 7 "stable enemy identity prevents overlap reticks"
            Expect.isGreaterThan hit.TotalCombatCandidates 0 "SpatialGrid query candidates are observable"
            let repeated = tick hit
            Expect.equal repeated.Enemies.Head.HitPoints 6.5 "one shot cannot damage the same enemy twice"

        testCase "AC6 bullet hit grants 0.80 seconds and roll i-frames reject damage" <| fun _ ->
            let p = initialModel.PlayerPosition
            let first = tick { initialModel with EnemyBullets = [ bullet 1 p 1 ] }
            Expect.equal (totalHalfHearts first.PlayerHealth) 5 "first bullet removes one half-heart"
            Expect.equal first.PostHitInvulnTicks postHitInvulnTicks "post-hit window starts at 96 ticks"
            let gated = tick { first with EnemyBullets = [ bullet 2 p 1 ] }
            Expect.equal (totalHalfHearts gated.PlayerHealth) 5 "second bullet inside the window is ignored"
            let rolling = tick { initialModel with DodgeIFrameTicks = 1; EnemyBullets = [ bullet 3 p 1 ] }
            Expect.equal (totalHalfHearts rolling.PlayerHealth) 6 "last roll i-frame protects"

        testCase "contact damage has per-enemy retick and 90 px per second knockback" <| fun _ ->
            let p = initialModel.PlayerPosition
            let first = tick { initialModel with Enemies = [ enemy 2 (add p (vec2 -1.0 0.0)) 10.0 1 ] }
            Expect.equal (totalHalfHearts first.PlayerHealth) 5 "contact damages once"
            Expect.isGreaterThanOrEqual first.PlayerVelocity.Vx 90.0 "contact applies documented impulse"
            let readyHealth = { first.PlayerHealth with RedHalfHearts = 6 }
            let ungated = { first with PlayerHealth = readyHealth; PostHitInvulnTicks = 0 }
            let tooSoon = tick ungated
            Expect.equal (totalHalfHearts tooSoon.PlayerHealth) 6 "same enemy cannot retick before 60 steps"
    ]

[<Tests>]
let resourceTests =
    testList "M3 health currency bombs and shops" [
        testCase "black then soul then red ordering emits one burst per depleted black heart and death is local" <| fun _ ->
            let health = { RedContainers = 3; RedHalfHearts = 2; SoulHalfHearts = 1; BlackHalfHearts = 4 }
            let resolved, bursts = applyDamage 6 health
            Expect.equal resolved.BlackHalfHearts 0 "black consumed first"
            Expect.equal resolved.SoulHalfHearts 0 "soul consumed second"
            Expect.equal resolved.RedHalfHearts 1 "red consumed last"
            Expect.equal bursts 2 "both depleted black hearts burst"
            let dead = tick { initialModel with PlayerHealth = { health with RedHalfHearts = 0; SoulHalfHearts = 0; BlackHalfHearts = 1 }; EnemyBullets = [ bullet 1 initialModel.PlayerPosition 1 ] }
            Expect.equal dead.PlayerLifeState Dead "zero health commits deterministic death state"

        testCase "temporary hearts and currencies hard cap and descent carry preserves resources" <| fun _ ->
            let health = addTemporaryHearts 20 20 initialModel.PlayerHealth
            Expect.equal (displayedHeartHalves health) 24 "display row is capped at twelve hearts"
            Expect.equal (addCurrency 3 98) 99 "overflow is wasted"
            let model = { initialModel with PlayerHealth = health; PlayerCurrency = { Coins = 99; Keys = 98; Bombs = 97 } }
            let carry = descentCarry model
            Expect.equal carry.Health health "all heart types persist"
            Expect.equal carry.Currency model.PlayerCurrency "all currencies persist"

        testCase "AC11 shop accepts affordable item and rejects unaffordable item" <| fun _ ->
            let item = { Id = "lens"; Modifiers = [ { Stat = DamageStat; Kind = Add; Value = 1.0 } ] }
            let slot = { Id = 4; Item = item; Cost = CoinCost 7 }
            let bought, accepted = purchaseShopSlot 4 { initialModel with PlayerCurrency = { initialModel.PlayerCurrency with Coins = 10 }; ShopSlots = [ slot ] }
            Expect.isTrue accepted "purchase accepted"
            Expect.equal bought.PlayerCurrency.Coins 3 "coins are spent"
            Expect.equal bought.PlayerStats.Damage 4.5 "item recomputes stats"
            Expect.isEmpty bought.ShopSlots "slot is emptied"
            let rejected, accepted = purchaseShopSlot 4 { initialModel with PlayerCurrency = { initialModel.PlayerCurrency with Coins = 5 }; ShopSlots = [ slot ] }
            Expect.isFalse accepted "purchase rejected"
            Expect.equal rejected.PlayerCurrency.Coins 5 "coins unchanged"
            Expect.hasLength rejected.ShopSlots 1 "item remains"

        testCase "bomb spends on edge, fuses, blasts, and chain detonates exactly once" <| fun _ ->
            let q = ViewerKeyboard.toKeyId (Letter 'Q')
            let snapshot = { initialModel.Input.Current with Keys = Set.singleton q }
            let dropped = update (InputChanged snapshot) initialModel |> fst |> update (Tick fixedDt) |> fst
            Expect.equal dropped.PlayerCurrency.Bombs 0 "bomb is spent on drop"
            Expect.hasLength dropped.Bombs 1 "one bomb spawns"
            let p = initialModel.PlayerPosition
            let model =
                { initialModel with
                    PlayerCurrency = { initialModel.PlayerCurrency with Bombs = 0 }
                    Bombs = [ { Id = 1; Position = p; FuseTicks = 1 }; { Id = 2; Position = add p (vec2 20.0 0.0); FuseTicks = 100 } ]
                    Enemies = [ enemy 9 (add p (vec2 30.0 0.0)) 100.0 0 ] }
            let exploded = tick model
            Expect.isEmpty exploded.Bombs "both bombs resolve once in the same step"
            Expect.equal exploded.Enemies.Head.HitPoints 20.0 "each blast applies its own 40 damage"
            Expect.equal (totalHalfHearts exploded.PlayerHealth) 4 "post-hit invulnerability limits self-damage to one heart"
            let later = tick exploded
            Expect.equal later.Enemies.Head.HitPoints 20.0 "resolved bombs never explode again"
    ]

[<Tests>]
let performanceBindingTests =
    testList "M3 performance binding" [
        testCase "complete-model digest binds M3 enemies bullets health and currency" <| fun _ ->
            let workload = expectedWorkloads |> List.find (fun item -> item.Id = "maximum-content")
            let baseline = definitionDigest workload
            let changed =
                { workload with
                    InitialState =
                        (fun () ->
                            let model = workload.InitialState()
                            { model with
                                PlayerCurrency = { model.PlayerCurrency with Coins = 1 }
                                EnemyBullets = model.EnemyBullets |> List.tail }) }
            Expect.notEqual (definitionDigest changed) baseline "new combat/resource state cannot escape the complete structural binding"
    ]
