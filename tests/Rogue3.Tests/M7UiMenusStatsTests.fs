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

let rec private sceneColors (node: SceneNode) =
    match node with
    | Group scenes -> scenes |> List.collect (fun scene -> scene.Nodes |> List.collect sceneColors)
    | Circle(_,_,fill)
    | FilledEllipse(_,fill)
    | Rectangle(_,fill)
    | Text(_,_,fill)
    | SizedText(_,_,_,fill) -> [fill]
    | PaintedRectangle(_,paint)
    | Ellipse(_,paint)
    | Line(_,_,paint)
    | Arc(_,_,_,paint) -> paint.Fill |> Option.toList
    | Translate(_,scene) -> scene.Nodes |> List.collect sceneColors
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

        test "latched difficulty changes every promised runtime consumer" {
            let start mode = initialModel |> update (SetDifficulty mode) |> fst |> update (StartRun 91UL) |> fst
            let easy, hard = start DifficultyMode.Easy, start DifficultyMode.Hard
            let combatId = easy.Floor.Rooms |> Map.values |> Seq.find (fun room -> room.RoomType=FloorGeneration.Combat) |> _.Id
            let easyRoom, hardRoom = loadM5Room combatId easy, loadM5Room combatId hard
            Expect.equal (hardRoom.M5Enemies.Length-easyRoom.M5Enemies.Length) 1 "Hard adds one elite to a combat room"
            let easyBase = easyRoom.M5Enemies |> List.last
            let matchingHard = hardRoom.M5Enemies |> List.find (fun actor -> actor.Id=easyBase.Id)
            Expect.floatClose Accuracy.high (matchingHard.HitPoints/easyBase.HitPoints) (1.18/1.08) "enemy HP uses the latched floor scale"

            let hit model =
                let bullet = {Id=777;Position=model.PlayerPosition;Velocity=Rogue3.Geometry.zero;Radius=4.0;Damage=1;Homing=0.0;AgeTicks=0}
                stepSim {model with EnemyBullets=[bullet]}
            Expect.isGreaterThan (hit easy).PostHitInvulnTicks (hit hard).PostHitInvulnTicks "Easy grants longer post-hit invulnerability"

            let bossId = easy.Floor.Rooms |> Map.values |> Seq.find (fun room -> room.RoomType=FloorGeneration.Boss) |> _.Id
            let hurt model = {model with PlayerHealth={model.PlayerHealth with RedHalfHearts=2}}
            let easyBoss = loadM5Room bossId easy |> hurt |> damageM5Boss System.Double.MaxValue
            let hardBoss = loadM5Room bossId hard |> hurt |> damageM5Boss System.Double.MaxValue
            Expect.equal easyBoss.PlayerHealth.RedHalfHearts 4 "Easy/Normal post-boss heal is applied"
            Expect.equal hardBoss.PlayerHealth.RedHalfHearts 2 "Hard disables post-boss healing"
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

        test "production settings host emits persistence and complete profile data" {
            let host = EvidenceCommands.interactiveHost
            let model = host.Init() |> fst
            let next, effects = host.Update (EvidenceCommands.ChangeVolume 0.42) model
            let batches = effects |> List.choose (function Persist requests -> Some requests | _ -> None)
            Expect.hasLength batches 1 "the production host forwards one persistence batch"
            Expect.hasLength batches.Head 1 "the batch contains one profile save"
            let profile =
                { next.Play.Profile with
                    Lifetime={next.Play.Profile.Lifetime with DeathsByCause=Map.ofList [DeathCause.Trap,2]}
                    UnlockedItems=Set.ofList ["compass"]
                    UnlockedCharacters=Set.ofList ["pilgrim"] }
            let payload = encodeMetaProfile profile
            Expect.stringContains payload "\"deathCounts\":[{\"cause\":\"trap\",\"count\":2}]" "death counts are preserved"
            Expect.stringContains payload "\"unlockedItems\":[\"compass\"]" "item unlocks are preserved"
            Expect.stringContains payload "\"unlockedCharacters\":[\"pilgrim\"]" "character unlocks are preserved"
        }

        test "stats aggregate into five depth buckets and two damage-per-floor series" {
            Expect.equal (depthHistogram [1;3;4;7;10;12;13;20]) [2;1;1;2;2] "fixed depth buckets"
            let stats = { initialModel.RunStats with DamageByFloor=Map.ofList [1,(20.0,7.0);2,(35.0,9.0)] }
            let model = { initialModel with RunStats=stats }
            let _, series = M7Ui.statsSeries model
            Expect.equal (series |> List.map _.Name) ["Dealt #2a78d6";"Taken #1baf7a"] "charts have explicit series labels/colors"
            Expect.equal (series |> List.map (fun item -> item.Points.Length)) [2;2] "both series cover each recorded floor"
        }

        test "run events and completion populate all requested counters" {
            let stats = {initialModel.RunStats with DepthReached=7;KillsByType=Map.ofList [Entities.EnemyKind.Fly,3]}
            let model = {initialModel with RunStats=stats;RunActive=true}
            let updated = model |> update RecordItemFound |> fst |> update (RecordCoinsCollected 6) |> fst |> update (CompleteRunStats(false,Some DeathCause.Trap)) |> fst
            Expect.equal (updated.RunStats.ItemsFound,updated.RunStats.CoinsCollected,updated.RunStats.DeathCause) (1,6,Some DeathCause.Trap) "run counters and cause are recorded"
            Expect.equal (updated.Profile.Lifetime.RunsPlayed,updated.Profile.Lifetime.DeepestFloor,updated.Profile.Lifetime.TotalKills) (1,7,3) "lifetime totals fold the completed run"
            Expect.equal updated.Profile.Lifetime.DeathsByCause[DeathCause.Trap] 1 "death cause is counted"
        }

        test "live projectile hits feed damage-per-floor and typed kill stats exactly once" {
            let combatId = initialModel.Floor.Rooms |> Map.values |> Seq.find (fun room -> room.RoomType=FloorGeneration.Combat) |> _.Id
            let room = loadM5Room combatId initialModel
            let enemy = room.Enemies.Head
            let shot =
                { Id=9001;Position=enemy.Position;Direction=Rogue3.Geometry.vec2 1.0 0.0;Velocity=Rogue3.Geometry.zero
                  Damage=enemy.HitPoints;FireRate=1.0;Speed=0.0;Range=1.0;Radius=enemy.Radius;Knockback=0.0
                  Pierce=0;HitsRemaining=1;BouncesRemaining=0;Homing=0.0;AgeTicks=0;MaxAgeTicks=10
                  DistanceTravelled=0.0;HitEnemyIds=Set.empty;SimStep=room.SimStepCount }
            let resolved = stepSim {room with ShotSpawns=[shot]}
            Expect.floatClose Accuracy.high resolved.RunStats.DamageDealt enemy.HitPoints "applied projectile damage is accumulated"
            let kind = room.M5Enemies |> List.find (fun actor -> actor.Id=enemy.Id) |> _.Kind
            Expect.equal resolved.RunStats.KillsByType[kind] 1 "M5 cleanup records one typed kill after the projectile death"
        }

        test "HUD exposes distinct soul/black hearts, special rooms, and a radial charge meter" {
            let roomId = initialModel.Floor.Rooms |> Map.values |> Seq.find (fun room -> room.RoomType=FloorGeneration.Boss) |> _.Id
            let model =
                {initialModel with
                    PlayerHealth={initialModel.PlayerHealth with SoulHalfHearts=2;BlackHalfHearts=2}
                    Floor={initialModel.Floor with MapRevealed=Set.add roomId initialModel.Floor.MapRevealed}}
            let scene = Render.hudSceneForSize {Width=1280;Height=720} model
            let colors = scene.Nodes |> List.collect sceneColors
            Expect.contains colors (Colors.rgba 78uy 186uy 232uy 255uy) "soul hearts use a distinct blue glyph"
            Expect.contains colors (Colors.rgba 38uy 30uy 48uy 255uy) "black hearts use a distinct dark glyph"
            Expect.contains colors (Colors.rgba 220uy 66uy 79uy 255uy) "boss rooms use a special minimap color"
            Expect.contains (Scene.describe scene) SceneElementKind.ArcElement "active charge is rendered as a radial arc"
        }

        test "a rebound movement key drives live gameplay through the production host" {
            let host = EvidenceCommands.interactiveHost
            let shell0 = Rogue3.GameShell.init EvidenceCommands.shellConfig
            let shell1 = Rogue3.GameShell.update Rogue3.GameShell.OpenSettings shell0 |> fst
            let shell2 = Rogue3.GameShell.update (Rogue3.GameShell.ArmRebind "move-up") shell1 |> fst
            let q = ViewerKeyboard.toKeyId (ViewerKey.Letter 'Q')
            let shell3 = Rogue3.GameShell.update (Rogue3.GameShell.CaptureKey q) shell2 |> fst
            let shell4 = Rogue3.GameShell.update Rogue3.GameShell.LeaveSettings shell3 |> fst
            let shell5 = Rogue3.GameShell.update Rogue3.GameShell.Start shell4 |> fst
            let play = update (StartRun 99UL) initialModel |> fst
            let model : EvidenceCommands.ShellHostModel = {Shell=shell5;Play=play;StatsOpen=false;HeldKeys=Set.empty}
            let down = host.Update (EvidenceCommands.RawKeyChanged(q,true)) model |> fst
            let moved = host.Update (EvidenceCommands.PlayDispatch (Tick (fixedDt*2.0))) down |> fst
            let released = host.Update (EvidenceCommands.RawKeyChanged(q,false)) moved |> fst
            Expect.isLessThan moved.Play.PlayerVelocity.Vy 0.0 "rebound Q produces move-up intent"
            Expect.isFalse (Set.contains "move-up" released.Play.Input.Current.Commands) "key-up clears semantic intent"
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
