module M8AudioTests

open System
open Expecto
open FS.GG.Audio.Core
open FS.GG.UI.SkiaViewer
open Rogue3
open Rogue3.Model

let private requested effects = Audio.interpret effects |> _.Requested

let private audioFromHost effects =
    effects
    |> List.collect (function ViewerEffect.PlayAudio batch -> batch | _ -> [])
    |> requested

let private musicOnly effects =
    effects
    |> List.filter (function PlayMusic _ | StopMusic -> true | _ -> false)

[<Tests>]
let audioTests =
    testList "M8 requested audio" [
        test "every fixed-step event maps to the exact §10 cue value and order" {
            let next =
                { Program.initialModel with
                    AudioEvents =
                        [ AudioEvent.ShotFired; AudioEvent.ShotHit; AudioEvent.EnemyDied
                          AudioEvent.PlayerHit; AudioEvent.PlayerDied; AudioEvent.DodgeRolled
                          AudioEvent.BombExploded ] }
            let actual = AudioCues.forTransition (Tick fixedDt) Program.initialModel next |> requested
            let expected =
                [ PlaySfx(SoundId "shot-fire", 0.55); PlaySfx(SoundId "shot-hit", 0.7)
                  PlaySfx(SoundId "enemy-death", 0.75); PlaySfx(SoundId "player-hit", 0.9)
                  PlaySfx(SoundId "player-death", 1.0); PlaySfx(SoundId "dodge-roll", 0.6)
                  PlaySfx(SoundId "bomb-explosion", 0.95) ]
            Expect.equal actual expected "the ordered transient batch preserves every occurrence without net-diff collapse"

            let firing =
                { Program.initialModel with
                    Input =
                        { Program.initialModel.Input with
                            Current = { Program.initialModel.Input.Current with Commands = Set [ "aim-right" ] } } }
            let _, hostEffects = Program.generatedHost.Update (Tick fixedDt) firing
            Expect.contains (audioFromHost hostEffects) (PlaySfx(SoundId "shot-fire", 0.55)) "a real fixed-step fire event reaches the production host recording boundary"
        }

        test "startup reaches the production launch host as clamped volume plus one title loop" {
            let _, effects = Program.interactiveHost.Init()
            Expect.equal
                (audioFromHost effects)
                [ FS.GG.Audio.Core.AudioEffect.SetMasterVolume 1.0
                  PlayMusic(TrackId "title-theme", true) ]
                "loaded settings and title context cross the same PlayAudio sink at Init"
        }

        test "context changes stop before one replacement loop and stable context does not restart" {
            let host = Program.generatedHost
            let started, startEffects = host.Update (StartRun 4242UL) Program.initialModel
            Expect.equal
                (audioFromHost startEffects |> musicOnly)
                [ StopMusic; PlayMusic(TrackId "floor-1-theme", true) ]
                "starting a run replaces title with exactly one floor loop"

            let bossId =
                started.Floor.Rooms
                |> Map.toSeq
                |> Seq.find (fun (_, room) -> room.RoomType = FloorGeneration.Boss)
                |> fst
            let boss, bossEffects = host.Update (EnterM5Room bossId) started
            Expect.equal
                (audioFromHost bossEffects |> musicOnly)
                [ StopMusic; PlayMusic(TrackId "boss-theme", true) ]
                "boss entry stops the old loop before requesting one boss loop"
            Expect.contains (audioFromHost bossEffects) (PlaySfx(SoundId "door-lock", 0.7)) "boss entry seals the room"
            Expect.contains (audioFromHost bossEffects) (PlaySfx(SoundId "boss-intro", 1.0)) "boss entry requests the intro cue"

            let damagedBoss, hitEffects = host.Update (DamageM5Boss 150.0) boss
            Expect.contains (audioFromHost hitEffects) (PlaySfx(SoundId "shot-hit", 0.7)) "boss damage requests the shot-hit cue"
            let phasedBoss, phaseEffects = host.Update (Tick fixedDt) damagedBoss
            Expect.contains (audioFromHost phaseEffects) (PlaySfx(SoundId "boss-phase", 0.9)) "the production phase transition requests its sting"
            let _, deathEffects = host.Update (DamageM5Boss 10000.0) phasedBoss
            Expect.contains (audioFromHost deathEffects) (PlaySfx(SoundId "boss-death", 1.0)) "boss death requests the specified cue"
            Expect.contains (audioFromHost deathEffects) (PlaySfx(SoundId "door-unlock", 0.7)) "boss clear unlocks the room"

            let _, stableEffects = host.Update NoOp boss
            Expect.isEmpty (audioFromHost stableEffects |> musicOnly) "a stable boss context never duplicates its loop"

            let descended, descendEffects = host.Update DescendFloor boss
            Expect.equal descended.FloorIndex 2 "production descent advanced the floor"
            Expect.equal
                (audioFromHost descendEffects |> musicOnly)
                [ StopMusic; PlayMusic(TrackId "floor-2-theme", true) ]
                "descent replaces the boss track with the new floor loop"
            Expect.contains (audioFromHost descendEffects) (PlaySfx(SoundId "floor-descend", 0.8)) "descent also requests its cue"

            let shell = Program.interactiveHost
            let shell0, _ = shell.Init()
            let shellRun, shellStartEffects = shell.Update (EvidenceCommands.StartFreshRun 4242UL) shell0
            Expect.equal (audioFromHost shellStartEffects |> musicOnly) [ StopMusic; PlayMusic(TrackId "floor-1-theme", true) ] "the actual menu-to-run route replaces title"
            let shellMenu, abandonEffects = shell.Update EvidenceCommands.AbandonRun shellRun
            Expect.equal shellMenu.Shell.Screen GameShell.MainMenu "abandon returns through the production shell"
            Expect.equal (audioFromHost abandonEffects |> musicOnly) [ StopMusic; PlayMusic(TrackId "title-theme", true) ] $"ending/abandoning a run restores exactly one title loop; effects={abandonEffects}"

            let resumable = { shell0 with Play={shell0.Play with RunActive=true} }
            let continued, continueEffects = shell.Update EvidenceCommands.ContinueRun resumable
            Expect.equal continued.Shell.Screen GameShell.Playing "continue enters the production play shell"
            Expect.equal (audioFromHost continueEffects |> musicOnly) [ StopMusic; PlayMusic(TrackId "floor-1-theme", true) ] "continue replaces the existing title loop with the restored model context"
        }

        test "production settings host clamps volume and mute/unmute reaches AudioEvidence.Requested" {
            let host = Program.interactiveHost
            let model0, _ = host.Init()
            let high, highEffects = host.Update (EvidenceCommands.ChangeVolume 4.0) model0
            Expect.contains (audioFromHost highEffects) (FS.GG.Audio.Core.AudioEffect.SetMasterVolume 1.0) "high volume clamps to one"
            let low, lowEffects = host.Update (EvidenceCommands.ChangeVolume -3.0) high
            Expect.contains (audioFromHost lowEffects) (FS.GG.Audio.Core.AudioEffect.SetMasterVolume 0.0) "low volume clamps to zero"
            let restored, _ = host.Update (EvidenceCommands.ChangeVolume 0.42) low
            let muted, muteEffects = host.Update (EvidenceCommands.ChangeMuted true) restored
            Expect.contains (audioFromHost muteEffects) (FS.GG.Audio.Core.AudioEffect.SetMasterVolume 0.0) "mute requests zero"
            let _, unmuteEffects = host.Update (EvidenceCommands.ChangeMuted false) muted
            Expect.contains (audioFromHost unmuteEffects) (FS.GG.Audio.Core.AudioEffect.SetMasterVolume 0.42) "unmute restores configured volume"

            let nanModel = Model.update (SetMasterVolume Double.NaN) Program.initialModel |> fst
            let nanRequest = AudioCues.forTransition (SetMasterVolume Double.NaN) Program.initialModel nanModel |> requested
            match nanRequest with
            | FS.GG.Audio.Core.AudioEffect.SetMasterVolume value :: _ ->
                Expect.isTrue (Double.IsFinite value && value >= 0.0 && value <= 1.0) "NaN normalizes to a safe finite boundary value"
            | other -> failtestf "expected a normalized master-volume request, got %A" other
        }

        test "pickup, room, boss and run-end transitions retain exact product-owned values" {
            let before = Program.initialModel
            let pickupCue kind =
                let slot : Entities.ShopSlot = { Id=71; Offer=Entities.ShopOffer.Consumable kind; Price=0; KeyLocked=false }
                let previous = { before with M5ShopSlots=[slot] }
                let next = { previous with M5ShopSlots=[{slot with Offer=Entities.ShopOffer.Empty}] }
                AudioCues.forTransition (InteractM5Shop slot.Id) previous next |> requested
            Expect.equal (pickupCue Entities.PickupKind.Coin3) [ PlaySfx(SoundId "pickup-coin",0.7) ] "coin acquisition has its exact cue"
            Expect.equal (pickupCue Entities.PickupKind.Key) [ PlaySfx(SoundId "pickup-key",0.7) ] "key acquisition has its exact cue"
            Expect.equal (pickupCue Entities.PickupKind.Bomb) [ PlaySfx(SoundId "pickup-bomb",0.7) ] "bomb acquisition has its exact cue"
            Expect.equal (pickupCue Entities.PickupKind.SoulHeart) [ PlaySfx(SoundId "pickup-heart",0.7) ] "heart acquisition has its exact cue"

            let item = Entities.baseItems.Head
            let itemSlot : Entities.ShopSlot = { Id=72; Offer=Entities.ShopOffer.Item item; Price=0; KeyLocked=false }
            let beforeItem = { before with M5ShopSlots=[itemSlot] }
            let afterItem = { beforeItem with M5ShopSlots=[{itemSlot with Offer=Entities.ShopOffer.Empty}] }
            Expect.equal (AudioCues.forTransition (InteractM5Shop itemSlot.Id) beforeItem afterItem |> requested) [ PlaySfx(SoundId "item-pickup",0.85) ] "item acquisition has its exact cue"

            let healedBoss = { before with PlayerHealth={before.PlayerHealth with RedHalfHearts=2}; M5Boss=Some (Entities.spawnBoss 91 Entities.BossKind.Gnawer before.PlayerPosition) }
            let afterBoss = { healedBoss with PlayerHealth={healedBoss.PlayerHealth with RedHalfHearts=4}; M5Boss=None }
            Expect.isFalse (AudioCues.forTransition (DamageM5Boss 10000.0) healedBoss afterBoss |> requested |> List.contains (PlaySfx(SoundId "pickup-heart",0.7))) "post-boss healing is not mislabeled as a pickup"
            let reset = { before with PlayerCurrency={before.PlayerCurrency with Coins=0;Keys=0;Bombs=0};PlayerHealth={before.PlayerHealth with RedHalfHearts=1} }
            Expect.isEmpty (AudioCues.forTransition (StartRun 99UL) reset before |> requested |> List.filter (function PlaySfx(SoundId id,_) when id.StartsWith("pickup-") -> true | _ -> false)) "run reset increases are not mislabeled as pickups"

            let ended, endEffects = Program.generatedHost.Update (CompleteRunStats(false, None)) { before with RunActive=true }
            Expect.isFalse ended.RunActive "production end-run transition completed"
            Expect.equal
                (audioFromHost endEffects |> musicOnly)
                [ StopMusic; PlayMusic(TrackId "game-over", true) ]
                "run end replaces the active loop without implementing the M9 screen"
        }
    ]
