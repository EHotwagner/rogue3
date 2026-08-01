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

let private facingOf (actor: EnemyActor) =
    match actor.State with
    | EnemyState.ChargerWindUp(direction, _)
    | EnemyState.ChargerDash(direction, _)
    | EnemyState.Dive(direction, _) -> direction
    | _ -> vec2 1.0 0.0

let enemyToken floorIndex (actor: EnemyActor) : Token =
    let definition = scaledDefinition floorIndex actor.Kind
    let facing = facingOf actor
    { Symbology.defaultToken with
        Cx = actor.Position.Vx
        Cy = actor.Position.Vy
        R = definition.Radius
        Heading = atan2 facing.Vy facing.Vx
        Faction = Faction.Enemy
        Klass = klassOf actor.Kind
        Sigil = sigilOf actor.Kind
        Health = max 0.0 (min 1.0 (actor.HitPoints / definition.HitPoints))
        Threat = threatTier definition.Threat }

let enemyTokens model = model.M5Enemies |> List.map (enemyToken model.FloorIndex)

let legibility model = enemyTokens model |> Legibility.scoreIn Grammar.Token

let acceptedLegibility model =
    (legibility model).Findings
    |> List.forall (fun finding ->
        finding.Severity <> Legibility.Severity.Error
        && finding.Channel = Legibility.Channel.Size)

let private obstacles model =
    model.M5Obstacles
    |> List.map (fun obstacle ->
        let fill =
            match obstacle.Kind with
            | ObstacleKind.TintedRock -> color 110uy 90uy 74uy 255uy
            | ObstacleKind.Spikes -> color 138uy 138uy 154uy 255uy
            | ObstacleKind.Pit -> color 10uy 7uy 16uy 255uy
            | ObstacleKind.Pot -> color 110uy 82uy 54uy 255uy
            | ObstacleKind.Rock -> color 90uy 74uy 110uy 255uy
        Scene.circle (point obstacle.Position) 20.0 fill)
    |> Scene.group

let private pickups model =
    model.M5ObstacleDrops
    |> List.mapi (fun index pickup ->
        let fill =
            match pickup with
            | PickupKind.Coin1
            | PickupKind.Coin3 -> color 245uy 197uy 66uy 255uy
            | PickupKind.Key -> color 217uy 177uy 74uy 255uy
            | PickupKind.Bomb -> color 43uy 43uy 43uy 255uy
            | PickupKind.HalfRedHeart -> color 232uy 66uy 79uy 255uy
            | PickupKind.SoulHeart -> color 74uy 120uy 232uy 255uy
            | PickupKind.Nothing -> color 0uy 0uy 0uy 0uy
        Scene.circle { X=80.0 + float index*18.0; Y=80.0 } 6.0 fill)
    |> Scene.group

let private shadows model =
    let positions = model.PlayerPosition :: (model.M5Enemies |> List.map _.Position)
    positions
    |> List.map (fun position ->
        Scene.filledEllipse
            { X=position.Vx-14.0; Y=position.Vy+8.0; Width=28.0; Height=8.0 }
            (color 0uy 0uy 0uy 64uy))
    |> Scene.group

let private enemies model =
    enemyTokens model
    |> List.map Symbology.token
    |> Scene.group

let private player model =
    Scene.group
        [ Scene.circle (point model.PlayerPosition) 13.0 (color 126uy 227uy 255uy 255uy)
          Scene.circle (point (add model.PlayerPosition (scale 16.0 model.Facing))) 3.0 (color 255uy 255uy 255uy 255uy) ]

let private projectiles model =
    Scene.group
        [ for shot in model.ShotSpawns do
              Scene.circle (point shot.Position) shot.Radius (color 127uy 227uy 255uy 255uy)
          for bullet in model.EnemyBullets do
              Scene.circle (point bullet.Position) bullet.Radius (color 255uy 90uy 90uy 255uy) ]

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

let private particles model = model.M6Particles |> List.map particleScene |> Scene.group

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

let layers model : LayerScene list =
    [ { Layer=RenderLayer.FloorBackground; Scene=Scene.filledRectangle { X=0.;Y=0.;Width=playfieldWidth;Height=playfieldHeight } (color 27uy 19uy 32uy 255uy) }
      { Layer=RenderLayer.FloorDecals; Scene=Scene.empty }
      { Layer=RenderLayer.Obstacles; Scene=obstacles model }
      { Layer=RenderLayer.Pickups; Scene=pickups model }
      { Layer=RenderLayer.Shadows; Scene=shadows model }
      { Layer=RenderLayer.Enemies; Scene=enemies model }
      { Layer=RenderLayer.Player; Scene=player model }
      { Layer=RenderLayer.Projectiles; Scene=projectiles model }
      { Layer=RenderLayer.Particles; Scene=particles model }
      { Layer=RenderLayer.Hud; Scene=Scene.textAt { X=playfieldWidth/2.0-28.0;Y=28.0 } $"{model.LeftScore} : {model.RightScore}" (color 240uy 240uy 240uy 255uy) }
      { Layer=RenderLayer.ScreenOverlays; Scene=Scene.empty } ]

let view model : SceneNode =
    let all = layers model
    let world = all |> List.take 9 |> List.map _.Scene |> Scene.group
    let offset = cameraOffset model
    let translatedWorld = if offset = zero then world else Scene.translate offset.Vx offset.Vy world
    Group [ translatedWorld; all.[9].Scene; all.[10].Scene ]
