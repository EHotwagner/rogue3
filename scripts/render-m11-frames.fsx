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

// 3. A combat room with live enemies: every doorway sealed until the room is cleared.
let combatRoomId = roomOfType FloorGeneration.Combat
shot "03-combat-room-sealed" (update (EnterM5Room combatRoomId) boot |> fst)

// 4. The boss room while the boss lives: every exit boss-sealed.
let bossRoomId = roomOfType FloorGeneration.Boss
let bossRoom = update (EnterM5Room bossRoomId) { boot with PlayerPosition = vec2 320.0 520.0 } |> fst
shot "04-boss-room-sealed" bossRoom

// 5. The boss room after the boss dies: exits open, reward on the floor, trapdoor at the centre.
let defeated = update (DamageM5Boss 100000.0) bossRoom |> fst
shot "05-boss-cleared-trapdoor" defeated

// 6. The player standing ON the trapdoor, which is the state `DescendFloor` now requires.
shot "06-standing-on-trapdoor" { defeated with PlayerPosition = trapdoorCenter }

// 7. The same combat room once cleared: the lock lifts, the real exits appear, and on this floor the
//    room also borders a still-hidden secret — so the cracked wall a player bombs is in the frame.
shot "07-combat-room-cleared-hidden-wall" (boot |> clearRoom combatRoomId |> update (EnterM5Room combatRoomId) |> fst)

// 8. A locked key door in play: the treasure room's cleared neighbour.
let treasureId = roomOfType FloorGeneration.Treasure
let treasureNeighbour =
    boot.Floor.Rooms.[treasureId].Doors
    |> List.pick (fun door -> if door.State = FloorGeneration.LockedKey then Some door.ToRoom else None)
shot "08-key-door-in-play" (boot |> clearRoom treasureNeighbour |> update (EnterM5Room treasureNeighbour) |> fst)


printfn "done"
