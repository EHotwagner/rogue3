module Rogue3.Tests.M5EntitiesBossRoomsTests

open Expecto
open FS.GG.Game.Core
open FS.GG.UI.KeyboardInput
open Rogue3.Geometry
open Rogue3.Entities
open Rogue3.FloorGeneration
open Rogue3.Model

let private context floor player rng wall playerHit = { FloorIndex=floor;Player=player;DropRng=rng;WallHit=wall;PlayerHit=playerHit }
let private advance count ctx actor = [1..count] |> List.fold(fun a _ -> (stepEnemy ctx a).Actor) actor

[<Tests>]
let tests =
  testList "M5 entities bosses rooms" [
    testCase "roster definitions and floor scaling are exact" <| fun _ ->
      Expect.equal roster.Length 8 "all enemy kinds"
      let values=roster|>List.map definition
      Expect.sequenceEqual (values|>List.map(fun d->d.Threat)) [1;1;2;1;3;3;4;6] "threats"
      Expect.floatClose Accuracy.high (14.0*1.12) (scaledDefinition 1 EnemyKind.Charger).HitPoints "floor hp"
      Expect.floatClose Accuracy.high 189.0 (enemyBulletSpeed 1) "floor bullets"
      Expect.equal (threatBudget 6) 18 "budget"

    testCase "Charger telegraphs, locks dash, and wall hit recovers" <| fun _ ->
      let rng=Rng.ofSeed 1UL
      let actor=spawn 1 1 EnemyKind.Charger (vec2 100. 100.)
      let wind=(stepEnemy (context 1 (vec2 200. 100.) rng false false) actor).Actor
      match wind.State with | EnemyState.ChargerWindUp(dir,n) -> Expect.equal n (ticks 0.6) "wind ticks"; Expect.floatClose Accuracy.high 1. dir.Vx "locked aim" | x->failtestf "%A" x
      let dashed=advance (ticks 0.6) (context 1 (vec2 100. 300.) rng false false) wind
      match dashed.State with | EnemyState.ChargerDash(dir,n)->Expect.floatClose Accuracy.high 1. dir.Vx "direction stayed locked";Expect.equal n (ticks 0.8) "dash ticks" | x->failtestf "%A" x
      let stopped=(stepEnemy (context 1 (vec2 0. 0.) rng true false) dashed).Actor
      match stopped.State with | EnemyState.ChargerRecover n->Expect.equal n (ticks 0.7) "recover" | x->failtestf "%A" x

    testCase "all timed behavior parameters emit documented patterns" <| fun _ ->
      let rng=Rng.ofSeed 2UL
      let player=vec2 250. 200.
      let emitted kind count =
        let mutable cur=spawn 4 1 kind (vec2 200. 200.)
        let mutable actions=[]
        for _ in 1..count do
          let s=stepEnemy (context 4 player rng false false) cur
          cur<-s.Actor
          actions<-actions@s.Actions
        actions
      Expect.isTrue (emitted EnemyKind.Spitter (ticks 2.2) |> List.exists(function EnemyAction.FireAimed s -> s=enemyBulletSpeed 4 |_->false)) "spitter telegraph+shot"
      Expect.isTrue (emitted EnemyKind.Turret (ticks 2.3) |> List.exists(function EnemyAction.FireBurst(4,0.,_) -> true |_->false)) "turret burst"
      Expect.isTrue (emitted EnemyKind.Caster (ticks 4.3) |> List.exists(function EnemyAction.FireRing(6,_) -> true |_->false)) "caster ring"
      Expect.isTrue (emitted EnemyKind.Brute (ticks 0.8) |> List.exists(function EnemyAction.Shockwave(140.,n,2,160.) when n=ticks 0.25 -> true |_->false)) "brute pound"
      let fly=spawn 1 3 EnemyKind.Fly (vec2 300. 300.) |> advance (ticks 2.0+1) (context 1 player rng false false)
      match fly.State with | EnemyState.Dive(_,n)->Expect.equal n (ticks 0.5) "fly committed dive" | x->failtestf "%A" x

    testCase "grub split is floor bounded and cannot recursively split" <| fun _ ->
      let grub=spawn 2 1 EnemyKind.Grub (vec2 10. 10.)
      Expect.isEmpty (grubSplit 1 10 grub) "no floor-one split"
      let children=grubSplit 2 10 grub
      Expect.equal children.Length 2 "two maggots"
      Expect.isTrue (children|>List.forall(fun c->c.Kind=EnemyKind.Maggot && not c.SplitEligible)) "children cannot split"
      Expect.isEmpty (grubSplit 3 20 children.Head) "maggot does not split"

    testCase "boss phases and declarative pattern payloads are complete" <| fun _ ->
      Expect.equal (bossPhase BossKind.Gnawer 109.) 2 "gnawer p2"
      Expect.equal (bossPhase BossKind.Maw 420.) 1 "maw p1"
      Expect.equal (bossPhase BossKind.Maw 130.) 3 "maw p3"
      let maw=bossDefinition BossKind.Maw
      Expect.equal maw.Patterns.Length 3 "three declarative phases"
      Expect.equal maw.Patterns.[0].GapIndex (Some 5) "bullet wall gap"
      Expect.equal maw.Patterns.[2].Homing 1.0 "homing finale"
      Expect.isFalse (choirRevives [0;ticks 2.;ticks 3.9]) "within window"
      Expect.isTrue (choirRevives [0;ticks 2.;ticks 4.1]) "outside window revives"

    testCase "weighted tables use exact weights and deterministic DropRng continuation" <| fun _ ->
      Expect.equal (roomClearTable|>List.sumBy(fun x->x.Weight)) 100 "room table"
      Expect.equal (potTable|>List.sumBy(fun x->x.Weight)) 100 "pot table"
      Expect.equal (tintedRockTable|>List.sumBy(fun x->x.Weight)) 100 "tinted table"
      let a,ra=rollDrop roomClearTable (Rng.ofSeed 88UL)
      let b,rb=rollDrop roomClearTable (Rng.ofSeed 88UL)
      Expect.equal (a,ra) (b,rb) "repeatable and threaded"

    testCase "room seals and only final death opens and rolls once" <| fun _ ->
      let room={IsBoss=false;Cleared=false;Doors=[Rogue3.Entities.DoorState.Open;Rogue3.Entities.DoorState.Open];LiveEnemyIds=Set.empty;Drop=None;Reward=None;Trapdoor=false}|>enterRoom [1;2]
      Expect.isTrue (room.Doors|>List.forall((=)Rogue3.Entities.DoorState.LockedClear)) "sealed"
      let first,r1=enemyDied 1 (Rng.ofSeed 7UL) room
      Expect.isFalse first.Cleared "not early"
      let final,r2=enemyDied 2 r1 first
      Expect.isTrue final.Cleared "same transition clear"
      Expect.isTrue (final.Doors|>List.forall((=)Rogue3.Entities.DoorState.Open)) "open"
      let repeated,r3=enemyDied 2 r2 final
      Expect.equal r3 r2 "no second drop draw"
      Expect.equal repeated.Drop final.Drop "drop stable"
      let boss={final with IsBoss=true;Reward=None;Trapdoor=false}|>bossCleared None
      Expect.isTrue boss.Trapdoor "boss trapdoor"

    testCase "obstacle policy covers blocking fly-over hazard and claimed destruction" <| fun _ ->
      Expect.isTrue (blocksMovement MovementClass.Grounded ObstacleKind.Pit) "ground blocked"
      Expect.isFalse (blocksMovement MovementClass.Flying ObstacleKind.Pit) "fly over"
      Expect.isFalse (blocksShots ObstacleKind.Pit) "shots over pit"
      Expect.equal (spikeDamage ObstacleKind.Spikes) 1 "spike hurt"
      let remains,drop,rng=destroyObstacle 1 (Rng.ofSeed 3UL) (obstacle ObstacleKind.Pot 1)
      Expect.isNone remains "pot destroyed"; Expect.isSome drop "seeded pot drop"
      let inert,none,rng2=destroyObstacle 99 rng (obstacle ObstacleKind.Rock 2)
      Expect.isSome inert "rock indestructible";Expect.isNone none "no roll";Expect.equal rng2 rng "rng untouched"

    testCase "layout item pool is deterministic and dupe free across fixtures" <| fun _ ->
      let drawAll seed =
        let mutable rng=Rng.ofSeed seed
        let mutable pool=itemPool []
        let mutable ids=[]
        for tag in ["treasure";"boss";"shop";"boss";"treasure"] do
          let item,next,p=drawItem tag rng pool
          rng<-next
          pool<-p
          ids<-(item|>Option.map(fun i->i.Id))::ids
        List.rev ids,pool
      let a,p1=drawAll 42UL
      let b,p2=drawAll 42UL
      Expect.equal a b "layout fixed"
      let ids=a|>List.choose id
      Expect.equal (ids|>List.distinct|>List.length) ids.Length "no duplicate"
      Expect.equal p1.Placed p2.Placed "pool state"

    testCase "shop has three deterministic slots prices locks and no restock" <| fun _ ->
      let slotsA,rngA,poolA=generateShop (Rng.ofSeed 99UL) (itemPool [])
      let slotsB,rngB,poolB=generateShop (Rng.ofSeed 99UL) (itemPool [])
      Expect.equal (slotsA,rngA,poolA) (slotsB,rngB,poolB) "fixed shop"
      Expect.equal slotsA.Length 3 "three slots"
      let slot={Id=0;Offer=ShopOffer.Consumable PickupKind.Key;Price=5;KeyLocked=false}
      let c,k,left,ok=purchase 5 0 slot
      Expect.equal (c,k,ok) (0,0,true) "purchased"
      Expect.equal left.Offer ShopOffer.Empty "emptied"
      let c2,k2,left2,ok2=purchase 4 0 slot
      Expect.equal (c2,k2,ok2,left2) (4,0,false,slot) "rejected unchanged"
      let _,_,stillEmpty,ok3=purchase 99 99 left
      Expect.isFalse ok3 "empty never restocks";Expect.equal stillEmpty left "stable empty"

    testCase "floor generation places deterministic treasure boss and shop fixtures across run pool" <| fun _ ->
      let first=generateWithPool 0x515151UL 2 (itemPool [])
      let repeat=generateWithPool 0x515151UL 2 (itemPool [])
      Expect.equal first repeat "same layout stream gives byte-equal fixtures"
      let second=generateWithPool 0x515151UL 3 first.ItemPool
      let itemIds (result:GenerationResult) =
        result.Floor.Rooms
        |> Map.toList
        |> List.collect(fun (_,room)->room.Fixtures)
        |> List.collect(function ItemPedestal item|BossReward item->[item.Id] | ShopStock slots->slots|>List.choose(function {Offer=ShopOffer.Item item}->Some item.Id |_->None) | Trapdoor|ConsumableReward _->[])
      let ids=itemIds first @ itemIds second
      Expect.equal (List.distinct ids).Length ids.Length "run-wide dupe-free"
      Expect.isTrue (first.Floor.Rooms|>Map.exists(fun _ room->room.RoomType=Treasure && room.Fixtures|>List.exists(function ItemPedestal _->true|_->false))) "treasure pedestal"
      Expect.isTrue (first.Floor.Rooms|>Map.exists(fun _ room->room.RoomType=Boss && room.Fixtures|>List.exists(function BossReward _->true|_->false))) "boss reward preselected"
      Expect.isTrue (first.Floor.Rooms|>Map.exists(fun _ room->room.RoomType=Shop && room.Fixtures|>List.exists(function ShopStock slots when slots.Length=3->true|_->false))) "three-slot shop"

    testCase "exhausted item tags generate deterministic consumable fallback fixtures" <| fun _ ->
      let exhausted={itemPool [] with Placed=baseItems|>List.map _.Id|>Set.ofList}
      let floor=generateWithPool 0x999UL 2 exhausted
      let rewards=floor.Floor.Rooms|>Map.toList|>List.collect(fun(_,room)->room.Fixtures)
      Expect.isTrue (rewards|>List.exists(function ConsumableReward _->true|_->false)) "fallback present"
      Expect.isFalse (rewards|>List.exists(function ItemPedestal _|BossReward _->true|_->false)) "no duplicate item"

    testCase "production update enters combat, kills roster, clears doors, and rolls once" <| fun _ ->
      let combatId=initialModel.Floor.Rooms|>Map.toList|>List.find(fun(_,room)->room.RoomType=Combat)|>fst
      let entered=update (EnterM5Room combatId) initialModel|>fst
      Expect.isGreaterThan entered.M5Enemies.Length 0 "anchors instantiated"
      Expect.equal (entered.M5Obstacles|>List.map _.Kind|>Set.ofList) (Set.ofList [ObstacleKind.Rock;ObstacleKind.TintedRock;ObstacleKind.Pot;ObstacleKind.Spikes;ObstacleKind.Pit]) "generated room instantiates every obstacle policy"
      Expect.isTrue (entered.M5Room.Doors|>List.forall((=)Rogue3.Entities.DoorState.LockedClear)) "production seal"
      let before=entered.DropRng
      let cleared=entered.M5Enemies|>List.map _.Id|>List.fold(fun model id->update (DamageM5Enemy(id,9999.)) model|>fst) entered
      Expect.isTrue cleared.M5Room.Cleared "production clear"
      Expect.isTrue (cleared.M5Room.Doors|>List.forall((=)Rogue3.Entities.DoorState.Open)) "production open"
      Expect.notEqual cleared.DropRng before "one production drop draw"

    testCase "production boss runs phase-three pattern and defeat grants reward trapdoor" <| fun _ ->
      let generated=generate 0xABCDUL 3
      let model={initialModel with FloorIndex=3;Floor=generated.Floor;LayoutRng=generated.LayoutRng;M5ItemPool=generated.ItemPool}
      let bossId=model.Floor.Rooms|>Map.toList|>List.find(fun(_,room)->room.RoomType=Boss)|>fst
      let entered=update (EnterM5Room bossId) model|>fst
      let boss=entered.M5Boss|>Option.get
      let phase3={boss with HitPoints=100.;PatternTicksLeft=1}
      let fired=update (Tick fixedDt) {entered with M5Boss=Some phase3}|>fst
      Expect.equal fired.M5Boss.Value.Phase 3 "production phase"
      Expect.equal fired.M5BossPatternEmissions 1 "declarative pattern emitted"
      Expect.isGreaterThan fired.EnemyBullets.Length 0 "pattern materialized"
      let firstBullet=fired.EnemyBullets.Head
      Expect.isGreaterThan (magnitude firstBullet.Velocity) 0. "pattern has trajectory"
      let advanced=update (Tick fixedDt) fired|>fst
      let moved=advanced.EnemyBullets|>List.find(fun bullet->bullet.Id=firstBullet.Id)
      Expect.notEqual moved.Position firstBullet.Position "production projectile advances"
      let defeated=update (DamageM5Boss 9999.) fired|>fst
      Expect.isNone defeated.M5Boss "boss removed"
      Expect.isTrue defeated.M5Room.Trapdoor "trapdoor"
      Expect.isSome defeated.M5Room.Reward "preselected reward"

    testCase "production shop purchase empties slot and never restocks" <| fun _ ->
      let generated=generate 0x123456UL 2
      let model={initialModel with FloorIndex=2;Floor=generated.Floor;LayoutRng=generated.LayoutRng;M5ItemPool=generated.ItemPool;PlayerCurrency={initialModel.PlayerCurrency with Coins=99;Keys=99}}
      let shopId=model.Floor.Rooms|>Map.toList|>List.find(fun(_,room)->room.RoomType=Shop)|>fst
      let entered=update (EnterM5Room shopId) model|>fst
      let slot=entered.M5ShopSlots.Head
      let bought=update (InteractM5Shop slot.Id) entered|>fst
      Expect.equal bought.M5ShopSlots.Head.Offer ShopOffer.Empty "emptied via update"
      let again=update (InteractM5Shop slot.Id) bought|>fst
      Expect.equal again.M5ShopSlots bought.M5ShopSlots "no restock"

    testCase "production obstacles block ground not shots hurt on spikes and destroy once" <| fun _ ->
      let pit=obstacle ObstacleKind.Pit 701|>obstacleAt(vec2 120. 100.)
      let pitRect:Rect={X=100.;Y=80.;Width=40.;Height=40.}
      let input={initialModel.Input.Current with Keys=Set.singleton(ViewerKeyboard.toKeyId(Letter 'D'));MousePosition=Some(vec2 400. 100.);MousePrimaryDown=true}
      let blocked={initialModel with PlayerPosition=vec2 86. 100.;PlayerVelocity=vec2 240. 0.;M5Obstacles=[pit];Obstacles=[pitRect]}|>update(InputChanged input)|>fst|>update(Tick fixedDt)|>fst
      Expect.floatClose Accuracy.high 87. blocked.PlayerPosition.Vx "grounded player stops at pit"
      Expect.isGreaterThan blocked.ShotSpawns.Length 0 "shot passes through pit"
      let spike=obstacle ObstacleKind.Spikes 702|>obstacleAt initialModel.PlayerPosition
      let hurt=update(Tick fixedDt) {initialModel with M5Obstacles=[spike]}|>fst
      Expect.equal hurt.PlayerHealth.RedHalfHearts 5 "spike damages grounded player"
      let pot=obstacle ObstacleKind.Pot 703|>obstacleAt(vec2 200. 200.)
      let before={initialModel with M5Obstacles=[pot]}
      let broken=update(DamageM5Obstacle(703,1)) before|>fst
      let repeated=update(DamageM5Obstacle(703,1)) broken|>fst
      Expect.isEmpty broken.M5Obstacles "pot destroyed"
      Expect.equal repeated.DropRng broken.DropRng "destroyed obstacle cannot draw twice"
      Expect.isLessThanOrEqual broken.M5ObstacleDrops.Length 1 "at most one drop"
      let rock=obstacle ObstacleKind.Rock 704
      let inert=update(DamageM5Obstacle(704,999)) {initialModel with M5Obstacles=[rock]}|>fst
      Expect.hasLength inert.M5Obstacles 1 "rock remains indestructible"
      let blockingRock=rock|>obstacleAt(vec2 108. 100.)
      let grounded=spawn 1 705 EnemyKind.Maggot (vec2 80. 100.)
      let flying=spawn 1 706 EnemyKind.Fly (vec2 80. 100.)
      let actorModel actor={initialModel with PlayerPosition=vec2 300. 100.;M5Enemies=[actor];Enemies=[{Id=actor.Id;Position=actor.Position;Velocity=zero;Radius=(definition actor.Kind).Radius;HitPoints=actor.HitPoints;ContactDamage=0;LastContactTick=None;HitFlashTicks=0}];M5Obstacles=[blockingRock]}
      let groundedStep=update(Tick fixedDt) (actorModel grounded)|>fst
      Expect.equal groundedStep.M5Enemies.Head.Position grounded.Position "grounded enemy respects rock"
      let pitForFly=obstacle ObstacleKind.Pit 707|>obstacleAt(vec2 110. 100.)
      let flyingStep=update(Tick fixedDt) {(actorModel flying) with M5Obstacles=[pitForFly]}|>fst
      Expect.notEqual flyingStep.M5Enemies.Head.Position flying.Position "flying enemy crosses pit"
      let enemyBullet={Id=708;Position=vec2 80. 100.;Velocity=vec2 3600. 0.;Radius=3.;Damage=1;Homing=0.;AgeTicks=0}
      let stopped=update(Tick fixedDt) {initialModel with M5Obstacles=[blockingRock];EnemyBullets=[enemyBullet]}|>fst
      Expect.isFalse (stopped.EnemyBullets|>List.exists(fun bullet->bullet.Id=708)) "enemy bullet hits shot-blocking rock"

    testCase "split children join the live set before room clear" <| fun _ ->
      let grub=spawn 2 800 EnemyKind.Grub (vec2 300. 300.)
      let room={IsBoss=false;Cleared=false;Doors=[Rogue3.Entities.DoorState.LockedClear];LiveEnemyIds=Set.singleton grub.Id;Drop=None;Reward=None;Trapdoor=false}
      let model={initialModel with FloorIndex=2;M5Enemies=[grub];Enemies=[{Id=grub.Id;Position=grub.Position;Velocity=zero;Radius=(definition grub.Kind).Radius;HitPoints=grub.HitPoints;ContactDamage=1;LastContactTick=None;HitFlashTicks=0}];M5Room=room}
      let split=update(DamageM5Enemy(grub.Id,9999.)) model|>fst
      Expect.isFalse split.M5Room.Cleared "children prevent premature clear"
      Expect.equal split.M5Enemies.Length 2 "two live children"
      Expect.equal split.M5Room.LiveEnemyIds.Count 2 "children are in room accounting"

    testCase "production choir deaths drive deterministic outside-window revival" <| fun _ ->
      let generated=generate 0xBEEFUL 2
      let model={initialModel with FloorIndex=2;Floor=generated.Floor;LayoutRng=generated.LayoutRng;M5ItemPool=generated.ItemPool}
      let bossId=model.Floor.Rooms|>Map.toList|>List.find(fun(_,room)->room.RoomType=Boss)|>fst
      let entered=update(EnterM5Room bossId) model|>fst
      let members=entered.M5ChoirMemberIds|>Set.toList
      Expect.equal members.Length 3 "three linked caster actors"
      let blockedBossDamage=update(DamageM5Boss 9999.) entered|>fst
      Expect.isSome blockedBossDamage.M5Boss "linked casters protect the Choir"
      let success=
        List.zip members [0;ticks 1.;ticks 3.9]
        |> List.fold(fun current (id,tick)->update(DamageM5Enemy(id,9999.)) {current with SimStepCount=tick}|>fst) entered
      Expect.isNone success.M5Boss "within-window caster deaths defeat Choir"
      Expect.isTrue success.M5Room.Trapdoor "successful window clears boss room"
      let killed=
        List.zip members [0;ticks 2.;ticks 4.1]
        |> List.fold(fun current (id,tick)->update(DamageM5Enemy(id,9999.)) {current with SimStepCount=tick}|>fst) entered
      Expect.isEmpty killed.M5ChoirMemberIds "actual actors died"
      let revived=update(Tick fixedDt) killed|>fst
      Expect.equal revived.M5ChoirMemberIds.Count 3 "three linked casters respawn"
      Expect.isEmpty revived.M5Boss.Value.ChoirKillTicks "window reset"
  ]
