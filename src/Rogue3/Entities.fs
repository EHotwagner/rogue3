module Rogue3.Entities

open System
open FS.GG.Game.Core
open Rogue3.Geometry

[<RequireQualifiedAccess>]
type EnemyKind = Grub | Maggot | Spitter | Fly | Charger | Turret | Caster | Brute

[<RequireQualifiedAccess>]
type MovementClass = Grounded | Flying

type EnemyDefinition =
    { Kind: EnemyKind; Radius: float; HitPoints: float; Speed: float; DashSpeed: float option
      Threat: int; ContactDamage: int; Movement: MovementClass }

let definition = function
    | EnemyKind.Grub -> { Kind=EnemyKind.Grub; Radius=12.; HitPoints=6.; Speed=70.; DashSpeed=None; Threat=1; ContactDamage=1; Movement=MovementClass.Grounded }
    | EnemyKind.Maggot -> { Kind=EnemyKind.Maggot; Radius=9.; HitPoints=3.; Speed=110.; DashSpeed=None; Threat=1; ContactDamage=1; Movement=MovementClass.Grounded }
    | EnemyKind.Spitter -> { Kind=EnemyKind.Spitter; Radius=14.; HitPoints=10.; Speed=40.; DashSpeed=None; Threat=2; ContactDamage=1; Movement=MovementClass.Grounded }
    | EnemyKind.Fly -> { Kind=EnemyKind.Fly; Radius=8.; HitPoints=2.; Speed=130.; DashSpeed=None; Threat=1; ContactDamage=1; Movement=MovementClass.Flying }
    | EnemyKind.Charger -> { Kind=EnemyKind.Charger; Radius=16.; HitPoints=14.; Speed=60.; DashSpeed=Some 320.; Threat=3; ContactDamage=2; Movement=MovementClass.Grounded }
    | EnemyKind.Turret -> { Kind=EnemyKind.Turret; Radius=18.; HitPoints=18.; Speed=0.; DashSpeed=None; Threat=3; ContactDamage=1; Movement=MovementClass.Grounded }
    | EnemyKind.Caster -> { Kind=EnemyKind.Caster; Radius=13.; HitPoints=12.; Speed=50.; DashSpeed=None; Threat=4; ContactDamage=1; Movement=MovementClass.Grounded }
    | EnemyKind.Brute -> { Kind=EnemyKind.Brute; Radius=22.; HitPoints=40.; Speed=45.; DashSpeed=None; Threat=6; ContactDamage=2; Movement=MovementClass.Grounded }

let roster = [ EnemyKind.Grub; EnemyKind.Maggot; EnemyKind.Spitter; EnemyKind.Fly; EnemyKind.Charger; EnemyKind.Turret; EnemyKind.Caster; EnemyKind.Brute ]
let hpScale floorIndex = 1.0 + 0.12 * float (max 1 floorIndex)
let bulletSpeedScale floorIndex = 1.0 + 0.05 * float (max 1 floorIndex)
let enemyBulletSpeed floorIndex = 180.0 * bulletSpeedScale floorIndex
let threatBudget floorIndex = 6 + 2 * max 1 floorIndex
let scaledDefinition floorIndex kind = let d = definition kind in { d with HitPoints = d.HitPoints * hpScale floorIndex }

[<RequireQualifiedAccess>]
type EnemyState =
    | Seeking of ticks:int
    | HopPause of ticksLeft:int
    | SpitterCooldown of ticksLeft:int
    | SpitterTelegraph of ticksLeft:int
    | Orbit of ticks:int
    | Dive of direction:Vec2 * ticksLeft:int
    | ReturnToOrbit of ticks:int
    | ChargerIdle
    | ChargerWindUp of direction:Vec2 * ticksLeft:int
    | ChargerDash of direction:Vec2 * ticksLeft:int
    | ChargerRecover of ticksLeft:int
    | TurretCooldown of ticksLeft:int * volley:int
    | CasterCooldown of ticksLeft:int
    | CasterArrival of ticksLeft:int
    | BruteCooldown of ticksLeft:int
    | BruteTelegraph of ticksLeft:int
    | BruteShockwave of ticksLeft:int

type EnemyActor =
    { Id:int; Kind:EnemyKind; Position:Vec2; Anchor:Vec2; HitPoints:float; State:EnemyState; SplitEligible:bool }

[<RequireQualifiedAccess>]
type EnemyAction =
    | FireAimed of speed:float
    | FireBurst of count:int * angleOffsetDeg:float * speed:float
    | FireRing of count:int * speed:float
    | Teleport of destination:Vec2
    | Shockwave of maxRadius:float * durationTicks:int * damage:int * knockback:float

type EnemyStepContext = { FloorIndex:int; Player:Vec2; WallHit:bool; PlayerHit:bool; DropRng:Rng }
type EnemyStep = { Actor:EnemyActor; Actions:EnemyAction list; DropRng:Rng }

let ticks seconds = int (Math.Round(seconds * 120.0))
let private normalize v =
    let length = sqrt (v.Vx*v.Vx + v.Vy*v.Vy)
    if length <= 1e-12 || not(Double.IsFinite length) then zero else scale (1.0/length) v
let private toward fromPos toPos = sub toPos fromPos |> normalize
let private distanceSquared a b = let d = sub a b in d.Vx*d.Vx + d.Vy*d.Vy
let private advance speed direction (actor:EnemyActor) = { actor with Position = add actor.Position (scale (speed / 120.0) direction) }

let initialState = function
    | EnemyKind.Grub -> EnemyState.Seeking 0
    | EnemyKind.Maggot -> EnemyState.Seeking 0
    | EnemyKind.Spitter -> EnemyState.SpitterCooldown (ticks 1.8)
    | EnemyKind.Fly -> EnemyState.Orbit 0
    | EnemyKind.Charger -> EnemyState.ChargerIdle
    | EnemyKind.Turret -> EnemyState.TurretCooldown(ticks 2.2, 0)
    | EnemyKind.Caster -> EnemyState.CasterCooldown(ticks 4.0)
    | EnemyKind.Brute -> EnemyState.BruteCooldown 0

let spawn floorIndex id kind pos =
    let d = scaledDefinition floorIndex kind
    { Id=id; Kind=kind; Position=pos; Anchor=pos; HitPoints=d.HitPoints; State=initialState kind; SplitEligible=(kind=EnemyKind.Grub) }

let private casterDestination player rng =
    let rec choose attempts next =
        let struct(x, r1) = Rng.nextInt 100 1180 next
        let struct(y, r2) = Rng.nextInt 100 620 r1
        let p = vec2 (float x) (float y)
        if distanceSquared p player >= 120.0*120.0 || attempts >= 15 then p, r2 else choose (attempts+1) r2
    choose 0 rng

let stepEnemy (context:EnemyStepContext) actor =
    let d = definition actor.Kind
    let speed = enemyBulletSpeed context.FloorIndex
    let result state actor actions rng = { Actor={actor with State=state}; Actions=actions; DropRng=rng }
    match actor.State with
    | EnemyState.Seeking n when actor.Kind=EnemyKind.Maggot && n >= ticks 0.55 -> result (EnemyState.HopPause(ticks 0.12)) actor [] context.DropRng
    | EnemyState.Seeking n -> result (EnemyState.Seeking(n+1)) (advance d.Speed (toward actor.Position context.Player) actor) [] context.DropRng
    | EnemyState.HopPause n when n <= 1 -> result (EnemyState.Seeking 0) actor [] context.DropRng
    | EnemyState.HopPause n -> result (EnemyState.HopPause(n-1)) actor [] context.DropRng
    | EnemyState.SpitterCooldown n when n <= 1 -> result (EnemyState.SpitterTelegraph(ticks 0.3)) actor [] context.DropRng
    | EnemyState.SpitterCooldown n -> result (EnemyState.SpitterCooldown(n-1)) actor [] context.DropRng
    | EnemyState.SpitterTelegraph n when n <= 1 -> result (EnemyState.SpitterCooldown(ticks 1.8)) actor [EnemyAction.FireAimed speed] context.DropRng
    | EnemyState.SpitterTelegraph n -> result (EnemyState.SpitterTelegraph(n-1)) actor [] context.DropRng
    | EnemyState.Orbit n when n >= ticks 2.0 -> result (EnemyState.Dive(toward actor.Position context.Player, ticks 0.5)) actor [] context.DropRng
    | EnemyState.Orbit n ->
        let angle = float n * 2.0*Math.PI / float (ticks 2.0)
        let pos = add actor.Anchor (vec2 (36.0*cos angle) (36.0*sin angle))
        result (EnemyState.Orbit(n+1)) {actor with Position=pos} [] context.DropRng
    | EnemyState.Dive(dir,n) when n <= 1 -> result (EnemyState.ReturnToOrbit 0) actor [] context.DropRng
    | EnemyState.Dive(dir,n) -> result (EnemyState.Dive(dir,n-1)) (advance d.Speed dir actor) [] context.DropRng
    | EnemyState.ReturnToOrbit _ when distanceSquared actor.Position actor.Anchor <= 4.0 -> result (EnemyState.Orbit 0) actor [] context.DropRng
    | EnemyState.ReturnToOrbit n -> result (EnemyState.ReturnToOrbit(n+1)) (advance d.Speed (toward actor.Position actor.Anchor) actor) [] context.DropRng
    | EnemyState.ChargerIdle when distanceSquared actor.Position context.Player <= 260.0*260.0 -> result (EnemyState.ChargerWindUp(toward actor.Position context.Player,ticks 0.6)) actor [] context.DropRng
    | EnemyState.ChargerIdle -> result EnemyState.ChargerIdle (advance d.Speed (toward actor.Position context.Player) actor) [] context.DropRng
    | EnemyState.ChargerWindUp(dir,n) when n <= 1 -> result (EnemyState.ChargerDash(dir,ticks 0.8)) actor [] context.DropRng
    | EnemyState.ChargerWindUp(dir,n) -> result (EnemyState.ChargerWindUp(dir,n-1)) actor [] context.DropRng
    | EnemyState.ChargerDash(_,_) when context.WallHit || context.PlayerHit -> result (EnemyState.ChargerRecover(ticks 0.7)) actor [] context.DropRng
    | EnemyState.ChargerDash(dir,n) when n <= 1 -> result (EnemyState.ChargerRecover(ticks 0.7)) actor [] context.DropRng
    | EnemyState.ChargerDash(dir,n) -> result (EnemyState.ChargerDash(dir,n-1)) (advance 320. dir actor) [] context.DropRng
    | EnemyState.ChargerRecover n when n <= 1 -> result EnemyState.ChargerIdle actor [] context.DropRng
    | EnemyState.ChargerRecover n -> result (EnemyState.ChargerRecover(n-1)) actor [] context.DropRng
    | EnemyState.TurretCooldown(n,v) when n <= 1 -> result (EnemyState.TurretCooldown(ticks 2.2,v+1)) actor [EnemyAction.FireBurst(4,float v*22.5,speed)] context.DropRng
    | EnemyState.TurretCooldown(n,v) -> result (EnemyState.TurretCooldown(n-1,v)) actor [] context.DropRng
    | EnemyState.CasterCooldown n when n <= 1 ->
        let destination,rng = casterDestination context.Player context.DropRng
        result (EnemyState.CasterArrival(ticks 0.2)) {actor with Position=destination} [EnemyAction.Teleport destination] rng
    | EnemyState.CasterCooldown n -> result (EnemyState.CasterCooldown(n-1)) actor [] context.DropRng
    | EnemyState.CasterArrival n when n <= 1 -> result (EnemyState.CasterCooldown(ticks 4.0)) actor [EnemyAction.FireRing(6,speed)] context.DropRng
    | EnemyState.CasterArrival n -> result (EnemyState.CasterArrival(n-1)) actor [] context.DropRng
    | EnemyState.BruteCooldown n when n <= 0 && distanceSquared actor.Position context.Player <= 80.0*80.0 -> result (EnemyState.BruteTelegraph(ticks 0.5)) actor [] context.DropRng
    | EnemyState.BruteCooldown n -> result (EnemyState.BruteCooldown(max 0 (n-1))) (advance d.Speed (toward actor.Position context.Player) actor) [] context.DropRng
    | EnemyState.BruteTelegraph n when n <= 1 -> result (EnemyState.BruteShockwave(ticks 0.25)) actor [EnemyAction.Shockwave(140.,ticks 0.25,2,160.)] context.DropRng
    | EnemyState.BruteTelegraph n -> result (EnemyState.BruteTelegraph(n-1)) actor [] context.DropRng
    | EnemyState.BruteShockwave n when n <= 1 -> result (EnemyState.BruteCooldown(ticks 2.5)) actor [] context.DropRng
    | EnemyState.BruteShockwave n -> result (EnemyState.BruteShockwave(n-1)) actor [] context.DropRng

let grubSplit floorIndex nextId actor =
    if floorIndex < 2 || actor.Kind<>EnemyKind.Grub || not actor.SplitEligible then []
    else [ spawn floorIndex nextId EnemyKind.Maggot (add actor.Position (vec2 -14. 0.)); spawn floorIndex (nextId+1) EnemyKind.Maggot (add actor.Position (vec2 14. 0.)) ] |> List.map(fun a->{a with SplitEligible=false})

type BulletPattern = { Count:int; ArcDegrees:float; Speed:float; SpinDegreesPerVolley:float; CadenceTicks:int; Homing:float; GapIndex:int option }
[<RequireQualifiedAccess>]
type BossKind = Gnawer | HollowChoir | Maw
type BossDefinition = { Kind:BossKind; BaseHitPoints:float; PhaseThresholds:float list; Patterns:BulletPattern list }
let private pattern count arc speed spin cadence homing gap = { Count=count; ArcDegrees=arc; Speed=speed; SpinDegreesPerVolley=spin; CadenceTicks=ticks cadence; Homing=homing; GapIndex=gap }
let bossDefinition = function
    | BossKind.Gnawer -> { Kind=BossKind.Gnawer; BaseHitPoints=220.; PhaseThresholds=[0.5]; Patterns=[pattern 1 0. 320. 0. 0.8 0. None; pattern 12 360. 180. 30. 3.0 0. None] }
    | BossKind.HollowChoir -> { Kind=BossKind.HollowChoir; BaseHitPoints=300.; PhaseThresholds=[0.5]; Patterns=[pattern 6 360. 180. 30. 1.2 0. None; pattern 6 360. 210. 30. 0.8 0. None] }
    | BossKind.Maw -> { Kind=BossKind.Maw; BaseHitPoints=420.; PhaseThresholds=[0.66;0.33]; Patterns=[pattern 11 180. 180. 8. 1.4 0. (Some 5); pattern 16 360. 200. 12. 1.0 0. None; pattern 8 360. 160. 17. 0.8 1.0 None] }
let bossPhase kind hitPoints =
    let d=bossDefinition kind
    d.PhaseThresholds |> List.filter(fun threshold -> hitPoints <= d.BaseHitPoints*threshold) |> List.length |> (+) 1
let choirRevives (killTicks:int list) = killTicks.Length=3 && (List.max killTicks - List.min killTicks > ticks 4.0)

type BossActor =
    { Id:int; Kind:BossKind; Position:Vec2; HitPoints:float; Phase:int
      PatternTicksLeft:int; Volley:int; ChoirKillTicks:int list }
[<RequireQualifiedAccess>]
type BossAction =
    | Emit of BulletPattern * angleOffsetDeg:float
    | Charge of direction:Vec2
    | SpawnMaggots of count:int
    | GroundPound of maxRadius:float * durationTicks:int * damage:int * knockback:float
    | ReviveChoir
type BossStep = { Boss:BossActor; Actions:BossAction list }
let spawnBoss id kind position =
    let d=bossDefinition kind
    { Id=id;Kind=kind;Position=position;HitPoints=d.BaseHitPoints;Phase=1
      PatternTicksLeft=d.Patterns.Head.CadenceTicks;Volley=0;ChoirKillTicks=[] }
let stepBoss player tick actor =
    let phase=bossPhase actor.Kind actor.HitPoints
    let d=bossDefinition actor.Kind
    let pattern=d.Patterns.[min (phase-1) (d.Patterns.Length-1)]
    if actor.Kind=BossKind.HollowChoir && choirRevives actor.ChoirKillTicks then
        { Boss={actor with Phase=phase;ChoirKillTicks=[];HitPoints=max actor.HitPoints (d.BaseHitPoints*0.5)};Actions=[BossAction.ReviveChoir] }
    elif actor.PatternTicksLeft>1 then
        { Boss={actor with Phase=phase;PatternTicksLeft=actor.PatternTicksLeft-1};Actions=[] }
    else
        let signature =
            match actor.Kind,phase with
            | BossKind.Gnawer,1 -> [BossAction.Charge(toward actor.Position player);BossAction.SpawnMaggots 2]
            | BossKind.Maw,2 -> [BossAction.GroundPound(140.,ticks 0.25,2,160.)]
            | _ -> []
        { Boss={actor with Phase=phase;PatternTicksLeft=pattern.CadenceTicks;Volley=actor.Volley+1}
          Actions=BossAction.Emit(pattern,float actor.Volley*pattern.SpinDegreesPerVolley)::signature }

[<RequireQualifiedAccess>]
type PickupKind = Nothing | Coin1 | Coin3 | HalfRedHeart | Key | Bomb | SoulHeart
type WeightedDrop = { Kind:PickupKind; Weight:int }
let roomClearTable = [PickupKind.Nothing,45; PickupKind.Coin1,22; PickupKind.Coin3,8; PickupKind.HalfRedHeart,12; PickupKind.Key,6; PickupKind.Bomb,5; PickupKind.SoulHeart,2] |> List.map(fun(k,w)->{Kind=k;Weight=w})
let potTable = [PickupKind.Nothing,40; PickupKind.Coin1,28; PickupKind.HalfRedHeart,12; PickupKind.Coin3,6; PickupKind.Key,6; PickupKind.Bomb,6; PickupKind.SoulHeart,2] |> List.map(fun(k,w)->{Kind=k;Weight=w})
let tintedRockTable = [PickupKind.Nothing,60; PickupKind.Coin1,30; PickupKind.HalfRedHeart,10] |> List.map(fun(k,w)->{Kind=k;Weight=w})
let rollDrop table rng =
    let total=table|>List.sumBy(fun e->max 0 e.Weight)
    if total<=0 then PickupKind.Nothing,rng else
    let struct(draw,next)=Rng.nextInt 1 total rng
    let rec pick cumulative = function | []->PickupKind.Nothing | x::xs -> let c=cumulative+x.Weight in if draw<=c then x.Kind else pick c xs
    pick 0 table,next

[<RequireQualifiedAccess>]
type ObstacleKind = Rock | TintedRock | Pot | Spikes | Pit
type Obstacle = { Id:int; Kind:ObstacleKind; Position:Vec2; HitPoints:int option; DropClaimed:bool }
let obstacle kind id = { Id=id; Kind=kind; Position=zero; HitPoints=(match kind with ObstacleKind.Pot->Some 1 | ObstacleKind.TintedRock->Some 40 | _->None); DropClaimed=false }
let obstacleAt position (value:Obstacle) = { value with Position=position }
let blocksMovement movement kind = match kind,movement with | ObstacleKind.Pit,MovementClass.Flying -> false | ObstacleKind.Pit,_ -> true | ObstacleKind.Spikes,_ -> false | _ -> true
let blocksShots = function ObstacleKind.Pit|ObstacleKind.Spikes -> false | _ -> true
let spikeDamage kind = if kind=ObstacleKind.Spikes then 1 else 0
let destroyObstacle damage rng obstacle =
    match obstacle.HitPoints with
    | None -> Some obstacle,None,rng
    | Some hp when hp-damage>0 -> Some {obstacle with HitPoints=Some(hp-damage)},None,rng
    | Some _ when obstacle.DropClaimed -> None,None,rng
    | Some _ ->
        let table = if obstacle.Kind=ObstacleKind.Pot then potTable else tintedRockTable
        let drop,next = rollDrop table rng
        None,(if drop=PickupKind.Nothing then None else Some drop),next

type ItemDefinition = { Id:string; Quality:int; Tags:Set<string> }
type ItemPool = { Available:ItemDefinition list; Placed:Set<string> }
let baseItems =
    [ "coal-heart",0,["treasure";"shop"]; "cracked-lens",1,["treasure";"shop"]; "iron-teeth",2,["boss";"treasure"]; "void-map",1,["shop"]; "maggot-crown",3,["boss"]; "choir-bell",2,["boss";"shop"] ]
    |> List.map(fun(id,q,tags)->{Id=id;Quality=q;Tags=Set.ofList tags})
let itemPool unlocked = { Available=(baseItems @ unlocked |> List.distinctBy(fun i->i.Id)); Placed=Set.empty }
let drawItem tag rng pool =
    let choices=pool.Available |> List.filter(fun i->Set.contains tag i.Tags && not(Set.contains i.Id pool.Placed)) |> List.sortBy(fun i->i.Id)
    if List.isEmpty choices then None,rng,pool
    else
        let struct(index,next)=Rng.nextInt 0 (choices.Length-1) rng
        let item=choices.[index]
        Some item,next,{pool with Placed=Set.add item.Id pool.Placed}

[<RequireQualifiedAccess>]
type ShopOffer = Empty | Item of ItemDefinition | Consumable of PickupKind
type ShopSlot = { Id:int; Offer:ShopOffer; Price:int; KeyLocked:bool }
let itemPrice item = 10 + (item.Quality-1)*3
let generateShop rng pool =
    let mutable next=rng
    let mutable p=pool
    let slots =
      [for id in 0..2 do
        let struct(mode,r1)=Rng.nextInt 0 3 next
        next<-r1
        if mode<3 then
          let item,r2,p2=drawItem "shop" next p
          next<-r2
          p<-p2
          match item with
          | Some value ->
              let struct(lockDraw,r3)=Rng.nextInt 0 4 next
              next<-r3
              yield {Id=id;Offer=ShopOffer.Item value;Price=itemPrice value;KeyLocked=(lockDraw=0)}
          | None -> yield {Id=id;Offer=ShopOffer.Consumable PickupKind.Coin3;Price=3;KeyLocked=false}
        else
          let consumables=[|PickupKind.HalfRedHeart,3;PickupKind.Key,5;PickupKind.Bomb,5;PickupKind.SoulHeart,6|]
          let struct(i,r2)=Rng.nextInt 0 3 next
          next<-r2
          let kind,price=consumables.[i]
          yield {Id=id;Offer=ShopOffer.Consumable kind;Price=price;KeyLocked=false}]
    slots,next,p
let purchase coins keys slot =
    if slot.Offer=ShopOffer.Empty || (slot.KeyLocked && keys<1) || (not slot.KeyLocked && coins<slot.Price) then coins,keys,slot,false
    elif slot.KeyLocked then coins,keys-1,{slot with Offer=ShopOffer.Empty},true
    else coins-slot.Price,keys,{slot with Offer=ShopOffer.Empty},true

[<RequireQualifiedAccess>]
type DoorState = Open | LockedClear | BossSealed
type CombatRoom = { IsBoss:bool; Cleared:bool; Doors:DoorState list; LiveEnemyIds:Set<int>; Drop:PickupKind option; Reward:ItemDefinition option; Trapdoor:bool }
let enterRoom enemyIds room = if room.Cleared then room else {room with LiveEnemyIds=Set.ofList enemyIds; Doors=room.Doors |> List.map(fun _->if room.IsBoss then DoorState.BossSealed else DoorState.LockedClear)}
let enemyDied enemyId rng room =
    let live=Set.remove enemyId room.LiveEnemyIds
    if room.Cleared || not(Set.isEmpty live) then {room with LiveEnemyIds=live},rng
    else let drop,next=rollDrop roomClearTable rng in {room with Cleared=true;LiveEnemyIds=live;Doors=room.Doors|>List.map(fun _->DoorState.Open);Drop=(if drop=PickupKind.Nothing then None else Some drop)},next
let bossCleared reward room = {room with Cleared=true;LiveEnemyIds=Set.empty;Doors=room.Doors|>List.map(fun _->DoorState.Open);Reward=reward;Trapdoor=true}
