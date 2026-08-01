module Rogue3.Render

open System
open FS.GG.UI.Scene
open FS.GG.UI.Symbology
open Rogue3.Geometry
open Rogue3.Model
open Rogue3.Entities

[<RequireQualifiedAccess>]
type RenderLayer =
    | FloorBackground | FloorDecals | Obstacles | Pickups | Shadows | Enemies
    | Player | Projectiles | Particles | Hud | ScreenOverlays

type LayerScene = { Layer: RenderLayer; Scene: Scene }

let layerOrder =
    [ RenderLayer.FloorBackground; RenderLayer.FloorDecals; RenderLayer.Obstacles
      RenderLayer.Pickups; RenderLayer.Shadows; RenderLayer.Enemies; RenderLayer.Player
      RenderLayer.Projectiles; RenderLayer.Particles; RenderLayer.Hud; RenderLayer.ScreenOverlays ]

let private color r g b a : Color = { Red=r; Green=g; Blue=b; Alpha=a }
let private point (v: Vec2) : Point = { X=v.Vx; Y=v.Vy }

type HudLayoutEvidence =
    { Size: Size
      HeartsBounds: Rect
      CurrencyBounds: Rect
      ChargeBounds: Rect
      MinimapBounds: Rect
      FloorNameBounds: Rect
      Overlaps: bool }

let hudLayoutForSize (size: Size) =
    let width, height = float size.Width, float size.Height
    let hearts = { X=24.0; Y=20.0; Width=min 384.0 (max 96.0 (width*0.34)); Height=32.0 }
    let currency = { X=24.0; Y=60.0; Width=230.0; Height=28.0 }
    let charge = { X=max 24.0 (width-100.0); Y=20.0; Width=72.0; Height=40.0 }
    let minimap = { X=max 24.0 (width-140.0); Y=70.0; Width=120.0; Height=120.0 }
    // The floor banner must clear the SOUTH DOORWAY. It used to sit at `height-52`, which put its
    // glyphs directly on top of the south door panel — on the one wall segment a player is looking at
    // when a room loads, which is exactly when the banner fires. `Overlaps` below only intersects HUD
    // rects against other HUD rects, so it can never see a HUD-over-gameplay collision.
    let floorName = { X=max 24.0 (width/2.0-150.0); Y=max 100.0 (height*0.80); Width=300.0; Height=32.0 }
    let intersects a b = a.X < b.X+b.Width && a.X+a.Width > b.X && a.Y < b.Y+b.Height && a.Y+a.Height > b.Y
    { Size=size; HeartsBounds=hearts; CurrencyBounds=currency; ChargeBounds=charge; MinimapBounds=minimap
      FloorNameBounds=floorName
      Overlaps=[hearts,currency; hearts,charge; currency,minimap; charge,minimap; minimap,floorName] |> List.exists (fun (a,b)->intersects a b) }

/// The named production HUD regions. Rendering and exact-scale evidence consume the same layout
/// record, so removing or renaming a region cannot be hidden by an unchanged aggregate node count.
let hudRegionsForSize size =
    let layout = hudLayoutForSize size
    [ "hearts", layout.HeartsBounds
      "currency", layout.CurrencyBounds
      "active-charge", layout.ChargeBounds
      "minimap", layout.MinimapBounds
      "floor-name", layout.FloorNameBounds ]

let hudSceneForSize (size: Size) model =
    let layout = hudLayoutForSize size
    let heartCount = min 12 model.PlayerHealth.RedContainers
    let filledRed = min (heartCount*2) model.PlayerHealth.RedHalfHearts
    let heartNodes =
        [ for i in 0..heartCount-1 do
            let x = layout.HeartsBounds.X + float i*32.0
            let fill = if filledRed >= (i+1)*2 then color 232uy 66uy 79uy 255uy elif filledRed = i*2+1 then color 232uy 66uy 79uy 150uy else color 84uy 72uy 88uy 255uy
            yield Scene.circle { X=x+12.0;Y=layout.HeartsBounds.Y+12.0 } 11.0 fill
          let soulCount=(model.PlayerHealth.SoulHalfHearts+1)/2
          for i in 0..soulCount-1 do
            let full=model.PlayerHealth.SoulHalfHearts>i*2+1
            yield Scene.circle {X=layout.HeartsBounds.X+float(heartCount+i)*32.0+12.0;Y=layout.HeartsBounds.Y+12.0} 11.0 (color 78uy 186uy 232uy (if full then 255uy else 150uy))
          let blackCount=(model.PlayerHealth.BlackHalfHearts+1)/2
          for i in 0..blackCount-1 do
            let full=model.PlayerHealth.BlackHalfHearts>i*2+1
            yield Scene.circle {X=layout.HeartsBounds.X+float(heartCount+soulCount+i)*32.0+12.0;Y=layout.HeartsBounds.Y+12.0} 11.0 (color 38uy 30uy 48uy (if full then 255uy else 150uy)) ]
    let currency = sprintf "COIN %02d   KEY %02d   BOMB %02d" model.PlayerCurrency.Coins model.PlayerCurrency.Keys model.PlayerCurrency.Bombs
    let chargeRatio = if model.ActiveChargeMaximum<=0 then 0.0 else float model.ActiveCharge/float model.ActiveChargeMaximum
    let chargeText = sprintf "ACTIVE %d/%d" model.ActiveCharge model.ActiveChargeMaximum
    let revealed = model.Floor.MapRevealed |> Set.toList |> List.choose (fun id -> Map.tryFind id model.Floor.Rooms)
    let mapNodes =
        revealed |> List.map (fun room ->
            let current = room.Id=model.Floor.CurrentRoom
            let fill =
                if current then color 255uy 220uy 80uy 255uy else
                match room.RoomType with
                | FloorGeneration.Treasure -> color 255uy 190uy 64uy 255uy
                | FloorGeneration.Shop -> color 72uy 204uy 132uy 255uy
                | FloorGeneration.Boss -> color 220uy 66uy 79uy 255uy
                | FloorGeneration.Secret | FloorGeneration.SuperSecret -> color 190uy 126uy 255uy 255uy
                | _ -> color 90uy 115uy 145uy 255uy
            Scene.filledRectangle
                { X=layout.MinimapBounds.X+56.0+float room.Cell.Col*10.0; Y=layout.MinimapBounds.Y+56.0+float room.Cell.Row*10.0; Width=8.0;Height=8.0 } fill)
    let floorName = sprintf "%d — THE BURROWS" model.FloorIndex
    Scene.group
        (heartNodes @
         [ Scene.textAt { X=layout.CurrencyBounds.X;Y=layout.CurrencyBounds.Y+18.0 } currency (color 255uy 244uy 205uy 255uy)
           Scene.circle { X=layout.ChargeBounds.X+20.0;Y=layout.ChargeBounds.Y+20.0 } 15.0 (color 35uy 42uy 56uy 255uy)
           Scene.arc {X=layout.ChargeBounds.X+3.0;Y=layout.ChargeBounds.Y+3.0;Width=34.0;Height=34.0} -90.0 (360.0*chargeRatio) (Paint.stroke (color 42uy 120uy 214uy 255uy) 5.0)
           Scene.textAt { X=layout.ChargeBounds.X-35.0;Y=layout.ChargeBounds.Y+38.0 } chargeText (color 255uy 255uy 255uy 255uy)
           Scene.filledRectangle layout.MinimapBounds (color 16uy 20uy 30uy 210uy)
           Scene.group mapNodes ] @
         (if model.FloorNameTicks>0 then [ Scene.textAt { X=layout.FloorNameBounds.X;Y=layout.FloorNameBounds.Y+24.0 } floorName (color 255uy 255uy 255uy 255uy) ] else []))

let klassOf = function
    | EnemyKind.Brute | EnemyKind.Turret -> Klass.Heavy
    | EnemyKind.Maggot | EnemyKind.Fly -> Klass.Scout
    | _ -> Klass.Mobile

let sigilOf = function
    | EnemyKind.Spitter | EnemyKind.Turret | EnemyKind.Caster -> Sigil.Bolt
    | EnemyKind.Charger | EnemyKind.Brute -> Sigil.Fang
    | _ -> Sigil.Ring

let threatTier threat =
    if threat <= 1 then 0.25
    elif threat <= 2 then 0.5
    elif threat <= 4 then 0.75
    else 1.0

let private toward fromPosition toPosition =
    let delta = sub toPosition fromPosition
    let length = sqrt (delta.Vx*delta.Vx + delta.Vy*delta.Vy)
    if length <= 1e-12 || not (Double.IsFinite length) then vec2 0.0 -1.0
    else scale (1.0/length) delta

let private facingOf playerPosition (actor: EnemyActor) =
    match actor.State with
    | EnemyState.ChargerWindUp(direction, _)
    | EnemyState.ChargerDash(direction, _)
    | EnemyState.Dive(direction, _) -> direction
    | EnemyState.Orbit ticks ->
        let angle = float ticks * 2.0*Math.PI / float (Rogue3.Entities.ticks 2.0)
        vec2 (-sin angle) (cos angle)
    | EnemyState.ReturnToOrbit _ -> toward actor.Position actor.Anchor
    | _ -> toward actor.Position playerPosition

let private headingOf direction = atan2 direction.Vx (-direction.Vy)

let speedTier speed =
    if speed <= 0.0 then 0
    elif speed <= 55.0 then 1
    elif speed <= 115.0 then 2
    else 3

let enemyToken floorIndex playerPosition (actor: EnemyActor) : Token =
    let definition = scaledDefinition floorIndex actor.Kind
    let facing = facingOf playerPosition actor
    { Symbology.defaultToken with
        Cx = actor.Position.Vx
        Cy = actor.Position.Vy
        R = definition.Radius
        Heading = headingOf facing
        Faction = Faction.Enemy
        Klass = klassOf actor.Kind
        Sigil = sigilOf actor.Kind
        Health = max 0.0 (min 1.0 (actor.HitPoints / definition.HitPoints))
        Threat = threatTier definition.Threat
        Speed = speedTier definition.Speed }

let enemyTokens model = model.M5Enemies |> List.map (enemyToken model.FloorIndex model.PlayerPosition)

let legibility model = enemyTokens model |> Legibility.scoreIn Grammar.Token

let acceptedLegibility model =
    (legibility model).Findings
    |> List.forall (fun finding ->
        finding.Severity <> Legibility.Severity.Error
        && finding.Channel = Legibility.Channel.Size)

let particleOpacity (particle: M6Particle) =
    1.0 - float particle.AgeTicks / float (max 1 particle.LifetimeTicks)
    |> max 0.0 |> min 1.0

let particleScene (particle: M6Particle) =
    let alpha = byte (Math.Round(255.0 * particleOpacity particle))
    let fill =
        match particle.Tint with
        | ParticleTint.Death -> color 232uy 66uy 79uy alpha
        | ParticleTint.Muzzle -> color 255uy 214uy 96uy alpha
        | ParticleTint.Explosion -> color 255uy 122uy 48uy alpha
    match particle.Shape with
    | ParticleShape.Circle -> Scene.circle (point particle.Position) particle.Radius fill
    | ParticleShape.Quad ->
        Scene.filledRectangle
            { X=particle.Position.Vx-particle.Radius; Y=particle.Position.Vy-particle.Radius
              Width=particle.Radius*2.0; Height=particle.Radius*2.0 } fill

let private particleFill particle =
    let alpha = byte (Math.Round(255.0 * particleOpacity particle))
    match particle.Tint with
    | ParticleTint.Death -> color 232uy 66uy 79uy alpha
    | ParticleTint.Muzzle -> color 255uy 214uy 96uy alpha
    | ParticleTint.Explosion -> color 255uy 122uy 48uy alpha

// The pool is deliberately large (600), so describe it with batched scene primitives instead of
// allocating one scene node per particle. Point batches preserve circle radius/colour; quad batches
// use per-vertex colours and two triangles per particle. The model still retains and steps every
// particle independently, while the production renderer presents the same visible inventory with a
// bounded number of draw nodes.
let private particlesScene particles =
    let circles =
        particles
        |> List.filter(fun particle->particle.Shape=ParticleShape.Circle)
        |> List.groupBy(fun particle->particle.Radius,particleFill particle)
        |> List.map(fun((radius,fill),values)->
            values|>List.map(fun particle->point particle.Position)|>fun points->Scene.points points (Paint.stroke fill (radius*2.0)))
    let quadVertices =
        particles
        |> List.filter(fun particle->particle.Shape=ParticleShape.Quad)
        |> List.collect(fun particle->
            let r=particle.Radius
            let x,y=particle.Position.Vx,particle.Position.Vy
            let fill=particleFill particle
            let vertex px py = {Position={X=px;Y=py};Color=Some fill}
            [vertex (x-r) (y-r);vertex (x+r) (y-r);vertex (x+r) (y+r)
             vertex (x-r) (y-r);vertex (x+r) (y+r);vertex (x-r) (y+r)])
    let quads=if List.isEmpty quadVertices then [] else [Scene.vertices VertexMode.Triangles quadVertices (Paint.fill (color 255uy 255uy 255uy 255uy))]
    Scene.group(circles@quads)

let private obstacleId = function
    | ObstacleKind.Rock -> "ObstacleRock"
    | ObstacleKind.TintedRock -> "ObstacleTintedRock"
    | ObstacleKind.Pot -> "ObstaclePot"
    | ObstacleKind.Spikes -> "ObstacleSpikes"
    | ObstacleKind.Pit -> "ObstaclePit"

let private obstacleHandle kind = "scene/obstacle/" + (obstacleId kind).Substring(8).ToLowerInvariant()

let private obstacleScene obstacle =
    match obstacle.Kind with
    | ObstacleKind.Rock -> Scene.circle (point obstacle.Position) 20.0 (color 90uy 74uy 110uy 255uy)
    | ObstacleKind.TintedRock -> Scene.circle (point obstacle.Position) 20.0 (color 110uy 90uy 74uy 255uy)
    | ObstacleKind.Pot ->
        Scene.filledEllipse { X=obstacle.Position.Vx-14.0;Y=obstacle.Position.Vy-18.0;Width=28.0;Height=36.0 } (color 110uy 82uy 54uy 255uy)
    | ObstacleKind.Spikes ->
        Scene.filledRectangle { X=obstacle.Position.Vx-18.0;Y=obstacle.Position.Vy-8.0;Width=36.0;Height=16.0 } (color 138uy 138uy 154uy 255uy)
    | ObstacleKind.Pit ->
        // A pit is a hazard and measured 1.10:1 against the floor — the lowest-contrast object in the
        // game. The rim is what makes it visible; the void stays dark so it still reads as a hole.
        Scene.group
            [ Scene.filledEllipse { X=obstacle.Position.Vx-24.0;Y=obstacle.Position.Vy-14.0;Width=48.0;Height=28.0 } (color 8uy 5uy 12uy 255uy)
              Scene.ellipse
                  { X=obstacle.Position.Vx-24.0;Y=obstacle.Position.Vy-14.0;Width=48.0;Height=28.0 }
                  (Paint.stroke (color 132uy 116uy 148uy 255uy) 3.0) ]

let private pickupIdentity = function
    | PickupKind.Coin1 -> Some("PickupCoin1", "scene/pickup/coin-1", 5.0, color 245uy 197uy 66uy 255uy)
    | PickupKind.Coin3 -> Some("PickupCoin3", "scene/pickup/coin-3", 8.0, color 255uy 225uy 92uy 255uy)
    | PickupKind.Key -> Some("PickupKey", "scene/pickup/key", 7.0, color 217uy 177uy 74uy 255uy)
    | PickupKind.Bomb -> Some("PickupBomb", "scene/pickup/bomb", 9.0, color 43uy 43uy 43uy 255uy)
    | PickupKind.HalfRedHeart -> Some("PickupHalfRedHeart", "scene/pickup/half-red-heart", 8.0, color 232uy 66uy 79uy 255uy)
    | PickupKind.SoulHeart -> Some("PickupSoulHeart", "scene/pickup/soul-heart", 9.0, color 74uy 120uy 232uy 255uy)
    | PickupKind.Nothing -> None

let bossToken model (boss: BossActor) =
    let definition = bossDefinition boss.Kind
    let radius, sigil =
        match boss.Kind with
        | BossKind.Gnawer -> 32.0, Sigil.Fang
        | BossKind.HollowChoir -> 38.0, Sigil.Ring
        | BossKind.Maw -> 44.0, Sigil.Bolt
    { Symbology.defaultToken with
        Cx=boss.Position.Vx; Cy=boss.Position.Vy; R=radius
        Heading=headingOf (toward boss.Position model.PlayerPosition)
        Faction=Faction.Enemy; Klass=Klass.Heavy; Sigil=sigil
        Health=max 0.0 (min 1.0 (boss.HitPoints/definition.BaseHitPoints))
        Threat=1.0; Speed=1 }

// ------------------------------------------------------------------------------------------------
// M11 room shell: walls and doors.
//
// ONE DOOR MODEL. Everything below reads `Floor.Rooms.[CurrentRoom].Doors` — the floor graph — for a
// door's existence, its `Direction` and its `DoorState`. `M5Room.Doors` contributes only the derived
// combat lock, index-aligned with the graph list by `loadM5Room`. Before M11 the renderer drew the
// cosmetic list alone, as an indexed strip at a fixed screen position, so doors neither sat on their
// walls nor distinguished `LockedKey` from `HiddenWall` — and a room whose cosmetic list was empty
// drew no exit at all, however many the floor graph gave it.
// ------------------------------------------------------------------------------------------------

let wallThickness = 24.0

// Raised from 62,52,70 / 92,80,104. Against the 27,19,32 floor those measured about 1.54:1, which is
// below any usable contrast floor for a 24-unit band; the walls read only because they were
// continuous and frame-aligned, not because they were visible.
let private stone = color 96uy 82uy 108uy 255uy
let private stoneEdge = color 146uy 130uy 162uy 255uy

/// The opening a door occupies in the wall its `Direction` names.
let doorwayRect direction : Rect =
    match direction with
    | FloorGeneration.North -> { X=playfieldWidth/2.0-doorwayHalfSpan; Y=0.0; Width=doorwayHalfSpan*2.0; Height=wallThickness }
    | FloorGeneration.South -> { X=playfieldWidth/2.0-doorwayHalfSpan; Y=playfieldHeight-wallThickness; Width=doorwayHalfSpan*2.0; Height=wallThickness }
    | FloorGeneration.West -> { X=0.0; Y=playfieldHeight/2.0-doorwayHalfSpan; Width=wallThickness; Height=doorwayHalfSpan*2.0 }
    | FloorGeneration.East -> { X=playfieldWidth-wallThickness; Y=playfieldHeight/2.0-doorwayHalfSpan; Width=wallThickness; Height=doorwayHalfSpan*2.0 }

/// The doors of the room the player is currently standing in, paired with the derived combat lock.
let currentRoomDoors model : (FloorGeneration.Door * DoorState) list =
    match Map.tryFind model.Floor.CurrentRoom model.Floor.Rooms with
    | None -> []
    | Some room ->
        room.Doors
        |> List.mapi (fun index door ->
            door, (model.M5Room.Doors |> List.tryItem index |> Option.defaultValue DoorState.Open))

/// The four room walls, with a gap wherever the current room has a door. A door cannot be drawn "at
/// its own wall" while no wall is drawn, and before M11 the room rendered as an unbounded void.
let roomWallsScene model =
    let directions = currentRoomDoors model |> List.map (fun (door, _) -> door.Direction) |> Set.ofList
    let hasDoor direction = Set.contains direction directions
    let slabs =
        [ if hasDoor FloorGeneration.North then
              yield { X=0.0;Y=0.0;Width=playfieldWidth/2.0-doorwayHalfSpan;Height=wallThickness }
              yield { X=playfieldWidth/2.0+doorwayHalfSpan;Y=0.0;Width=playfieldWidth/2.0-doorwayHalfSpan;Height=wallThickness }
          else yield { X=0.0;Y=0.0;Width=playfieldWidth;Height=wallThickness }
          if hasDoor FloorGeneration.South then
              yield { X=0.0;Y=playfieldHeight-wallThickness;Width=playfieldWidth/2.0-doorwayHalfSpan;Height=wallThickness }
              yield { X=playfieldWidth/2.0+doorwayHalfSpan;Y=playfieldHeight-wallThickness;Width=playfieldWidth/2.0-doorwayHalfSpan;Height=wallThickness }
          else yield { X=0.0;Y=playfieldHeight-wallThickness;Width=playfieldWidth;Height=wallThickness }
          if hasDoor FloorGeneration.West then
              yield { X=0.0;Y=0.0;Width=wallThickness;Height=playfieldHeight/2.0-doorwayHalfSpan }
              yield { X=0.0;Y=playfieldHeight/2.0+doorwayHalfSpan;Width=wallThickness;Height=playfieldHeight/2.0-doorwayHalfSpan }
          else yield { X=0.0;Y=0.0;Width=wallThickness;Height=playfieldHeight }
          if hasDoor FloorGeneration.East then
              yield { X=playfieldWidth-wallThickness;Y=0.0;Width=wallThickness;Height=playfieldHeight/2.0-doorwayHalfSpan }
              yield { X=playfieldWidth-wallThickness;Y=playfieldHeight/2.0+doorwayHalfSpan;Width=wallThickness;Height=playfieldHeight/2.0-doorwayHalfSpan }
          else yield { X=playfieldWidth-wallThickness;Y=0.0;Width=wallThickness;Height=playfieldHeight } ]
    Scene.group
        ((slabs |> List.map (fun slab -> Scene.filledRectangle slab stone))
         @ [ Scene.rectangleWithPaint
                 { X=wallThickness/2.0;Y=wallThickness/2.0
                   Width=playfieldWidth-wallThickness;Height=playfieldHeight-wallThickness }
                 (Paint.stroke stoneEdge 2.0) ])

/// The element id and handle a door presents, given the floor-graph state and the derived combat
/// lock. `HiddenWall` wins over the lock — a wall does not become a sealed door when enemies are
/// alive — and the lock wins over `Open`, because a sealed room really has no usable exit.
let doorPresentation (graphState: FloorGeneration.DoorState) (lock: DoorState) =
    match graphState, lock with
    | FloorGeneration.HiddenWall, _ -> "DoorHiddenWall", "scene/door/hidden-wall"
    | _, DoorState.BossSealed -> "DoorBossSealed", "scene/door/boss-sealed"
    | _, DoorState.LockedClear -> "DoorLockedClear", "scene/door/locked-clear"
    | FloorGeneration.LockedKey, _ -> "DoorLockedKey", "scene/door/locked-key"
    | FloorGeneration.BossDoor, _ -> "DoorBossDoor", "scene/door/boss-door"
    | FloorGeneration.Open, _ -> "DoorOpen", "scene/door/open"

/// How far past the wall, into the room, a door's threshold is drawn. The door then reads as a
/// frame you walk through rather than a stripe painted on the very edge of the screen.
let doorApron = 18.0

let private doorScene elementId direction =
    let opening = doorwayRect direction
    let horizontal = opening.Width > opening.Height
    // Wall-local frame: `along` runs across the doorway, `inward` runs into the room. Every door is
    // drawn in these terms, so one description serves all four walls.
    let sign =
        match direction with
        | FloorGeneration.North
        | FloorGeneration.West -> 1.0
        | _ -> -1.0
    let faceAlong = (if horizontal then opening.X + opening.Width/2.0 else opening.Y + opening.Height/2.0)
    let faceAcross =
        match direction with
        | FloorGeneration.North -> 0.0
        | FloorGeneration.South -> playfieldHeight
        | FloorGeneration.West -> 0.0
        | FloorGeneration.East -> playfieldWidth
    let at along inward : Point =
        if horizontal then { X=faceAlong+along; Y=faceAcross+sign*inward }
        else { X=faceAcross+sign*inward; Y=faceAlong+along }
    /// A rectangle in wall-local terms: centred on the doorway, spanning `halfAlong` either side and
    /// running from `fromInward` to `toInward` into the room.
    let slab halfAlong fromInward toInward : Rect =
        let a0, a1 = faceAlong - halfAlong, faceAlong + halfAlong
        let c0 = faceAcross + sign * (min fromInward toInward)
        let c1 = faceAcross + sign * (max fromInward toInward)
        let lo, hi = min c0 c1, max c0 c1
        if horizontal then { X=a0; Y=lo; Width=a1-a0; Height=hi-lo }
        else { X=lo; Y=a0; Width=hi-lo; Height=a1-a0 }
    let panel = slab doorwayHalfSpan 0.0 (wallThickness + doorApron)
    let span = doorwayHalfSpan
    match elementId with
    | "DoorOpen" ->
        // An opening you can walk through: a dark threshold punched past the wall, flanked by lit
        // jambs, with a chevron pointing OUT through the gap.
        Scene.group
            [ Scene.filledRectangle panel (color 16uy 11uy 20uy 255uy)
              Scene.filledRectangle (slab span 0.0 6.0) (color 75uy 196uy 122uy 255uy)
              Scene.filledRectangle (slab (span - 6.0) (wallThickness + doorApron - 5.0) (wallThickness + doorApron)) (color 52uy 132uy 88uy 255uy)
              Scene.line (at (-span + 3.0) 4.0) (at (-span + 3.0) (wallThickness + doorApron)) (Paint.stroke (color 96uy 220uy 146uy 255uy) 5.0)
              Scene.line (at (span - 3.0) 4.0) (at (span - 3.0) (wallThickness + doorApron)) (Paint.stroke (color 96uy 220uy 146uy 255uy) 5.0)
              Scene.line (at (-18.0) 26.0) (at 0.0 8.0) (Paint.stroke (color 150uy 255uy 190uy 255uy) 5.0)
              Scene.line (at 18.0 26.0) (at 0.0 8.0) (Paint.stroke (color 150uy 255uy 190uy 255uy) 5.0) ]
    | "DoorLockedKey" ->
        // A key door: a brass plate filling the whole doorway, with a keyhole in the middle of it.
        Scene.group
            [ Scene.filledRectangle panel (color 201uy 148uy 54uy 255uy)
              Scene.rectangleWithPaint panel (Paint.stroke (color 255uy 232uy 160uy 255uy) 4.0)
              Scene.circle (at 0.0 20.0) 9.0 (color 32uy 22uy 12uy 255uy)
              Scene.line (at 0.0 24.0) (at 0.0 36.0) (Paint.stroke (color 32uy 22uy 12uy 255uy) 7.0)
              Scene.line (at (-span + 10.0) 21.0) (at (-24.0) 21.0) (Paint.stroke (color 140uy 98uy 30uy 255uy) 4.0)
              Scene.line (at 24.0 21.0) (at (span - 10.0) 21.0) (Paint.stroke (color 140uy 98uy 30uy 255uy) 4.0) ]
    | "DoorBossDoor" ->
        // A boss doorway: ENTERABLE. Its mark is a toothed maw — deliberately built from a disc and
        // upright teeth, with NO diagonal stroke anywhere, because the sealed presentation below is an
        // X and a mark that is a visual SUBSET of another mark is not a distinct mark. These two
        // states mean opposite things ("the way on" versus "you cannot pass"), so they may not share
        // an idiom.
        Scene.group
            ([ Scene.filledRectangle panel (color 158uy 40uy 50uy 255uy)
               Scene.rectangleWithPaint panel (Paint.stroke (color 255uy 150uy 150uy 255uy) 4.0)
               Scene.filledEllipse (slab 30.0 8.0 34.0) (color 22uy 6uy 8uy 255uy) ]
             @ [ for offset in [ -21.0; -7.0; 7.0; 21.0 ] ->
                    Scene.line (at offset 10.0) (at offset 32.0) (Paint.stroke (color 255uy 226uy 170uy 255uy) 5.0) ])
    | "DoorHiddenWall" ->
        // A cracked wall. It reads as WALL, not door — but the seam tells a player where to bomb.
        //
        // The trim line is redrawn ACROSS the panel. Without it the panel punched a 112-unit hole in
        // the room's trim rectangle, and that break was far louder than the crack itself: it betrayed
        // every secret on the floor at a glance, which is the opposite of a hidden wall.
        Scene.group
            [ Scene.filledRectangle (slab doorwayHalfSpan 0.0 wallThickness) stone
              Scene.line (at (-doorwayHalfSpan) (wallThickness / 2.0)) (at doorwayHalfSpan (wallThickness / 2.0)) (Paint.stroke stoneEdge 2.0)
              Scene.line (at (-34.0) 3.0) (at (-12.0) 13.0) (Paint.stroke (color 168uy 152uy 184uy 255uy) 4.0)
              Scene.line (at (-12.0) 13.0) (at 6.0 4.0) (Paint.stroke (color 168uy 152uy 184uy 255uy) 4.0)
              Scene.line (at 6.0 4.0) (at 30.0 16.0) (Paint.stroke (color 168uy 152uy 184uy 255uy) 4.0)
              Scene.line (at (-4.0) 8.0) (at 3.0 21.0) (Paint.stroke (color 168uy 152uy 184uy 255uy) 3.0)
              Scene.line (at 12.0 9.0) (at 18.0 20.0) (Paint.stroke (color 168uy 152uy 184uy 255uy) 3.0) ]
    | "DoorLockedClear" ->
        // Sealed by combat: iron bars across the opening while anything in the room is still alive.
        Scene.group
            ([ Scene.filledRectangle panel (color 74uy 78uy 96uy 255uy)
               Scene.rectangleWithPaint panel (Paint.stroke (color 150uy 158uy 184uy 255uy) 3.0) ]
             @ [ for offset in [ -36.0; -12.0; 12.0; 36.0 ] ->
                    Scene.line (at offset 3.0) (at offset (wallThickness + doorApron - 3.0)) (Paint.stroke (color 196uy 204uy 226uy 255uy) 7.0) ])
    | _ ->
        // Sealed by the boss fight: a barred X. The horizontal bolt is what makes it categorically a
        // BARRIER rather than a marked doorway, and it is the mark the enterable boss door above does
        // not share. The X colour is pulled away from the enemy-bullet red so a sealed door and an
        // incoming projectile are not the same hue.
        Scene.group
            [ Scene.filledRectangle panel (color 96uy 20uy 28uy 255uy)
              Scene.rectangleWithPaint panel (Paint.stroke (color 176uy 58uy 66uy 255uy) 3.0)
              Scene.line (at (-span + 10.0) 6.0) (at (span - 10.0) (wallThickness + doorApron - 6.0)) (Paint.stroke (color 236uy 214uy 220uy 255uy) 7.0)
              Scene.line (at (-span + 10.0) (wallThickness + doorApron - 6.0)) (at (span - 10.0) 6.0) (Paint.stroke (color 236uy 214uy 220uy 255uy) 7.0)
              Scene.filledRectangle (slab (span - 4.0) (wallThickness / 2.0 + 2.0) (wallThickness / 2.0 + 12.0)) (color 236uy 214uy 220uy 255uy) ]

let private trapdoorBounds =
    { X=trapdoorCenter.Vx-trapdoorHalfWidth; Y=trapdoorCenter.Vy-trapdoorHalfHeight
      Width=trapdoorHalfWidth*2.0; Height=trapdoorHalfHeight*2.0 }

/// The trapdoor, drawn where the guard tests for it: the centre of the room.
///
/// The SHAPE is carried by the bright rim and the descending chevrons, not by the interior fill. A
/// near-black hole on a near-black floor measures about 1.05:1 and simply is not visible; the amber
/// furniture around it is what a player actually sees.
let trapdoorScene () =
    let lip = color 214uy 166uy 96uy 255uy
    Scene.group
        ([ Scene.filledRectangle trapdoorBounds (color 6uy 4uy 8uy 255uy)
           Scene.rectangleWithPaint trapdoorBounds (Paint.stroke lip 6.0)
           // A lit inner lip, so the opening reads as a hole with a near edge rather than a panel.
           Scene.line
               { X=trapdoorBounds.X+5.0;Y=trapdoorBounds.Y+5.0 }
               { X=trapdoorBounds.X+trapdoorBounds.Width-5.0;Y=trapdoorBounds.Y+5.0 }
               (Paint.stroke (color 255uy 214uy 150uy 255uy) 4.0) ]
         @ [ for row in [ -6.0; 4.0; 14.0 ] ->
                Scene.group
                    [ Scene.line
                        { X=trapdoorCenter.Vx-16.0;Y=trapdoorCenter.Vy+row-6.0 }
                        { X=trapdoorCenter.Vx;Y=trapdoorCenter.Vy+row } (Paint.stroke lip 4.0)
                      Scene.line
                        { X=trapdoorCenter.Vx+16.0;Y=trapdoorCenter.Vy+row-6.0 }
                        { X=trapdoorCenter.Vx;Y=trapdoorCenter.Vy+row } (Paint.stroke lip 4.0) ] ])

/// Drawn ONLY while the descent guard would accept: the room depicts a trapdoor and the player is
/// standing on it. Without this there is no on-screen difference between standing on the trapdoor and
/// standing beside it, while the guard demands an interact press — a keypress with no affordance.
let trapdoorReadyScene () =
    let halo = color 255uy 226uy 150uy 255uy
    let ring =
        { X=trapdoorBounds.X-10.0; Y=trapdoorBounds.Y-10.0
          Width=trapdoorBounds.Width+20.0; Height=trapdoorBounds.Height+20.0 }
    Scene.group
        [ Scene.rectangleWithPaint ring (Paint.stroke halo 3.0)
          Scene.circle { X=ring.X;Y=ring.Y } 5.0 halo
          Scene.circle { X=ring.X+ring.Width;Y=ring.Y } 5.0 halo
          Scene.circle { X=ring.X;Y=ring.Y+ring.Height } 5.0 halo
          Scene.circle { X=ring.X+ring.Width;Y=ring.Y+ring.Height } 5.0 halo
          Scene.textAt { X=trapdoorCenter.Vx-52.0;Y=trapdoorBounds.Y-22.0 } "E  DESCEND" halo ]

type RenderedElement =
    { ElementId: string
      Handle: string
      Layer: RenderLayer
      Scene: Scene }

let private rendered elementId handle layer scene =
    { ElementId=elementId; Handle=handle; Layer=layer; Scene=scene }

let renderedElementsIn grammar model : RenderedElement list =
    [ yield rendered "FloorBackground" "scene/floor-background" RenderLayer.FloorBackground
                (Scene.filledRectangle { X=0.;Y=0.;Width=playfieldWidth;Height=playfieldHeight } (color 27uy 19uy 32uy 255uy))

      for obstacle in model.M5Obstacles do
          yield rendered (obstacleId obstacle.Kind) (obstacleHandle obstacle.Kind) RenderLayer.Obstacles (obstacleScene obstacle)

      // Obstacle drops used to be drawn at X=80+index*24, Y=80 — directly under the currency readout,
      // on a layer BELOW the HUD, so a pickup a player must walk onto was clipped behind the word
      // "COIN". They are laid out in the room instead, clear of every doorway and of the HUD bands.
      // `M5ObstacleDrops` still carries no world position, so this is a legible placeholder rather
      // than a placed pickup; giving drops a position and a collection rule is separate roadmap work.
      for index, pickup in model.M5ObstacleDrops |> List.indexed do
          match pickupIdentity pickup with
          | Some(elementId, handle, radius, fill) ->
              yield rendered elementId handle RenderLayer.Pickups
                        (Scene.group
                            [ Scene.circle { X=300.0 + float index*52.0; Y=520.0 } (radius+3.0) (color 18uy 13uy 22uy 255uy)
                              Scene.circle { X=300.0 + float index*52.0; Y=520.0 } radius fill ])
          | None -> ()

      for index, (slot: Rogue3.Entities.ShopSlot) in model.M5ShopSlots |> List.indexed do
          let width =
              match slot.Offer with
              | ShopOffer.Item item -> 20.0 + float item.Quality*4.0
              | ShopOffer.Consumable _ -> 18.0
              | ShopOffer.Empty -> 12.0
          yield rendered "ShopItem" "scene/shop-item" RenderLayer.Pickups
                    (Scene.filledRectangle { X=520.0+float index*90.0;Y=160.0;Width=width;Height=20.0 } (color 166uy 116uy 232uy 255uy))

      yield rendered "RoomWalls" "scene/room-walls" RenderLayer.FloorDecals (roomWallsScene model)

      for door, lock in currentRoomDoors model do
          let elementId, handle = doorPresentation door.State lock
          yield rendered elementId handle RenderLayer.Obstacles (doorScene elementId door.Direction)

      match model.M5Room.Drop with
      | Some pickup ->
          match pickupIdentity pickup with
          | Some(_, _, radius, fill) ->
              yield rendered "RoomDrop" "scene/room-drop" RenderLayer.Pickups
                        (Scene.circle { X=640.0;Y=430.0 } (radius+2.0) fill)
          | None -> ()
      | None -> ()

      match model.M5Room.Reward with
      | Some reward ->
          yield rendered "RoomReward" "scene/room-reward" RenderLayer.Pickups
                    (Scene.filledRectangle { X=620.0;Y=450.0;Width=40.0+float reward.Quality*5.0;Height=26.0 } (color 190uy 126uy 255uy 255uy))
      | None -> ()

      // Rendered from the SAME predicate the descent guard tests, so the fixture a player sees is the
      // fixture the guard accepts. `M5Room.Trapdoor` alone can be true while the floor records no
      // fixture, and drawing that state would promise a descent the guard then refuses.
      if trapdoorPresent model then
          yield rendered "Trapdoor" "scene/trapdoor" RenderLayer.FloorDecals (trapdoorScene ())
          if canDescend model then
              yield rendered "TrapdoorReady" "scene/trapdoor-ready" RenderLayer.FloorDecals (trapdoorReadyScene ())

      let shadowPositions =
          model.PlayerPosition
          :: ((model.M5Enemies |> List.map _.Position)
              @ (model.M5Boss |> Option.map (fun boss -> [boss.Position]) |> Option.defaultValue []))
      yield rendered "Shadow" "scene/shadow" RenderLayer.Shadows
                (shadowPositions
                 |> List.map (fun position ->
                     Scene.filledEllipse { X=position.Vx-14.0;Y=position.Vy+8.0;Width=28.0;Height=8.0 } (color 0uy 0uy 0uy 64uy))
                 |> Scene.group)

      for actor in model.M5Enemies do
          let id = "Enemy" + string actor.Kind
          yield rendered id ("token/enemy/" + (string actor.Kind).ToLowerInvariant()) RenderLayer.Enemies
                    (Symbology.render grammar (enemyToken model.FloorIndex model.PlayerPosition actor))

      match model.M5Boss with
      | Some boss ->
          let id = "Boss" + string boss.Kind
          yield rendered id ("token/boss/" + (string boss.Kind).ToLowerInvariant()) RenderLayer.Enemies
                    (Symbology.render grammar (bossToken model boss))
      | None -> ()

      yield rendered "Player" "scene/player" RenderLayer.Player
                (Scene.group
                    [ Scene.circle (point model.PlayerPosition) 13.0 (color 126uy 227uy 255uy 255uy)
                      Scene.circle (point (add model.PlayerPosition (scale 16.0 model.Facing))) 3.0 (color 255uy 255uy 255uy 255uy) ])

      for shot in model.ShotSpawns do
          yield rendered "PlayerShot" "scene/player-shot" RenderLayer.Projectiles
                    (Scene.circle (point shot.Position) shot.Radius (color 127uy 227uy 255uy 255uy))
      for bullet in model.EnemyBullets do
          yield rendered "EnemyBullet" "scene/enemy-bullet" RenderLayer.Projectiles
                    (Scene.circle (point bullet.Position) bullet.Radius (color 255uy 90uy 90uy 255uy))
      for bomb in model.Bombs do
          yield rendered "PlacedBomb" "scene/placed-bomb" RenderLayer.Projectiles
                    (Scene.group
                        [ Scene.circle (point bomb.Position) 12.0 (color 35uy 35uy 42uy 255uy)
                          Scene.circle (point (add bomb.Position (vec2 8.0 -9.0))) 3.0 (color 255uy 150uy 48uy 255uy) ])

      if not model.M6Particles.IsEmpty then
          yield rendered "Particle" "effects/particle" RenderLayer.Particles
                    (particlesScene model.M6Particles)

      yield rendered "HudScore" "scene/hud-score" RenderLayer.Hud
                (hudSceneForSize { Width=1280;Height=720 } model)

      match model.LastRunSummary,model.RunOutcome with
      | Some summary,Some outcome ->
          let heading=match outcome with RunOutcome.Victory->"VICTORY"|RunOutcome.GameOver->"GAME OVER"
          let unlocks=if summary.UnlocksEarned.IsEmpty then "Unlocks: none" else "Unlocks: "+String.concat ", " summary.UnlocksEarned
          yield rendered "RunResultOverlay" "scene/run-result" RenderLayer.ScreenOverlays
                    (Scene.group
                        [ Scene.filledRectangle {X=250.0;Y=150.0;Width=780.0;Height=420.0} (color 12uy 10uy 18uy 235uy)
                          Scene.textAt {X=520.0;Y=220.0} heading (color 255uy 220uy 90uy 255uy)
                          Scene.textAt {X=470.0;Y=280.0} (sprintf "SCORE  %d" summary.Score) (color 255uy 255uy 255uy 255uy)
                          Scene.textAt {X=390.0;Y=330.0} (sprintf "Floors %d   Bosses %d   Kills %d" summary.FloorsCleared summary.BossKills summary.EnemyKills) (color 195uy 194uy 183uy 255uy)
                          Scene.textAt {X=390.0;Y=375.0} (sprintf "Coins %d   Items %d   No-hit floors %d" summary.CoinsCollected summary.ItemsCollected summary.NoHitFloors) (color 195uy 194uy 183uy 255uy)
                          Scene.textAt {X=390.0;Y=425.0} unlocks (color 126uy 227uy 255uy 255uy)
                          Scene.textAt {X=455.0;Y=500.0} "Choose an action below" (color 255uy 255uy 255uy 255uy) ])
      | _ -> () ]

let renderedElements model = renderedElementsIn Grammar.Token model

let cameraOffset model =
    match model.M6CameraTransition with
    | None -> zero
    | Some transition ->
        let remaining = 1.0 - min 1.0 (float transition.ElapsedTicks / float m6CameraDurationTicks)
        match transition.Direction with
        | RoomSlideDirection.North -> vec2 0.0 (-playfieldHeight * remaining)
        | RoomSlideDirection.East -> vec2 (playfieldWidth * remaining) 0.0
        | RoomSlideDirection.South -> vec2 0.0 (playfieldHeight * remaining)
        | RoomSlideDirection.West -> vec2 (-playfieldWidth * remaining) 0.0

let layersIn grammar model : LayerScene list =
    let elements = renderedElementsIn grammar model
    layerOrder
    |> List.map (fun layer ->
        { Layer=layer
          Scene=elements |> List.filter (fun item -> item.Layer=layer) |> List.map _.Scene |> Scene.group })

let layers model = layersIn Grammar.Token model

let viewIn grammar model : SceneNode =
    let all = layersIn grammar model
    let world = all |> List.take 9 |> List.map _.Scene |> Scene.group
    let offset = cameraOffset model
    let translatedWorld = if offset = zero then world else Scene.translate offset.Vx offset.Vy world
    Group [ translatedWorld; all.[9].Scene; all.[10].Scene ]

let view model = viewIn Grammar.Token model

let viewForSize size model =
    let all = layersIn Grammar.Token model
    let world = all |> List.take 9 |> List.map _.Scene |> Scene.group
    let offset = cameraOffset model
    let translatedWorld = if offset = zero then world else Scene.translate offset.Vx offset.Vy world
    Group [ translatedWorld; hudSceneForSize size model; all.[10].Scene ]
