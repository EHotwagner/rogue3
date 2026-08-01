// M11 render-and-look harness.
//
// Rasterise the PRODUCTION frame (`Rogue3.View.view`, the same projection the shipped viewer draws)
// for each room and door state a player meets, so the frames can be looked at rather than argued
// about. Run after `dotnet build`:
//
//   dotnet fsi scripts/render-m11-frames.fsx
//
// Frames land in readiness/012-m11-playability-visual-legibility/frames/<name>/.

#I "../src/Rogue3/bin/Debug/net10.0"
#r "FS.GG.UI.Scene.dll"
#r "FS.GG.UI.Symbology.dll"
#r "FS.GG.UI.SkiaViewer.dll"
#r "FS.GG.UI.Symbology.Render.dll"
#r "FS.GG.Game.Core.dll"
#r "Rogue3.dll"

open FS.GG.UI.Scene
open FS.GG.UI.Symbology.Render
open Rogue3
open Rogue3.Geometry
open Rogue3.Model

let outputRoot = "readiness/012-m11-playability-visual-legibility/frames"
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
    printfn "%-28s %s" name path
    printfn "%-28s   elements: %s" "" elements

/// Rewrite the current room's floor-graph doors, so a single frame can show every door state.
let withDoors states (model: Model) =
    let roomId = model.Floor.CurrentRoom
    let room = model.Floor.Rooms.[roomId]
    let doors =
        states
        |> List.mapi (fun index (direction, state) ->
            { FloorGeneration.ToRoom = 700 + index
              FloorGeneration.Direction = direction
              FloorGeneration.State = state })
    { model with
        Floor = { model.Floor with Rooms = Map.add roomId { room with Doors = doors } model.Floor.Rooms }
        M5Room = { model.M5Room with Doors = doors |> List.map (fun _ -> Entities.DoorState.Open) } }

let withLock lock (model: Model) =
    { model with M5Room = { model.M5Room with Doors = model.M5Room.Doors |> List.map (fun _ -> lock) } }

let boot = initialModel

// 1. The room a player actually boots into.
shot "01-start-room" boot

// 2. Every floor-graph door state at once, one per wall.
let allStates =
    boot
    |> withDoors
        [ FloorGeneration.North, FloorGeneration.Open
          FloorGeneration.East, FloorGeneration.LockedKey
          FloorGeneration.South, FloorGeneration.BossDoor
          FloorGeneration.West, FloorGeneration.HiddenWall ]
shot "02-all-door-states" allStates

/// Mark a room cleared on the floor, the way killing its last enemy does, so its doorways stop
/// showing the combat lock and show whatever the floor graph says instead.
let clearRoom roomId (model: Model) =
    { model with Floor = FloorGeneration.recordRoomCleared roomId model.Floor }

let roomOfType kind =
    boot.Floor.Rooms
    |> Map.toList
    |> List.pick (fun (id, room) -> if room.RoomType = kind then Some id else None)

/// Walk to `target` the way a player does — across doors, through `TraverseDoor`, clearing each room
/// on the way. This matters for the frames: `EnterM5Room` teleports by id and never touches
/// `Floor.MapRevealed`, so a frame captured that way shows a MINIMAP THAT IS A LIE — the start room
/// marked current while the player stands somewhere else entirely.
let travelTo target (model: Model) =
    let rec route visited roomId =
        if roomId = target then Some [ roomId ]
        else
            model.Floor.Rooms.[roomId].Doors
            |> List.filter (fun door -> door.State = FloorGeneration.Open || door.State = FloorGeneration.BossDoor)
            |> List.filter (fun door -> not (Set.contains door.ToRoom visited))
            |> List.tryPick (fun door ->
                route (Set.add door.ToRoom visited) door.ToRoom |> Option.map (fun rest -> roomId :: rest))

    match route (Set.singleton model.Floor.CurrentRoom) model.Floor.CurrentRoom with
    | None -> update (EnterM5Room target) model |> fst
    | Some path ->
        path
        |> List.skip 1
        |> List.fold
            (fun current roomId ->
                let cleared =
                    current.M5Enemies
                    |> List.map _.Id
                    |> List.fold (fun m enemyId -> update (DamageM5Enemy(enemyId, 99999.0)) m |> fst) current
                update (TraverseDoor roomId) cleared |> fst)
            model

// 3. A combat room with live enemies: every doorway sealed until the room is cleared.
let combatRoomId = roomOfType FloorGeneration.Combat
shot "03-combat-room-sealed" (update (EnterM5Room combatRoomId) boot |> fst)

// 4. The boss room while the boss lives: every exit boss-sealed. Reached by crossing doors, so the
//    minimap shows the rooms the player really walked through.
let bossRoomId = roomOfType FloorGeneration.Boss
let bossRoom = travelTo bossRoomId boot
shot "04-boss-room-sealed" bossRoom

// 5. The boss room after the boss dies: exits open, reward on the floor, trapdoor at the centre.
let defeated = update (DamageM5Boss 100000.0) { bossRoom with PlayerPosition = vec2 320.0 520.0 } |> fst
shot "05-boss-cleared-trapdoor" defeated

// 6. The player standing ON the trapdoor — the state `DescendFloor` requires, and now a visibly
//    different frame rather than one where the player merely covers the chevrons.
shot "06-standing-on-trapdoor" { defeated with PlayerPosition = trapdoorCenter }

// 7. A cleared combat room: the lock lifts, the real exits appear, and on this floor the room also
//    borders a still-hidden secret, so the cracked wall a player bombs is in the frame.
shot "07-combat-room-cleared-hidden-wall" (boot |> clearRoom combatRoomId |> travelTo combatRoomId)

// 8. A locked key door in play: the treasure room's cleared neighbour.
let treasureId = roomOfType FloorGeneration.Treasure
let treasureNeighbour =
    boot.Floor.Rooms.[treasureId].Doors
    |> List.pick (fun door -> if door.State = FloorGeneration.LockedKey then Some door.ToRoom else None)
shot "08-key-door-in-play" (boot |> clearRoom treasureNeighbour |> travelTo treasureNeighbour)



// ------------------------------------------------------------------------------------------------
// 9. The contact sheet. Every remaining catalogued element, so no declared gameplay visual ships
//    without a human having looked at it. The independent critic found two real defects here that no
//    door frame could have surfaced.
// ------------------------------------------------------------------------------------------------
let inventoryFrame name model = shot name model

inventoryFrame
    "09-pickups-and-drops"
    { boot with
        M5ObstacleDrops =
            [ Entities.PickupKind.Coin1; Entities.PickupKind.Coin3; Entities.PickupKind.HalfRedHeart
              Entities.PickupKind.Key; Entities.PickupKind.Bomb; Entities.PickupKind.SoulHeart ]
        M5Room = { boot.M5Room with Drop = Some Entities.PickupKind.Key } }

inventoryFrame
    "10-enemy-roster"
    { boot with
        M5Enemies =
            Entities.roster
            |> List.mapi (fun index kind ->
                Entities.spawn 3 (500 + index) kind (vec2 (200.0 + float (index % 4) * 200.0) (220.0 + float (index / 4) * 200.0))) }

inventoryFrame "11-boss-hollow-choir" { boot with M5Boss = Some(Entities.spawnBoss 700 Entities.BossKind.HollowChoir (vec2 640.0 300.0)) }
inventoryFrame "12-boss-maw" { boot with M5Boss = Some(Entities.spawnBoss 701 Entities.BossKind.Maw (vec2 640.0 300.0)) }

inventoryFrame
    "13-shop-and-reward"
    (let slots, _, _ = Entities.generateShop (FS.GG.Game.Core.Rng.ofSeed 0xA55AUL) (Entities.itemPool [])
     { boot with
         M5ShopSlots = slots
         M5Room = { boot.M5Room with Reward = Some Entities.baseItems.Head } })

inventoryFrame
    "14-projectiles-and-bombs"
    { boot with
        ShotSpawns = spawnShots 1 1 (vec2 520.0 360.0) zero (vec2 1.0 0.0) basePlayerStats
        EnemyBullets = [ for index in 0..5 -> { Id=index; Position=vec2 (700.0 + float index*40.0) 320.0; Velocity=zero; Radius=4.0; Damage=1; Homing=0.0; AgeTicks=0 } ]
        Bombs = [ { Id=1; Position=vec2 460.0 440.0; FuseTicks=bombFuseTicks } ] }

inventoryFrame "15-particles" (update (SpawnM6Particles(220, vec2 640.0 360.0, ParticleTint.Explosion)) boot |> fst)

inventoryFrame
    "16-run-result-overlay"
    (finishRun false (Some DeathCause.Trap) { boot with RunActive = true; RunStats = { emptyRunStats with DepthReached = 3 } })

printfn "done"
