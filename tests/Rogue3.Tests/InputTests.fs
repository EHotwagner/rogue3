module Rogue3.Tests.InputTests

open Expecto
open Rogue3.Model
open Rogue3.Geometry
open Rogue3.EvidenceCommands
open FS.GG.UI.KeyboardInput
open FS.GG.UI.Controls

let private key key = ViewerKeyboard.toKeyId key
let private tick dt model = Rogue3.Model.update (Tick dt) model |> fst
let private apply msg model = Rogue3.Model.update msg model |> fst

let private snapshot keys mousePosition mouseDown leftStick rightStick trigger =
    { emptyInputSnapshot with
        Keys = Set.ofList keys
        MousePosition = mousePosition
        MousePrimaryDown = mouseDown
        Gamepad =
            { LeftStick = leftStick
              RightStick = rightStick
              RightTrigger = trigger
              Buttons = Set.empty } }

let private close expected actual message =
    Expect.floatClose Accuracy.high expected actual message

[<Tests>]
let inputTests =
    testList "m1-input-twin-stick" [
        test "Tick derives a key edge once, then commits previous after simulation" {
            let space = key Space
            let latched = apply (KeyChanged(space, true)) initialModel
            let after = tick (2.0 * fixedDt) latched

            Expect.equal after.Input.PressedThisTick (Set.singleton space) "the Tick publishes current minus previous"
            Expect.equal after.Input.Previous after.Input.Current "previous becomes current after drained simulation"
            Expect.equal after.EdgeActionCount 1 "a two-step Tick consumes the edge on its first fixed step only"

            let held = tick fixedDt after
            Expect.isEmpty held.Input.PressedThisTick "a held key is not a second edge"
            Expect.equal held.EdgeActionCount 1 "held input does not repeat the edge action"
        }

        test "a sub-step Tick retains an unconsumed edge for the next simulation step" {
            let space = key Space
            let latched = apply (KeyChanged(space, true)) initialModel
            let noStep = tick (0.5 * fixedDt) latched
            Expect.equal noStep.Input.Previous emptyInputSnapshot "no simulation means previous is not advanced"

            let stepped = tick (0.5 * fixedDt) noStep
            Expect.equal stepped.Input.PressedThisTick (Set.singleton space) "the next drained step receives the retained edge"
            Expect.equal stepped.EdgeActionCount 1 "the retained edge is consumed once"
        }

        test "AC 9 moves left while mouse-aimed shot travels right with velocity inheritance" {
            let player = initialModel.PlayerPosition
            let input = snapshot [ key (Letter 'A') ] (Some(vec2 (player.Vx + 100.0) player.Vy)) true zero zero 0.0
            let after = initialModel |> apply (InputChanged input) |> tick fixedDt
            let shot = after.ShotSpawns |> List.exactlyOne

            close -20.0 after.PlayerVelocity.Vx "A accelerates left by 2400 px/s2 for one fixed step"
            close 0.0 after.PlayerVelocity.Vy "left movement is cardinal"
            close 1.0 shot.Direction.Vx "mouse aim remains right independently of movement"
            close 0.0 shot.Direction.Vy "rightward shot is cardinal"
            close 415.0 shot.Velocity.Vx "420 aim speed inherits 0.25 * the current -20 player velocity"
            close 0.0 shot.Velocity.Vy "velocity inheritance does not rotate the shot"
        }

        test "gamepad left and right sticks remain independent and analog" {
            let input = snapshot [] None false (vec2 -1.0 0.0) (vec2 0.6 0.8) 0.75
            let after = initialModel |> apply (InputChanged input) |> tick fixedDt

            close -1.0 after.LastResolvedInput.Move.Vx "left stick moves left"
            close 0.0 after.LastResolvedInput.Move.Vy "left stick move is cardinal"
            close 0.6 after.LastResolvedInput.Aim.Vx "right stick preserves its analog x component"
            close 0.8 after.LastResolvedInput.Aim.Vy "right stick preserves its analog y component"
            Expect.equal after.LastResolvedInput.AimSource GamepadAim "right stick owns aim when arrows are absent"
            Expect.equal after.ShotSpawns.Length 1 "deflected right stick/trigger holds fire"
        }

        test "arrow-key aiming snaps to all eight directions" {
            let diagonal = 1.0 / sqrt 2.0
            let cases =
                [ [ ArrowUp ], vec2 0.0 -1.0
                  [ ArrowDown ], vec2 0.0 1.0
                  [ ArrowLeft ], vec2 -1.0 0.0
                  [ ArrowRight ], vec2 1.0 0.0
                  [ ArrowUp; ArrowLeft ], vec2 -diagonal -diagonal
                  [ ArrowUp; ArrowRight ], vec2 diagonal -diagonal
                  [ ArrowDown; ArrowLeft ], vec2 -diagonal diagonal
                  [ ArrowDown; ArrowRight ], vec2 diagonal diagonal ]

            for arrows, expected in cases do
                let input = snapshot (arrows |> List.map key) None false zero zero 0.0
                let resolved = resolveInput initialModel.PlayerPosition Set.empty input
                close expected.Vx resolved.Aim.Vx $"%A{arrows} x"
                close expected.Vy resolved.Aim.Vy $"%A{arrows} y"
                Expect.equal resolved.AimSource ArrowAim "arrow combinations use the snapped source"
        }

        test "mouse aim preserves a non-cardinal 360-degree direction" {
            let player = initialModel.PlayerPosition
            let input = snapshot [] (Some(vec2 (player.Vx + 3.0) (player.Vy + 4.0))) false zero zero 0.0
            let resolved = resolveInput player Set.empty input
            close 0.6 resolved.Aim.Vx "mouse x normalizes without snapping"
            close 0.8 resolved.Aim.Vy "mouse y normalizes without snapping"
            Expect.equal resolved.AimSource MouseAim "mouse owns aim when no stick/arrows are active"
        }

        test "held fire emits immediately and at the 2.5 per-second cadence" {
            let player = initialModel.PlayerPosition
            let held = snapshot [] (Some(vec2 (player.Vx + 10.0) player.Vy)) true zero zero 0.0
            let afterHeld =
                [ 1..96 ]
                |> List.fold (fun model _ -> tick fixedDt model) (apply (InputChanged held) initialModel)

            Expect.equal afterHeld.ShotSpawns.Length 3 "0.8 seconds yields events at acquisition, 0.4, and 0.8 seconds"

            let released = apply (PointerChanged(player, Some false)) afterHeld |> tick fixedDt
            Expect.equal released.ShotSpawns.Length 3 "release prevents another event"

            let repressed = apply (PointerChanged(vec2 (player.Vx + 10.0) player.Vy, Some true)) released |> tick fixedDt
            Expect.equal repressed.ShotSpawns.Length 4 "re-press fires immediately"

            let triggerOnly = snapshot [] None false zero zero 0.75
            let triggerShot = initialModel |> apply (InputChanged triggerOnly) |> tick fixedDt
            Expect.equal triggerShot.ShotSpawns.Head.Direction initialModel.Facing "right trigger fires along the retained facing when the stick is neutral"

            let longHeld =
                [ 1..3100 ]
                |> List.fold (fun model _ -> tick fixedDt model) (apply (InputChanged held) initialModel)
            Expect.isGreaterThan longHeld.TotalShotSpawns maxShotSpawnHistory "the monotonic counter observes emissions beyond retained history"
            Expect.isNonEmpty longHeld.ShotSpawns "held fire retains currently live projectiles"
            Expect.isLessThanOrEqual longHeld.ShotSpawns.Length maxShotSpawnHistory "live range-bounded projectiles remain below the safety cap"
            Expect.isTrue (longHeld.ShotSpawns |> List.forall (fun shot -> shot.AgeTicks <= shot.MaxAgeTicks)) "every retained projectile remains within range"
        }

        test "the production shell host carries raw key down, held ticks, and key up" {
            let host = interactiveHost
            let boot, _ = host.Init()
            let playing, _ = host.Update (ShellDispatch Rogue3.GameShell.Start) boot
            let a = key (Letter 'A')
            let down, _ = host.Update (RawKeyChanged(a, true)) playing
            let first, _ = host.Update (PlayDispatch(Tick fixedDt)) down
            let second, _ = host.Update (PlayDispatch(Tick fixedDt)) first
            let up, _ = host.Update (RawKeyChanged(a, false)) second
            let stopped, _ = host.Update (PlayDispatch(Tick fixedDt)) up

            Expect.isTrue (first.Play.PlayerVelocity.Vx < 0.0) "native down reaches first production fixed tick"
            Expect.isTrue (second.Play.PlayerVelocity.Vx < 0.0) "held snapshot reaches the second fixed tick"
            close -15.0 stopped.Play.PlayerVelocity.Vx "native up applies one fixed step of 3000 px/s2 friction"
            close 0.0 stopped.Play.PlayerVelocity.Vy "release does not introduce another axis"
        }

        test "coordinate-bearing pointer interactions map to pointer snapshot messages" {
            let downInteraction = PressedDown("play", PointerButton.Primary, 900.0, 360.0)
            let upInteraction = ReleasedUp("play", PointerButton.Primary, 901.0, 361.0)
            let down = pointerInteractionToMsg downInteraction
            let up = pointerInteractionToMsg upInteraction

            Expect.equal down (Some(PointerChanged(vec2 900.0 360.0, Some true))) "primary press carries position and held state"
            Expect.equal up (Some(PointerChanged(vec2 901.0 361.0, Some false))) "primary release carries position and held state"

            let host = interactiveHost
            let boot, _ = host.Init()
            let playing, _ = host.Update (ShellDispatch Rogue3.GameShell.Start) boot
            let routed = host.MapPointer downInteraction |> Option.get
            let after, _ = host.Update routed playing
            Expect.equal after.Play.Input.Current.MousePosition (Some(vec2 900.0 360.0)) "the production host MapPointer reaches the play snapshot"
            Expect.isTrue after.Play.Input.Current.MousePrimaryDown "the production host retains primary-down fire state"
        }
    ]
