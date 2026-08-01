module Rogue3M7UiMenusStatsTests

open Expecto
open FS.GG.UI.Scene
open FS.GG.UI.Controls
open FS.GG.UI.Controls.Elmish
open FS.GG.UI.SkiaViewer
open FS.GG.UI.KeyboardInput
open FS.GG.UI.Canvas
open Rogue3
open Rogue3.Model

let private pointer phase x y : ViewerPointerInput =
    { Phase=phase; X=x; Y=y; Button=Some ViewerPointerButtonKind.Primary; DeltaX=0.0; DeltaY=0.0 }

let private click controlId model =
    let host = EvidenceCommands.interactiveHost
    let size = EvidenceCommands.shellConfig.InitialDisplay.Resolution
    let frame = Control.renderTree host.Theme size (host.View size model)
    Expect.isTrue (Set.contains controlId frame.BoundIds) $"{controlId} is a real authored binding"
    let available: FS.GG.UI.Layout.AvailableSpace =
        { Width=float size.Width; WidthMode=FS.GG.UI.Layout.Exactly
          Height=float size.Height; HeightMode=FS.GG.UI.Layout.Exactly }
    let evaluated = FS.GG.UI.Layout.Layout.evaluate available frame.Layout
    let bounds = (evaluated.Bounds |> List.find (fun item -> item.NodeId=controlId)).Bounds
    let x,y = bounds.X+bounds.Width/2.0, bounds.Y+bounds.Height/2.0
    let fold current messages = messages |> List.fold (fun state msg -> host.Update msg state |> fst) current
    let pointerState, down = ControlsElmish.routeInteractivePointer host (Pointer.init()) size model (pointer ViewerPointerPhaseKind.Pressed x y)
    let afterDown = fold model down
    let proof = ControlsElmish.captureRespondsProof host pointerState size afterDown (pointer ViewerPointerPhaseKind.Released x y)
    let _, up = ControlsElmish.routeInteractivePointer host pointerState size afterDown (pointer ViewerPointerPhaseKind.Released x y)
    proof, fold afterDown up

let rec private sceneTexts (node: SceneNode) =
    match node with
    | Group scenes -> scenes |> List.collect (fun scene -> scene.Nodes |> List.collect sceneTexts)
    | Text(_,value,_)
    | SizedText(_,value,_,_) -> [value]
    | Translate(_,scene) -> scene.Nodes |> List.collect sceneTexts
    | _ -> []

[<Tests>]
let m7UiMenusStatsTests =
    testList "M7 UI menus stats" [
        test "responsive HUD reserves non-overlapping anchors at both required output sizes" {
            for size in [{Width=1280;Height=720};{Width=1920;Height=1080}] do
                let layout = Render.hudLayoutForSize size
                Expect.isFalse layout.Overlaps $"HUD anchors do not overlap at {size.Width}x{size.Height}"
                Expect.isGreaterThanOrEqual layout.MinimapBounds.X 0.0 "minimap remains on-screen"
                Expect.isLessThanOrEqual (layout.MinimapBounds.X+layout.MinimapBounds.Width) (float size.Width) "minimap remains on-screen"
                let labels = (Render.hudSceneForSize size initialModel).Nodes |> List.collect sceneTexts
                Expect.contains labels "COIN 00   KEY 01   BOMB 01" "currency uses fixed two-digit HUD formatting"
                Expect.contains labels "ACTIVE 2/6" "active charge is rendered"
        }

        test "difficulty table is exact and a running run latches its start selection" {
            let easy = difficultyScaling DifficultyMode.Easy
            let normal = difficultyScaling DifficultyMode.Normal
            let hard = difficultyScaling DifficultyMode.Hard
            Expect.equal (easy.EnemyHpScale,easy.PostHitInvulnSeconds,easy.DropNothingWeight,easy.ExtraStartingContainers) (0.08,1.10,35,1) "Easy table"
            Expect.equal (normal.EnemyHpScale,normal.PostHitInvulnSeconds,normal.DropNothingWeight) (0.12,0.80,45) "Normal table"
            Expect.equal (hard.EnemyHpScale,hard.PostHitInvulnSeconds,hard.DropNothingWeight,hard.ExtraElitePerCombatRoom,hard.PostBossHeal) (0.18,0.55,55,1,false) "Hard table"
            let selected = update (SetDifficulty DifficultyMode.Easy) initialModel |> fst
            let running = update (StartRun 71UL) selected |> fst
            let changedMidRun = update (SetDifficulty DifficultyMode.Hard) running |> fst
            Expect.equal changedMidRun.ActiveDifficulty (Some easy) "current run remains Easy"
            Expect.equal changedMidRun.Profile.Settings.Difficulty DifficultyMode.Hard "profile records next-run preference"
            let nextRun = update (StartRun 72UL) changedMidRun |> fst
            Expect.equal nextRun.ActiveDifficulty (Some hard) "next run latches Hard"
        }

        test "game setting transition emits an honest record-only MetaProfile request" {
            let next = update (SetMasterVolume 0.42) initialModel |> fst
            let requests = profilePersistenceRequestsForTransition (SetMasterVolume 0.42) initialModel next
            let evidence = Persistence.interpretRecordOnly requests
            Expect.equal evidence.Backend PersistenceBackend.RecordOnly "headless evidence never claims durability"
            Expect.hasLength evidence.Requested 1 "one preference transition requests one save"
            Expect.stringContains (encodeMetaProfile next.Profile) "\"masterVolume\":0.420000" "payload is deterministic and versioned by its envelope"
            Expect.isEmpty (profilePersistenceRequestsForTransition NoOp next next) "non-setting messages do not request writes"
        }

        test "stats aggregate into five depth buckets and two damage-per-floor series" {
            Expect.equal (depthHistogram [1;3;4;7;10;12;13;20]) [2;1;1;2;2] "fixed depth buckets"
            let stats = { initialModel.RunStats with DamageByFloor=Map.ofList [1,(20.0,7.0);2,(35.0,9.0)] }
            let model = { initialModel with RunStats=stats }
            let _, series = M7Ui.statsSeries model
            Expect.equal (series |> List.map _.Name) ["Dealt #2a78d6";"Taken #1baf7a"] "charts have explicit series labels/colors"
            Expect.equal (series |> List.map (fun item -> item.Points.Length)) [2;2] "both series cover each recorded floor"
        }

        test "actual host clicks open stats, change scope, and return without advancing play" {
            let model0 = EvidenceCommands.interactiveHost.Init() |> fst
            let openProof, stats = click "stats" model0
            Expect.equal openProof.Verdict Responsive "Stats click visibly changes the production host tree"
            Expect.isTrue stats.StatsOpen "stats route is open"
            let scopeProof, lifetime = click "scope-lifetime" stats
            Expect.equal scopeProof.Verdict Responsive "scope click changes the rendered selection model"
            Expect.equal lifetime.Play.StatScope StatScope.Lifetime "scope is Lifetime"
            let closeProof, closed = click "stats-back" lifetime
            Expect.equal closeProof.Verdict Responsive "Back click returns to shell"
            Expect.isFalse closed.StatsOpen "stats route closes"
            Expect.equal closed.Shell.Screen Rogue3.GameShell.MainMenu "returns to the menu that opened stats"
        }

        test "actual host new-run click and Esc pause/resume use one router" {
            let host = EvidenceCommands.interactiveHost
            let model0 = host.Init() |> fst
            let proof, playing = click "new-run" model0
            Expect.equal proof.Verdict Responsive "New Run click visibly enters gameplay"
            Expect.equal playing.Shell.Screen Rogue3.GameShell.Playing "gameplay is active"
            Expect.isTrue playing.Play.RunActive "run is explicitly active"
            let escape = host.MapKey ViewerKey.Escape true |> Option.get
            let paused = host.Update escape playing |> fst
            Expect.equal paused.Shell.Screen Rogue3.GameShell.Paused "Esc pauses"
            let resumed = host.Update escape paused |> fst
            Expect.equal resumed.Shell.Screen Rogue3.GameShell.Playing "Esc resumes through the same generic shell"
        }
    ]
