module Rogue3M6RenderingEnemySymbologyTests

open Expecto
open FS.GG.UI.Scene
open FS.GG.UI.Symbology
open Rogue3
open Rogue3.Geometry
open Rogue3.Model
open Rogue3.Entities

let private rosterModel =
    roster
    |> List.mapi (fun index kind -> spawn 1 (1000+index) kind (vec2 (180.0+float index*105.0) 320.0))
    |> fun enemies -> { initialModel with M5Enemies=enemies }

[<Tests>]
let m6RenderingEnemySymbologyTests =
    testList "M6 rendering and enemy symbology" [
        test "production renderer preserves the eleven back-to-front layers" {
            let actual = Render.layers rosterModel |> List.map _.Layer
            Expect.equal actual Render.layerOrder "painter order is an explicit stable contract"
            Expect.equal actual.Length 11 "M6 owns exactly the eleven §8 layers"
            Expect.equal (View.view rosterModel) (Render.view rosterModel) "production View consumes the renderer"
        }

        test "EnemyActor ChannelMap preserves hitboxes and distinguishes the whole roster" {
            let tokens = Render.enemyTokens rosterModel
            Expect.equal tokens.Length roster.Length "all eight kinds are projected"
            List.zip rosterModel.M5Enemies tokens
            |> List.iter (fun (actor, token) ->
                let definition = definition actor.Kind
                Expect.equal token.R definition.Radius "drawn radius equals the collision radius"
                Expect.equal token.Faction Faction.Enemy "all live enemies use enemy faction"
                Expect.isGreaterThan token.Health 0.0 "live health is normalized"
                Expect.isLessThanOrEqual token.Health 1.0 "health remains in domain")
            let encodings = tokens |> List.map (fun t -> t.R,t.Klass,t.Sigil,t.Threat) |> List.distinct
            Expect.equal encodings.Length roster.Length "every kind has a distinct pre-attentive encoding"
        }

        test "legibility pins only the physics-faithful Size warning" {
            let report = Render.legibility rosterModel
            Expect.isNonEmpty report.Findings "eight physical radii intentionally exceed visual Size capacity"
            report.Findings
            |> List.iter (fun finding ->
                Expect.notEqual finding.Severity Legibility.Severity.Error finding.Message
                Expect.equal finding.Channel Legibility.Channel.Size finding.Message)
            Expect.isTrue (Render.acceptedLegibility rosterModel) "no other warning is accepted"
        }

        test "particle pool keeps the newest 600 and fixed steps advance fade and expiry" {
            let spawned = update (SpawnM6Particles(650, vec2 320.0 240.0, ParticleTint.Explosion)) initialModel |> fst
            Expect.equal spawned.M6Particles.Length m6MaxParticles "pool is capped"
            Expect.equal spawned.M6Particles.Head.Id 51 "oldest overflow is recycled"
            Expect.equal (spawned.M6Particles |> List.last).Id 650 "newest request is retained"
            let first = spawned.M6Particles.Head
            let stepped = stepSim spawned
            let advanced = stepped.M6Particles |> List.find (fun p -> p.Id=first.Id)
            Expect.equal advanced.AgeTicks 1 "particle age advances on the fixed tick"
            Expect.notEqual advanced.Position first.Position "velocity advances position"
            Expect.isLessThan (Render.particleOpacity advanced) (Render.particleOpacity first) "fade is monotone"
        }

        test "room camera slide is active before 0.35 seconds and identity at 42 ticks" {
            let started = update (BeginM6RoomTransition RoomSlideDirection.East) initialModel |> fst
            Expect.equal (Render.cameraOffset started) (vec2 playfieldWidth 0.0) "entry begins one room away"
            let beforeSettle = [1..41] |> List.fold (fun model _ -> stepSim model) started
            Expect.isSome beforeSettle.M6CameraTransition "41/120 seconds is still in transition"
            Expect.notEqual (Render.cameraOffset beforeSettle) zero "pre-settle frame remains translated"
            let settled = stepSim beforeSettle
            Expect.isNone settled.M6CameraTransition "42 ticks equals exactly 0.35 seconds"
            Expect.equal (Render.cameraOffset settled) zero "settled render lowers to identity"
        }
    ]
