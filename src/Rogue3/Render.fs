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

// M13: the HUD is described one REGION at a time and re-composed in the original order, so the scene
// handed to the viewer is byte-identical while the gameplay-visual inventory gains a row per region.
// One `HudScore` row covering hearts, currency, charge, banner and the whole minimap meant deleting
// the boss-room minimap colour left the coverage audit complete — a catalogue that cannot see the
// thing rot is not a catalogue.
let private hudHeartNodes (size: Size) (model: Model) =
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
    heartNodes

let private hudCurrencyNodes (size: Size) (model: Model) =
    let layout = hudLayoutForSize size
    let currency = sprintf "COIN %02d   KEY %02d   BOMB %02d" model.PlayerCurrency.Coins model.PlayerCurrency.Keys model.PlayerCurrency.Bombs
    [ Scene.textAt { X=layout.CurrencyBounds.X;Y=layout.CurrencyBounds.Y+18.0 } currency (color 255uy 244uy 205uy 255uy) ]

let private hudChargeNodes (size: Size) (model: Model) =
    let layout = hudLayoutForSize size
    let chargeRatio = if model.ActiveChargeMaximum<=0 then 0.0 else float model.ActiveCharge/float model.ActiveChargeMaximum
    let chargeText = sprintf "ACTIVE %d/%d" model.ActiveCharge model.ActiveChargeMaximum
    [ Scene.circle { X=layout.ChargeBounds.X+20.0;Y=layout.ChargeBounds.Y+20.0 } 15.0 (color 35uy 42uy 56uy 255uy)
      Scene.arc {X=layout.ChargeBounds.X+3.0;Y=layout.ChargeBounds.Y+3.0;Width=34.0;Height=34.0} -90.0 (360.0*chargeRatio) (Paint.stroke (color 42uy 120uy 214uy 255uy) 5.0)
      Scene.textAt { X=layout.ChargeBounds.X-35.0;Y=layout.ChargeBounds.Y+38.0 } chargeText (color 255uy 255uy 255uy 255uy) ]

let private hudMinimapNodes (size: Size) (model: Model) =
    let layout = hudLayoutForSize size
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
    [ Scene.filledRectangle layout.MinimapBounds (color 16uy 20uy 30uy 210uy)
      Scene.group mapNodes ]

let private hudFloorBannerNodes (size: Size) (model: Model) =
    let layout = hudLayoutForSize size
    let floorName = sprintf "%d — THE BURROWS" model.FloorIndex
    if model.FloorNameTicks>0 then [ Scene.textAt { X=layout.FloorNameBounds.X;Y=layout.FloorNameBounds.Y+24.0 } floorName (color 255uy 255uy 255uy 255uy) ] else []

/// The named HUD regions in the order they are composed. Splitting the inventory must not change one
/// byte of what the viewer receives, so `hudSceneForSize` is exactly this concatenation.
let hudRegionScenes (size: Size) (model: Model) =
    [ "HudHearts", "scene/hud/hearts", hudHeartNodes size model
      "HudCurrency", "scene/hud/currency", hudCurrencyNodes size model
      "HudActiveCharge", "scene/hud/active-charge", hudChargeNodes size model
      "HudMinimap", "scene/hud/minimap", hudMinimapNodes size model
      "HudFloorBanner", "scene/hud/floor-banner", hudFloorBannerNodes size model ]

let hudSceneForSize (size: Size) (model: Model) =
    hudRegionScenes size model |> List.collect (fun (_, _, nodes) -> nodes) |> Scene.group

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

/// Re-exported from `Model`, which owns the geometry so the drawn band and the player's collider are
/// one value (work item 014, DEC-008). Callers and tests keep reading `Render.wallThickness`.
let wallThickness = Rogue3.Model.wallThickness

/// A simulation rect in the scene vocabulary. Field-for-field: the two `Rect` records are
/// label-identical and deliberately kept as distinct types (see `Vec2.fs`).
let private sceneRect (r: Rogue3.Geometry.SimRect) : Rect =
    { X = r.X; Y = r.Y; Width = r.Width; Height = r.Height }

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

/// The doors of `roomId`, paired with the combat lock `locks` supplies for each index.
let roomDoorsOf roomId (locks: DoorState list) model : (FloorGeneration.Door * DoorState) list =
    match Map.tryFind roomId model.Floor.Rooms with
    | None -> []
    | Some room ->
        room.Doors
        |> List.mapi (fun index door ->
            door, (locks |> List.tryItem index |> Option.defaultValue DoorState.Open))

/// The doors of the room the player is currently standing in, paired with the derived combat lock.
let currentRoomDoors model : (FloorGeneration.Door * DoorState) list =
    roomDoorsOf model.Floor.CurrentRoom model.M5Room.Doors model

/// The four room walls, with a gap wherever the room has a door. A door cannot be drawn "at its own
/// wall" while no wall is drawn, and before M11 the room rendered as an unbounded void.
///
/// M13: the slabs come from `Model.roomWallSlabsFor`, the SAME value the player's swept cast uses, so
/// the band a player can see and the band a player stops at cannot drift apart.
let roomWallsSceneFor directions =
    Scene.group
        ((Rogue3.Model.roomWallSlabsFor directions |> List.map (fun slab -> Scene.filledRectangle (sceneRect slab) stone))
         @ [ Scene.rectangleWithPaint
                 { X=wallThickness/2.0;Y=wallThickness/2.0
                   Width=playfieldWidth-wallThickness;Height=playfieldHeight-wallThickness }
                 (Paint.stroke stoneEdge 2.0) ])

let roomWallsScene model =
    roomWallsSceneFor (currentRoomDoors model |> List.map (fun (door, _) -> door.Direction) |> Set.ofList)

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
///
/// Re-exported from `Model`, which needs it to keep a placed fixture out from under a door panel.
let doorApron = Rogue3.Model.doorApron

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

// ------------------------------------------------------------------------------------------------
// M13 placed room fixtures. Shop stock and the reward pedestal stand on a plinth at a position
// `Model.placeRoomFixtures` chose for THIS room, and a shop slot states its terms: the price under
// the stock, and a brass keyhole plate when the slot wants a key instead of coins.
// ------------------------------------------------------------------------------------------------

let private plinth (at: Vec2) =
    Scene.filledEllipse { X=at.Vx-26.0;Y=at.Vy+6.0;Width=52.0;Height=14.0 } (color 52uy 42uy 60uy 255uy)

let shopSlotScene (at: Vec2) (slot: Rogue3.Entities.ShopSlot) =
    let stock =
        match slot.Offer with
        | ShopOffer.Item item ->
            let width = 20.0 + float item.Quality*4.0
            [ Scene.filledRectangle { X=at.Vx-width/2.0;Y=at.Vy-22.0;Width=width;Height=22.0 } (color 166uy 116uy 232uy 255uy)
              Scene.rectangleWithPaint { X=at.Vx-width/2.0;Y=at.Vy-22.0;Width=width;Height=22.0 } (Paint.stroke (color 226uy 200uy 255uy 255uy) 2.0) ]
        | ShopOffer.Consumable _ ->
            [ Scene.circle { X=at.Vx;Y=at.Vy-12.0 } 11.0 (color 132uy 208uy 236uy 255uy) ]
        | ShopOffer.Empty ->
            // An emptied slot is a bare plinth with a dashed outline: distinct from stocked, and it
            // carries no price, because there is nothing left to charge for.
            [ Scene.rectangleWithPaint { X=at.Vx-11.0;Y=at.Vy-22.0;Width=22.0;Height=22.0 } (Paint.stroke (color 96uy 88uy 104uy 255uy) 2.0) ]
    let priceLabel =
        match slot.Offer with
        | ShopOffer.Empty -> []
        | _ when slot.KeyLocked ->
            // A key-locked slot costs a KEY, not coins, so it says so rather than showing a price the
            // player cannot pay with. The brass plate and keyhole are the same idiom as a key door.
            [ Scene.filledRectangle { X=at.Vx-15.0;Y=at.Vy+22.0;Width=30.0;Height=20.0 } (color 201uy 148uy 54uy 255uy)
              Scene.circle { X=at.Vx;Y=at.Vy+30.0 } 5.0 (color 32uy 22uy 12uy 255uy)
              Scene.line { X=at.Vx;Y=at.Vy+32.0 } { X=at.Vx;Y=at.Vy+38.0 } (Paint.stroke (color 32uy 22uy 12uy 255uy) 4.0) ]
        | _ ->
            [ Scene.textAt { X=at.Vx-14.0;Y=at.Vy+34.0 } (sprintf "%dc" slot.Price) (color 255uy 225uy 92uy 255uy) ]
    Scene.group ((plinth at :: stock) @ priceLabel)

let roomRewardScene (at: Vec2) (reward: Rogue3.Entities.ItemDefinition) =
    let width = 26.0 + float reward.Quality*5.0
    Scene.group
        [ plinth at
          Scene.filledRectangle { X=at.Vx-width/2.0;Y=at.Vy-26.0;Width=width;Height=26.0 } (color 190uy 126uy 255uy 255uy)
          Scene.rectangleWithPaint { X=at.Vx-width/2.0;Y=at.Vy-26.0;Width=width;Height=26.0 } (Paint.stroke (color 244uy 226uy 255uy 255uy) 2.0)
          Scene.circle { X=at.Vx;Y=at.Vy-38.0 } 5.0 (color 255uy 244uy 205uy 255uy) ]

let private floorBackgroundScene =
    Scene.filledRectangle { X=0.;Y=0.;Width=playfieldWidth;Height=playfieldHeight } (color 27uy 19uy 32uy 255uy)

// ------------------------------------------------------------------------------------------------
// M13 room transition. `cameraOffset` translates the world a FULL playfield at `remaining = 1.0`, so
// on its own it does not slide a room in — it slides the only room away and leaves the screen empty.
// M11 measured that and chose not to start the slide at all.
//
// The fix is a second room, not a shorter slide. `roomShellScene` draws one room's shell; the
// renderer emits it pre-translated one playfield BACK along the slide axis, so the single existing
// `Scene.translate` in `viewIn` carries both rooms and the product still has exactly one camera
// transform. The M6 contract — one room away at tick 0, identity at 42 — is untouched.
// ------------------------------------------------------------------------------------------------

/// Where the departed room sits relative to the entered room, in world units.
let departedRoomStep direction =
    match direction with
    | RoomSlideDirection.North -> vec2 0.0 playfieldHeight
    | RoomSlideDirection.South -> vec2 0.0 -playfieldHeight
    | RoomSlideDirection.East -> vec2 -playfieldWidth 0.0
    | RoomSlideDirection.West -> vec2 playfieldWidth 0.0

/// One room's shell: floor, wall band, doors and the trapdoor fixture when the floor records one.
///
/// Doors are read from the floor graph with the combat lock LIFTED, which is honest rather than
/// convenient: `playerRoomIntentsIn` refuses a crossing through a sealed doorway, so the only room a
/// player can be sliding away from is a room whose lock has already lifted.
let roomShellScene roomId model =
    match Map.tryFind roomId model.Floor.Rooms with
    | None -> Scene.group []
    | Some room ->
        let directions = room.Doors |> List.map _.Direction |> Set.ofList
        let doors =
            room.Doors
            |> List.map (fun door ->
                let elementId, _ = doorPresentation door.State DoorState.Open
                doorScene elementId door.Direction)
        let trapdoor =
            if room.Fixtures |> List.contains FloorGeneration.Trapdoor then [ trapdoorScene () ] else []
        Scene.group ([ floorBackgroundScene; roomWallsSceneFor directions ] @ doors @ trapdoor)

/// The departed room's shell, placed one room back along the slide axis, or nothing when no crossing
/// is in flight.
let departedRoomScene model =
    match model.M6CameraTransition with
    | None -> None
    | Some transition ->
        let step = departedRoomStep transition.Direction
        Some (Scene.translate step.Vx step.Vy (roomShellScene transition.FromRoom model))

// ------------------------------------------------------------------------------------------------
// M13 world-space state. Four states that decide whether a run continues had no visual at all: the
// player is untouchable for 0.80 s after a hit and 0.40 s into a roll, is committed to a 0.45 s roll
// that ignores the speed clamp, can be down while the frame still shows a live-looking disc, and an
// enemy can be a fifth of a second from a charge that the frame does not warn about.
//
// Each is drawn ON the actor it describes, in world space, so it slides with the room and reads at
// the place the player is already looking.
// ------------------------------------------------------------------------------------------------

/// Invulnerable: a broken ring around the player. Broken rather than solid so it cannot be mistaken
/// for the solid shot/bullet discs, and gapped on the diagonals so the facing pip stays readable.
let playerInvulnerableScene model =
    let p = point model.PlayerPosition
    let ring = { X=p.X-20.0; Y=p.Y-20.0; Width=40.0; Height=40.0 }
    let arc from sweep = Scene.arc ring from sweep (Paint.stroke (color 226uy 244uy 255uy 220uy) 3.0)
    Scene.group [ arc -80.0 70.0; arc 10.0 70.0; arc 100.0 70.0; arc 190.0 70.0 ]

/// Rolling: a wedge of motion trailing the player's velocity, so the direction and the commitment are
/// both visible. Drawn from the velocity rather than the aim, because a roll ignores aim.
let playerDodgeRollScene model =
    let heading = if model.PlayerVelocity = zero then normalizeOrZero model.Facing else normalizeOrZero model.PlayerVelocity
    let back = sub model.PlayerPosition (scale 26.0 heading)
    let side = vec2 -heading.Vy heading.Vx
    Scene.group
        [ Scene.line (point (add back (scale 9.0 side))) (point (add model.PlayerPosition (scale 9.0 side))) (Paint.stroke (color 126uy 227uy 255uy 150uy) 4.0)
          Scene.line (point (sub back (scale 9.0 side))) (point (sub model.PlayerPosition (scale 9.0 side))) (Paint.stroke (color 126uy 227uy 255uy 150uy) 4.0)
          Scene.circle (point back) 6.0 (color 126uy 227uy 255uy 90uy) ]

/// Down: the disc goes grey and gains a cross, so a dead player is not a live-looking cyan disc.
let playerDownScene model =
    let p = point model.PlayerPosition
    Scene.group
        [ Scene.circle p 15.0 (color 62uy 56uy 68uy 235uy)
          Scene.line { X=p.X-9.0;Y=p.Y-9.0 } { X=p.X+9.0;Y=p.Y+9.0 } (Paint.stroke (color 232uy 66uy 79uy 255uy) 4.0)
          Scene.line { X=p.X-9.0;Y=p.Y+9.0 } { X=p.X+9.0;Y=p.Y-9.0 } (Paint.stroke (color 232uy 66uy 79uy 255uy) 4.0) ]

/// An enemy committed to a wind-up, dash or dive: a warning bar along the direction it committed to,
/// on the floor-decal layer so it reads as ground marking rather than as another actor.
let telegraphOf (actor: EnemyActor) =
    let committed =
        match actor.State with
        | EnemyState.ChargerWindUp(direction, _)
        | EnemyState.ChargerDash(direction, _)
        | EnemyState.Dive(direction, _) -> Some direction
        | _ -> None
    committed
    |> Option.map (fun direction ->
        let heading = if direction = zero then vec2 0.0 -1.0 else scale (1.0 / (max 1e-9 (sqrt (direction.Vx*direction.Vx + direction.Vy*direction.Vy)))) direction
        let far = add actor.Position (scale 96.0 heading)
        let side = vec2 -heading.Vy heading.Vx
        Scene.group
            [ Scene.line (point actor.Position) (point far) (Paint.stroke (color 255uy 122uy 48uy 130uy) 10.0)
              Scene.line (point (add far (scale 14.0 side))) (point far) (Paint.stroke (color 255uy 190uy 96uy 200uy) 4.0)
              Scene.line (point (sub far (scale 14.0 side))) (point far) (Paint.stroke (color 255uy 190uy 96uy 200uy) 4.0) ])

type RenderedElement =
    { ElementId: string
      Handle: string
      Layer: RenderLayer
      Scene: Scene }

let private rendered elementId handle layer scene =
    { ElementId=elementId; Handle=handle; Layer=layer; Scene=scene }

let renderedElementsIn grammar model : RenderedElement list =
    let placements =
        Rogue3.Model.placeRoomFixtures model.M5Obstacles (model.M5ShopSlots.Length + (if model.M5Room.Reward.IsSome then 1 else 0))
    [ yield rendered "FloorBackground" "scene/floor-background" RenderLayer.FloorBackground floorBackgroundScene

      // M13: the room being LEFT, drawn one playfield back along the slide axis. Emitted on the
      // floor-background layer so the one camera translate in `viewIn` carries it with everything else.
      match departedRoomScene model with
      | Some scene -> yield rendered "DepartedRoom" "scene/departed-room" RenderLayer.FloorBackground scene
      | None -> ()

      for obstacle in model.M5Obstacles do
          yield rendered (obstacleId obstacle.Kind) (obstacleHandle obstacle.Kind) RenderLayer.Obstacles (obstacleScene obstacle)

      // M13: a drop lies where the obstacle stood, and `Model.collectFloorPickups` lets a player walk
      // onto it. It used to be drawn at X=300+index*52, Y=520 — an indexed strip that was neither the
      // pot's position nor collectable, because `M5ObstacleDrops` carried no position at all.
      for pickup in model.M5ObstacleDrops do
          match pickupIdentity pickup.Kind with
          | Some(elementId, handle, radius, fill) ->
              yield rendered elementId handle RenderLayer.Pickups
                        (Scene.group
                            [ Scene.circle (point pickup.Position) (radius+3.0) (color 18uy 13uy 22uy 255uy)
                              Scene.circle (point pickup.Position) radius fill ])
          | None -> ()

      // M13: shop stock stands on the room floor at a placed position, and says what it costs.
      // It used to be drawn at X=520+index*90, Y=160 — a fixed screen row that sits on top of the
      // pot in M11's `13-shop-and-reward` frame, with no price and no lock state anywhere.
      for index, (slot: Rogue3.Entities.ShopSlot) in model.M5ShopSlots |> List.indexed do
          let at = placements |> List.tryItem index |> Option.defaultValue (vec2 (playfieldWidth/2.0) 520.0)
          yield rendered "ShopItem" "scene/shop-item" RenderLayer.Pickups (shopSlotScene at slot)

      match model.M5Room.Reward with
      | Some reward ->
          let at = placements |> List.tryItem model.M5ShopSlots.Length |> Option.defaultValue (vec2 (playfieldWidth/2.0) 440.0)
          yield rendered "RoomReward" "scene/room-reward" RenderLayer.Pickups (roomRewardScene at reward)
      | None -> ()

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

      // M13: the four states that decide whether a player lives, drawn in the world on the actor they
      // describe. Before this they were invisible model fields — `PostHitInvulnTicks`, `DodgeIFrameTicks`,
      // `DodgeRollTicks`, `PlayerLifeState` and an enemy's wind-up all changed how the game behaved
      // with nothing on screen to say so.
      if model.PlayerLifeState = Alive && (model.PostHitInvulnTicks > 0 || model.DodgeIFrameTicks > 0) then
          yield rendered "PlayerInvulnerable" "scene/player-invulnerable" RenderLayer.Player (playerInvulnerableScene model)

      if model.DodgeRollTicks > 0 then
          yield rendered "PlayerDodgeRoll" "scene/player-dodge-roll" RenderLayer.Player (playerDodgeRollScene model)

      if model.PlayerLifeState = Dead then
          yield rendered "PlayerDown" "scene/player-down" RenderLayer.Player (playerDownScene model)

      for actor in model.M5Enemies do
          match telegraphOf actor with
          | Some scene -> yield rendered "EnemyTelegraph" "scene/enemy-telegraph" RenderLayer.FloorDecals scene
          | None -> ()

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

      // M13: one element per HUD REGION. `hudSceneForSize` composes exactly these node lists in this
      // order, so the viewer's scene is unchanged while the coverage audit can now see a region rot.
      for elementId, handle, nodes in hudRegionScenes { Width=1280;Height=720 } model do
          if not (List.isEmpty nodes) then
              yield rendered elementId handle RenderLayer.Hud (Scene.group nodes)

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
