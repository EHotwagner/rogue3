module Rogue3.GameplayVisualInventory

// Runtime-owned visual inventory and registry. This is deliberately independent of
// tests/Rogue3.Tests/element-visuals.catalog: the catalog describes dispositions, while this module
// declares the gameplay elements that MUST receive one and supplies the projection the real View uses.

open FS.GG.UI.Scene
open FS.GG.UI.Symbology
open Microsoft.FSharp.Reflection
open Rogue3.Model
open Rogue3.Geometry

type GameplayVisualElement =
    | Ball
    | LeftPaddle
    | RightPaddle
    | Score
    | Playfield
    | EnemyGrub | EnemyMaggot | EnemySpitter | EnemyFly
    | EnemyCharger | EnemyTurret | EnemyCaster | EnemyBrute
    | Particle

let all =
    FSharpType.GetUnionCases typeof<GameplayVisualElement>
    |> Array.map (fun case -> FSharpValue.MakeUnion(case, [||]) :?> GameplayVisualElement)
    |> Array.toList

let elementId =
    function
    | Ball -> "Ball"
    | LeftPaddle -> "LeftPaddle"
    | RightPaddle -> "RightPaddle"
    | Score -> "Score"
    | Playfield -> "Playfield"
    | EnemyGrub -> "EnemyGrub"
    | EnemyMaggot -> "EnemyMaggot"
    | EnemySpitter -> "EnemySpitter"
    | EnemyFly -> "EnemyFly"
    | EnemyCharger -> "EnemyCharger"
    | EnemyTurret -> "EnemyTurret"
    | EnemyCaster -> "EnemyCaster"
    | EnemyBrute -> "EnemyBrute"
    | Particle -> "Particle"

type VisualBinding =
    { Element: GameplayVisualElement
      Handle: string
      RequiredStates: (string * Model) list
      Project: Model -> Scene }

type RuntimeProjection =
    { Element: GameplayVisualElement
      Handle: string
      Scene: Scene }

let private foreground: Color =
    { Red = 240uy
      Green = 240uy
      Blue = 240uy
      Alpha = 255uy }

let private accent: Color =
    { Red = 120uy
      Green = 200uy
      Blue = 255uy
      Alpha = 255uy }

let private playfieldFill: Color =
    { Red = 18uy
      Green = 22uy
      Blue = 30uy
      Alpha = 255uy }

let private stepped = stepSim initialModel
let private movedLeft = movePaddle LeftSide PaddleDown initialModel
let private scored = { initialModel with LeftScore = initialModel.LeftScore + 1 }
let private m6ParticleModel = update (SpawnM6Particles(1, initialModel.PlayerPosition, ParticleTint.Explosion)) initialModel |> fst
let private enemyKind = function
    | EnemyGrub -> Some Rogue3.Entities.EnemyKind.Grub
    | EnemyMaggot -> Some Rogue3.Entities.EnemyKind.Maggot
    | EnemySpitter -> Some Rogue3.Entities.EnemyKind.Spitter
    | EnemyFly -> Some Rogue3.Entities.EnemyKind.Fly
    | EnemyCharger -> Some Rogue3.Entities.EnemyKind.Charger
    | EnemyTurret -> Some Rogue3.Entities.EnemyKind.Turret
    | EnemyCaster -> Some Rogue3.Entities.EnemyKind.Caster
    | EnemyBrute -> Some Rogue3.Entities.EnemyKind.Brute
    | _ -> None

let private binding =
    function
    | Ball ->
        { Element = Ball
          Handle = "scene/ball"
          RequiredStates = [ "initial", initialModel; "advanced", stepped ]
          Project =
            fun model ->
                let ball = model.Ball.Pos
                { Nodes = [ Rectangle((ball.Vx - 6.0, ball.Vy - 6.0, 12.0, 12.0), accent) ] } }
    | LeftPaddle ->
        { Element = LeftPaddle
          Handle = "scene/left-paddle"
          RequiredStates = [ "initial", initialModel; "moved", movedLeft ]
          Project =
            fun model ->
                { Nodes = [ Rectangle((16.0, model.LeftPaddleY, 8.0, model.PaddleHeight), foreground) ] } }
    | RightPaddle ->
        { Element = RightPaddle
          Handle = "scene/right-paddle"
          RequiredStates = [ "initial", initialModel ]
          Project =
            fun model ->
                { Nodes =
                    [ Rectangle(
                          (model.Playfield.Vx - 24.0, model.RightPaddleY, 8.0, model.PaddleHeight),
                          foreground
                      ) ] } }
    | Score ->
        { Element = Score
          Handle = "scene/score"
          RequiredStates = [ "initial", initialModel; "scored", scored ]
          Project =
            fun model ->
                { Nodes =
                    [ Text(
                          (model.Playfield.Vx / 2.0 - 28.0, 28.0),
                          $"{model.LeftScore} : {model.RightScore}",
                          foreground
                      ) ] } }
    | Playfield ->
        { Element = Playfield
          Handle = "scene/playfield"
          RequiredStates = [ "initial", initialModel ]
          Project =
            fun model ->
                { Nodes = [ Rectangle((0.0, 0.0, model.Playfield.Vx, model.Playfield.Vy), playfieldFill) ] } }
    | Particle ->
        { Element = Particle
          Handle = "effects/particle"
          RequiredStates = [ "spawned", m6ParticleModel ]
          Project = fun model -> model.M6Particles |> List.map Rogue3.Render.particleScene |> Scene.group }
    | element ->
        let kind = enemyKind element |> Option.get
        let actor = Rogue3.Entities.spawn 1 100 kind (vec2 300.0 300.0)
        let state = { initialModel with M5Enemies = [ actor ] }
        { Element = element
          Handle = "token/enemy/" + (elementId element).Substring(5).ToLowerInvariant()
          RequiredStates = [ "roster", state ]
          Project = fun model ->
              let live = model.M5Enemies |> List.tryFind (fun candidate -> candidate.Kind = kind) |> Option.defaultValue actor
              Symbology.token (Rogue3.Render.enemyToken model.FloorIndex live) }

let bindings = all |> List.map binding

let registeredBindings =
    bindings
    |> List.map (fun item -> elementId item.Element, item.Handle)

let representativeModels =
    let rosterModel =
        Rogue3.Entities.roster
        |> List.mapi (fun index kind -> Rogue3.Entities.spawn 1 (200+index) kind (vec2 (180.0+float index*100.0) 300.0))
        |> fun enemies -> { m6ParticleModel with M5Enemies=enemies }
    [ initialModel; stepped; movedLeft; scored; rosterModel ]

let project (model: Model) : RuntimeProjection list =
    bindings
    |> List.map (fun item ->
        { Element = item.Element
          Handle = item.Handle
          Scene = item.Project model })
