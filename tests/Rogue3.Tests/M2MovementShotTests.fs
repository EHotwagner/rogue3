module Rogue3.Tests.M2MovementShotTests

open System
open Expecto
open FS.GG.Game.Core
open FS.GG.UI.KeyboardInput
open Rogue3.Geometry
open Rogue3.Model
open Rogue3.PerformanceEvidence

let private close expected actual message =
    Expect.floatClose Accuracy.high expected actual message

let private tick model = update (Tick fixedDt) model |> fst
let private key value = ViewerKeyboard.toKeyId value

let private withKeys keys model =
    let snapshot = { model.Input.Current with Keys = Set.ofList keys }
    update (InputChanged snapshot) model |> fst

let private room: Rect = { X = 0.0; Y = 0.0; Width = playfieldWidth; Height = playfieldHeight }

let private angleDegrees vector = atan2 vector.Vy vector.Vx * 180.0 / Math.PI

[<Tests>]
let movementTests =
    testList "M2 movement" [
        testCase "acceleration friction normalization and speed clamp are fixed-step bounded" <| fun _ ->
            let cardinal = initialModel |> withKeys [ Letter 'D' |> key ] |> tick
            close 20.0 (magnitude cardinal.PlayerVelocity) "2400 px/s2 adds 20 px/s per fixed step"

            let diagonal = initialModel |> withKeys [ Letter 'D' |> key; Letter 'S' |> key ] |> tick
            close 20.0 (magnitude diagonal.PlayerVelocity) "diagonal input is normalized before acceleration"
            close diagonal.PlayerVelocity.Vx diagonal.PlayerVelocity.Vy "diagonal axes are equal"

            let released = { initialModel with PlayerVelocity = vec2 240.0 0.0 } |> tick
            close 215.0 released.PlayerVelocity.Vx "3000 px/s2 friction removes 25 px/s"

            let fastStats = { basePlayerStats with SpeedMultiplier = 10.0 }
            let saturated =
                { initialModel with PlayerStats = fastStats; PlayerVelocity = vec2 800.0 0.0 }
                |> withKeys [ Letter 'D' |> key ]
                |> tick
            close 540.0 (magnitude saturated.PlayerVelocity) "effective speed and velocity clamp at 540"

        testCase "radius-13 player slides along an AABB obstacle by X then Y" <| fun _ ->
            // Board item #20: the collider is DERIVED from a real obstacle by `blockingObstacleRects`
            // rather than handed in as a free-floating rect the game could never produce. A Rock
            // centred at (120, 50) is the 40x40 box spanning X 100..140, so the left face the player
            // stops at is exactly where the hand-written rect's was.
            let rock =
                Rogue3.Entities.obstacle Rogue3.Entities.ObstacleKind.Rock 1
                |> Rogue3.Entities.obstacleAt (vec2 120.0 50.0)
            let velocity = normalizeOrZero (vec2 1.0 1.0) |> scale 240.0
            let model =
                { initialModel with
                    PlayerPosition = vec2 86.0 50.0
                    PlayerVelocity = velocity
                    Obstacles = [ rock ] }
                |> withKeys [ Letter 'D' |> key; Letter 'S' |> key ]
                |> tick
            close 87.0 model.PlayerPosition.Vx "circle stops with its radius at the wall face"
            Expect.isGreaterThan model.PlayerPosition.Vy 50.0 "unblocked Y movement continues"
            Expect.isLessThanOrEqual (magnitude model.PlayerVelocity) 240.0 "ordinary speed remains clamped"

        testCase "dodge impulse timers cooldown and fire lockout are deterministic" <| fun _ ->
            let input =
                { initialModel.Input.Current with
                    Keys = Set.singleton (key Space)
                    MousePosition = Some(vec2 900.0 360.0)
                    MousePrimaryDown = true }
            let started = initialModel |> update (InputChanged input) |> fst |> tick
            close rollSpeed (magnitude started.PlayerVelocity) "roll starts at 460 px/s"
            Expect.equal started.DodgeIFrameTicks (dodgeIFrameTicks - 1) "activation consumes the first of 48 i-frame ticks"
            Expect.equal started.DodgeRollTicks (rollDurationTicks - 1) "roll lasts 54 fixed ticks"
            Expect.equal started.DodgeCooldownTicks (dodgeCooldownTicks - 1) "cooldown is 108 fixed ticks start-to-start"
            Expect.isEmpty started.ShotSpawns "fire is locked during dodge i-frames"

            let throughIFrames = [ 1 .. dodgeIFrameTicks - 1 ] |> List.fold (fun m _ -> stepSim m) started
            Expect.equal throughIFrames.DodgeIFrameTicks 0 "exactly 48 steps including activation consume i-frames"
            Expect.isEmpty throughIFrames.ShotSpawns "the last committed i-frame step remains fire-locked"
            let firesAfter = stepSim throughIFrames
            Expect.hasLength firesAfter.ShotSpawns 1 "held fire resumes on the first step after i-frames"

            let cooled = [ 1 .. dodgeCooldownTicks - dodgeIFrameTicks ] |> List.fold (fun m _ -> stepSim m) firesAfter
            Expect.equal cooled.DodgeCooldownTicks 0 "cooldown reaches zero only after the declared interval"
    ]

[<Tests>]
let projectileTests =
    testList "M2 projectiles" [
        testCase "AC4 centered three-shot fan is -9 0 +9 and snapshots stats" <| fun _ ->
            let stats =
                { basePlayerStats with
                    Damage = 7.0; FireRate = 5.0; ShotSpeed = 600.0; Range = 2.0
                    ShotRadius = 7.0; Knockback = 55.0; Multishot = 3; Pierce = 2
                    Bounce = 1; Homing = 0.25 }
            let shots = spawnShots 10 40 (vec2 100.0 100.0) zero (vec2 1.0 0.0) stats
            Expect.equal shots.Length 3 "one event spawns exactly multishot projectiles"
            List.zip [ -9.0; 0.0; 9.0 ] shots
            |> List.iter (fun (expected, shot) ->
                close expected (angleDegrees shot.Direction) "fan angle"
                close 600.0 (magnitude shot.Velocity) "zero-inheritance velocity has shotSpeed magnitude"
                Expect.equal shot.Damage 7.0 "damage snapshot"
                Expect.equal shot.FireRate 5.0 "fire-rate snapshot"
                Expect.equal shot.Range 2.0 "range snapshot"
                Expect.equal shot.Radius 7.0 "radius snapshot"
                Expect.equal shot.HitsRemaining 3 "pierce two permits three distinct hits")

        testCase "velocity inheritance offsets live shots without rotating their base directions" <| fun _ ->
            let shot = spawnShots 1 1 zero (vec2 -240.0 0.0) (vec2 1.0 0.0) basePlayerStats |> List.exactlyOne
            close 360.0 shot.Velocity.Vx "shot inherits 0.25 of leftward player velocity"
            close 0.0 (angleDegrees shot.Direction) "base aim remains independently rightward"

        testCase "AC10 age and room exit terminate and bounce is finite" <| fun _ ->
            let shot = spawnShots 1 1 (vec2 640.0 360.0) zero (vec2 1.0 0.0) basePlayerStats |> List.exactlyOne
            let unboundedRoom: Rect = { X = 0.0; Y = 0.0; Width = 5000.0; Height = 720.0 }
            let afterLifetime =
                [ 1 .. shot.MaxAgeTicks ]
                |> List.fold (fun current _ -> stepShots unboundedRoom [] [] current |> fun (s, _, _) -> s) [ shot ]
            Expect.hasLength afterLifetime 1 "shot survives while age equals range"
            let expired, _, _ = stepShots unboundedRoom [] [] afterLifetime
            Expect.isEmpty expired "shot expires when age exceeds range"

            let nearEdge = { shot with Position = vec2 1274.0 360.0 }
            let left, _, _ = stepShots room [] [] [ nearEdge ]
            Expect.isEmpty left "no-bounce shot expires on leaving room"

            let bouncing = { nearEdge with BouncesRemaining = 1 }
            let bounced, _, _ = stepShots room [] [] [ bouncing ]
            Expect.hasLength bounced 1 "one remaining bounce keeps the shot"
            Expect.isLessThan bounced.Head.Velocity.Vx 0.0 "room-face bounce reflects velocity"
            Expect.equal bounced.Head.BouncesRemaining 0 "bounce budget decrements"

        testCase "swept obstacle hit cannot tunnel and consumes bounce" <| fun _ ->
            let thinWall: Rect = { X = 95.0; Y = 0.0; Width = 1.0; Height = 200.0 }
            let fastStats = { basePlayerStats with ShotSpeed = 900.0; Bounce = 1 }
            let shot = spawnShots 1 1 (vec2 90.0 100.0) zero (vec2 1.0 0.0) fastStats |> List.exactlyOne
            let stepped, queries, _ = stepShots room [ thinWall ] [] [ shot ]
            Expect.hasLength stepped 1 "swept cast observes the thin wall"
            Expect.equal queries 1 "one wall produces one exact cast"
            Expect.isLessThan stepped.Head.Velocity.Vx 0.0 "wall normal reflects velocity"
            Expect.equal stepped.Head.BouncesRemaining 0 "the only bounce is consumed"

        testCase "homing turn is capped, stable, and range still guarantees termination" <| fun _ ->
            let stats = { basePlayerStats with Homing = 1.0; Range = 0.4 }
            let shot = spawnShots 1 3 (vec2 100.0 100.0) zero (vec2 1.0 0.0) stats |> List.exactlyOne
            let targets = [ { Id = 2; Position = vec2 100.0 200.0 }; { Id = 1; Position = vec2 100.0 200.0 } ]
            let turned, _, queries = stepShots room [] targets [ shot ]
            close 3.0 (angleDegrees turned.Head.Velocity) "homing one caps at 360 degrees/s = 3 degrees/tick"
            Expect.equal queries 2 "cost records every target considered"
            let ended =
                [ 1 .. shot.MaxAgeTicks + 1 ]
                |> List.fold (fun current _ -> stepShots room [] targets current |> fun (s, _, _) -> s) [ shot ]
            Expect.isEmpty ended "homing never defeats range termination"

        testCase "non-finite projectile state terminates instead of poisoning the replay" <| fun _ ->
            let shot = spawnShots 1 1 zero zero (vec2 1.0 0.0) basePlayerStats |> List.exactlyOne
            let invalid = { shot with Velocity = vec2 Double.NaN 0.0 }
            let stepped, _, _ = stepShots room [] [] [ invalid ]
            Expect.isEmpty stepped "invalid projectile is removed"
    ]

[<Tests>]
let performanceBindingTests =
    testList "M2 performance definition binding" [
        testCase "helper-backed initial state and messages invalidate the workload digest" <| fun _ ->
            let workload = expectedWorkloads |> List.find (fun item -> item.Id = "maximum-content")
            let baseline = definitionDigest workload
            let changedState =
                { workload with
                    InitialState = fun () ->
                        let model = workload.InitialState()
                        { model with Ball = { model.Ball with Pos = vec2 321.0 123.0 } } }
            let changedMessages =
                { workload with
                    MessagesAt = fun frame -> if frame = 2 then NoOp :: workload.MessagesAt frame else workload.MessagesAt frame }
            Expect.notEqual (definitionDigest changedState) baseline "a previously omitted complete-model field stales Authored"
            Expect.notEqual (definitionDigest changedMessages) baseline "a frame-2-only message helper change stales Authored"
    ]
