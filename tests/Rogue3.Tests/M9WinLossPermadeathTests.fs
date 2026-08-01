module M9WinLossPermadeathTests

open System
open System.IO
open System.Threading
open Expecto
open FS.GG.Audio.Core
open FS.GG.UI.Canvas
open FS.GG.UI.SkiaViewer
open Rogue3
open Rogue3.Model

let private tempDirectory () = Path.Combine(Path.GetTempPath(),"rogue3-m9-"+Guid.NewGuid().ToString("N"))
let private kills count = Map.ofList [Entities.EnemyKind.Grub,count]

[<Tests>]
let tests =
    testList "M9 win loss permadeath and profile persistence" [
        test "§11 score is exact and cosmetic while end-run unlocks use milestones" {
            let stats={emptyRunStats with DepthReached=6;FloorsCleared=6;BossKills=3;KillsByType=kills 12;CoinsCollected=7;ItemsFound=2;RunSeconds=12.9;DamageByFloor=Map.ofList[2,(10.0,1.0)]}
            Expect.equal (runScore stats) 49915 "the complete §11 formula uses floored seconds and five no-hit floors"
            let started={initialModel with RunActive=true;RunSeed=77UL;RunStats=stats}
            let ended=update (CompleteRunStats(false,Some DeathCause.Trap)) started|>fst
            Expect.equal ended.RunOutcome (Some RunOutcome.GameOver) "loss records a terminal outcome"
            Expect.equal ended.LastRunSummary.Value.Score 49915 "summary retains the exact cosmetic score"
            Expect.contains ended.Profile.UnlockedItems "cracked-lens" "depth milestone unlocks independently of score"
            Expect.contains ended.Profile.UnlockedItems "glass-cannon" "boss-kill milestone unlocks independently of score"
            Expect.equal ended.Profile.BestScoresBySeed.[77UL] 49915 "best score is recorded per seed"
            Expect.equal ended.RunStats emptyRunStats "active run statistics are discarded after snapshotting"
            let again=update (CompleteRunStats(false,None)) ended|>fst
            Expect.equal again.Profile.Lifetime.RunsPlayed 1 "terminal evaluation is idempotent"
        }

        test "production floor-6 boss defeat yields Victory, discard, unlock, persistence and audio" {
            let boss=Entities.spawnBoss 901 Entities.BossKind.Maw initialModel.PlayerPosition
            let before={initialModel with RunActive=true;FloorIndex=6;M5Boss=Some boss;RunStats={emptyRunStats with DepthReached=6;BossKills=2;FloorsCleared=5}}
            let after=update (DamageM5Boss 100000.0) before|>fst
            Expect.equal after.RunOutcome (Some RunOutcome.Victory) "the production boss damage route wins on floor six"
            Expect.isFalse after.RunActive "the run is no longer resumable"
            Expect.isNone after.M5Boss "boss/run entities are discarded"
            Expect.contains after.Profile.UnlockedItems "abyssal-crown" "victory awards its special unlock"
            Expect.equal after.LastRunSummary.Value.FloorsCleared 6 "final boss clearance is tallied"
            let requests=profilePersistenceRequestsForTransition (DamageM5Boss 100000.0) before after|>Persistence.interpretRecordOnly
            Expect.equal requests.Requested.Length 1 "terminal profile mutation emits exactly one save request"
            let audio=AudioCues.forTransition (DamageM5Boss 100000.0) before after|>Audio.interpret|>_.Requested
            Expect.contains audio (PlayMusic(TrackId "victory",true)) "victory music reaches the production transition seam"
            let text=Render.view after|>Rogue3BehaviorTests.sceneText
            Expect.stringContains text "VICTORY" "result screen is production-rendered"
            Expect.stringContains text (string after.LastRunSummary.Value.Score) "the final score is visible"
        }

        test "a production player shot defeats the floor-6 boss and terminal ticks freeze simulation" {
            let boss={Entities.spawnBoss 901 Entities.BossKind.Maw initialModel.PlayerPosition with HitPoints=1.0}
            let shot=spawnShots 1 1 boss.Position Rogue3.Geometry.zero (Rogue3.Geometry.vec2 1.0 0.0) basePlayerStats|>List.head
            let before={initialModel with RunActive=true;FloorIndex=6;M5Boss=Some boss;ShotSpawns=[shot];RunStats={emptyRunStats with DepthReached=6;BossKills=2;FloorsCleared=5}}
            let won=update (Tick fixedDt) before|>fst
            Expect.equal won.RunOutcome (Some RunOutcome.Victory) "the actual fixed-step projectile collision reaches Victory"
            let frozen=update (Tick 1.0) won|>fst
            Expect.equal frozen.SimStepCount won.SimStepCount "terminal simulation does not advance"
            Expect.equal frozen.TickCount won.TickCount "terminal host ticks are pure no-ops"
            Expect.equal frozen won "all terminal state stays frozen until a result action"
        }

        test "production fixed-step death at zero half-hearts yields GameOver and discards the run" {
            let before={initialModel with RunActive=true;PlayerHealth={initialModel.PlayerHealth with RedHalfHearts=0};RunStats={emptyRunStats with DepthReached=3}}
            let after=update (Tick 0.0) before|>fst
            Expect.equal after.RunOutcome (Some RunOutcome.GameOver) "end-of-step death resolves to GameOver"
            Expect.isFalse after.RunActive "permadeath prevents continue"
            Expect.isEmpty after.EnemyBullets "run combat state is discarded"
            Expect.contains after.Profile.UnlockedItems "cracked-lens" "death still evaluates end-run unlocks"
            Expect.equal after.Profile.Lifetime.RunsPlayed 1 "lifetime tally increments once"
            let audio=AudioCues.forTransition (Tick 0.0) before after|>Audio.interpret|>_.Requested
            Expect.contains audio (PlayMusic(TrackId "game-over",true)) "game-over music is routed from the real death transition"
        }

        test "real file backend debounces latest JSON and proves temp-file atomic rename" {
            let directory=tempDirectory()
            Directory.CreateDirectory(directory)|>ignore
            let path=Path.Combine(directory,ProfileStore.profileFileName)
            try
                use store=new ProfileStore.Store(path,TimeSpan.FromMilliseconds 60.0)
                let p1={defaultMetaProfile with Lifetime={defaultMetaProfile.Lifetime with RunsPlayed=1}}
                let p2={defaultMetaProfile with Lifetime={defaultMetaProfile.Lifetime with RunsPlayed=2};BestScoresBySeed=Map.ofList[44UL,1234]}
                store.Request p1
                store.Request p2
                Expect.equal store.WriteCount 0 "no write occurs before the debounce window is flushed"
                store.Flush()
                Expect.equal store.WriteCount 1 "requests inside the quiet window collapse into one durable write"
                Expect.equal (ProfileStore.load path) (ProfileStore.Loaded p2) "only the latest profile round-trips from disk"
                let evidence=store.LastWrite.Value
                Expect.isTrue evidence.TempCreated "the sibling temp file existed before replacement"
                Expect.isTrue evidence.Renamed "the destination exists after atomic replacement"
                Expect.isFalse evidence.TempExistsAfter "rename leaves no partial temp file"
            finally
                if Directory.Exists directory then Directory.Delete(directory,true)
        }

        test "boot load distinguishes absent/unreadable/version fallback and production shell uses the store" {
            let directory=tempDirectory()
            Directory.CreateDirectory(directory)|>ignore
            let path=Path.Combine(directory,ProfileStore.profileFileName)
            try
                Expect.equal (ProfileStore.load path) ProfileStore.Absent "missing profile is a normal fresh boot"
                File.WriteAllText(path,"{broken")
                match ProfileStore.load path with ProfileStore.Unreadable _->()|other->failtestf "expected unreadable malformed JSON, got %A" other
                File.WriteAllText(path,(encodeMetaProfile defaultMetaProfile).Replace("\"version\":1","\"version\":2"))
                match ProfileStore.load path with ProfileStore.Unreadable reason->Expect.stringContains reason "version" "unsupported version is explicit"|other->failtestf "expected unsupported version, got %A" other
                let saved={defaultMetaProfile with Lifetime={defaultMetaProfile.Lifetime with RunsPlayed=9};UnlockedItems=Set["cracked-lens"]}
                ProfileStore.writeAtomic path saved
                use store=new ProfileStore.Store(path,TimeSpan.FromHours 1.0)
                let host=EvidenceCommands.createInteractiveHost store
                let booted,_=host.Init()
                Expect.equal booted.Play.Profile saved "production shell loads MetaProfile before its first frame"
                let changed,effects=host.Update (EvidenceCommands.ChangeVolume 0.25) booted
                Expect.isFalse (effects|>List.exists(function ViewerEffect.Persist _->true|_->false)) "the product host realizes persistence before the sinkless pointer launcher"
                store.Flush()
                match ProfileStore.load path with
                | ProfileStore.Loaded profile->Expect.equal profile.Settings.MasterVolume changed.Play.Profile.Settings.MasterVolume "shell changes become durable"
                | other->failtestf "expected durable shell profile, got %A" other
            finally
                if Directory.Exists directory then Directory.Delete(directory,true)
        }
    ]
