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
      Expect.isGreaterThan entered.Enemies.Length 0 "anchors instantiated"
      Expect.equal (entered.Obstacles|>List.map _.Kind|>Set.ofList) (Set.ofList [ObstacleKind.Rock;ObstacleKind.TintedRock;ObstacleKind.Pot;ObstacleKind.Spikes;ObstacleKind.Pit]) "generated room instantiates every obstacle policy"
      Expect.isTrue (entered.Room.Doors|>List.forall((=)Rogue3.Entities.DoorState.LockedClear)) "production seal"
      let before=entered.DropRng
      let cleared=entered.Enemies|>List.map _.Id|>List.fold(fun model id->update (DamageM5Enemy(id,9999.)) model|>fst) entered
      Expect.isTrue cleared.Room.Cleared "production clear"
      Expect.isTrue (cleared.Room.Doors|>List.forall((=)Rogue3.Entities.DoorState.Open)) "production open"
      Expect.notEqual cleared.DropRng before "one production drop draw"

    testCase "production boss runs phase-three pattern and defeat grants reward trapdoor" <| fun _ ->
      let generated=generate 0xABCDUL 3
      let model={initialModel with FloorIndex=3;Floor=generated.Floor;LayoutRng=generated.LayoutRng;ItemPool=generated.ItemPool}
      let bossId=model.Floor.Rooms|>Map.toList|>List.find(fun(_,room)->room.RoomType=Boss)|>fst
      let entered=update (EnterM5Room bossId) model|>fst
      let boss=entered.Boss|>Option.get
      let phase3={boss with HitPoints=100.;PatternTicksLeft=1}
      let fired=update (Tick fixedDt) {entered with Boss=Some phase3}|>fst
      Expect.equal fired.Boss.Value.Phase 3 "production phase"
      Expect.equal fired.BossPatternEmissions 1 "declarative pattern emitted"
      Expect.isGreaterThan fired.EnemyBullets.Length 0 "pattern materialized"
      let firstBullet=fired.EnemyBullets.Head
      Expect.isGreaterThan (magnitude firstBullet.Velocity) 0. "pattern has trajectory"
      let advanced=update (Tick fixedDt) fired|>fst
      let moved=advanced.EnemyBullets|>List.find(fun bullet->bullet.Id=firstBullet.Id)
      Expect.notEqual moved.Position firstBullet.Position "production projectile advances"
      let defeated=update (DamageM5Boss 9999.) fired|>fst
      Expect.isNone defeated.Boss "boss removed"
      Expect.isTrue defeated.Room.Trapdoor "trapdoor"
      Expect.isSome defeated.Room.Reward "preselected reward"

    testCase "production shop purchase empties slot and never restocks" <| fun _ ->
      let generated=generate 0x123456UL 2
      let model={initialModel with FloorIndex=2;Floor=generated.Floor;LayoutRng=generated.LayoutRng;ItemPool=generated.ItemPool;PlayerCurrency={initialModel.PlayerCurrency with Coins=99;Keys=99}}
      let shopId=model.Floor.Rooms|>Map.toList|>List.find(fun(_,room)->room.RoomType=Shop)|>fst
      let entered=update (EnterM5Room shopId) model|>fst
      let slot=entered.ShopSlots.Head
      let bought=update (InteractM5Shop slot.Id) entered|>fst
      Expect.equal bought.ShopSlots.Head.Offer ShopOffer.Empty "emptied via update"
      let again=update (InteractM5Shop slot.Id) bought|>fst
      Expect.equal again.ShopSlots bought.ShopSlots "no restock"

    testCase "production obstacles block ground not shots hurt on spikes and destroy once" <| fun _ ->
      // Board item #20: the blocking rect is DERIVED from this obstacle by `blockingObstacleRects`,
      // so the test no longer hands the model a hand-written twin that could disagree with it.
      let pit=obstacle ObstacleKind.Pit 701|>obstacleAt(vec2 120. 100.)
      let input={initialModel.Input.Current with Keys=Set.singleton(ViewerKeyboard.toKeyId(Letter 'D'));MousePosition=Some(vec2 400. 100.);MousePrimaryDown=true}
      let blocked={initialModel with PlayerPosition=vec2 86. 100.;PlayerVelocity=vec2 240. 0.;Obstacles=[pit]}|>update(InputChanged input)|>fst|>update(Tick fixedDt)|>fst
      Expect.floatClose Accuracy.high 87. blocked.PlayerPosition.Vx "grounded player stops at pit"
      Expect.isGreaterThan blocked.ShotSpawns.Length 0 "shot passes through pit"
      let spike=obstacle ObstacleKind.Spikes 702|>obstacleAt initialModel.PlayerPosition
      let hurt=update(Tick fixedDt) {initialModel with Obstacles=[spike]}|>fst
      Expect.equal hurt.PlayerHealth.RedHalfHearts 5 "spike damages grounded player"
      let pot=obstacle ObstacleKind.Pot 703|>obstacleAt(vec2 200. 200.)
      let before={initialModel with Obstacles=[pot]}
      let broken=update(DamageM5Obstacle(703,1)) before|>fst
      let repeated=update(DamageM5Obstacle(703,1)) broken|>fst
      Expect.isEmpty broken.Obstacles "pot destroyed"
      Expect.equal repeated.DropRng broken.DropRng "destroyed obstacle cannot draw twice"
      Expect.isLessThanOrEqual broken.ObstacleDrops.Length 1 "at most one drop"
      let rock=obstacle ObstacleKind.Rock 704
      let inert=update(DamageM5Obstacle(704,999)) {initialModel with Obstacles=[rock]}|>fst
      Expect.hasLength inert.Obstacles 1 "rock remains indestructible"
      let blockingRock=rock|>obstacleAt(vec2 108. 100.)
      let grounded=spawn 1 705 EnemyKind.Maggot (vec2 80. 100.)
      let flying=spawn 1 706 EnemyKind.Fly (vec2 80. 100.)
      let actorModel actor={initialModel with PlayerPosition=vec2 300. 100.;Enemies=[actor];Obstacles=[blockingRock]}
      let groundedStep=update(Tick fixedDt) (actorModel grounded)|>fst
      Expect.equal groundedStep.Enemies.Head.Position grounded.Position "grounded enemy respects rock"
      let pitForFly=obstacle ObstacleKind.Pit 707|>obstacleAt(vec2 110. 100.)
      let flyingStep=update(Tick fixedDt) {(actorModel flying) with Obstacles=[pitForFly]}|>fst
      Expect.notEqual flyingStep.Enemies.Head.Position flying.Position "flying enemy crosses pit"
      let enemyBullet={Id=708;Position=vec2 80. 100.;Velocity=vec2 3600. 0.;Radius=3.;Damage=1;Homing=0.;AgeTicks=0}
      let stopped=update(Tick fixedDt) {initialModel with Obstacles=[blockingRock];EnemyBullets=[enemyBullet]}|>fst
      Expect.isFalse (stopped.EnemyBullets|>List.exists(fun bullet->bullet.Id=708)) "enemy bullet hits shot-blocking rock"

    testCase "split children join the live set before room clear" <| fun _ ->
      let grub=spawn 2 800 EnemyKind.Grub (vec2 300. 300.)
      let room={IsBoss=false;Cleared=false;Doors=[Rogue3.Entities.DoorState.LockedClear];LiveEnemyIds=Set.singleton grub.Id;Drop=None;Reward=None;Trapdoor=false}
      let model={initialModel with FloorIndex=2;Enemies=[grub];Room=room}
      let split=update(DamageM5Enemy(grub.Id,9999.)) model|>fst
      Expect.isFalse split.Room.Cleared "children prevent premature clear"
      Expect.equal split.Enemies.Length 2 "two live children"
      Expect.equal split.Room.LiveEnemyIds.Count 2 "children are in room accounting"

    testCase "production choir deaths drive deterministic outside-window revival" <| fun _ ->
      let generated=generate 0xBEEFUL 2
      let model={initialModel with FloorIndex=2;Floor=generated.Floor;LayoutRng=generated.LayoutRng;ItemPool=generated.ItemPool}
      let bossId=model.Floor.Rooms|>Map.toList|>List.find(fun(_,room)->room.RoomType=Boss)|>fst
      let entered=update(EnterM5Room bossId) model|>fst
      let members=entered.ChoirMemberIds|>Set.toList
      Expect.equal members.Length 3 "three linked caster actors"
      let blockedBossDamage=update(DamageM5Boss 9999.) entered|>fst
      Expect.isSome blockedBossDamage.Boss "linked casters protect the Choir"
      let success=
        List.zip members [0;ticks 1.;ticks 3.9]
        |> List.fold(fun current (id,tick)->update(DamageM5Enemy(id,9999.)) {current with SimStepCount=tick}|>fst) entered
      Expect.isNone success.Boss "within-window caster deaths defeat Choir"
      Expect.isTrue success.Room.Trapdoor "successful window clears boss room"
      let killed=
        List.zip members [0;ticks 2.;ticks 4.1]
        |> List.fold(fun current (id,tick)->update(DamageM5Enemy(id,9999.)) {current with SimStepCount=tick}|>fst) entered
      Expect.isEmpty killed.ChoirMemberIds "actual actors died"
      let revived=update(Tick fixedDt) killed|>fst
      Expect.equal revived.ChoirMemberIds.Count 3 "three linked casters respawn"
      Expect.isEmpty revived.Boss.Value.ChoirKillTicks "window reset"

    // ------------------------------------------------------------------------------------------
    // Board item #20 — the product carries ONE generation of world state.
    // ------------------------------------------------------------------------------------------

    testCase "the pre-M5 world-state generation is absent from the model surface" <| fun _ ->
      // Reflection rather than a source grep: a grep is satisfied by deleting a line and can be
      // fooled by a comment, whereas this reads the shape the product actually compiles. `Model`
      // carried `Enemies`/`Obstacles`/`ShopSlots` beside `M5Enemies`/`M5Obstacles`/`M5ShopSlots`
      // with no rule about which was authoritative, and a reader picking the wrong one is what
      // shipped the §14.21 dead-actor defect.
      //
      // Board item #60 retired the `M5` prefix, so the surviving generation now OCCUPIES the three
      // names the removed one used to. That makes the original by-NAME absence check unsatisfiable
      // and, worse, silently self-contradictory — both lists became `Enemies`/`Obstacles`/
      // `ShopSlots`. The property #20 exists to protect was never really about the names: it is
      // that exactly ONE field describes each concern, and that the one that survived is the
      // ENTITIES-typed generation. That is what is asserted here, by TYPE, which no rename can
      // satisfy vacuously.
      let fieldsOfType (target: System.Type) =
        Reflection.FSharpType.GetRecordFields typeof<Model>
        |> Array.filter (fun field -> field.PropertyType = target)
        |> Array.map _.Name
      for name, generation in
        [ "Enemies", typeof<Rogue3.Entities.EnemyActor list>
          "Obstacles", typeof<Rogue3.Entities.Obstacle list>
          "ShopSlots", typeof<Rogue3.Entities.ShopSlot list> ] do
        // `generation.Name` renders a generic as `FSharpList\`1` — the element type, which is the
        // entire point, is missing from the message. `ToString()` keeps it.
        Expect.equal
          (fieldsOfType generation)
          [| name |]
          $"exactly one Model field carries {generation} and it is {name} — a second field OF THE SAME TYPE is the two-generation defect returning. A twin of a DIFFERENT type is caught by the Rect-cache assertions below, not by this one"
      // The pre-M5 records themselves were deleted with the fields, and stay deleted. A revived
      // `Model.Enemy` is how a second generation would come back while the count check above still
      // reads one, because the reborn field would be typed on the reborn record.
      let modelTypes =
        typeof<Model>.Assembly.GetTypes()
        |> Array.choose (fun t -> if isNull t.FullName then None else Some t.FullName)
        |> Set.ofArray
      for removed in [ "Rogue3.Model+Enemy"; "Rogue3.Model+ShopSlot"; "Rogue3.Model+ShopCost" ] do
        Expect.isFalse (Set.contains removed modelTypes) $"the pre-M5 record {removed} stays deleted"
      // The exact shape #20 deleted was `Obstacles: Rect list` — a free-floating collider cache
      // beside the typed obstacle list. The by-type count above cannot see it coming back, because
      // a `Rect list` is not an `Obstacle list`; only this does. `blockingObstacleRects` derives
      // that set on demand and stores it nowhere, which is the property being protected.
      // Both rectangle types are checked: `blockingObstacleRects` returns `SimRect list`, which is
      // the shape the deleted cache held, and `FS.GG.UI.Scene.Rect` is the other `Rect` in scope
      // across this product — naming only one of them would leave the obvious near-miss open.
      Expect.equal
        (fieldsOfType typeof<SimRect list>)
        [||]
        "no Model field caches a SimRect list — the derived collider set is computed on demand by blockingObstacleRects, never stored, so it cannot go stale"
      Expect.equal
        (fieldsOfType typeof<FS.GG.UI.Scene.Rect list>)
        [||]
        "and no Model field caches a Scene.Rect list either"
      let msgCases =
        Reflection.FSharpType.GetUnionCases typeof<Msg> |> Array.map _.Name |> Set.ofArray
      Expect.isFalse (Set.contains "InteractShop" msgCases) "the second shop message is gone"
      Expect.isTrue (Set.contains "InteractM5Shop" msgCases) "the one that was wired survives"
      // The three facts the removed enemy record carried moved onto the actor rather than being
      // deleted with it; the two that are functions of Kind did NOT move.
      let actorFields =
        Reflection.FSharpType.GetRecordFields typeof<EnemyActor> |> Array.map _.Name |> Set.ofArray
      for moved in [ "Velocity"; "LastContactTick"; "HitFlashTicks" ] do
        Expect.isTrue (Set.contains moved actorFields) $"EnemyActor absorbed {moved} from the removed record"
      for derived in [ "Radius"; "ContactDamage" ] do
        Expect.isFalse (Set.contains derived actorFields) $"{derived} is read from `definition`, never stored per instance"

    // ------------------------------------------------------------------------------------------
    // Board item #60 — the model surface carries no milestone numbers, and instrumentation is
    // separated from gameplay state.
    // ------------------------------------------------------------------------------------------

    testCase "the model surface carries no milestone prefix and no loose cost counter" <| fun _ ->
      // Reflection for the same reason #20's guard above uses it: this reads the shape the product
      // compiles, so a re-introduced `M5Foo` field cannot pass by living in a file nothing greps.
      let modelFields =
        Reflection.FSharpType.GetRecordFields typeof<Model> |> Array.map _.Name
      // Every leading digit is skipped, not just one. An earlier version required
      // `Char.IsUpper name.[2]`, which silently passed `M13ObstacleDrops` because `name.[2]` is '3'
      // — and this project's milestones already run to M14, so the two-digit case is the one that
      // would actually occur.
      let carriesMilestonePrefix (name: string) =
        if name.Length < 3 || name.[0] <> 'M' || not (System.Char.IsDigit name.[1]) then false
        else
          let rest = name.Substring 1
          let digits = rest |> Seq.takeWhile System.Char.IsDigit |> Seq.length
          digits < rest.Length && System.Char.IsUpper rest.[digits]
      let prefixed = modelFields |> Array.filter carriesMilestonePrefix
      Expect.isEmpty prefixed "no Model field carries a milestone-number prefix; the milestone is not a property of the state"
      // The detector itself is guarded, because a prefix check that stopped detecting would pass
      // silently forever. The two-digit case is the one an earlier version of this test missed.
      for positive in [ "M5Enemies"; "M6Particles"; "M13ObstacleDrops"; "M14Items" ] do
        Expect.isTrue (carriesMilestonePrefix positive) $"{positive} is a milestone-prefixed name and must be detected as one"
      for negative in [ "Enemies"; "Model"; "M"; "M5"; "Multishot"; "MaxAgeTicks"; "M5x" ] do
        Expect.isFalse (carriesMilestonePrefix negative) $"{negative} is not a milestone-prefixed name"
      // The seven cost counters live in the sub-record and NOT loose on Model. Both halves matter:
      // the absence alone is satisfied by deleting them, which would silently unbind every
      // performance cost driver.
      let counters =
        [ "TotalShotSpawns"; "TotalWallQueries"; "TotalHomingQueries"; "TotalCombatCandidates"
          "TotalSecretRevealCandidates"; "TotalDoorSensorQueries"; "TotalFloorPickupCandidates" ]
      let instrumentationFields =
        Reflection.FSharpType.GetRecordFields typeof<InstrumentationCounters>
        |> Array.map _.Name
        |> Set.ofArray
      for counter in counters do
        Expect.isFalse
          (Array.contains counter modelFields)
          $"{counter} is instrumentation, not gameplay state, so it does not sit loose on Model"
        Expect.isTrue
          (Set.contains counter instrumentationFields)
          $"{counter} still exists, on InstrumentationCounters — its cost driver reads it"
      Expect.equal
        instrumentationFields
        (Set.ofList counters)
        "InstrumentationCounters holds exactly the seven counters and nothing else has drifted into it"
      Expect.isTrue
        (Array.contains "Instrumentation" modelFields)
        "Model reaches the counters through exactly one field"
      // Every counter starts at zero, so a fresh run's deltas are the run's own work. `initialModel`
      // building this by hand is how one of the seven could silently start non-zero.
      Expect.equal
        initialModel.Instrumentation
        zeroInstrumentation
        "the initial model starts every counter at zero"

    testCase "a shot that kills an enemy resolves that death exactly once through the drop cleanup" <| fun _ ->
      // §14.21 closure. The defect was that `resolveShotCombat` rebuilt the legacy `Enemies` list
      // from a live filter, so a killed actor vanished from the list cleanup read while surviving
      // in `Enemies` — and a dead actor kept taking turns. With one list there is nothing to
      // vanish from: the actor stays at zero hit points until `stepM5Entities`'s cleanup removes
      // it, and that cleanup is the ONLY thing that rolls the drop, credits the kill and clears
      // the room. Restore the "drop zero-hit-point actors in shot resolution" behaviour and every
      // assertion below the first fails, because the cleanup never sees the corpse.
      let victim={spawn 1 4242 EnemyKind.Grub (vec2 400. 300.) with HitPoints=1.0;SplitEligible=false}
      let room={IsBoss=false;Cleared=false;Doors=[Rogue3.Entities.DoorState.LockedClear]
                LiveEnemyIds=Set.singleton victim.Id;Drop=None;Reward=None;Trapdoor=false}
      let stats={basePlayerStats with Pierce=0}
      let shot=spawnShots 0 1 (vec2 400. 300.) zero (vec2 1. 0.) stats|>List.exactlyOne
      let before={initialModel with Enemies=[victim];Room=room;ShotSpawns=[shot]}
      let after=update(Tick fixedDt) before|>fst
      Expect.isFalse (after.Enemies|>List.exists(fun actor->actor.Id=victim.Id)) "no representation of the killed actor survives the step"
      Expect.equal (after.RunStats.KillsByType|>Map.tryFind EnemyKind.Grub) (Some 1) "the cleanup credits exactly one typed kill"
      Expect.isFalse (Set.contains victim.Id after.Room.LiveEnemyIds) "and removes it from the room's live set"
      Expect.isTrue after.Room.Cleared "so the last death clears the room"
      Expect.notEqual after.DropRng before.DropRng "the death rolled its drop exactly once, advancing the drop stream"

    testCase "an actor's radius and contact damage come from its kind, and combat uses them" <| fun _ ->
      // The central move of board item #20 was replacing two STORED per-instance fields with
      // functions of `Kind`. A critic's mutation run found the radius half completely unguarded:
      // `actorRadius` could be pinned to 1.0, 12.0 or 64.0 and the whole suite stayed green -- 64.0
      // being the exact unspawnable shape the removal exists to make unrepresentable. These two
      // assertions kill any constant, because no two roster kinds share a radius profile.
      for kind in roster do
        let actor = spawn 1 1 kind (vec2 400. 300.)
        Expect.equal (actorRadius actor) (definition kind).Radius $"%A{kind} radius is read from `definition`"
        Expect.equal (actorContactDamage actor) (definition kind).ContactDamage $"%A{kind} contact damage is read from `definition`"
      // And combat really consumes it: at 25 units from a 5-unit shot, a Brute (radius 22) is inside
      // the overlap and a Fly (radius 8) is outside. A constant radius makes both agree.
      let stats = {basePlayerStats with Pierce=4}
      let shot = spawnShots 0 1 (vec2 400. 300.) zero (vec2 1. 0.) stats |> List.exactlyOne
      let fireAt kind =
        let actor = {spawn 1 77 kind (vec2 425. 300.) with HitPoints=500.0}
        let stepped = stepSim {initialModel with ShotSpawns=[shot]; Enemies=[actor]}
        stepped.Enemies |> List.exactlyOne |> _.HitPoints
      Expect.isLessThan (fireAt EnemyKind.Brute) 500.0 "a Brute's 22-unit radius reaches the shot"
      Expect.equal (fireAt EnemyKind.Fly) 500.0 "a Fly's 8-unit radius does not"

    testCase "a death emits exactly one EnemyDied audio event and a survivor emits none" <| fun _ ->
      // `resolveCombat` derives the death count from `Enemies` now, and a critic found that
      // dropping either `HitPoints > 0.0` filter left the suite green while the game stopped
      // emitting death audio entirely. Nothing in the product asserted the simulation ever emits
      // this event; the cue-mapping tests hand-build their own event lists.
      let stats = {basePlayerStats with Pierce=0}
      let shot = spawnShots 0 1 (vec2 400. 300.) zero (vec2 1. 0.) stats |> List.exactlyOne
      let fire hp =
        let victim = {spawn 1 4300 EnemyKind.Grub (vec2 400. 300.) with HitPoints=hp; SplitEligible=false}
        let room = {IsBoss=false;Cleared=false;Doors=[Rogue3.Entities.DoorState.LockedClear]
                    LiveEnemyIds=Set.singleton victim.Id;Drop=None;Reward=None;Trapdoor=false}
        let stepped = update(Tick fixedDt) {initialModel with ShotSpawns=[shot];Enemies=[victim];Room=room} |> fst
        stepped.AudioEvents |> List.filter ((=) AudioEvent.EnemyDied) |> List.length
      Expect.equal (fire 1.0) 1 "a killed actor emits exactly one death cue"
      Expect.equal (fire 500.0) 0 "an actor that survives the same shot emits none"

    testCase "a bomb kill is resolved by the same cleanup, in the step the bomb explodes" <| fun _ ->
      // Before board item #20 a bomb kill was written to the legacy `Enemies` list only and reached
      // the actor list through an `hpById` re-sync that ran AFTER the cleanup fold, so the corpse
      // survived a whole extra fixed step and took one more AI turn before its drop was rolled.
      // That is the same defect class as section 14.21 and it is closed by the same removal. No test
      // covered a bomb KILL at all -- the two bomb tests use enemies that survive the blast.
      let victim = {spawn 1 4400 EnemyKind.Grub (vec2 700. 390.) with HitPoints=30.0; SplitEligible=false}
      let room = {IsBoss=false;Cleared=false;Doors=[Rogue3.Entities.DoorState.LockedClear]
                  LiveEnemyIds=Set.singleton victim.Id;Drop=None;Reward=None;Trapdoor=false}
      let before =
        {initialModel with
          PlayerPosition=vec2 100. 100.
          Bombs=[{Id=1;Position=vec2 700. 390.;FuseTicks=1}]
          Enemies=[victim];Room=room}
      let after = update(Tick fixedDt) before |> fst
      Expect.isEmpty after.Bombs "the bomb resolves in this step"
      Expect.isFalse (after.Enemies|>List.exists(fun actor->actor.Id=victim.Id)) "and the actor it killed is gone in the SAME step"
      Expect.equal (after.RunStats.KillsByType|>Map.tryFind EnemyKind.Grub) (Some 1) "with its kill credited once"
      Expect.isTrue after.Room.Cleared "and the room cleared once"
      Expect.notEqual after.DropRng before.DropRng "and its drop rolled once"

    testCase "shots pass through a Pit and are stopped by a Rock" <| fun _ ->
      // The shot pass-through filter subtracts the non-shot-blocking obstacles from the player's
      // collider set. A critic found it could be deleted, inverted, or silently reduced to the
      // structural no-op it used to be, with the suite green throughout -- and that the existing
      // assertion counts shots spawned in a tick during which no shot ever reaches the obstacle,
      // so it cannot fail. This fires down a corridor and compares the two kinds.
      let aim = {initialModel.Input.Current with MousePosition=Some(vec2 1100. 300.); MousePrimaryDown=true}
      let survivors kind =
        let blocker = obstacle kind 4500 |> obstacleAt (vec2 520. 300.)
        {initialModel with PlayerPosition=vec2 400. 300.; Obstacles=[blocker]}
        |> update (InputChanged aim) |> fst
        |> fun start -> [1..60] |> List.fold (fun current _ -> update (Tick fixedDt) current |> fst) start
        |> fun ended -> ended.ShotSpawns |> List.filter (fun shot -> shot.Position.Vx > 560.0) |> List.length
      Expect.isGreaterThan (survivors ObstacleKind.Pit) 0 "a Pit blocks movement but not shots"
      Expect.equal (survivors ObstacleKind.Rock) 0 "a Rock blocks both"

    testCase "the player's blocking rects are derived from Obstacles with no stored copy" <| fun _ ->
      // The removed `Obstacles` field was this expression, cached at four assignment sites. The
      // derivation is the single description now, so destroying an obstacle changes what the player
      // sweeps without any reducer remembering to refresh anything.
      let rock=obstacle ObstacleKind.Rock 900|>obstacleAt(vec2 300. 300.)
      let spikes=obstacle ObstacleKind.Spikes 901|>obstacleAt(vec2 500. 300.)
      let pot=obstacle ObstacleKind.Pot 902|>obstacleAt(vec2 700. 300.)
      let model={initialModel with Obstacles=[rock;spikes;pot]}
      Expect.hasLength (blockingObstacleRects model.Obstacles) 2 "spikes do not block movement; the rock and the pot do"
      let smashed=update(DamageM5Obstacle(902,1)) model|>fst
      Expect.hasLength (blockingObstacleRects smashed.Obstacles) 1 "a destroyed pot leaves the collider set in the same reducer"
  ]
