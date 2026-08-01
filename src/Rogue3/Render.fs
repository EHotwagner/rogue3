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
        Scene.filledEllipse { X=obstacle.Position.Vx-24.0;Y=obstacle.Position.Vy-14.0;Width=48.0;Height=28.0 } (color 10uy 7uy 16uy 255uy)

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

      for index, pickup in model.M5ObstacleDrops |> List.indexed do
          match pickupIdentity pickup with
          | Some(elementId, handle, radius, fill) ->
              yield rendered elementId handle RenderLayer.Pickups
                        (Scene.circle { X=80.0 + float index*24.0; Y=80.0 } radius fill)
          | None -> ()

      for index, (slot: Rogue3.Entities.ShopSlot) in model.M5ShopSlots |> List.indexed do
          let width =
              match slot.Offer with
              | ShopOffer.Item item -> 20.0 + float item.Quality*4.0
              | ShopOffer.Consumable _ -> 18.0
              | ShopOffer.Empty -> 12.0
          yield rendered "ShopItem" "scene/shop-item" RenderLayer.Pickups
                    (Scene.filledRectangle { X=520.0+float index*90.0;Y=160.0;Width=width;Height=20.0 } (color 166uy 116uy 232uy 255uy))

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

      if not model.M6Particles.IsEmpty then
          yield rendered "Particle" "effects/particle" RenderLayer.Particles
                    (model.M6Particles |> List.map particleScene |> Scene.group)

      yield rendered "HudScore" "scene/hud-score" RenderLayer.Hud
                (Scene.textAt { X=playfieldWidth/2.0-28.0;Y=28.0 } $"{model.LeftScore} : {model.RightScore}" (color 240uy 240uy 240uy 255uy)) ]

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
