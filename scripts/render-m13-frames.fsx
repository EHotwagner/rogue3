// M13 render-and-look harness.
//
// Sibling of `scripts/render-m11-frames.fsx`. Rasterise the PRODUCTION frame (`Rogue3.View.view`, the
// same projection the shipped viewer draws) for each M13 row, so the frames can be looked at rather
// than argued about. Every row of this milestone was FOUND by looking at M11's frames; none of them
// is reachable by a requirements-derived test, so these frames are first-class evidence rather than
// decoration. Run after `dotnet build`:
//
//   dotnet fsi scripts/render-m13-frames.fsx
//
// Frames land in readiness/014-m13-room-transition-pickups-world-state/frames/<name>/.

#I "../src/Rogue3/bin/Debug/net10.0"
#r "FS.GG.UI.Scene.dll"
#r "FS.GG.UI.Symbology.dll"
#r "FS.GG.UI.SkiaViewer.dll"
#r "FS.GG.UI.Symbology.Render.dll"
#r "FS.GG.Game.Core.dll"
#r "FS.GG.UI.KeyboardInput.dll"
#r "Rogue3.dll"

open FS.GG.UI.Scene
open FS.GG.UI.Symbology.Render
open FS.GG.UI.KeyboardInput
open Rogue3
open Rogue3.Geometry
open Rogue3.Model

let outputRoot = "readiness/014-m13-room-transition-pickups-world-state/frames"
System.IO.Directory.CreateDirectory outputRoot |> ignore

let shot name model =
    let frame = { Nodes = [ View.view model ] }
    let path = Render.toPng { Width = 1280; Height = 720 } frame (System.IO.Path.Combine(outputRoot, name))
    let elements =
        Render.renderedElements model
        |> List.map _.ElementId
        |> List.countBy id
        |> List.sortBy fst
        |> List.map (fun (id, count) -> if count = 1 then id else $"{id}x{count}")
        |> String.concat " "
    printfn "%-34s %s" name path
    printfn "%-34s   elements: %s" "" elements

let boot = initialModel

// ------------------------------------------------------------------------------------------------
// 1-3. The crossing. This is the milestone's headline row: M11 measured that starting the slide
// would show 0.35 s of empty screen, and suppressed the slide rather than ship it. Three points of
// one east crossing, driven through the PRODUCTION input route so the transition is the one a player
// gets rather than one a fixture asserted.
// ------------------------------------------------------------------------------------------------

let key letter = ViewerKeyboard.toKeyId (ViewerKey.Letter letter)
let setKey letter down model = update (KeyChanged(key letter, down)) model |> fst
let tick model = update (Tick fixedDt) model |> fst

let steerStep (target: Vec2) model =
    let axis delta negative positive m =
        if delta > 2.0 then m |> setKey positive true |> setKey negative false
        elif delta < -2.0 then m |> setKey negative true |> setKey positive false
        else m |> setKey negative false |> setKey positive false
    model
    |> axis (target.Vx - model.PlayerPosition.Vx) 'A' 'D'
    |> axis (target.Vy - model.PlayerPosition.Vy) 'W' 'S'
    |> tick

let walkUntil target until budget model =
    let mutable current = model
    let mutable steps = 0
    while steps < budget && not (until current) do
        current <- steerStep target current
        steps <- steps + 1
    current

let departed = boot.Floor.CurrentRoom
let firstOpen = boot.Floor.Rooms.[departed].Doors |> List.find (fun door -> door.State = FloorGeneration.Open)
let doorwayTarget =
    let wall = wallMidpoint firstOpen.Direction
    match firstOpen.Direction with
    | FloorGeneration.North -> vec2 wall.Vx (wall.Vy + 4.0)
    | FloorGeneration.South -> vec2 wall.Vx (wall.Vy - 4.0)
    | FloorGeneration.West -> vec2 (wall.Vx + 4.0) wall.Vy
    | FloorGeneration.East -> vec2 (wall.Vx - 4.0) wall.Vy

let crossed = walkUntil doorwayTarget (fun m -> m.Floor.CurrentRoom <> departed) 900 boot
let atTick elapsed =
    { crossed with
        M6CameraTransition =
            crossed.M6CameraTransition |> Option.map (fun transition -> { transition with ElapsedTicks = elapsed }) }

// Sampled so the SET reads as one motion. A fresh-context visual critic pointed out that tick 0 is
// the departed room alone -- correct (the entered room is exactly one playfield away, which is M6's
// contract), and a real improvement on M11's black screen, but on its own it is static and carries no
// directional cue at all. Ticks 8, 21 and 34 each show BOTH rooms with the seam in a different place,
// so the direction of travel is readable from the frames rather than from the caption.
shot "01-crossing-start" (atTick 8)
shot "02-crossing-midpoint" (atTick 21)
shot "03-crossing-settling" (atTick 34)

// ------------------------------------------------------------------------------------------------
// 4-6. Placed and collectable world content.
// ------------------------------------------------------------------------------------------------

// Drops lie where the obstacles stood, and a player walks onto them. Smash every obstacle in the
// room through the production reducer, so the positions are the ones the game itself produces.
let smashed =
    boot.M5Obstacles
    |> List.fold (fun model obstacle -> update (DamageM5Obstacle(obstacle.Id, 999)) model |> fst) boot

// The whole pickup vocabulary, each one AT A REAL OBSTACLE POSITION. The first version of this frame
// substituted a 3x2 lattice of invented coordinates, which a visual critic caught: the frame that is
// supposed to prove "a drop lies where the obstacle stood" was overwriting exactly the fact it
// claimed. The weighted drop table rolls at most one or two pickups per room, so the frame seeds one
// of each kind onto the positions the room's own obstacles occupy, and prints them for the record.
let dropKinds =
    [ Entities.PickupKind.Coin1; Entities.PickupKind.Coin3; Entities.PickupKind.HalfRedHeart
      Entities.PickupKind.Key; Entities.PickupKind.Bomb; Entities.PickupKind.SoulHeart ]

printfn "real reducer drops: %A" (smashed.M5ObstacleDrops |> List.map (fun d -> d.Kind, d.Position.Vx, d.Position.Vy))
printfn "obstacle anchors:   %A" (boot.M5Obstacles |> List.map (fun o -> o.Kind, o.Position.Vx, o.Position.Vy))

shot
    "04-positioned-drops"
    { boot with
        M5ObstacleDrops =
            boot.M5Obstacles
            |> List.truncate dropKinds.Length
            |> List.mapi (fun index obstacle ->
                { Id = 6000 + index
                  Room = boot.Floor.CurrentRoom
                  Kind = dropKinds.[index]
                  Position = obstacle.Position })
        // The obstacles that dropped them are gone, which is what makes the position legible.
        M5Obstacles = boot.M5Obstacles |> List.skip (min boot.M5Obstacles.Length dropKinds.Length) }

// A priced, partly key-locked shop standing on the room floor, clear of the furniture.
let shopSlots, _, _ = Entities.generateShop (FS.GG.Game.Core.Rng.ofSeed 0xA55AUL) (Entities.itemPool [])
shot
    "05-shop-priced-and-locked"
    { boot with
        M5ShopSlots =
            shopSlots
            |> List.mapi (fun index slot -> if index = 1 then { slot with KeyLocked = true } else slot) }

// The boss reward on its plinth, with the shop absent so the placement of a single fixture is legible.
shot "06-boss-reward-placed" { boot with M5Room = { boot.M5Room with Reward = Some Entities.baseItems.Head } }

// ------------------------------------------------------------------------------------------------
// 7. The wall is the wall. Drive the player hard into the north wall and look at where it stops.
// ------------------------------------------------------------------------------------------------

let pressedNorth = walkUntil (vec2 200.0 -400.0) (fun _ -> false) 400 boot
printfn "pressed-north player=(%.2f, %.2f) wallThickness=%.1f radius=%.1f"
    pressedNorth.PlayerPosition.Vx pressedNorth.PlayerPosition.Vy Render.wallThickness playerRadius
shot "07-player-pressed-into-north-wall" pressedNorth

// ------------------------------------------------------------------------------------------------
// 8-11. The four world-space state visuals.
// ------------------------------------------------------------------------------------------------

shot "08-player-invulnerable" { boot with PostHitInvulnTicks = postHitInvulnTicks }
shot "09-player-dodge-roll" { boot with DodgeRollTicks = rollDurationTicks; PlayerVelocity = vec2 rollSpeed 0.0 }
// Dead AND at zero health. The first version showed a downed avatar under three full red hearts --
// a state play cannot reach, and the two indicators contradicting each other is the opposite of what
// this frame is for.
shot
    "10-player-down"
    { boot with
        PlayerLifeState = Dead
        PlayerHealth = { boot.PlayerHealth with RedHalfHearts = 0 } }

shot
    "11-enemy-telegraph"
    { boot with
        M5Enemies =
            // Both wind-ups are aimed AT THE PLAYER, which is what a telegraph is for. The first
            // version used arbitrary vectors and the Fly's arrow pointed noticeably away from the
            // threat it is supposed to be teaching you to dodge.
            let aimedAt (from: Vec2) = normalizeOrZero (sub boot.PlayerPosition from)
            [ { Entities.spawn 1 100 Entities.EnemyKind.Charger (vec2 380.0 260.0) with
                  State = Entities.EnemyState.ChargerWindUp(aimedAt (vec2 380.0 260.0), 20) }
              { Entities.spawn 1 101 Entities.EnemyKind.Fly (vec2 900.0 480.0) with
                  State = Entities.EnemyState.Dive(aimedAt (vec2 900.0 480.0), 15) } ] }

// ------------------------------------------------------------------------------------------------
// 12. The HUD, with every region carrying content, so a reviewer can see what each catalogue row
//     now covers on its own.
// ------------------------------------------------------------------------------------------------

shot
    "12-hud-regions"
    { boot with
        PlayerHealth = { RedContainers = 4; RedHalfHearts = 5; SoulHalfHearts = 3; BlackHalfHearts = 2 }
        PlayerCurrency = { Coins = 42; Keys = 3; Bombs = 7 }
        ActiveCharge = 4
        FloorNameTicks = 120
        Floor = { boot.Floor with MapRevealed = boot.Floor.Rooms |> Map.toList |> List.map fst |> Set.ofList } }

printfn "done"
