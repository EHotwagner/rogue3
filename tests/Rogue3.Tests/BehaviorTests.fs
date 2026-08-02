module Rogue3BehaviorTests

open System
open Expecto
open Rogue3.Program
open Rogue3.Model
open FS.GG.UI.Scene
open FS.GG.Game.Core

// Feature 060 (FR-005): replaceable scaffold-BEHAVIOR tests. These call the scaffold
// rogue3's `view`/`update`/host/scene-text directly, so when you replace the scaffold
// model with your own you rewrite THIS file. `GovernanceTests.fs` (compiled first) keeps
// its model-agnostic source/structure/evidence scans green across that swap.

let rec collectSceneNodes node =
    seq {
        yield node
        // Recurse into EVERY container case. This match is deliberately exhaustive — no `_`
        // wildcard — so a new `SceneNode` case added to FS.GG.UI.Scene surfaces here as an
        // incomplete-match warning (FS0025) naming the unhandled case, instead of being
        // silently swallowed by a catch-all and dropping every node beneath it (#899). The
        // library's own walker (`Scene.describe`) is exhaustive for the same reason; mirror it.
        match node with
        | Group scenes ->
            for scene in scenes do
                for child in scene.Nodes do
                    yield! collectSceneNodes child
        | ClipNode(_, scene)
        | ColorSpaceNode(_, scene)
        | PerspectiveNode(_, scene)
        | Translate(_, scene) ->
            for child in scene.Nodes do
                yield! collectSceneNodes child
        | PictureNode picture ->
            for child in picture.Scene.Nodes do
                yield! collectSceneNodes child
        | CachedSubtree boundary ->
            for child in boundary.Scene.Nodes do
                yield! collectSceneNodes child
        // Leaf nodes carry no child scenes. Enumerated explicitly (not a `_` wildcard) so the
        // exhaustiveness guard above stays armed — a new case forces a decision here.
        | Empty
        | Rectangle _
        | PaintedRectangle _
        | Circle _
        | FilledEllipse _
        | Ellipse _
        | Line _
        | Path _
        | Points _
        | Vertices _
        | Arc _
        | Text _
        | TextRun _
        | Image _
        | RegionNode _
        | Chart _
        | SizedText _
        | GlyphRun _ -> ()
    }

let sceneText node =
    collectSceneNodes node
    |> Seq.choose (function
        | Text(_, value, _) -> Some value
        | TextRun run -> Some run.Text
        | SizedText(_, value, _, _) -> Some value
        | GlyphRun run -> Some run.Data.Text
        | _ -> None)
    |> String.concat " "

// Regression guard for #899: the scene walker must recurse through EVERY container case.
// `Translate` and `CachedSubtree` were added to `SceneNode` after this helper was first
// written, and a `| _ -> ()` catch-all silently dropped everything beneath them — a rogue3
// that wraps its world layers in a `Translate` camera-slide got a walker that under-reported
// with no error. This test is profile-independent (it exercises the shared helpers directly,
// not the rogue3 `view`), so it guards the fix across every scaffold profile.
[<Tests>]
let sceneWalkerTests =
    let black = { Red = 0uy; Green = 0uy; Blue = 0uy; Alpha = 255uy }

    testList "scene-walker (#899)" [
        test "collectSceneNodes recurses through Translate and CachedSubtree; sceneText reads their text" {
            let translated =
                Translate((5.0, 5.0), { Nodes = [ Text((0.0, 0.0), "hello", black); SizedText((0.0, 0.0), "sized", 12.0, black) ] })
            let cached =
                CachedSubtree { CacheId = 1UL; Fingerprint = 2UL; Scene = { Nodes = [ Text((1.0, 1.0), "cached", black) ] } }
            let root = Group [ { Nodes = [ translated; cached ] } ]

            let nodes = collectSceneNodes root |> Seq.toList
            // Group + Translate + (Text, SizedText) + CachedSubtree + Text = 6. Pre-fix, the two
            // container children were dropped and this was 3.
            Expect.equal (List.length nodes) 6 "the walker yields every node beneath Translate and CachedSubtree"

            let text = sceneText root
            Expect.stringContains text "hello" "Text beneath a Translate is reached"
            Expect.stringContains text "sized" "SizedText is recognised as text-bearing"
            Expect.stringContains text "cached" "Text beneath a CachedSubtree is reached"
        }
    ]

open FS.GG.UI.KeyboardInput
open FS.GG.UI.SkiaViewer
open FS.GG.UI.Controls
open Rogue3.Geometry // Vec2 (Vx/Vy) — the collision-safe positions the starter uses (feature 250)

// One fixed sim step's worth of elapsed time — enough for `update (Tick oneStep)` to drain a step.
let private oneStep = fixedDt

// A raw viewer key-DOWN event, built exactly as the host receives one from the window. The #911
// scene-host driver (`runKeyScriptToModel` / `auditKeyWiring` / `reachableMessages`) normalizes the
// RawKey itself ("w" -> Letter 'W', "ArrowUp" -> ArrowUp), so a test hands it the raw string the
// live runtime would, never a pre-normalized ViewerKey.
let private downKey raw : ViewerKeyEvent =
    { RawKey = raw; Direction = ViewerKeyDirection.KeyDown }

// GAME family (feature 220): replaceable scaffold-behaviour tests. These drive the Pong
// skeleton's update/view/tick/host directly, so when you swap in your own game you rewrite THIS
// file. GovernanceTests.fs stays model-agnostic and keeps passing across the swap (SC-004).
[<Tests>]
let behaviorTests =
    testList "rogue3-behavior" [
        test "generated rogue3 test suite is wired" {
            Expect.equal 1 1 "rogue3 tests run"
        }

        test "game default view renders gameplay and the Hollow Depths HUD as a scene" {
            let scene = Rogue3.Program.view Rogue3.Program.initialModel
            let nodes = collectSceneNodes scene |> Seq.toList
            let text = sceneText scene

            Expect.isTrue (List.length nodes >= 5) "game view draws the gameplay scene and HUD"
            Expect.stringContains text "COIN 00" "the production HUD renders two-digit currency"
            Expect.stringContains text "ACTIVE 2/6" "the production HUD renders active charge"
        }

        test "game tick advances the ball and the tick count (the default is a live, moving rogue3)" {
            let before = Rogue3.Program.initialModel
            let after, effects = Rogue3.Program.update (Tick oneStep) before

            Expect.notEqual after.Ball before.Ball "a tick integrates the ball position"
            Expect.equal after.TickCount (before.TickCount + 1) "a tick advances the tick count"
            Expect.isEmpty effects "a pure game tick emits no host command"
        }

        test "M0 fixed 120 Hz drain banks fractions and clamps a huge stall to five steps (AC #8)" {
            let model0 = Rogue3.Program.initialModel
            let decimalFrame, _ = Rogue3.Program.update (Tick 0.033) model0
            Expect.equal decimalFrame.SimStepCount 3 "decimal 0.033 contains three whole 1/120-second steps"
            Expect.isGreaterThan decimalFrame.SimAccumulator 0.0 "the decimal-frame fraction remains banked"
            Expect.isLessThan decimalFrame.SimAccumulator fixedDt "the banked fraction remains below one step"

            let exactFrame, _ = Rogue3.Program.update (Tick (1.0 / 30.0)) model0
            Expect.equal exactFrame.SimStepCount 4 "exact 1/30 second advances four 120 Hz steps"
            Expect.floatClose Accuracy.high exactFrame.SimAccumulator 0.0 "the exact frame leaves no meaningful remainder"

            let stalled, _ = Rogue3.Program.update (Tick 1.0) model0
            Expect.equal stalled.SimStepCount maxSteps "a one-second stall advances no more than MAX_STEPS"
            Expect.isGreaterThanOrEqual stalled.SimAccumulator 0.0 "the clamped remainder is non-negative"
            Expect.isLessThan stalled.SimAccumulator fixedDt "the clamp retains no unbounded catch-up debt"
        }

        test "M0 banked accumulator combines sub-step host frames deterministically" {
            let model0 = Rogue3.Program.initialModel
            let first, _ = Rogue3.Program.update (Tick (fixedDt * 0.6)) model0
            let second, _ = Rogue3.Program.update (Tick (fixedDt * 0.6)) first
            Expect.equal first.SimStepCount 0 "the first sub-step frame does not advance the sim"
            Expect.equal second.SimStepCount 1 "the second frame drains the banked whole step"
            Expect.floatClose Accuracy.high second.SimAccumulator (fixedDt * 0.2) "the residual fraction remains banked"
        }

        test "M0 same seed reproduces independent LayoutRng and DropRng streams" {
            let first = initialModelForSeed 0xC0FFEEUL
            let replay = initialModelForSeed 0xC0FFEEUL
            Expect.equal first.LayoutRng replay.LayoutRng "the layout stream is seed-reproducible"
            Expect.equal first.DropRng replay.DropRng "the drop stream is seed-reproducible"
            Expect.notEqual first.LayoutRng first.DropRng "the split streams start decorrelated"

            let struct (_, advancedDrop) = Rng.nextInt 0 99 first.DropRng
            let combatAdvanced = { first with DropRng = advancedDrop }
            Expect.equal combatAdvanced.LayoutRng first.LayoutRng "advancing drops cannot perturb layout state"
            Expect.notEqual combatAdvanced.DropRng first.DropRng "the draw writes back the advanced drop stream"
        }

        test "M0 logical 1280x720 world-to-screen transform is centered, uniform, and invertible" {
            Expect.equal Rogue3.Program.initialModel.Playfield (vec2 1280.0 720.0) "the model owns the specified logical extent"
            let transform = worldScreenTransform 1600.0 1200.0
            Expect.floatClose Accuracy.high transform.Scale 1.25 "the limiting width chooses one uniform scale"
            Expect.floatClose Accuracy.high transform.OffsetVx 0.0 "no horizontal letterbox is needed"
            Expect.floatClose Accuracy.high transform.OffsetVy 150.0 "vertical surplus is centered"

            let logical = vec2 640.0 360.0
            let physical = worldToScreen transform logical
            Expect.equal physical (vec2 800.0 600.0) "logical center maps to physical center"
            let roundTrip = screenToWorld transform physical
            Expect.floatClose Accuracy.high roundTrip.Vx logical.Vx "inverse recovers world x"
            Expect.floatClose Accuracy.high roundTrip.Vy logical.Vy "inverse recovers world y"
        }

        test "game keyboard input moves the paddles and records the last input" {
            let model0 = Rogue3.Program.initialModel
            let leftUp, _ = Rogue3.Program.update (ViewerInput(Letter 'W', true)) model0
            let rightDown, _ = Rogue3.Program.update (ViewerInput(ArrowDown, true)) model0

            Expect.isLessThan leftUp.LeftPaddleY model0.LeftPaddleY "W moves the left paddle up"
            Expect.isGreaterThan rightDown.RightPaddleY model0.RightPaddleY "Down moves the right paddle down"
            Expect.equal rightDown.LastInput (Some ArrowDown) "the last input key is recorded"
        }

        test "game MovePaddle clamps paddles inside the playfield" {
            let model0 = Rogue3.Program.initialModel

            let raised =
                List.replicate 100 (MovePaddle(LeftSide, PaddleUp))
                |> List.fold (fun m msg -> fst (Rogue3.Program.update msg m)) model0

            Expect.isTrue (raised.LeftPaddleY >= 0.0) "the paddle never leaves the top of the playfield"
        }

        test "game scores and re-serves when the ball passes an undefended edge" {
            let model0 = Rogue3.Program.initialModel

            let missed =
                { model0 with
                    Ball = { Pos = vec2 18.0 10.0; Velocity = vec2 -8.0 model0.Ball.Velocity.Vy }
                    LeftPaddleY = 300.0 }

            let scored, _ = Rogue3.Program.update (Tick oneStep) missed

            Expect.equal scored.RightScore 1 "the right side scores when the ball passes the left edge"
            Expect.equal scored.Ball.Pos.Vx (model0.Playfield.Vx / 2.0) "the ball re-serves to the centre"
        }

        test "generated game host exposes viewer input and tick mapping and advances the game" {
            let host = Rogue3.Program.generatedHost
            let model0 = fst (host.Init())

            Expect.isSome (host.MapKey ArrowUp true) "generatedHost maps viewer keys to messages"
            Expect.isSome (host.Tick (TimeSpan.FromMilliseconds 16.0)) "generatedHost ticks at >=16ms"

            let updated, effects = host.Update (Tick oneStep) model0
            Expect.notEqual updated model0 "generatedHost.Update advances the game"
            Expect.isNonEmpty effects "generatedHost returns a render effect to SkiaViewer"
        }

        // Host-boot (#991/#1000) + the audio-init seam (#458). The game's turnkey DEFAULT launch is
        // now the pointer-host `interactiveHost` booting the generic game shell, so this asserts what
        // the LAUNCH host does at `Init` — model-agnostically (the shell's default-launch BEHAVIOUR),
        // not a brittle model substring:
        //   (1) it boots the shell's MAIN MENU (`Init -> MainMenu`), not straight into the play model;
        //   (2) it still routes the LOADED initial play state through the audio seam (#458).
        //
        // (2) is only catchable at the SINK, not the model: a restored setting the mixer was never
        // told about is indistinguishable, from inside the model, from one applied correctly. So it
        // asks what the host actually HANDED OUT — the ViewerEffect list `Init` returns, which is what
        // `ControlsElmish.runInteractiveAppWithAudio` feeds to the audio sink. The failure leg is real:
        // drop the `AudioCues.forTransition Started` route from the shell host's `Init` and this reds.
        test "generated game host boots the shell main menu and cues the initial state through the same seam (#991/#1000/#458)" {
            let host = Rogue3.Program.interactiveHost
            let model0, initEffects = host.Init()

            // (1) host-boot: the turnkey default launches into the shell's main menu.
            Expect.equal
                model0.Shell.Screen
                Rogue3.GameShell.MainMenu
                "the interactive launch host boots into the shell main menu (Init -> MainMenu), not the raw play model"

            // (2) #458: the loaded initial play state still reaches the audio sink at Init.
            let expected =
                Rogue3.AudioCues.forTransition Started Rogue3.Program.initialModel Rogue3.Program.initialModel

            Expect.isNonEmpty
                expected
                "the scaffold ships `Started` wired to a cue — an empty one would make this test vacuous (#458)"

            let cued =
                initEffects
                |> List.collect (fun effect ->
                    match effect with
                    | PlayAudio cues -> cues
                    | _ -> [])

            Expect.equal
                cued
                expected
                "interactiveHost.Init routes the initial play model through AudioCues.forTransition Started — state that is LOADED rather than transitioned into must still reach the audio sink (#458)"
        }

        // Issue #1012: acceptance at the ACTUAL generated launch host. This is deliberately not a
        // GameShell.update test: native pointer/key values enter InteractiveAppHost, retained hit
        // bounds dispatch authored bindings, and held input advances only on fixed ticks.
        test "interactive shell host proves default geometry, retained menu/rebind clicks, and key down -> ticks -> up (#1012)" {
            let host = Rogue3.Program.interactiveHost
            let size = Rogue3.Program.viewerOptions.InitialSize

            Expect.equal
                size
                Rogue3.EvidenceCommands.shellConfig.InitialDisplay.Resolution
                "the default native surface and authored shell resolution are one coordinate space"

            let defaultWindow =
                Rogue3.GameShell.windowBehavior Rogue3.EvidenceCommands.shellConfig.InitialDisplay

            Expect.equal defaultWindow.StartupState ViewerWindowStartupState.Normal "default launch explicitly uses the shell's Windowed behavior"

            Expect.equal
                Rogue3.Program.viewerOptions.LogicalSize
                (Some Rogue3.EvidenceCommands.shellConfig.InitialDisplay.Resolution)
                "the generated game seeds SkiaViewer with the shell's logical canvas"

            let pointer phase x y : ViewerPointerInput =
                { Phase = phase
                  X = x
                  Y = y
                  Button = Some ViewerPointerButtonKind.Primary
                  DeltaX = 0.0
                  DeltaY = 0.0 }

            let foldMessages model messages =
                messages |> List.fold (fun current msg -> fst (host.Update msg current)) model

            let rendered model =
                FS.GG.UI.Controls.Control.renderTree host.Theme size (host.View size model)

            let centre controlId model =
                let frame = rendered model
                Expect.isTrue (Set.contains controlId frame.BoundIds) $"{controlId} is an authored bound control, not a silent test target"

                let available: FS.GG.UI.Layout.AvailableSpace =
                    { Width = float size.Width
                      WidthMode = FS.GG.UI.Layout.Exactly
                      Height = float size.Height
                      HeightMode = FS.GG.UI.Layout.Exactly }

                let layout = FS.GG.UI.Layout.Layout.evaluate available frame.Layout
                let bounds = (layout.Bounds |> List.find (fun b -> b.NodeId = controlId)).Bounds
                bounds.X + bounds.Width / 2.0, bounds.Y + bounds.Height / 2.0

            let clickWithProof controlId model =
                let x, y = centre controlId model
                let state, down =
                    FS.GG.UI.Controls.Elmish.ControlsElmish.routeInteractivePointer
                        host
                        (Pointer.init ())
                        size
                        model
                        (pointer ViewerPointerPhaseKind.Pressed x y)
                let afterDown = foldMessages model down
                let proof =
                    FS.GG.UI.Controls.Elmish.ControlsElmish.captureRespondsProof
                        host
                        state
                        size
                        afterDown
                        (pointer ViewerPointerPhaseKind.Released x y)
                let _, up =
                    FS.GG.UI.Controls.Elmish.ControlsElmish.routeInteractivePointer
                        host state size afterDown (pointer ViewerPointerPhaseKind.Released x y)
                proof, foldMessages afterDown up

            let model0 = fst (host.Init())
            let startProof, playing = clickWithProof "start" model0
            Expect.equal startProof.Verdict FS.GG.UI.Controls.Elmish.Responsive "retained Start click visibly enters play"
            Expect.equal playing.Shell.Screen Rogue3.GameShell.Playing "the native retained pointer route dispatches Start"

            let raw key isDown = host.MapKey key isDown |> Option.get
            let tickMessage () = host.Tick (TimeSpan.FromMilliseconds 16.0) |> Option.get
            let keyDown = fst (host.Update (raw (Letter 'W') true) playing)
            let afterTick1 = fst (host.Update (tickMessage ()) keyDown)
            let afterTick2 = fst (host.Update (tickMessage ()) afterTick1)
            let keyUp = fst (host.Update (raw (Letter 'W') false) afterTick2)
            let afterReleaseTick = fst (host.Update (tickMessage ()) keyUp)

            Expect.isTrue (afterTick1.Play.PlayerPosition.Vy < keyDown.Play.PlayerPosition.Vy) "held W advances on the first fixed tick"
            Expect.isTrue (afterTick2.Play.PlayerPosition.Vy < afterTick1.Play.PlayerPosition.Vy) "held W remains active across a second fixed tick"
            Expect.isFalse (Set.contains (ViewerKeyboard.toKeyId (Letter 'W')) keyUp.HeldKeys) "key-up clears the retained host control"
            Expect.equal afterReleaseTick.Play.LastResolvedInput.Move Rogue3.Geometry.zero "the next tick receives no movement intent (existing velocity may decelerate)"

            let configProof, settings = clickWithProof "config" model0
            Expect.equal configProof.Verdict FS.GG.UI.Controls.Elmish.Responsive "retained Config click visibly opens settings"

            let rebindId = "rebind-move-up"

            let rebindProof, armed = clickWithProof rebindId settings
            Expect.equal rebindProof.Verdict FS.GG.UI.Controls.Elmish.Responsive "retained rebind-row click visibly enters capture"
            Expect.isSome armed.Shell.Rebinding "the clicked row arms the next raw key"
            let capturedCommand = armed.Shell.Rebinding.Value

            let captured = fst (host.Update (raw (Letter 'Q') true) armed)
            Expect.equal captured.Shell.Rebinding None "the next native raw key completes capture"
            Expect.equal
                (Keymap.resolve captured.Shell.Keymap (ViewerKeyboard.toKeyId (Letter 'Q')))
                (Some capturedCommand)
                "the real host's raw-key seam installs the captured key"
            Expect.equal
                (Keymap.resolve captured.Shell.Keymap (ViewerKeyboard.toKeyId (Letter 'W')))
                None
                "the real retained-host capture replaces the command binding instead of adding a second key"

            // Issue #1014: change the selected logical canvas, prove the host-boundary effect, then
            // click the same semantic Config control from its corresponding physical point on a
            // deliberately differently-shaped surface. SkiaViewer's exact runtime inverse is gated
            // in its package suite; this generated-rogue3 assertion proves the emitted host wiring
            // and retained control compose with it.
            let resolution1080: FS.GG.UI.Scene.Size = { Width = 1920; Height = 1080 }
            let shell1080, shellEffects =
                Rogue3.GameShell.update (Rogue3.GameShell.SetResolution resolution1080) model0.Shell

            let viewerEffects =
                shellEffects |> List.collect Rogue3.EvidenceCommands.viewerEffectsForShellEffect

            Expect.exists
                viewerEffects
                (function ApplyLogicalCanvas size -> size = resolution1080 | _ -> false)
                "DisplayChanged dynamically installs the selected logical canvas"

            for mode in
                [ Rogue3.GameShell.Windowed
                  Rogue3.GameShell.Borderless
                  Rogue3.GameShell.Fullscreen ] do
                let _, modeEffects =
                    Rogue3.GameShell.update (Rogue3.GameShell.SetDisplayMode mode) shell1080

                let modeViewerEffects =
                    modeEffects
                    |> List.collect Rogue3.EvidenceCommands.viewerEffectsForShellEffect

                Expect.exists
                    modeViewerEffects
                    (function ApplyWindowOptions _ -> true | _ -> false)
                    $"{mode} emits its presentation request"

                Expect.exists
                    modeViewerEffects
                    (function ApplyLogicalCanvas size -> size = resolution1080 | _ -> false)
                    $"{mode} keeps the selected logical coordinate policy"

            let model1080 = { model0 with Shell = shell1080 }
            let surface: FS.GG.UI.Scene.Size = { Width = 1600; Height = 1000 }
            let frame1080 = FS.GG.UI.Controls.Control.renderTree host.Theme resolution1080 (host.View resolution1080 model1080)
            let available1080: FS.GG.UI.Layout.AvailableSpace =
                { Width = float resolution1080.Width
                  WidthMode = FS.GG.UI.Layout.Exactly
                  Height = float resolution1080.Height
                  HeightMode = FS.GG.UI.Layout.Exactly }
            let layout1080 = FS.GG.UI.Layout.Layout.evaluate available1080 frame1080.Layout
            let configBounds = (layout1080.Bounds |> List.find (fun b -> b.NodeId = "config")).Bounds
            let logicalX = configBounds.X + configBounds.Width / 2.0
            let logicalY = configBounds.Y + configBounds.Height / 2.0
            let fit = LogicalCanvas.fit resolution1080 surface
            let physicalX = logicalX * fit.Scale + fit.OffsetX
            let physicalY = logicalY * fit.Scale + fit.OffsetY
            let routedX, routedY = LogicalCanvas.toLogicalPoint resolution1080 surface physicalX physicalY
            let state1080, down1080 =
                FS.GG.UI.Controls.Elmish.ControlsElmish.routeInteractivePointer
                    host (Pointer.init ()) resolution1080 model1080
                    (pointer ViewerPointerPhaseKind.Pressed routedX routedY)
            let afterDown1080 = foldMessages model1080 down1080
            let _, up1080 =
                FS.GG.UI.Controls.Elmish.ControlsElmish.routeInteractivePointer
                    host state1080 resolution1080 afterDown1080
                    (pointer ViewerPointerPhaseKind.Released routedX routedY)
            let configured1080 = foldMessages afterDown1080 up1080
            Expect.equal
                configured1080.Shell.Screen
                Rogue3.GameShell.Settings
                "the corresponding physical point still activates Config after 1280x720 -> 1920x1080"
        }

        // Issue #1022: this is intentionally capability-gated rather than replaced by another effect-
        // emission assertion. On a desktop host it drives the generated shell's DisplayChanged path
        // through ControlsElmish, the shared ViewerEffect interpreter, and the real native-window
        // mutation boundary; on a headless host it records the irreducible native tier as skipped.
        //
        // Issue #63 EXTENDS this test rather than adding a second native launch. `Borderless` is now
        // served by exclusive fullscreen, and this is the ONLY tier that can observe what the
        // framework actually did with the request — the pure seam can only say what was asked for.
        // Both modes are therefore driven here and both must be observed as `mode=fullscreen`; an
        // observation of `mode=windowedfullscreen` is the state that bricked the window and reds.
        //
        // Honest limit, since the loop looks like it doubles the evidence and does not: once the
        // seam maps `Borderless` onto `Fullscreen`, the two iterations are observationally
        // IDENTICAL by construction, and a mutant that drives `Fullscreen` twice survives. The
        // Borderless iteration therefore confirms the composed product path is accepted natively;
        // it adds no discrimination the Fullscreen iteration does not already provide. It is kept
        // because it is the composition that would break if the mapping were rewired, not because
        // it independently tests the framework.
        test "DisplayChanged reaches the live native window-mode boundary (#1022), and Borderless lands as exclusive fullscreen there (#63)" {
            if not (Viewer.runtimeCapability().PersistentWindow) then
                skiptest "SKIPPED(tier=T2 native-window/GL): no persistent desktop window is available"

            let driveMode mode =
                let observed = ResizeArray<ViewerDiagnosticEvent>()
                let host =
                    { Rogue3.Program.interactiveHost with
                        Tick = fun _ -> Some(Rogue3.EvidenceCommands.ShellDispatch(Rogue3.GameShell.SetDisplayMode mode))
                        Diagnostics =
                            { Viewer.defaultDiagnostics with
                                Categories = Set.add ViewerDiagnosticCategory.Window Viewer.defaultDiagnostics.Categories
                                Sink = Some observed.Add } }

                let result =
                    FS.GG.UI.Controls.Elmish.ControlsElmish.Live.runScriptWithWindowBehavior
                        Rogue3.Program.viewerOptions
                        (Rogue3.GameShell.windowBehavior Rogue3.EvidenceCommands.shellConfig.InitialDisplay)
                        host
                        [ FS.GG.UI.Controls.Elmish.FrameInput.Idle
                          FS.GG.UI.Controls.Elmish.FrameInput.Idle ]

                match result with
                | Result.Error failure -> failtestf "live generated host failed for %A: %s" mode failure.Message
                | Result.Ok _ -> ()

                observed
                |> Seq.filter (fun diagnostic -> diagnostic.Category = ViewerDiagnosticCategory.Window)
                |> Seq.map _.Message
                |> List.ofSeq

            for mode in [ Rogue3.GameShell.Fullscreen; Rogue3.GameShell.Borderless ] do
                let windowDiagnostics = driveMode mode

                Expect.exists
                    windowDiagnostics
                    (fun message -> message.Contains "Runtime window behavior applied: mode=fullscreen")
                    $"{mode}'s generated DisplayChanged request is observed after real native mutation, not only at emission"

                Expect.isFalse
                    (windowDiagnostics |> List.exists (fun message -> message.Contains "mode=windowedfullscreen"))
                    $"{mode} never reaches the native boundary as the work-area-derived windowedfullscreen state (#63)"
        }

        // Issue #63: BORDERLESS BRICKED THE UI, AND NO TEST NOTICED.
        //
        // Reported from real play: selecting Borderless moved the window half off screen and left
        // every button dead. `Esc` still worked. That asymmetry is the whole diagnosis — `MapKey`
        // is coordinate-free, while a pointer sample is hit-tested against the render tree after
        // being inverted through the logical-canvas fit. A fit taken against the WRONG surface
        // inverts every sample to a point outside every control's bounds, no authored `OnClick`
        // matches, and `MapPointer` (which the shell menu does not implement) has no fallback to
        // catch it. The click is silently discarded.
        //
        // WHY THE EXISTING COVERAGE DID NOT CATCH IT. The `#1014` block above asserts that a mode
        // change EMITS `ApplyWindowOptions` and `ApplyLogicalCanvas`. Both were emitted correctly
        // the entire time the bug shipped; the effect wiring was never the fault. An emission
        // assertion cannot distinguish a request that works from one that bricks the window.
        //
        // WHAT THIS TEST CAN AND CANNOT PROVE — stated plainly, because it is easy to overclaim.
        //
        //   CANNOT: it cannot fail because of the `WindowedFullscreen`-versus-`Fullscreen` choice.
        //   The defect lives in the HOST's derivation of the output surface from the request, and
        //   nothing headless can observe that: this test must supply a surface, and it supplies the
        //   same one to the forward fit and the inverse, which `SkiaViewer.fsi` documents as exact
        //   inverses. `display.Mode` is not an input to either. A reviewer who reverts the
        //   mitigation and neutralises the two shape assertions below will find the pointer
        //   assertions still green, and that is expected rather than a bug in them.
        //
        //   CAN: (1) pin the mitigation's SHAPE — that Borderless is not offered and that no
        //   offered mode asks for `WindowedFullscreen`; these are the assertions that red on a
        //   revert, together with the seam test in ShellBehaviorTests. (2) Exercise the whole
        //   production pointer route for every offered mode end to end, so a mode change that
        //   broke authored bindings, layout or the settings tree would be caught.
        //
        //   The negative control below is deliberately NOT listed as a third capability. It
        //   demonstrates a property of `LogicalCanvas` — that a mis-taken fit really does land a
        //   sample off the control, rather than the miss being an out-of-range rejection — but no
        //   product code applies that fit, so no single-edit change to `src/` can make it red. It
        //   documents the failure mode; it does not guard against it.
        //
        // The boundary itself is evidenced elsewhere: by the native-window test above, which
        // observes what the framework was actually asked for, and by the play session and
        // FS-GG/FS.GG.Rendering#1196 upstream.
        test "every offered display mode keeps the menu clickable through the POST-change surface fit (#63)" {
            let host = Rogue3.Program.interactiveHost
            let logical = Rogue3.EvidenceCommands.shellConfig.InitialDisplay.Resolution

            // Three surfaces. The numbers are borrowed from the reporter's host for readability,
            // but do NOT read significance into them: a mutation critic set `postChangeSurface` to
            // 999x4001 and this test stayed green, because the forward fit and the inverse are
            // taken against the SAME surface and cancel for any value. What the three do provide is
            // magnitude — the negative control below separates only because the stale surfaces
            // differ from the post-change one by roughly a factor of two. Bring either within a
            // pixel of it and it stops discriminating, which is a limit of the control, not a bug.
            let preChangeSurface: FS.GG.UI.Scene.Size = logical
            let postChangeSurface: FS.GG.UI.Scene.Size = { Width = 2560; Height = 1440 }
            let boundingBoxSurface: FS.GG.UI.Scene.Size = { Width = 3440; Height = 2880 }

            let pointer phase x y : ViewerPointerInput =
                { Phase = phase
                  X = x
                  Y = y
                  Button = Some ViewerPointerButtonKind.Primary
                  DeltaX = 0.0
                  DeltaY = 0.0 }

            let foldMessages model messages =
                messages |> List.fold (fun current msg -> fst (host.Update msg current)) model

            /// The logical centre of an authored, BOUND control — the guard is the point: an
            /// unbound id would make every "the click fired" claim below unfalsifiable.
            let logicalCentre controlId model =
                let frame = FS.GG.UI.Controls.Control.renderTree host.Theme logical (host.View logical model)

                Expect.isTrue
                    (Set.contains controlId frame.BoundIds)
                    $"{controlId} is an authored bound control, not a silent test target"

                let available: FS.GG.UI.Layout.AvailableSpace =
                    { Width = float logical.Width
                      WidthMode = FS.GG.UI.Layout.Exactly
                      Height = float logical.Height
                      HeightMode = FS.GG.UI.Layout.Exactly }

                let layout = FS.GG.UI.Layout.Layout.evaluate available frame.Layout
                let bounds = (layout.Bounds |> List.find (fun b -> b.NodeId = controlId)).Bounds
                bounds.X + bounds.Width / 2.0, bounds.Y + bounds.Height / 2.0

            /// Press+release a physical (on-screen) sample, inverting it through the fit for
            /// `surface` — the same `LogicalCanvas` inverse the host owns. Handing a DIFFERENT
            /// surface here is precisely how the defect is modelled.
            let clickPhysicalThrough (surface: FS.GG.UI.Scene.Size) physicalX physicalY model =
                let routedX, routedY = LogicalCanvas.toLogicalPoint logical surface physicalX physicalY

                let state, down =
                    FS.GG.UI.Controls.Elmish.ControlsElmish.routeInteractivePointer
                        host (Pointer.init ()) logical model
                        (pointer ViewerPointerPhaseKind.Pressed routedX routedY)

                let afterDown = foldMessages model down

                let proof =
                    FS.GG.UI.Controls.Elmish.ControlsElmish.captureRespondsProof
                        host state logical afterDown
                        (pointer ViewerPointerPhaseKind.Released routedX routedY)

                let _, up =
                    FS.GG.UI.Controls.Elmish.ControlsElmish.routeInteractivePointer
                        host state logical afterDown
                        (pointer ViewerPointerPhaseKind.Released routedX routedY)

                proof, foldMessages afterDown up

            /// A control's true on-screen position on `surface`, via the forward fit.
            let physicalOn (surface: FS.GG.UI.Scene.Size) controlId model =
                let logicalX, logicalY = logicalCentre controlId model
                let fit = LogicalCanvas.fit logical surface
                logicalX * fit.Scale + fit.OffsetX, logicalY * fit.Scale + fit.OffsetY

            // Reach Settings through the production route, not by injecting a message.
            let model0 = fst (host.Init())
            let settingsX, settingsY = physicalOn preChangeSurface "config" model0
            let configProof, settings = clickPhysicalThrough preChangeSurface settingsX settingsY model0

            Expect.equal configProof.Verdict FS.GG.UI.Controls.Elmish.Responsive "the settings screen is reached by a real click, before any mode change"
            Expect.equal settings.Shell.Screen Rogue3.GameShell.Settings "the pointer route opened Settings"

            // The withdrawal itself. A regression that re-adds the button reds here, and the loop
            // below then also exercises it — so the mitigation cannot rot silently in either half.
            Expect.isFalse
                (List.contains Rogue3.GameShell.Borderless Rogue3.EvidenceCommands.shellConfig.DisplayModes)
                "Borderless is not offered while the framework's WindowedFullscreen derivation bricks the window (#63)"

            for mode in Rogue3.EvidenceCommands.shellConfig.DisplayModes do
                // Change the mode by CLICKING its own settings button, through the pointer route.
                let modeId =
                    match mode with
                    | Rogue3.GameShell.Windowed -> "mode-windowed"
                    | Rogue3.GameShell.Borderless -> "mode-borderless"
                    | Rogue3.GameShell.Fullscreen -> "mode-fullscreen"

                let modeX, modeY = physicalOn preChangeSurface modeId settings
                let _, changed = clickPhysicalThrough preChangeSurface modeX modeY settings

                Expect.equal changed.Shell.Display.Mode mode $"clicking {modeId} selects {mode} through the production pointer route"

                // The request that reaches the framework must not be the one that bricks the
                // window. This is the assertion the emission-only coverage above cannot make.
                let requested = (Rogue3.GameShell.windowBehavior changed.Shell.Display).StartupState

                Expect.notEqual
                    requested
                    ViewerWindowStartupState.WindowedFullscreen
                    $"{mode} must not ask for the work-area-derived WindowedFullscreen state (#63)"

                // After the change, a sample at Back's on-screen point on the POST-change surface
                // still fires Back's OnClick and leaves Settings. See the CAN/CANNOT note above:
                // this exercises the route, it does not pin the host's surface derivation.
                let backX, backY = physicalOn postChangeSurface "back" changed
                let backProof, left = clickPhysicalThrough postChangeSurface backX backY changed

                Expect.equal backProof.Verdict FS.GG.UI.Controls.Elmish.Responsive $"after switching to {mode}, a click at Back's on-screen centre visibly responds"
                Expect.equal left.Shell.Screen Rogue3.GameShell.MainMenu $"after switching to {mode}, Back's authored OnClick still fires — the UI is not dead (#63)"

                // NEGATIVE CONTROL. Invert that same on-screen sample through the surfaces a stale
                // fit would use. Both must fail to reach Back, or the assertion above proves
                // nothing about which surface the fit was taken against.
                for staleSurface, label in
                    [ preChangeSurface, "the pre-change window surface"
                      boundingBoxSurface, "the bounding box of both outputs" ] do
                    let _, misrouted = clickPhysicalThrough staleSurface backX backY changed

                    Expect.equal
                        misrouted.Shell.Screen
                        Rogue3.GameShell.Settings
                        $"a sample inverted through {label} misses Back entirely — this is the dead-button failure mode, and it is what makes the assertion above load-bearing (#63)"
        }

        // Issue #63, the third guard: a player whose saved settings still say `borderless` must not
        // be left looking at a settings screen with NO display mode selected.
        //
        // `modeOfToken` still decodes the retired token, and the settings view marks a mode by
        // comparing `model.Display.Mode` against `Config.DisplayModes` — so an un-normalised
        // restore matches neither offered button, the menu disagrees with the window the product is
        // actually running, and every later `DisplayChanged` re-persists the retired token. This
        // asserts the rendered marker, not the model field, because the marker is the thing the
        // player sees and the model field is one refactor away from not implying it.
        test "a settings file restoring the withdrawn Borderless mode still shows exactly one selected mode (#63)" {
            let host = Rogue3.Program.interactiveHost
            let size = Rogue3.EvidenceCommands.shellConfig.InitialDisplay.Resolution
            let model0 = fst (host.Init())

            let settingsScreenFor (mode: Rogue3.GameShell.DisplayMode) =
                let shell, _ = Rogue3.GameShell.update Rogue3.GameShell.OpenSettings model0.Shell
                { model0 with Shell = { shell with Display = { shell.Display with Mode = mode } } }

            /// The mode labels the settings screen draws, with the "> " selection marker intact.
            /// Compared for EQUALITY against each candidate, never by substring: "> Windowed"
            /// contains "Windowed", so a substring test could never tell selected from unselected.
            let modeLabelsOn model =
                let scene = FS.GG.UI.Controls.Control.renderTree host.Theme size (host.View size model) |> _.Scene

                let drawn =
                    scene.Nodes
                    |> Seq.collect collectSceneNodes
                    |> Seq.choose (function
                        | Text(_, value, _) -> Some value
                        | TextRun run -> Some run.Text
                        | SizedText(_, value, _, _) -> Some value
                        | GlyphRun run -> Some run.Data.Text
                        | _ -> None)
                    |> List.ofSeq

                [ for label in [ "Windowed"; "Borderless"; "Fullscreen" ] do
                      for text in drawn do
                          if text = label || text = "> " + label then yield text ]

            let selected labels = labels |> List.filter (fun (text: string) -> text.StartsWith "> ")

            // The un-normalised state, shown first so the guard is not asserting a vacuous truth.
            let unnormalised = modeLabelsOn (settingsScreenFor Rogue3.GameShell.Borderless)

            Expect.isNonEmpty unnormalised "the settings screen draws its display-mode rows"
            Expect.isEmpty (selected unnormalised) "an un-normalised restored Borderless marks NO offered mode — this is the state the load-seam guard exists to prevent (#63)"

            // What `loadShellSettings` actually hands the host — driven through the SAME decoder it
            // uses, over real `encodeSettings` bytes, so this covers the guard's WIRING and not just
            // the guard. A mutation critic showed the difference: calling the retirement directly
            // left `loadShellSettings` free to drop it entirely with the suite still green.
            let borderlessBlob =
                Rogue3.GameShell.encodeSettings (settingsScreenFor Rogue3.GameShell.Borderless).Shell

            Expect.equal
                (Rogue3.GameShell.decodeSettings borderlessBlob model0.Shell).Display.Mode
                Rogue3.GameShell.Borderless
                "the blob really does carry the retired token — otherwise the assertion below would pass for the wrong reason"

            let restored = Rogue3.EvidenceCommands.decodeShellSettings borderlessBlob model0.Shell

            Expect.equal restored.Display.Mode Rogue3.GameShell.Fullscreen "a settings file carrying the retired token is retired onto the mode that now serves it, by the decoder the load path uses"

            let normalised = modeLabelsOn (settingsScreenFor restored.Display.Mode)

            Expect.equal
                (selected normalised)
                [ "> Fullscreen" ]
                "after normalisation exactly one offered mode is marked, and it is the one the window is really running (#63)"

            // Every other mode is untouched by the retirement — this is a targeted retirement of one
            // withdrawn value, not a reset of the player's display preference. Driven through the
            // same real decoder, so an over-broad retirement reds here.
            for mode in Rogue3.EvidenceCommands.shellConfig.DisplayModes do
                let blob = Rogue3.GameShell.encodeSettings (settingsScreenFor mode).Shell
                let kept = Rogue3.EvidenceCommands.decodeShellSettings blob model0.Shell
                Expect.equal kept.Display.Mode mode $"{mode} is an offered mode and survives the restore decode unchanged"

            // The resolution must survive the retirement too: it is a different field of the same
            // record, and a retirement written as a whole-record replacement would silently reset it.
            let borderlessAt1080 =
                let shell, _ = Rogue3.GameShell.update (Rogue3.GameShell.SetResolution { Width = 1920; Height = 1080 }) (settingsScreenFor Rogue3.GameShell.Borderless).Shell
                Rogue3.EvidenceCommands.decodeShellSettings (Rogue3.GameShell.encodeSettings shell) model0.Shell

            Expect.equal borderlessAt1080.Display.Resolution { Width = 1920; Height = 1080 } "retiring the withdrawn MODE preserves the player's chosen RESOLUTION"
            Expect.equal borderlessAt1080.Display.Mode Rogue3.GameShell.Fullscreen "and still retires the mode"
        }

        // Issue #63, the SECOND producer. Found by a mutation critic, not by the fix.
        //
        // The three guards above cover the settings screen, the shell seam and the settings file.
        // They do not cover the COMMAND LINE, which reaches `ViewerWindowBehaviorRequest` by an
        // entirely separate route — `Program.main` -> `windowFlagSupplied` -> `toViewerLaunchRequest`
        // — and which had no test of any kind.
        //
        // Two facts combined to make that route brick the window at launch:
        //   * `parseWindowBehavior` defaults `Startup = "windowed-fullscreen"`, and
        //   * `windowFlagSupplied` is true for SIX flags, five of which say nothing about startup
        //     state (`--window-resize`, `--window-maximize`, `--window-position`, `--window-backend`,
        //     `--window-options-file`).
        //
        // So `--window-backend vulkan` alone opted the launch into the exact state that bricks the
        // UI. That is not hypothetical: the work-board guidance launches this product with
        // `--window-options-file`, and a file containing only `position=` would have triggered it.
        // A default that takes effect ONLY in combination with an unrelated flag is the worst shape
        // this could have — it is invisible from the flag the operator actually typed.
        //
        // The assertion is exhaustive over the flag vocabulary rather than sampled, because the
        // defect was in a DEFAULT: any test that named the startup values would have missed it.
        test "no command-line flag combination launches into WindowedFullscreen while #1196 is open (#63)" {
            let startupValues = [ "normal"; "maximized"; "minimized"; "fullscreen"; "windowed-fullscreen" ]

            let flagCombinations =
                [ []
                  [ "--window-resize"; "fixed-size" ]
                  [ "--window-resize"; "resizable" ]
                  [ "--window-maximize"; "maximizable" ]
                  [ "--window-maximize"; "not-maximizable" ]
                  [ "--window-position"; "centered" ]
                  [ "--window-position"; "10,20" ]
                  [ "--window-backend"; "vulkan" ]
                  [ "--window-backend"; "opengl" ]
                  [ "--window-backend"; "software" ]
                  [ "--window-backend"; "default" ]
                  [ "--window-options-file"; "/nonexistent-window-options.txt" ]
                  for value in startupValues -> [ "--window-startup"; value ] ]

            for flags in flagCombinations do
                let behaviour = Rogue3.WindowOptions.parseWindowBehavior flags
                let request = Rogue3.WindowOptions.toViewerLaunchRequest behaviour

                Expect.notEqual
                    request.StartupState
                    ViewerWindowStartupState.WindowedFullscreen
                    $"launching with %A{flags} must not request the work-area-derived WindowedFullscreen state that bricks the window (#63)"

            // The flags that do NOT mention startup must not silently change it either. This is the
            // assertion that actually names the defect: it was the DEFAULT reaching the launch
            // request through an unrelated flag, not the explicit selection.
            let normalRequest =
                Rogue3.WindowOptions.toViewerLaunchRequest (Rogue3.WindowOptions.parseWindowBehavior [])

            for flags in
                [ [ "--window-backend"; "vulkan" ]
                  [ "--window-position"; "centered" ]
                  [ "--window-resize"; "resizable" ]
                  [ "--window-maximize"; "maximizable" ] ] do
                let request =
                    Rogue3.WindowOptions.toViewerLaunchRequest (Rogue3.WindowOptions.parseWindowBehavior flags)

                Expect.equal
                    request.StartupState
                    normalRequest.StartupState
                    $"%A{flags} says nothing about startup state, so it must not change the startup state the launch requests (#63)"

            // And the guard's own precondition: these flags really do route the launch through
            // `toViewerLaunchRequest` rather than the shell default, which is what makes the
            // assertions above load-bearing rather than vacuous.
            for flags in
                [ [ "--window-backend"; "vulkan" ]
                  [ "--window-position"; "centered" ]
                  [ "--window-resize"; "resizable" ]
                  [ "--window-maximize"; "maximizable" ]
                  [ "--window-options-file"; "/nonexistent-window-options.txt" ]
                  [ "--window-startup"; "normal" ] ] do
                Expect.isTrue
                    (Rogue3.WindowOptions.windowFlagSupplied flags)
                    $"%A{flags} routes the launch through toViewerLaunchRequest, so the request it builds is the one the window gets"

            Expect.isFalse
                (Rogue3.WindowOptions.windowFlagSupplied [])
                "with no window flag the launch uses the shell's own InitialDisplay instead"
        }

        // Issue #912: PLAYED THROUGH THE HOST — the one altitude the host tests above never reach.
        //
        // They prove the halves in isolation: `MapKey ArrowUp true` returns SOME message, and
        // `Update` (handed a message) advances the model. Neither COMPOSES them, and the gap between
        // the halves is exactly where a rogue3 ships green-but-unplayable — a key that maps to a
        // message `update` quietly ignores, or a `mapKey` that folds every key into a key-state
        // snapshot no gameplay ever reads. The model-level test below (`update (ViewerInput …)`)
        // INJECTS the message; it never asks whether pressing the key PRODUCES it.
        //
        // `runKeyScriptToModel` asks the whole question. It folds a raw key SCRIPT through the host's
        // own `dispatchKey` (`normalizeEvent -> MapKey -> Update`) — the SAME fold the live runtime
        // performs — from `Init`'s model to the final one, no window and no GL. Rougue1 shipped "v1
        // complete" with 108 green tests while unplayable precisely because the suite injected
        // messages straight into `update` and nothing ever exercised key -> host -> update. This is
        // the shape of test that would have caught it — rewrite its script/assertions for your game.
        test "generated game host PLAYS THROUGH: a pressed key reaches gameplay, not just a message" {
            let host = Rogue3.Program.generatedHost
            let restedLeftPaddle = (fst (host.Init())).LeftPaddleY

            // 'W' drives the LEFT paddle UP (Model.paddleForKey -> movePaddle). Folded key -> mapKey
            // -> update through the real driver, two presses move it. If `mapKey` stopped wrapping the
            // key, or `update` stopped routing `ViewerInput` to `movePaddle`, the paddle would not
            // move and this reddens — where injecting `ViewerInput(Letter 'W', true)` stays green.
            let played, _ = GeneratedAppHost.runKeyScriptToModel host [ downKey "w"; downKey "w" ]

            Expect.isTrue
                (played.LeftPaddleY < restedLeftPaddle)
                "two 'w' presses, folded key -> mapKey -> update, move the left paddle up: the key reaches gameplay"
            Expect.equal played.LastInput (Some(Letter 'W')) "the host records the last key it actually routed"
        }

        // Issue #911/#912: HANDLED-BUT-UNWIRED guard — the scene-host analogue of the click path's
        // `BoundIds` ("a key that is bound is a key that dispatches"). `reachableMessages` returns
        // every message the host's runtime SOURCES: `mapKey` over the probed keys, plus `Tick` when a
        // sample is given. A `Msg` case your `update` handles but that appears in NO source is
        // dispatched by nothing — the Rougue1 defect. List the intents a player must be able to reach
        // and assert each is sourced; it reddens the moment you add a handled intent (a Fire, a Jump)
        // and forget to wire an input — or a tick — to it. (This starter's `mapKey` wraps EVERY key as
        // `ViewerInput`, so `auditKeyWiring.Dead` is structurally empty here and the PLAYED-THROUGH
        // test above is the load-bearing guard; a game whose `mapKey` returns intent messages directly
        // gets a non-vacuous `Dead` too — see the fs-gg-elmish skill.)
        test "generated game host SOURCES the intents a player must reach (handled-but-unwired guard)" {
            let host = Rogue3.Program.generatedHost
            let probe = [ downKey "w"; downKey "s"; downKey "ArrowUp"; downKey "ArrowDown" ]
            let reachable = GeneratedAppHost.reachableMessages host probe (Some(TimeSpan.FromMilliseconds 16.0))

            Expect.isTrue
                (reachable |> List.exists (function ViewerInput _ -> true | _ -> false))
                "the keyboard is a live source of ViewerInput — the seam the paddle router reads, not a dead key-state snapshot"
            Expect.isTrue
                (reachable |> List.exists (function Tick _ -> true | _ -> false))
                "the clock is a live source of Tick — the fixed-step sim advances from a real source, not only from injected ticks"
        }

        test "generated game host boundary keeps app commands separate from viewer effects" {
            let model0 = Rogue3.Program.initialModel
            let hosted, appCommands, viewerEffects = Rogue3.Program.interpretAtHostBoundary (Tick oneStep) model0

            Expect.notEqual hosted model0 "host boundary applies the pure update result"
            Expect.isEmpty appCommands "the game tick produces no app command"
            Expect.exists viewerEffects (function RenderScene _ -> true | _ -> false) "host boundary emits a render effect separately"
        }

        test "game layout evidence re-points HUD onto the score strip and gameplay onto the playfield" {
            let report = Rogue3.Program.layoutEvidenceForSize { Width = 640; Height = 480 } Rogue3.Program.initialModel

            Expect.equal report.ProofLevel ReadableLayout "game layout report proves readable layout"
            Expect.isSome report.HudRegion "score region is named"
            Expect.isSome report.GameplayRegion "playfield region is named"
            Expect.isNonEmpty report.TextBounds "score text bounds are present"
            Expect.isNonEmpty report.GameplayBounds "active ball bounds are present"
            Expect.equal report.OverlapStatus NoLayoutOverlap "score and playfield bounds do not overlap"
        }

        test "game active item (the ball) stays inside the playfield region" {
            let size: FS.GG.UI.Scene.Size = { Width = 640; Height = 480 }
            let model0 = Rogue3.Program.initialModel
            let ticked = List.replicate 30 (Tick oneStep) |> List.fold (fun m msg -> fst (Rogue3.Program.update msg m)) model0

            let region = Rogue3.Program.gameplayRegionForSize size
            let bounds = Rogue3.Program.activeGameplayBoundsForSize size ticked

            Expect.isTrue (Rogue3.Program.boundsInside region.Bounds bounds.Bounds) "the ball stays inside the playfield region"
            Expect.isTrue (Rogue3.Program.movementUsesGameplayRegion size ticked) "movement policy is region based"
            Expect.isTrue (Rogue3.Program.spawnUsesGameplayRegion size model0) "spawn policy is region based"
            Expect.isTrue (Rogue3.Program.collisionUsesGameplayRegion size ticked) "collision policy is region based"
        }

        test "game layout validation accepts a readable report and rejects a factless one" {
            let good = Rogue3.Program.layoutEvidenceForSize { Width = 640; Height = 480 } Rogue3.Program.initialModel
            let goodResult = Rogue3.Program.validateGeneratedLayout good
            let broken = { good with HudRegion = None; GameplayBounds = [] }
            let brokenResult = Rogue3.Program.validateGeneratedLayout broken

            Expect.isTrue goodResult.Accepted "a readable game layout validates"
            Expect.isFalse brokenResult.Accepted "a layout missing facts is rejected"
            Expect.equal brokenResult.FailureClass (Some MissingLayoutFacts) "missing facts are classified"
        }

        test "generated default game dispatches input, advances over time, and keeps evidence flags opt-in" {
            let model0 = Rogue3.Program.initialModel
            let moved, _ = Rogue3.Program.update (ViewerInput(ArrowUp, true)) model0
            Expect.notEqual moved model0 "keyboard input changes game state"

            match Rogue3.Program.tick (TimeSpan.FromMilliseconds 500.0) with
            | Some tickMsg ->
                let afterTick, _ = Rogue3.Program.update tickMsg moved
                Expect.notEqual afterTick moved "time-based tick advances game state"
            | None -> failtest "generated tick must advance game state over time"

            let source = System.IO.File.ReadAllText(System.IO.Path.Combine(__SOURCE_DIRECTORY__, "..", "..", "src", "Rogue3", "Program.fs"))
            let defaultBranch = source.Substring(source.LastIndexOf("| None ->", StringComparison.Ordinal))
            // #991/#1000 + Governance#297: the game family now boots the generic game shell on the
            // pointer-aware `interactiveHost`. Asserted by the default-launch BEHAVIOUR — the audio sink
            // and the persistent host reach the launcher — not the launcher-overload substring, so this
            // survives both a model swap and a sanctioned launcher upgrade (the model-agnostic contract
            // the durable GovernanceTests spine owns; reused here rather than re-pinned).
            Rogue3GovernanceTests.assertDefaultLaunchHonoursEffects defaultBranch
            Expect.isFalse (defaultBranch.Contains("--launch-evidence")) "launch evidence flag stays out of normal launch branch"
            Expect.isFalse (defaultBranch.Contains("--bounded-smoke")) "bounded smoke flag stays out of normal launch branch"
            Expect.isFalse (defaultBranch.Contains("self-closed-for-evidence=true")) "normal launch does not report evidence self-close"
        }

        // #136 (epic #134): the --window-diagnostics probe must agree with the real launch. Its
        // verdict is derived from the SAME gate Viewer.runApp consults (Viewer.runtimeCapability()),
        // so the reported live-window capability equals that gate and it never fabricates an observed
        // window failure — the self-report/reality mismatch the reporter hit.
        test "window diagnostics verdict matches the real runtime gate (#136 / epic #134)" {
            let dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "window-diagnostics-" + System.Guid.NewGuid().ToString("N"))
            let path = System.IO.Path.Combine(dir, "window-diagnostics.txt")
            let exitCode = Rogue3.EvidenceCommands.windowDiagnostics path
            let output = System.IO.File.ReadAllText path
            System.IO.Directory.Delete(dir, true)

            let capability = Viewer.runtimeCapability ()
            let supportedText = if capability.PersistentWindow then "true" else "false"

            Expect.equal exitCode 0 "window diagnostics command succeeds"
            Expect.stringContains output ("persistent-window-supported=" + supportedText) "probe reports the real runtime-capability gate verbatim, not a fabricated verdict"
            Expect.stringContains output "diagnostic-class=window-visibility" "probe still enumerates the window-visibility class"

            Expect.isFalse (output.Contains "visible=observed:false") "probe never fabricates an observed window-invisibility"
            Expect.isFalse (output.Contains "status=failed") "probe never reports a failed status it did not observe"
            Expect.isFalse (output.Contains "taskbar-only" && output.Contains "status=ok") "taskbar-only is never reported ok"

            if capability.PersistentWindow then
                Expect.isFalse (output.Contains "status=unsupported") "a window-capable host is never told a live window is impossible"
            else
                Expect.stringContains output "status=unsupported" "an unsupported host is reported unsupported (matching the real launch), not failed"
        }

        // #901/#1030: the --view-image probe renders the FULL rogue3 view at an explicit logical
        // size through the window-free CPU readback. Two non-default sizes prove the command does
        // not merely relabel a fixed PNG, and invalid dimensions must fail instead of falling back.
        test "view-image probe renders requested logical sizes and rejects invalid dimensions (#901/#1030)" {
            let pngSignature = [| 0x89uy; 0x50uy; 0x4Euy; 0x47uy; 0x0Duy; 0x0Auy; 0x1Auy; 0x0Auy |]

            let readBigEndianInt32 (bytes: byte array) offset =
                (int bytes[offset] <<< 24)
                ||| (int bytes[offset + 1] <<< 16)
                ||| (int bytes[offset + 2] <<< 8)
                ||| int bytes[offset + 3]

            for width, height in [ 320, 200; 901, 507 ] do
                let dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "view-image-" + System.Guid.NewGuid().ToString("N"))
                let path = System.IO.Path.Combine(dir, "view-image.png")
                let exitCode = Rogue3.EvidenceCommands.viewImageAtSize path width height
                let metadata = System.IO.File.ReadAllText(path + ".metadata.txt")
                let pngExists = System.IO.File.Exists path
                let pngBytes = if pngExists then System.IO.File.ReadAllBytes path else [||]
                System.IO.Directory.Delete(dir, true)

                Expect.equal exitCode 0 "valid view-image dimensions produce pixels or an honest unsupported result"
                Expect.stringContains metadata "command=--view-image" "records the evidence command"
                Expect.stringContains metadata $"requested-size={width}x{height}" "records the requested logical size"
                Expect.stringContains metadata $"output-size={width}x{height}" "records the render surface size"

                if metadata.Contains "status=ok" then
                    Expect.isTrue pngExists "a supported host writes the PNG file"
                    Expect.equal (pngBytes |> Array.truncate 8) pngSignature "the readback is a decodable PNG"
                    Expect.equal (readBigEndianInt32 pngBytes 16) width "the PNG IHDR width matches the request"
                    Expect.equal (readBigEndianInt32 pngBytes 20) height "the PNG IHDR height matches the request"
                    Expect.stringContains metadata $"actual-size={width}x{height}" "metadata records the PNG-header size"
                    Expect.stringContains metadata "dimensions-match=True" "metadata proves requested and actual dimensions agree"
                    Expect.stringContains metadata "renders-full-view=true" "the probe renders the full rogue3 view"
                else
                    Expect.stringContains metadata "status=unsupported" "an unsupported host reports unsupported (never fabricated pixels)"

            let invalidPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "view-image-invalid-" + System.Guid.NewGuid().ToString("N") + ".png")
            let invalid = Rogue3.EvidenceCommands.tryRunEvidenceCommand [ "--view-image"; invalidPath; "0"; "720" ]
            Expect.equal invalid (Some 1) "non-positive explicit dimensions return a nonzero command result"
            Expect.isFalse (System.IO.File.Exists invalidPath) "invalid dimensions never fall back to a default-size image"

            let oversizedPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "view-image-oversized-" + System.Guid.NewGuid().ToString("N") + ".png")
            let oversized =
                Rogue3.EvidenceCommands.tryRunEvidenceCommand
                    [ "--view-image"; oversizedPath; "2000000000"; "2000000000" ]
            Expect.equal oversized (Some 1) "oversized dimensions fail before attempting a CPU raster allocation"
            Expect.isFalse (System.IO.File.Exists oversizedPath) "an oversized request writes no image"
        }

        // #139 (revised for #991/#1000): the input boundary must be SURFACED where a game author first
        // wires input (a comment at the input-mapping site) and must stay ACCURATE to the emitted host
        // contract, so it cannot silently rot if the seams change. The game family now boots the
        // pointer-host shell by default, so the note states that the PLAY-INPUT mapping is keyboard-only
        // while the turnkey launch is the pointer-aware interactive host. Source/contract scan — no
        // host launch needed.
        test "play-input boundary is surfaced at the input-wiring site and accurate to the host contract (#139/#1000)" {
            let readRogue3File parts =
                System.IO.File.ReadAllText(System.IO.Path.Combine(Array.append [| __SOURCE_DIRECTORY__; ".."; ".." |] parts))

            let typeBlock (source: string) (name: string) =
                let start = source.IndexOf("type " + name, System.StringComparison.Ordinal)
                let next = source.IndexOf("type ", start + 5, System.StringComparison.Ordinal)
                let stop = if next < 0 then source.Length else next
                source.Substring(start, stop - start)

            // A1 — the boundary is present at the game input-wiring site (Model.fs, by paddleForKey).
            let modelSource = readRogue3File [| "src"; "Rogue3"; "Model.fs" |]
            Expect.stringContains modelSource "HOST INPUT BOUNDARY" "Model.fs surfaces the input-wiring boundary at the input-wiring site"
            Expect.stringContains (modelSource.ToLowerInvariant()) "keyboard-only" "the boundary states the play-input mapping (paddleForKey) is keyboard-only"
            Expect.stringContains modelSource "runInteractiveApp" "the boundary names the pointer-aware interactive host the turnkey default now launches on"
            Expect.stringContains modelSource "MapPointer" "the boundary names the pointer seam the interactive host adds"

            // A2 — accurate: the emitted ViewerKey has NO mouse/pointer case (keyboard keys only).
            let keyboardFsi = readRogue3File [| "docs"; "api-surface"; "KeyboardInput"; "KeyboardInput.fsi" |]
            let viewerKeyBlock = typeBlock keyboardFsi "ViewerKey"
            Expect.stringContains viewerKeyBlock "ArrowLeft" "ViewerKey enumerates keyboard keys"
            Expect.isFalse (viewerKeyBlock.Contains "Mouse") "ViewerKey has no mouse case — the note's core claim"
            Expect.isFalse (viewerKeyBlock.Contains "Pointer") "ViewerKey has no pointer case — the note's core claim"

            // A3 — accurate: the default host (GeneratedAppHost) exposes MapKey but NOT MapPointer;
            // the pointer-aware InteractiveAppHost is where MapPointer lives.
            let viewerFsi = readRogue3File [| "docs"; "api-surface"; "SkiaViewer"; "SkiaViewer.fsi" |]
            let generatedHostBlock = typeBlock viewerFsi "GeneratedAppHost"
            Expect.stringContains generatedHostBlock "MapKey:" "the default host exposes a keyboard MapKey seam"
            Expect.isFalse (generatedHostBlock.Contains "MapPointer") "the default host has NO pointer seam (keyboard-only) — the boundary is real"
            let interactiveHostBlock = typeBlock viewerFsi "InteractiveViewerHost"
            Expect.stringContains interactiveHostBlock "MapPointer:" "the pointer-aware interactive host is where the mouse seam actually lives"
            // the note's author-facing signpost names must exist in the shipped contract (accuracy of FR-002)
            Expect.stringContains viewerFsi "InteractiveAppHost" "the note's named pointer-aware host type is real in the shipped surface"
            Expect.stringContains viewerFsi "runInteractiveApp" "the note's named interactive-host entry point is real in the shipped surface"
        }
    ]
