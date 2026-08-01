module Rogue3M11PlayabilityLegibilityTests

// M11: playability and visual legibility.
//
// THE RULE THIS FILE EXISTS TO ENFORCE: a claim about what a player can do is proven by driving the
// PRODUCTION INPUT ROUTE — `KeyChanged` and `Tick`, the only two messages the shipped host produces
// from a keypress. M10 proved the door reducers correct while nothing dispatched them, because
// coverage was measured over messages the product defines rather than actions a player can take.
// Where a test below does dispatch a door message directly it says so and names the test that
// proved that message reachable from a key.

open System
open System.IO
open Expecto
open FS.GG.UI.Scene
open FS.GG.UI.KeyboardInput
open FS.GG.Game.Harness
open FS.GG.Audio.Core
open FS.GG.UI.SkiaViewer
open Rogue3
open Rogue3.Geometry
open Rogue3.FloorGeneration
open Rogue3.Model

// ------------------------------------------------------------------------------------------------
// The production input route, and nothing else.
// ------------------------------------------------------------------------------------------------

let private key letter = ViewerKeyboard.toKeyId (Letter letter)

let private setKey letter down model =
    update (KeyChanged(key letter, down)) model |> fst

let private tick model = update (Tick fixedDt) model |> fst

/// Hold whichever movement keys drive the player toward `target`, then advance one fixed step. Only
/// `KeyChanged` and `Tick` are ever dispatched, so this is exactly what a player holding WASD does.
let private steerStep (target: Vec2) model =
    let axis delta negative positive m =
        if delta > 4.0 then m |> setKey positive true |> setKey negative false
        elif delta < -4.0 then m |> setKey negative true |> setKey positive false
        else m |> setKey negative false |> setKey positive false

    model
    |> axis (target.Vx - model.PlayerPosition.Vx) 'A' 'D'
    |> axis (target.Vy - model.PlayerPosition.Vy) 'W' 'S'
    |> tick

/// Walk toward `target` until `until` holds or the step budget runs out. Returns the model and the
/// number of fixed steps spent, so a test can assert the walk actually happened.
let private walkUntil target until budget model =
    let mutable current = model
    let mutable steps = 0
    while steps < budget && not (until current) do
        current <- steerStep target current
        steps <- steps + 1
    current, steps

/// A point in the doorway on `direction`: the wall midpoint pulled just inside the room. The player
/// cannot literally reach it (collision holds the centre one radius off the wall) but steering at it
/// presses the player into the sensor, which is what a player does.
let private doorwayTarget direction =
    let wall = wallMidpoint direction
    match direction with
    | North -> vec2 wall.Vx (wall.Vy + 4.0)
    | South -> vec2 wall.Vx (wall.Vy - 4.0)
    | West -> vec2 (wall.Vx + 4.0) wall.Vy
    | East -> vec2 (wall.Vx - 4.0) wall.Vy

let private releaseAll model =
    [ 'W'; 'A'; 'S'; 'D'; 'E' ] |> List.fold (fun m letter -> setKey letter false m) model

let private boot = initialModel

/// The centre of everything a scene draws, in logical room coordinates. Stroked geometry reports
/// conservative bounds, so a door's PLACEMENT is asserted through its centre rather than its extent.
let private centreOf scene =
    SceneInspection.inspect { X = 0.0; Y = 0.0; Width = playfieldWidth; Height = playfieldHeight } scene
    |> List.filter _.Contributes
    |> List.choose (fun node ->
        match node.Bounds with
        | SceneDrawableBounds.Known bounds -> Some bounds
        | _ -> None)
    |> function
        | [] -> None
        | bounds ->
            let left = bounds |> List.map _.X |> List.min
            let top = bounds |> List.map _.Y |> List.min
            let right = bounds |> List.map (fun b -> b.X + b.Width) |> List.max
            let bottom = bounds |> List.map (fun b -> b.Y + b.Height) |> List.max
            Some((left + right) / 2.0, (top + bottom) / 2.0)

let private currentRoom (model: Model) = model.Floor.Rooms.[model.Floor.CurrentRoom]

let private doorTowards direction (model: Model) =
    currentRoom model |> fun room -> room.Doors |> List.find (fun door -> door.Direction = direction)

let private roomOfType kind (model: Model) =
    model.Floor.Rooms |> Map.toList |> List.find (fun (_, room) -> room.RoomType = kind) |> fst

/// Kill everything alive in the loaded room. This dispatches `DamageM5Enemy` directly: it is combat
/// staging, not a door claim, and M3 already proves that message is reachable from a fired shot.
let private audioFromHost effects =
    effects
    |> List.collect (function ViewerEffect.PlayAudio batch -> batch | _ -> [])
    |> Audio.interpret
    |> _.Requested

let private clearLoadedRoom (model: Model) =
    model.M5Enemies
    |> List.map _.Id
    |> List.fold (fun current enemyId -> update (DamageM5Enemy(enemyId, 99999.0)) current |> fst) model

// ------------------------------------------------------------------------------------------------

[<Tests>]
let m11PlayabilityLegibilityTests =
    testList
        "M11 playability and visual legibility"
        [ test "FR-007 the room a player boots into presents the floor graph's real exits" {
              Expect.isNonEmpty (currentRoom boot).Doors "the floor graph gives the START room exits"
              Expect.equal
                  boot.M5Room.Doors.Length
                  (currentRoom boot).Doors.Length
                  "the derived combat-lock projection carries one entry per floor-graph door, not an empty list"

              let doorElements =
                  Render.renderedElements boot
                  |> List.filter (fun element -> element.ElementId.StartsWith "Door")

              Expect.equal
                  doorElements.Length
                  (currentRoom boot).Doors.Length
                  "and every one of them is drawn in the frame a player boots into"

              Expect.isTrue
                  (Render.renderedElements boot |> List.exists (fun element -> element.ElementId = "RoomWalls"))
                  "the room a player boots into has walls, so its exits sit on something"
          }

          test "FR-001 a player walks out of the starting room and back, from keys and ticks alone" {
              let start = boot.Floor.CurrentRoom
              let north = doorTowards North boot
              Expect.equal north.State Open "the starting room's north door is crossable"

              let crossed, outSteps =
                  walkUntil (doorwayTarget North) (fun m -> m.Floor.CurrentRoom <> start) 900 boot

              Expect.equal crossed.Floor.CurrentRoom north.ToRoom "walking into the doorway crossed it"
              Expect.isGreaterThan outSteps 1 "the crossing took real movement, not a single step"
              Expect.isTrue crossed.Floor.Rooms.[north.ToRoom].Visited "the destination is marked visited"
              Expect.contains crossed.Floor.MapRevealed north.ToRoom "and revealed on the map"
              Expect.floatClose
                  Accuracy.medium
                  crossed.PlayerPosition.Vy
                  (playfieldHeight - doorwayClearance)
                  "the player arrives at the reciprocal doorway of the destination"

              // The arrival must sit OUTSIDE the doorway sensor, or a crossing would re-trigger itself
              // and the player would bounce between two rooms forever.
              Expect.isFalse
                  (doorwaySensorContains South crossed.PlayerPosition)
                  "the arrival clears the sensor of the doorway it came through"

              // The destination is a live combat room, so its doorways seal. Walking at the door does
              // not get the player out until the room is cleared — that is the rule, not a defect.
              Expect.isFalse crossed.M5Room.Cleared "the destination is a live combat room"
              let pressing, _ = walkUntil (doorwayTarget South) (fun _ -> false) 240 (releaseAll crossed)
              Expect.equal pressing.Floor.CurrentRoom north.ToRoom "a sealed doorway cannot be walked back through"

              // Cross into a room that is already clear, let go of every key, and stand there: the
              // arrival must not re-cross by itself.
              let intoOpenRoom, _ =
                  walkUntil
                      (doorwayTarget North)
                      (fun m -> m.Floor.CurrentRoom <> start)
                      900
                      { boot with Floor = recordRoomCleared north.ToRoom boot.Floor }

              let idle =
                  [ 1..180 ] |> List.fold (fun m _ -> tick m) (releaseAll intoOpenRoom)

              Expect.equal idle.Floor.CurrentRoom north.ToRoom "an arrival into an open room does not bounce back"

              let returned, backSteps =
                  walkUntil (doorwayTarget South) (fun m -> m.Floor.CurrentRoom <> north.ToRoom) 900 idle

              Expect.equal returned.Floor.CurrentRoom start "walking back through the doorway returns to the start room"
              Expect.isGreaterThan backSteps 1 "the return took real movement too"
              Expect.isTrue returned.Floor.Rooms.[start].Cleared "the room the player returns to is as they left it"
          }

          test "FR-002 walking into a key door with a key opens it and spends exactly one key" {
              let treasure = roomOfType Treasure boot
              let neighbour =
                  boot.Floor.Rooms.[treasure].Doors
                  |> List.pick (fun door -> if door.State = LockedKey then Some door.ToRoom else None)

              let locked (model: Model) fromRoom toRoom =
                  model.Floor.Rooms.[fromRoom].Doors
                  |> List.exists (fun door -> door.ToRoom = toRoom && door.State = LockedKey)

              // Stand in the neighbour, cleared, holding one key.
              let atDoor =
                  { boot with Floor = { boot.Floor with Rooms = boot.Floor.Rooms } }
                  |> fun model -> { model with Floor = recordRoomCleared neighbour model.Floor }
                  |> update (EnterM5Room neighbour)
                  |> fst
                  |> fun model -> { model with PlayerCurrency = { model.PlayerCurrency with Keys = 1 } }

              Expect.isTrue (locked atDoor neighbour treasure) "the approach record starts LockedKey"
              Expect.isTrue (locked atDoor treasure neighbour) "and so does its reciprocal record"

              let direction = (atDoor.Floor.Rooms.[neighbour].Doors |> List.find (fun door -> door.ToRoom = treasure)).Direction

              let unlocked, steps =
                  walkUntil (doorwayTarget direction) (fun m -> not (locked m neighbour treasure)) 900 atDoor

              Expect.isGreaterThan steps 1 "the player really walked to the door"
              Expect.isFalse (locked unlocked neighbour treasure) "walking into the key door opened the approach record"
              Expect.isFalse (locked unlocked treasure neighbour) "and its reciprocal record"
              Expect.equal unlocked.PlayerCurrency.Keys 0 "exactly one key was spent"

              // Continuing to hold into the doorway crosses it, and never charges a second key.
              let entered, _ = walkUntil (doorwayTarget direction) (fun m -> m.Floor.CurrentRoom = treasure) 900 unlocked
              Expect.equal entered.Floor.CurrentRoom treasure "the now-open door is crossable by the same gesture"
              Expect.equal entered.PlayerCurrency.Keys 0 "and crossing never charges again"

              // With no key at all, the same approach changes nothing.
              let keyless = { atDoor with PlayerCurrency = { atDoor.PlayerCurrency with Keys = 0 } }
              let refused, _ = walkUntil (doorwayTarget direction) (fun _ -> false) 240 keyless
              Expect.isTrue (locked refused neighbour treasure) "with no key the door stays locked"
              Expect.equal refused.Floor.CurrentRoom neighbour "and the player stays put"
              Expect.equal refused.PlayerCurrency keyless.PlayerCurrency "and no currency moves"
          }

          test "FR-003 every rendered door comes from the current room's floor-graph door records" {
              let rooms =
                  boot.Floor.Rooms
                  |> Map.toList
                  |> List.map (fun (roomId, _) -> update (EnterM5Room roomId) boot |> fst)

              for model in rooms do
                  let graphDoors = (currentRoom model).Doors
                  let rendered =
                      Render.renderedElements model
                      |> List.filter (fun element -> element.ElementId.StartsWith "Door")

                  Expect.equal
                      rendered.Length
                      graphDoors.Length
                      $"room {model.Floor.CurrentRoom} draws exactly one door per floor-graph door record"

                  let expected =
                      graphDoors
                      |> List.mapi (fun index door ->
                          let lock = model.M5Room.Doors |> List.tryItem index |> Option.defaultValue Entities.DoorState.Open
                          Render.doorPresentation door.State lock)

                  Expect.equal
                      (rendered |> List.map (fun element -> element.ElementId, element.Handle))
                      expected
                      $"room {model.Floor.CurrentRoom} draws the presentation the graph state and the combat lock imply"
          }

          test "FR-004 each door is drawn on its own wall and every state looks different" {
              let states =
                  [ North, Open, Entities.DoorState.Open
                    East, LockedKey, Entities.DoorState.Open
                    South, BossDoor, Entities.DoorState.Open
                    West, HiddenWall, Entities.DoorState.Open
                    North, Open, Entities.DoorState.LockedClear
                    North, BossDoor, Entities.DoorState.BossSealed ]

              let frames =
                  states
                  |> List.map (fun (direction, graphState, lock) ->
                      let roomId = boot.Floor.CurrentRoom
                      let room = boot.Floor.Rooms.[roomId]
                      let model =
                          { boot with
                              Floor =
                                  { boot.Floor with
                                      Rooms =
                                          Map.add
                                              roomId
                                              { room with Doors = [ { ToRoom = 900; Direction = direction; State = graphState } ] }
                                              boot.Floor.Rooms }
                              M5Room = { boot.M5Room with Doors = [ lock ] } }
                      let element =
                          Render.renderedElements model
                          |> List.find (fun item -> item.ElementId.StartsWith "Door")
                      direction, element)

              let ids = frames |> List.map (fun (_, element) -> element.ElementId)
              Expect.equal (List.distinct ids) ids "the six door presentations are six distinct elements"

              let handles = frames |> List.map (fun (_, element) -> element.Handle)
              Expect.equal (List.distinct handles) handles "each carries its own stable handle"

              let digests =
                  frames |> List.map (fun (_, element) -> (SceneCodec.export element.Scene).CanonicalBytes)
              Expect.equal (List.distinct digests).Length digests.Length "and each draws something visibly different"

              // Each door must be CENTRED ON THE WALL ITS DIRECTION NAMES. Before M11 every door was
              // drawn as an indexed strip at a fixed X=590+index*46, Y=48 — the same place regardless
              // of which wall the door was on, and regardless of how many walls the room had.
              for direction, element in frames do
                  let doorway = Render.doorwayRect direction
                  let centre = centreOf element.Scene
                  Expect.isSome centre $"the {direction} door draws something with bounds"
                  let cx, cy = Option.get centre
                  Expect.floatClose
                      { Accuracy.medium with absolute = 12.0 }
                      cx
                      (doorway.X + doorway.Width / 2.0)
                      $"the {direction} door is centred on its own wall horizontally"
                  Expect.floatClose
                      { Accuracy.medium with absolute = 16.0 }
                      cy
                      (doorway.Y + doorway.Height / 2.0)
                      $"the {direction} door is centred on its own wall vertically"

              // And in a real four-door room the four doors land in four different places.
              let bootCentres =
                  Render.renderedElements boot
                  |> List.filter (fun element -> element.ElementId.StartsWith "Door")
                  |> List.choose (fun element -> centreOf element.Scene)
              Expect.hasLength bootCentres 4 "the starting room draws four doors"
              Expect.equal (List.distinct bootCentres).Length 4 "each at its own wall, not stacked in one indexed strip"
          }

          test "FR-005 the gameplay-visual inventory covers every door presentation and the room walls" {
              let declared = GameplayVisualInventory.all |> List.map GameplayVisualInventory.elementId |> Set.ofList
              for required in [ "DoorOpen"; "DoorLockedKey"; "DoorBossDoor"; "DoorHiddenWall"; "DoorLockedClear"; "DoorBossSealed"; "RoomWalls"; "Trapdoor" ] do
                  Expect.isTrue (Set.contains required declared) $"{required} is declared in the production-owned inventory"

              // `binding.Project` ends in `Scene.group`, and `Scene.group []` still yields one node —
              // so asserting on `scene.Nodes` here would pass for an element production never emitted.
              // Ask the production renderer directly instead.
              for binding in GameplayVisualInventory.bindings do
                  for state, model in binding.RequiredStates do
                      let elementId = GameplayVisualInventory.elementId binding.Element
                      let emitted =
                          Render.renderedElements model
                          |> List.filter (fun item -> item.ElementId = elementId && item.Handle = binding.Handle)
                      Expect.isNonEmpty emitted $"{elementId}/{state} is emitted by the production renderer"
                      Expect.isNonEmpty (binding.Project model).Nodes $"{elementId}/{state} projects a scene"
          }

          test "FR-006 the committed render-and-look frames exist for every room and door state" {
              let root =
                  Path.Combine(__SOURCE_DIRECTORY__, "..", "..", "readiness", "012-m11-playability-visual-legibility", "frames")

              Expect.isTrue (Directory.Exists root) "the render-and-look evidence directory is committed"

              // Frames 09-16 are the contact sheet: every catalogued element that no room-or-door frame
              // shows. An independent critic found two real defects in them that no door frame could
              // have surfaced, which is the argument for covering the whole inventory rather than the
              // subject of the milestone.
              let expected =
                  [ "01-start-room"; "02-all-door-states"; "03-combat-room-sealed"; "04-boss-room-sealed"
                    "05-boss-cleared-trapdoor"; "06-standing-on-trapdoor"; "07-combat-room-cleared-hidden-wall"
                    "08-key-door-in-play"; "09-pickups-and-drops"; "10-enemy-roster"; "11-boss-hollow-choir"
                    "12-boss-maw"; "13-shop-and-reward"; "14-projectiles-and-bombs"; "15-particles"
                    "16-run-result-overlay" ]

              Expect.equal
                  (Directory.GetDirectories root |> Array.map Path.GetFileName |> Array.sort |> Array.toList)
                  (List.sort expected)
                  "the committed frame set is exactly the authored one"

              for frame in expected do
                  let directory = Path.Combine(root, frame)
                  Expect.isTrue (Directory.Exists directory) $"{frame} was rendered"
                  Expect.isNonEmpty (Directory.GetFiles(directory, "*.png")) $"{frame} committed a production-frame PNG"
          }

          test "FR-008 DescendFloor is refused unless the room depicts a trapdoor and the player is on it" {
              // The starting room has no trapdoor: this is the exact state the shipped build descended
              // from, and the reason level progression's journeys passed without a floor ever existing.
              let refused = update DescendFloor { boot with PlayerPosition = trapdoorCenter } |> fst
              Expect.equal refused.FloorIndex boot.FloorIndex "no trapdoor fixture means no descent"
              Expect.equal (Replay.modelBytes refused) (Replay.modelBytes boot) "and the model is untouched"

              let bossId = roomOfType Boss boot
              let cleared =
                  { boot with Floor = clearBoss bossId boot.Floor } |> update (EnterM5Room bossId) |> fst

              Expect.isTrue cleared.M5Room.Trapdoor "clearing the boss leaves a trapdoor the room presents"

              let offTrapdoor = { cleared with PlayerPosition = vec2 200.0 200.0 }
              let stillHere = update DescendFloor offTrapdoor |> fst
              Expect.equal stillHere.FloorIndex boot.FloorIndex "standing away from the trapdoor does not descend"

              let onTrapdoor = { cleared with PlayerPosition = trapdoorCenter }
              let descended = update DescendFloor onTrapdoor |> fst
              Expect.equal descended.FloorIndex (boot.FloorIndex + 1) "standing on it does"
          }

          test "FR-008 the interact key descends only from a trapdoor, through keys and ticks alone" {
              let bossId = roomOfType Boss boot
              let cleared =
                  { boot with Floor = clearBoss bossId boot.Floor }
                  |> update (EnterM5Room bossId)
                  |> fst
                  |> clearLoadedRoom
                  |> fun model -> { model with PlayerPosition = trapdoorCenter }

              let pressed = cleared |> setKey 'E' true |> tick
              Expect.equal pressed.FloorIndex (boot.FloorIndex + 1) "the interact key on the trapdoor descends a floor"
              Expect.equal pressed.Floor.CurrentRoom 0 "and lands in the new floor's START room"
              Expect.equal pressed.Floor.Rooms.[0].RoomType Start "which really is a START room"
              Expect.isNonEmpty pressed.M5Room.Doors "loaded through the production room seam, so it presents its exits"

              // The same keypress somewhere with no trapdoor changes nothing.
              let elsewhere = boot |> setKey 'E' true |> tick
              Expect.equal elsewhere.FloorIndex boot.FloorIndex "the interact key alone never descends"
          }

          test "FR-009 the trapdoor is reachable: cross doors to the boss room, beat it, descend" {
              // Hop one is walked with keys, which is what "reachable" means; the remaining hops use
              // the same `TraverseDoor` message that walk dispatches (proved by FR-001 above), because
              // steering a player through five rooms of obstacles is not what this row is about.
              let bossId = roomOfType Boss boot

              let rec path visited roomId =
                  if roomId = bossId then Some [ roomId ]
                  else
                      boot.Floor.Rooms.[roomId].Doors
                      |> List.filter (fun door -> door.State = Open || door.State = BossDoor)
                      |> List.filter (fun door -> not (Set.contains door.ToRoom visited))
                      |> List.tryPick (fun door ->
                          path (Set.add door.ToRoom visited) door.ToRoom
                          |> Option.map (fun rest -> roomId :: rest))

              let route = path (Set.singleton boot.Floor.CurrentRoom) boot.Floor.CurrentRoom
              Expect.isSome route "the boss room is reachable from the starting room through crossable doors"
              let route = Option.get route

              let firstHop = route |> List.item 1
              let firstDirection = (doorTowards ((currentRoom boot).Doors |> List.find (fun door -> door.ToRoom = firstHop) |> _.Direction) boot).Direction
              let walked, steps = walkUntil (doorwayTarget firstDirection) (fun m -> m.Floor.CurrentRoom = firstHop) 900 boot
              Expect.equal walked.Floor.CurrentRoom firstHop "the first hop of the route is walked, not dispatched"
              Expect.isGreaterThan steps 1 "and it took real movement"

              let atBoss =
                  route
                  |> List.skip 2
                  |> List.fold
                      (fun model roomId ->
                          model
                          |> clearLoadedRoom
                          |> update (TraverseDoor roomId)
                          |> fst)
                      (releaseAll walked |> clearLoadedRoom)

              Expect.equal atBoss.Floor.CurrentRoom bossId "the walk reaches the boss room"
              Expect.isSome atBoss.M5Boss "which has a live boss in it"
              Expect.isFalse atBoss.M5Room.Trapdoor "and no trapdoor until the boss falls"

              let defeated = update (DamageM5Boss 100000.0) atBoss |> fst
              Expect.isNone defeated.M5Boss "the boss is defeated"
              Expect.hasLength
                  (defeated.Floor.Rooms.[bossId].Fixtures |> List.filter ((=) Trapdoor))
                  1
                  "the floor records exactly one trapdoor"
              Expect.isTrue defeated.M5Room.Trapdoor "the loaded room presents it"
              Expect.isTrue
                  (Render.renderedElements defeated |> List.exists (fun element -> element.ElementId = "Trapdoor"))
                  "and the production frame draws it"

              let onIt = { defeated with PlayerPosition = trapdoorCenter } |> setKey 'E' true |> tick
              Expect.equal onIt.FloorIndex (boot.FloorIndex + 1) "and a player standing on it descends"
          }

          test "FR-010 a production journey boots, moves, crosses a door and returns" {
              let start = boot.Floor.CurrentRoom
              let north = doorTowards North boot

              // Two runner-issued journeys, so every claim below is read off runner OUTPUT rather than
              // off a re-simulation of the same script. The first must END in the room behind the north
              // door; the second, booted from nowhere but the product, must end back in the start room.
              let outbound =
                  [ yield JourneyEvent.Start
                    yield JourneyEvent.KeyInput(Letter 'W', true)
                    for _ in 1..320 -> JourneyEvent.FixedTick ]

              let out = PerformanceEvidence.runM11RoundTripJourney outbound

              Expect.equal (JourneyReceipt.result out.Receipt) JourneyResult.Passed "every issued event mapped to a production message"
              Expect.isTrue (JourneyReceipt.terminalPredicateReached out.Receipt) "the outbound journey reached its terminal predicate"
              Expect.equal out.Final.Floor.CurrentRoom north.ToRoom "the journey crossed into the room behind the north door"
              Expect.notEqual out.Final.Floor.CurrentRoom start "which is not the room it started in"

              let inbound =
                  [ yield JourneyEvent.KeyInput(Letter 'W', false)
                    yield JourneyEvent.KeyInput(Letter 'S', true)
                    for _ in 1..320 -> JourneyEvent.FixedTick ]

              let back =
                  inbound
                  |> List.fold
                      (fun model event ->
                          match event with
                          | JourneyEvent.KeyInput(Letter letter, pressed) -> setKey letter pressed model
                          | _ -> tick model)
                      out.Final

              Expect.equal back.Floor.CurrentRoom start "and the same held-key gesture brings the player home"
              Expect.equal (JourneyReceipt.steps out.Receipt) (List.length outbound) "the runner consumed the whole outbound script"
          }

          test "FR-011 an unwired player action reports Unbound instead of being inexpressible" {
              // Asking to cross a wall the current room has no door on is a displayed action nothing
              // binds, and the runner must SAY SO rather than no-op silently. The boot is owned by the
              // product (`m11NoNorthDoorBoot`), because a journey assembled partly by a test is not the
              // shipped composition and the runner is right to refuse it.
              Expect.isEmpty
                  (PerformanceEvidence.m11NoNorthDoorBoot().Floor.Rooms.[boot.Floor.CurrentRoom].Doors
                   |> List.filter (fun door -> door.Direction = North))
                  "the unbound-action boot really has no north door"

              let run =
                  PerformanceEvidence.runM11UnboundActionJourney
                      [ JourneyEvent.MenuAction(PerformanceEvidence.PlayerAction.CrossDoor North) ]

              match JourneyReceipt.result run.Receipt with
              | JourneyResult.Failed reason ->
                  Expect.stringContains reason "cross-door-north" "the failure names the unwired action"
              | JourneyResult.Passed -> failtest "an unwired player action must not pass silently"

              // The same vocabulary, wired, maps to a production message.
              let bound =
                  PerformanceEvidence.runM11BoundActionJourney
                      [ JourneyEvent.MenuAction(PerformanceEvidence.PlayerAction.CrossDoor North); JourneyEvent.FixedTick ]

              Expect.equal (JourneyReceipt.result bound.Receipt) JourneyResult.Passed "the wired action maps"
              Expect.equal
                  bound.Final.Floor.CurrentRoom
                  (doorTowards North boot).ToRoom
                  "and really crosses the door it names"
          }

          test "FR-008 the descent cue is audible on the route a player takes, not only on a dispatch" {
              // The cue used to key on the `DescendFloor` MESSAGE. M11 reaches a descent from inside a
              // `Tick`, so the audio seam never sees that message on a real descent — a player pressing
              // E on a trapdoor heard nothing while a test that dispatched the message heard the cue.
              // That is this milestone's own defect class, one layer down.
              let host = Program.generatedHost
              let bossId = roomOfType Boss boot

              let staged =
                  { boot with Floor = clearBoss bossId boot.Floor }
                  |> update (EnterM5Room bossId)
                  |> fst
                  |> clearLoadedRoom
                  |> fun model -> { model with PlayerPosition = trapdoorCenter }

              let pressed, _ = host.Update (KeyChanged(key 'E', true)) staged
              let descended, effects = host.Update (Tick fixedDt) pressed

              Expect.equal descended.FloorIndex (boot.FloorIndex + 1) "the route really descended"

              Expect.contains
                  (audioFromHost effects)
                  (PlaySfx(SoundId "floor-descend", 0.8))
                  "and the descent cue is requested on that route"

              // A refused descent stays silent.
              let refusedModel, refusedEffects = host.Update (Tick fixedDt) (setKey 'E' true boot)
              Expect.equal refusedModel.FloorIndex boot.FloorIndex "no trapdoor, no descent"
              Expect.isFalse
                  (audioFromHost refusedEffects |> List.contains (PlaySfx(SoundId "floor-descend", 0.8)))
                  "and a refused descent is silent"
          }

          test "FR-005 the HUD never paints over a doorway, and pickups are drawn in the room" {
              // `hudLayoutForSize.Overlaps` only intersects HUD rects against other HUD rects, so it
              // cannot see a HUD region sitting on top of a door. The floor banner used to do exactly
              // that to the south doorway — the one wall a player looks at as a room loads.
              let size: Size = { Width = 1280; Height = 720 }
              let intersects (a: Rect) (b: Rect) =
                  a.X < b.X + b.Width && a.X + a.Width > b.X && a.Y < b.Y + b.Height && a.Y + a.Height > b.Y

              for regionId, bounds in Render.hudRegionsForSize size do
                  for direction in [ North; East; South; West ] do
                      let doorway = Render.doorwayRect direction
                      let panel =
                          match direction with
                          | North -> { doorway with Height = doorway.Height + Render.doorApron }
                          | South -> { doorway with Y = doorway.Y - Render.doorApron; Height = doorway.Height + Render.doorApron }
                          | West -> { doorway with Width = doorway.Width + Render.doorApron }
                          | East -> { doorway with X = doorway.X - Render.doorApron; Width = doorway.Width + Render.doorApron }
                      Expect.isFalse (intersects bounds panel) $"the HUD region {regionId} does not overlap the {direction} doorway"

              // Obstacle drops used to be drawn under the currency readout and clipped behind it.
              let dropped = { boot with M5ObstacleDrops = [ Entities.PickupKind.Coin1; Entities.PickupKind.Key; Entities.PickupKind.Bomb ] }
              let pickups =
                  Render.renderedElements dropped
                  |> List.filter (fun element -> element.ElementId.StartsWith "Pickup")
              Expect.hasLength pickups 3 "every drop is drawn"
              for element in pickups do
                  match centreOf element.Scene with
                  | Some(cx, cy) ->
                      for regionId, bounds in Render.hudRegionsForSize size do
                          Expect.isFalse
                              (cx >= bounds.X && cx <= bounds.X + bounds.Width && cy >= bounds.Y && cy <= bounds.Y + bounds.Height)
                              $"the pickup is not drawn inside the HUD region {regionId}"
                      Expect.isTrue (cy > Render.wallThickness && cy < playfieldHeight - Render.wallThickness) "and is inside the room"
                  | None -> failtest "a pickup drew nothing"
          }

          test "FR-009 standing on the trapdoor is visible, and the drawn fixture is the one the guard tests" {
              let bossId = roomOfType Boss boot
              let cleared =
                  { boot with Floor = clearBoss bossId boot.Floor } |> update (EnterM5Room bossId) |> fst

              let beside = { cleared with PlayerPosition = vec2 240.0 240.0 }
              let onIt = { cleared with PlayerPosition = trapdoorCenter }

              let has elementId model =
                  Render.renderedElements model |> List.exists (fun element -> element.ElementId = elementId)

              Expect.isTrue (has "Trapdoor" beside) "the trapdoor is drawn whenever the room records it"
              Expect.isFalse (has "TrapdoorReady" beside) "but standing away from it promises nothing"
              Expect.isTrue (has "TrapdoorReady" onIt) "standing on it is a visibly different state"
              Expect.isTrue (canDescend onIt) "which is exactly the state the guard accepts"
              Expect.isFalse (canDescend beside) "and the state it refuses"

              // A model whose loaded room claims a trapdoor the FLOOR does not record must not draw one:
              // drawing it would promise a descent the guard then refuses.
              let desynced = { boot with M5Room = { boot.M5Room with Trapdoor = true } }
              Expect.isFalse (trapdoorPresent desynced) "the floor records no fixture"
              Expect.isFalse (has "Trapdoor" desynced) "so nothing is drawn"
          }

          test "FR-004 floor generation emits the hidden-wall state a player can see and bomb" {
              let floor = (generate 123UL 4).Floor
              Expect.isNonEmpty (floor.PendingSecrets |> Map.toList) "the floor has a hidden secret"

              for KeyValue(struct (adjacent, secret), _) in floor.PendingSecrets do
                  Expect.isTrue
                      (floor.Rooms.[adjacent].Doors |> List.exists (fun door -> door.ToRoom = secret && door.State = HiddenWall))
                      "the room bordering a hidden secret carries a HiddenWall door record"
                  Expect.isTrue
                      (floor.Rooms.[secret].Doors |> List.exists (fun door -> door.ToRoom = adjacent && door.State = HiddenWall))
                      "and so does its reciprocal"
                  Expect.contains floor.Graph.[adjacent] secret "with its graph adjacency committed"

              // A hidden wall is visible and impassable until it is bombed.
              let (KeyValue(struct (adjacent, secret), _)) = floor.PendingSecrets |> Seq.head
              let blocked, travelled = tryTraverseDoor secret { floor with CurrentRoom = adjacent }
              Expect.isNone travelled "a hidden wall cannot be walked through"
              Expect.equal blocked.CurrentRoom adjacent "and the player stays put"

              let revealed = revealSecret adjacent secret floor
              Expect.hasLength
                  (revealed.Rooms.[adjacent].Doors |> List.filter (fun door -> door.ToRoom = secret))
                  1
                  "the reveal FLIPS the hidden wall rather than growing a second door to the same room"
              Expect.isTrue
                  (revealed.Rooms.[adjacent].Doors |> List.exists (fun door -> door.ToRoom = secret && door.State = Open))
                  "and it becomes an open door"

              for KeyValue(roomId, room) in revealed.Rooms do
                  for door in room.Doors do
                      Expect.contains revealed.Graph.[roomId] door.ToRoom $"room {roomId} has no door without its graph adjacency"
          } ]
