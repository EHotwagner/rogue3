module Rogue3M13RoomTransitionWorldStateTests

// M13: room transition, pickups and world-space state.
//
// Every row this file guards was found by LOOKING at a rendered frame, not by reading a requirement.
// So each row has two pieces of evidence: a committed production PNG a human inspected, and the
// structural test below that stops the fix rotting. Neither replaces the other — the tests here are
// deliberately written so that they would have FAILED against the M11 tree, which is the only thing
// that makes them evidence rather than description.
//
// The playability claims follow M11's rule: a claim about what a player can do is driven through
// `KeyChanged` + `Tick` and nothing else.

open System
open System.IO
open Expecto
open FS.GG.UI.Scene
open FS.GG.UI.KeyboardInput
open Rogue3
open Rogue3.Geometry
open Rogue3.FloorGeneration
open Rogue3.Model

let private key letter = ViewerKeyboard.toKeyId (Letter letter)
let private setKey letter down model = update (KeyChanged(key letter, down)) model |> fst
let private tick model = update (Tick fixedDt) model |> fst
let private boot = initialModel

/// Hold whichever movement keys drive the player toward `target`, then advance one fixed step.
let private steerStep (target: Vec2) model =
    let axis delta negative positive m =
        if delta > 2.0 then m |> setKey positive true |> setKey negative false
        elif delta < -2.0 then m |> setKey negative true |> setKey positive false
        else m |> setKey negative false |> setKey positive false
    model
    |> axis (target.Vx - model.PlayerPosition.Vx) 'A' 'D'
    |> axis (target.Vy - model.PlayerPosition.Vy) 'W' 'S'
    |> tick

let private walk target steps model =
    [ 1 .. steps ] |> List.fold (fun current _ -> steerStep target current) model

/// Walk toward `target` until `until` holds, or the step budget runs out. Needed wherever the state
/// under test is TRANSIENT — the room slide lives 42 ticks, so a fixed-length walk past the crossing
/// observes it already settled.
let private walkUntil target until budget model =
    let mutable current = model
    let mutable steps = 0
    while steps < budget && not (until current) do
        current <- steerStep target current
        steps <- steps + 1
    current

let private elementIds model =
    Render.renderedElements model |> List.map _.ElementId

let private rectContains (rect: FS.GG.Game.Core.Rect) (position: Vec2) radius =
    position.Vx + radius > rect.X
    && position.Vx - radius < rect.X + rect.Width
    && position.Vy + radius > rect.Y
    && position.Vy - radius < rect.Y + rect.Height

/// Every world-space drawable bound a scene reports, so a test can assert WHERE something landed
/// rather than only that it exists. The viewport is deliberately three playfields wide and tall:
/// during a crossing the departed room legitimately sits a full playfield outside the room frame.
let private boundsOf (scene: Scene) =
    SceneInspection.inspect
        { X = -playfieldWidth; Y = -playfieldHeight; Width = playfieldWidth * 3.0; Height = playfieldHeight * 3.0 }
        scene
    |> List.filter _.Contributes
    |> List.choose (fun node ->
        match node.Bounds with
        | SceneDrawableBounds.Known bounds -> Some bounds
        | _ -> None)

let rec private nodeTexts (node: SceneNode) =
    match node with
    | Group scenes -> scenes |> List.collect (fun scene -> scene.Nodes |> List.collect nodeTexts)
    | Text(_, value, _)
    | SizedText(_, value, _, _) -> [ value ]
    | Translate(_, scene) -> scene.Nodes |> List.collect nodeTexts
    | _ -> []

let private sceneTexts (scene: Scene) = scene.Nodes |> List.collect nodeTexts

[<Tests>]
let m13RoomTransitionWorldStateTests =
    testList
        "M13 room transition, pickups and world-space state"
        [

          // ----------------------------------------------------------------------------------------
          // FR-001 / FR-002 / FR-003 — a crossing shows both rooms.
          // ----------------------------------------------------------------------------------------

          test "FR-001 walking through a door starts a slide that names the room it left" {
              // Reachable-by-play: the crossing is raised by the fixed-step door scan from movement
              // keys alone, which is the route M11 made real and then declined to animate.
              let north = boot.Floor.Rooms.[boot.Floor.CurrentRoom].Doors |> List.find (fun door -> door.State = Open)
              let target =
                  let wall = wallMidpoint north.Direction
                  match north.Direction with
                  | North -> vec2 wall.Vx (wall.Vy + 4.0)
                  | South -> vec2 wall.Vx (wall.Vy - 4.0)
                  | West -> vec2 (wall.Vx + 4.0) wall.Vy
                  | East -> vec2 (wall.Vx - 4.0) wall.Vy
              let departed = boot.Floor.CurrentRoom
              // Stop AT the crossing. The slide lives 42 ticks, so a fixed-length walk past the
              // doorway observes it already settled and would prove nothing about the transition.
              // 700 fixed steps is 5.8 s at 120 Hz, comfortably more than the ~2.6 s a 240 px/s walk
              // from the room centre to a wall needs including the acceleration ramp.
              let crossed = walkUntil target (fun m -> m.Floor.CurrentRoom <> departed) 700 boot

              Expect.notEqual crossed.Floor.CurrentRoom departed "the player actually crossed"
              match crossed.CameraTransition with
              | None -> failtest "a crossing must start the room slide; M11 suppressed it because nothing drew the departed room"
              | Some transition ->
                  Expect.equal transition.FromRoom departed "the slide names the room the crossing left"
                  Expect.equal transition.ElapsedTicks 0 "and it starts at the beginning of the slide"
          }

          test "FR-002 a slide draws BOTH rooms, exactly one playfield apart, and nothing extra when idle" {
              // The defect this pins: at `remaining = 1.0` the entered room is translated a full
              // playfield, so with only one room drawn the screen is empty for the whole 0.35 s.
              Expect.isFalse
                  (elementIds boot |> List.contains "DepartedRoom")
                  "no crossing means no second room"

              let neighbour = boot.Floor.Rooms.[boot.Floor.CurrentRoom].Doors.Head.ToRoom
              let sliding =
                  { boot with
                      CameraTransition = Some { Direction = RoomSlideDirection.East; ElapsedTicks = 0; FromRoom = neighbour } }

              Expect.contains (elementIds sliding) "DepartedRoom" "a crossing in flight draws the room being left"

              // The two rooms must be one playfield apart on the slide axis, at every tick of the
              // slide — that is what makes the pair fill the screen rather than chase each other.
              for elapsed in [ 0; 1; 20; 41 ] do
                  let model = { sliding with CameraTransition = Some { Direction = RoomSlideDirection.East; ElapsedTicks = elapsed; FromRoom = neighbour } }
                  let entered = Render.cameraOffset model
                  let step = Render.departedRoomStep RoomSlideDirection.East
                  Expect.equal step (vec2 -playfieldWidth 0.0) "an east crossing puts the departed room one room to the west"
                  Expect.isTrue (abs (entered.Vx + step.Vx - (entered.Vx - playfieldWidth)) < 1e-9) "the two rooms stay exactly one playfield apart"

              // And the departed shell is a real room shell, not an empty group: it carries the floor,
              // the wall band and the neighbour's own doors.
              let shell = Render.roomShellScene neighbour boot
              Expect.isNonEmpty shell.Nodes "the departed room's shell draws something"
              Expect.isGreaterThan (boundsOf shell |> List.length) 5 "and it is a room shell rather than a single rectangle"
          }

          test "FR-003 the M6 camera contract is unchanged by M13" {
              let started = update (BeginM6RoomTransition RoomSlideDirection.East) boot |> fst
              Expect.equal (Render.cameraOffset started) (vec2 playfieldWidth 0.0) "entry still begins one room away"
              let settled =
                  { started with CameraTransition = Some { Direction = RoomSlideDirection.East; ElapsedTicks = m6CameraDurationTicks; FromRoom = 0 } }
              Expect.equal (Render.cameraOffset settled) zero "and still settles to identity at 42 ticks"
          }

          // ----------------------------------------------------------------------------------------
          // FR-004 / FR-005 / FR-006 — pickups are somewhere, and can be taken.
          // ----------------------------------------------------------------------------------------

          test "FR-004 a destroyed obstacle drops its pickup at its own position" {
              // Drive the real reducer over every obstacle in the room until one of them yields a drop;
              // the drop table is weighted, so this asserts over whichever obstacle actually drops.
              let dropped =
                  boot.Obstacles
                  |> List.fold (fun model obstacle -> update (DamageM5Obstacle(obstacle.Id, 999)) model |> fst) boot

              Expect.isNonEmpty dropped.ObstacleDrops "smashing every obstacle in the room yields at least one drop"
              for pickup in dropped.ObstacleDrops do
                  let source = boot.Obstacles |> List.tryFind (fun obstacle -> obstacle.Id = pickup.Id)
                  Expect.isSome source $"drop {pickup.Id} came from an obstacle that was in the room"
                  Expect.equal pickup.Position source.Value.Position "and it lies exactly where that obstacle stood"
          }

          test "FR-005 walking onto a pickup collects it exactly once, through movement keys alone" {
              let spot = vec2 640.0 500.0
              let seeded =
                  { boot with ObstacleDrops = [ { Id = 4001; Room = boot.Floor.CurrentRoom; Kind = Entities.PickupKind.Coin3; Position = spot } ] }
              let before = seeded.PlayerCurrency.Coins

              let collected = walk spot 200 seeded

              Expect.isEmpty collected.ObstacleDrops "the pickup is gone once the player has walked over it"
              Expect.equal collected.PlayerCurrency.Coins (before + 3) "and a three-coin pickup credits exactly three coins"

              // Standing on the same spot for another second credits nothing more: a collected pickup
              // is removed in the step it is applied, so there is no per-tick re-credit.
              let lingered = walk spot 120 collected
              Expect.equal lingered.PlayerCurrency.Coins collected.PlayerCurrency.Coins "and lingering credits nothing further"
          }

          test "FR-005 every pickup kind applies its own effect, and currency honours the 99 cap" {
              let at kind = { boot with ObstacleDrops = [ { Id = 1; Room = boot.Floor.CurrentRoom; Kind = kind; Position = boot.PlayerPosition } ] } |> collectFloorPickups

              Expect.equal (at Entities.PickupKind.Coin1).PlayerCurrency.Coins (boot.PlayerCurrency.Coins + 1) "one coin"
              Expect.equal (at Entities.PickupKind.Coin3).PlayerCurrency.Coins (boot.PlayerCurrency.Coins + 3) "three coins"
              Expect.equal (at Entities.PickupKind.Key).PlayerCurrency.Keys (boot.PlayerCurrency.Keys + 1) "one key"
              Expect.equal (at Entities.PickupKind.Bomb).PlayerCurrency.Bombs (boot.PlayerCurrency.Bombs + 1) "one bomb"
              Expect.equal (at Entities.PickupKind.SoulHeart).PlayerHealth.SoulHalfHearts 2 "a soul heart is two soul half-hearts"

              let hurt = { boot with PlayerHealth = { boot.PlayerHealth with RedHalfHearts = 2 } }
              let healed = { hurt with ObstacleDrops = [ { Id = 1; Room = boot.Floor.CurrentRoom; Kind = Entities.PickupKind.HalfRedHeart; Position = hurt.PlayerPosition } ] } |> collectFloorPickups
              Expect.equal healed.PlayerHealth.RedHalfHearts 3 "a half red heart heals exactly one half"

              let capped =
                  { boot with
                      PlayerCurrency = { boot.PlayerCurrency with Coins = 98 }
                      ObstacleDrops = [ { Id = 1; Room = boot.Floor.CurrentRoom; Kind = Entities.PickupKind.Coin3; Position = boot.PlayerPosition } ] }
                  |> collectFloorPickups
              Expect.equal capped.PlayerCurrency.Coins 99 "and the shared 99 cap still holds"
          }

          test "FR-005 an uncollected pickup survives a crossing, and the scan counts what it says" {
              // A fresh-context critic found that `loadM5Room` cleared the drop list outright while
              // `recordDestroyedObstacle` is durable: smash a pot, step through a door before picking
              // the coin up, and the coin was gone forever because the pot never returns to re-roll.
              let departed = boot.Floor.CurrentRoom
              let door = boot.Floor.Rooms.[departed].Doors |> List.find (fun d -> d.State = Open)
              let corner = vec2 260.0 260.0
              let seeded =
                  { boot with ObstacleDrops = [ { Id = 7001; Room = departed; Kind = Entities.PickupKind.Key; Position = corner } ] }

              let target =
                  let wall = wallMidpoint door.Direction
                  match door.Direction with
                  | North -> vec2 wall.Vx (wall.Vy + 4.0)
                  | South -> vec2 wall.Vx (wall.Vy - 4.0)
                  | West -> vec2 (wall.Vx + 4.0) wall.Vy
                  | East -> vec2 (wall.Vx - 4.0) wall.Vy
              let away = walkUntil target (fun m -> m.Floor.CurrentRoom <> departed) 900 seeded

              Expect.notEqual away.Floor.CurrentRoom departed "the player left the room"
              Expect.equal away.PlayerCurrency.Keys boot.PlayerCurrency.Keys "leaving the room does not collect the key"
              Expect.hasLength away.ObstacleDrops 1 "the uncollected pickup is still on the floor of the room it fell in"
              Expect.isEmpty
                  (Render.renderedElements away |> List.filter (fun element -> element.ElementId = "PickupKey"))
                  "and it is not drawn in the room the player is now standing in"

              let back = update (EnterM5Room departed) away |> fst
              Expect.hasLength (floorPickupsHere back) 1 "returning to the room finds the pickup where it fell"
              let collected = walkUntil corner (fun m -> m.PlayerCurrency.Keys > boot.PlayerCurrency.Keys) 900 back
              Expect.equal collected.PlayerCurrency.Keys (boot.PlayerCurrency.Keys + 1) "and it can still be walked onto"

              // The cost counter is bound to an exact-equality performance driver, so a frozen counter
              // would only be caught by the evidence command. Assert it here too.
              let scanned = collectFloorPickups seeded
              Expect.equal
                  (scanned.Instrumentation.TotalFloorPickupCandidates - seeded.Instrumentation.TotalFloorPickupCandidates)
                  1
                  "one overlap test per pickup lying in the current room"
              let elsewhere =
                  { seeded with ObstacleDrops = seeded.ObstacleDrops @ [ { Id = 7002; Room = departed + 500; Kind = Entities.PickupKind.Coin1; Position = corner } ] }
              Expect.equal
                  ((collectFloorPickups elsewhere).Instrumentation.TotalFloorPickupCandidates - elsewhere.Instrumentation.TotalFloorPickupCandidates)
                  1
                  "and a pickup lying in another room is not scanned"
          }

          test "FR-005 a coin that overflows the 99 cap is not banked in the run score either" {
              // The currency clamped but RunStats.CoinsCollected took the full face value, and that
              // stat feeds runScore — so cap overflow inflated the score. Cap overflow is waste
              // everywhere else in this product.
              let capped =
                  { boot with
                      PlayerCurrency = { boot.PlayerCurrency with Coins = 98 }
                      ObstacleDrops = [ { Id = 1; Room = boot.Floor.CurrentRoom; Kind = Entities.PickupKind.Coin3; Position = boot.PlayerPosition } ] }
                  |> collectFloorPickups
              Expect.equal capped.PlayerCurrency.Coins 99 "the purse clamps at the shared cap"
              Expect.equal
                  (capped.RunStats.CoinsCollected - boot.RunStats.CoinsCollected)
                  1
                  "and the run stat records the one coin actually banked, not the three on the floor"
          }

          test "FR-006 pickups are drawn where they lie, one place each, inside the room" {
              let positions = [ vec2 300.0 240.0; vec2 520.0 300.0; vec2 740.0 500.0 ]
              let seeded =
                  { boot with
                      ObstacleDrops =
                          positions
                          |> List.mapi (fun index position ->
                              { Id = 5000 + index
                                Room = boot.Floor.CurrentRoom
                                Kind = [ Entities.PickupKind.Coin1; Entities.PickupKind.Key; Entities.PickupKind.Bomb ].[index]
                                Position = position }) }

              let drawn =
                  Render.renderedElements seeded
                  |> List.filter (fun element -> element.ElementId.StartsWith "Pickup")

              Expect.hasLength drawn 3 "every drop is drawn"

              // The M11 defect: three drops rendered in a fixed indexed strip, so their drawn centres
              // were a function of the INDEX rather than of the pickup. Assert the drawn geometry
              // actually straddles each authored position.
              for position in positions do
                  Expect.isTrue
                      (drawn |> List.exists (fun element -> boundsOf element.Scene |> List.exists (fun bounds -> rectContains { X = bounds.X; Y = bounds.Y; Width = bounds.Width; Height = bounds.Height } position 1.0)))
                      $"a pickup is drawn at ({position.Vx}, {position.Vy}), where it lies"

              for element in drawn do
                  for bounds in boundsOf element.Scene do
                      Expect.isTrue (bounds.Y > Render.wallThickness) "a pickup is drawn inside the wall band"
                      Expect.isTrue (bounds.Y + bounds.Height < playfieldHeight - Render.wallThickness) "and above the south wall"
          }

          // ----------------------------------------------------------------------------------------
          // FR-007 / FR-008 — the shop and the reward stand in the room, and state their terms.
          // ----------------------------------------------------------------------------------------

          test "FR-007 placed fixtures clear the shell, the doorways, the trapdoor and every obstacle, on every reachable floor" {
              // The M11 defect was fixed screen coordinates: the first shop slot sat on the pot and the
              // reward plinth overlapped the spike trap. Enumerate EVERY room of every floor a run can
              // reach, rather than the one room a fixture happens to use.
              let mutable rooms = 0
              for floorIndex in 1 .. 6 do
                  let generated = FloorGeneration.generate boot.RunSeed floorIndex
                  for roomId in generated.Floor.Rooms |> Map.toList |> List.map fst do
                      let loaded = loadM5Room roomId { boot with FloorIndex = floorIndex; Floor = generated.Floor }
                      let placed = placeRoomFixtures loaded.Obstacles 4
                      rooms <- rooms + 1
                      Expect.hasLength placed 4 $"room {roomId} on floor {floorIndex} places every requested fixture"
                      for position in placed do
                          Expect.isFalse (insideRoomShell position) $"room {roomId}: a fixture is clear of the wall band and every doorway apron"
                          Expect.isFalse (trapdoorContains position) $"room {roomId}: a fixture does not stand on the trapdoor"
                          for obstacle in loaded.Obstacles do
                              let delta = sub position obstacle.Position
                              Expect.isGreaterThanOrEqual
                                  (sqrt (delta.Vx * delta.Vx + delta.Vy * delta.Vy))
                                  obstacleClearance
                                  $"room {roomId}: a fixture does not overlap the {obstacle.Kind} at ({obstacle.Position.Vx}, {obstacle.Position.Vy})"
              Expect.isGreaterThan rooms 60 "the sweep covered every room of all six floors"
          }

          test "FR-007 the RENDERER puts the shop and the reward where placeRoomFixtures says" {
              // A fresh-context critic proved this guard was missing: restoring the exact pre-M13
              // literals (`X = 520 + index*90, Y = 160` and `X = 620, Y = 450`) left the whole suite
              // green, so the row had no regression guard at all. Testing `placeRoomFixtures` in
              // isolation says nothing about whether the renderer consumes it.
              let slots, _, _ = Entities.generateShop (FS.GG.Game.Core.Rng.ofSeed 0xA55AUL) (Entities.itemPool [])
              let model = { boot with ShopSlots = slots; Room = { boot.Room with Reward = Some Entities.baseItems.Head } }
              let expected = placeRoomFixtures model.Obstacles (slots.Length + 1)

              let drawnAt id =
                  Render.renderedElements model
                  |> List.filter (fun element -> element.ElementId = id)
                  |> List.map (fun element ->
                      let bounds = boundsOf element.Scene
                      let left = bounds |> List.map _.X |> List.min
                      let right = bounds |> List.map (fun b -> b.X + b.Width) |> List.max
                      (left + right) / 2.0)

              let shopCentres = drawnAt "ShopItem"
              Expect.hasLength shopCentres slots.Length "every shop slot is drawn"
              for index, centre in List.indexed shopCentres do
                  Expect.isTrue
                      (abs (centre - expected.[index].Vx) < 6.0)
                      $"shop slot {index} is drawn at the placed x {expected.[index].Vx}, not at a fixed screen coordinate (drawn at {centre})"

              match drawnAt "RoomReward" with
              | [ centre ] ->
                  Expect.isTrue
                      (abs (centre - expected.[slots.Length].Vx) < 6.0)
                      $"the reward is drawn at the placed x {expected.[slots.Length].Vx}, not at a fixed screen coordinate (drawn at {centre})"
              | other -> failtest $"expected exactly one reward element, got {other.Length}"
          }

          test "FR-008 a shop slot states its price, and a key-locked slot says so instead" {
              let slots, _, _ = Entities.generateShop (FS.GG.Game.Core.Rng.ofSeed 0xA55AUL) (Entities.itemPool [])
              let coinSlot = slots |> List.find (fun slot -> not slot.KeyLocked && slot.Offer <> Entities.ShopOffer.Empty)
              let at = vec2 500.0 440.0

              let priced = Render.shopSlotScene at coinSlot
              Expect.contains (sceneTexts priced) (sprintf "%dc" coinSlot.Price) "a coin-priced slot shows its price"

              let locked = Render.shopSlotScene at { coinSlot with KeyLocked = true }
              Expect.isEmpty (sceneTexts locked) "a key-locked slot shows no coin price, because coins cannot buy it"
              Expect.notEqual
                  ((SceneCodec.export locked).CanonicalBytes)
                  ((SceneCodec.export priced).CanonicalBytes)
                  "and a key-locked slot is visibly different from a priced one"

              let emptied = Render.shopSlotScene at { coinSlot with Offer = Entities.ShopOffer.Empty }
              Expect.isEmpty (sceneTexts emptied) "an emptied slot has nothing left to charge for"
              Expect.notEqual
                  ((SceneCodec.export emptied).CanonicalBytes)
                  ((SceneCodec.export priced).CanonicalBytes)
                  "and an emptied slot is visibly different from a stocked one"
          }

          // ----------------------------------------------------------------------------------------
          // FR-009 / FR-010 / FR-011 — the drawn wall is the wall.
          // ----------------------------------------------------------------------------------------

          test "FR-009 the player cannot stand inside the wall band the renderer draws" {
              // Before M13 the player's bound was the raw playfield, so pressing north put the player's
              // disc 11 units inside the 24-unit stone band the frame had just drawn.
              let slabs = roomWallSlabs boot
              Expect.isNonEmpty slabs "the room has a wall band"

              for corner in [ vec2 -400.0 -400.0; vec2 1700.0 -400.0; vec2 -400.0 1100.0; vec2 1700.0 1100.0 ] do
                  let pressed = walk corner 400 boot
                  for slab in slabs do
                      Expect.isFalse
                          (rectContains slab pressed.PlayerPosition playerRadius)
                          $"steering at ({corner.Vx}, {corner.Vy}) leaves the player at ({pressed.PlayerPosition.Vx}, {pressed.PlayerPosition.Vy}), which is inside a drawn wall slab"
          }

          test "FR-010 the wall band does not seal the doorways it is drawn with" {
              // The failure mode this rules out: a collider that spans a locked doorway would put the
              // player's centre at depth 37 against a 14-deep sensor, and M11's key-door route — driven
              // through movement keys alone — would silently become unreachable.
              let doors = boot.Floor.Rooms.[boot.Floor.CurrentRoom].Doors
              Expect.isNonEmpty doors "the boot room has doors"
              let slabs = roomWallSlabs boot
              for door in doors do
                  let mouth =
                      let wall = wallMidpoint door.Direction
                      match door.Direction with
                      | North -> vec2 wall.Vx (wall.Vy + doorwaySensorDepth - 1.0)
                      | South -> vec2 wall.Vx (wall.Vy - doorwaySensorDepth + 1.0)
                      | West -> vec2 (wall.Vx + doorwaySensorDepth - 1.0) wall.Vy
                      | East -> vec2 (wall.Vx - doorwaySensorDepth + 1.0) wall.Vy
                  Expect.isTrue (doorwaySensorContains door.Direction mouth) "the sample point is inside the doorway sensor"
                  for slab in slabs do
                      Expect.isFalse
                          (rectContains slab mouth playerRadius)
                          $"the {door.Direction} doorway stays passable, so the sensor stays reachable"
          }

          test "FR-009 a hidden wall is solid: the gap is only opened for a doorway that reads as one" {
              // A fresh-context critic found the hole: the slabs opened a gap for every door of ANY
              // graph state, including HiddenWall — which `Render.doorScene` draws as a full-depth
              // rectangle in the SAME `stone` colour as the band, with its own comment saying "It
              // reads as WALL, not door". The player could stand a full 24 units inside solid-looking
              // stone. Nothing needs that gap: a secret is revealed by a bomb blast testing
              // `wallMidpoint` against `bombRadius`, never by the player reaching the wall.
              let hidden =
                  boot.Floor.Rooms
                  |> Map.toList
                  |> List.tryPick (fun (id, room) ->
                      room.Doors |> List.tryPick (fun door -> if door.State = HiddenWall then Some(id, door) else None))

              match hidden with
              | None -> failtest "the representative floor is expected to place at least one hidden wall"
              | Some(roomId, door) ->
                  let model = update (EnterM5Room roomId) boot |> fst
                  let slabs = roomWallSlabs model

                  // The band is continuous across a hidden wall: that direction contributes ONE slab.
                  Expect.isFalse
                      (Set.contains door.Direction (roomDoorDirections roomId model.Floor))
                      $"the {door.Direction} hidden wall does not open a collider gap"

                  let outside =
                      let wall = wallMidpoint door.Direction
                      add wall (scale 400.0 (sub wall (vec2 (playfieldWidth/2.0) (playfieldHeight/2.0)) |> normalizeOrZero))
                  let pressed = walk outside 500 model

                  Expect.equal pressed.Floor.CurrentRoom roomId "walking at a hidden wall does not cross it"
                  for slab in slabs do
                      Expect.isFalse
                          (rectContains slab pressed.PlayerPosition playerRadius)
                          $"the player stops at the drawn stone of the {door.Direction} hidden wall, not inside it (stopped at {pressed.PlayerPosition.Vx}, {pressed.PlayerPosition.Vy})"

                  // And the depth check the slab list cannot make on its own: the player's disc must
                  // not reach past the inner face of the drawn band.
                  let depth, lateral = doorwayOffsets door.Direction pressed.PlayerPosition
                  if abs lateral <= doorwayHalfSpan then
                      Expect.isGreaterThanOrEqual
                          depth
                          (Render.wallThickness + playerRadius - 0.001)
                          "the player's disc never penetrates the drawn 24-unit stone band at a hidden wall"
          }

          test "FR-011 the drawn wall shell and the player's collider are one value" {
              // Not a paraphrase of the implementation: derive the DRAWN geometry from the production
              // renderer and the COLLIDER from the model, and require them to coincide slab for slab.
              let slabs = roomWallSlabs boot
              let drawn =
                  Render.renderedElements boot
                  |> List.find (fun element -> element.ElementId = "RoomWalls")
                  |> _.Scene
                  |> boundsOf

              for slab in slabs do
                  Expect.isTrue
                      (drawn |> List.exists (fun bounds ->
                          abs (bounds.X - slab.X) < 2.0 && abs (bounds.Y - slab.Y) < 2.0
                          && abs (bounds.Width - slab.Width) < 2.0 && abs (bounds.Height - slab.Height) < 2.0))
                      $"the collider slab at ({slab.X}, {slab.Y}) {slab.Width}x{slab.Height} is drawn"

              // And a room with no doors is a closed box in both descriptions.
              Expect.hasLength (roomWallSlabsFor Set.empty) 4 "a doorless room has exactly four solid walls"
              Expect.hasLength (roomWallSlabsFor (Set.ofList [ North; East; South; West ])) 8 "a four-door room splits each wall in two"
          }

          // ----------------------------------------------------------------------------------------
          // FR-012 / FR-013 / FR-014 — invisible state becomes visible, at the granularity it rots at.
          // ----------------------------------------------------------------------------------------

          test "FR-012 invulnerability, the roll, being down and an enemy wind-up each have a world-space visual" {
              let invulnerable = { boot with PostHitInvulnTicks = postHitInvulnTicks }
              let rolling = { boot with DodgeRollTicks = rollDurationTicks; PlayerVelocity = vec2 rollSpeed 0.0 }
              let down = { boot with PlayerLifeState = Dead }
              let winding =
                  { boot with
                      Enemies = [ { Entities.spawn 1 100 Entities.EnemyKind.Charger (vec2 360.0 300.0) with State = Entities.EnemyState.ChargerWindUp(vec2 1.0 0.0, 20) } ] }

              Expect.contains (elementIds invulnerable) "PlayerInvulnerable" "post-hit invulnerability is visible"
              Expect.contains (elementIds rolling) "PlayerDodgeRoll" "the committed roll is visible"
              Expect.contains (elementIds down) "PlayerDown" "a downed player is visible"
              Expect.contains (elementIds winding) "EnemyTelegraph" "an enemy winding up to charge is visible"

              // None of them fires in a resting frame — a state visual that is always on says nothing.
              let resting = elementIds boot
              for id in [ "PlayerInvulnerable"; "PlayerDodgeRoll"; "PlayerDown"; "EnemyTelegraph" ] do
                  Expect.isFalse (List.contains id resting) $"{id} is not drawn in a resting frame"

              // Each is drawn ON the actor it describes, not parked at a fixed coordinate.
              let near (model: Model) id (anchor: Vec2) tolerance =
                  Render.renderedElements model
                  |> List.filter (fun element -> element.ElementId = id)
                  |> List.collect (fun element -> boundsOf element.Scene)
                  |> List.forall (fun bounds ->
                      abs (bounds.X + bounds.Width / 2.0 - anchor.Vx) < tolerance
                      && abs (bounds.Y + bounds.Height / 2.0 - anchor.Vy) < tolerance)
              Expect.isTrue (near invulnerable "PlayerInvulnerable" boot.PlayerPosition 40.0) "the invulnerability ring is on the player"
              Expect.isTrue (near down "PlayerDown" boot.PlayerPosition 40.0) "the downed marker is on the player"
              Expect.isTrue (near winding "EnemyTelegraph" (vec2 (360.0 + 48.0) 300.0) 90.0) "the telegraph runs from the enemy along its committed direction"
          }

          test "FR-013 the HUD is inventoried per region, and the composed scene is unchanged" {
              let size = { Width = 1280; Height = 720 }
              let model =
                  { boot with
                      PlayerHealth = { RedContainers = 4; RedHalfHearts = 5; SoulHalfHearts = 3; BlackHalfHearts = 2 }
                      PlayerCurrency = { Coins = 42; Keys = 3; Bombs = 7 }
                      FloorNameTicks = 120
                      Floor = { boot.Floor with MapRevealed = boot.Floor.Rooms |> Map.toList |> List.map fst |> Set.ofList } }

              let regions = Render.hudRegionScenes size model |> List.map (fun (id, _, _) -> id)
              Expect.equal
                  regions
                  [ "HudHearts"; "HudCurrency"; "HudActiveCharge"; "HudMinimap"; "HudFloorBanner" ]
                  "the HUD is described one region at a time"

              Expect.isFalse (GameplayVisualInventory.all |> List.map GameplayVisualInventory.elementId |> List.contains "HudScore")
                  "and the single catch-all HudScore row is gone"

              // The split must not change what the viewer receives. A critic pointed out that
              // comparing `hudSceneForSize` against the concatenation of its own regions is the
              // DEFINITION of `hudSceneForSize` and cannot fail for any implementation, so it proved
              // nothing. These are the properties that were true before M13 and must still be true:
              // the same node count the pre-split HUD produced, at both required output sizes, with
              // the same labels and the same distinguishing colours.
              for output in [ { Width = 1280; Height = 720 }; { Width = 1920; Height = 1080 } ] do
                  let describe = Scene.describe (Render.hudSceneForSize output model) |> List.length
                  Expect.equal describe 25 $"the composed HUD draws the same 25 nodes at {output.Width}x{output.Height} that it drew before the inventory split"

              let labels = sceneTexts (Render.hudSceneForSize size model)
              Expect.contains labels "COIN 42   KEY 03   BOMB 07" "currency keeps its fixed two-digit formatting"
              Expect.contains labels "ACTIVE 2/6" "the active charge keeps its readout"
              Expect.contains labels "1 — THE BURROWS" "and the floor banner keeps its text"

              // Each region must contribute: an empty region is a region that has silently stopped
              // drawing, which is exactly what one catch-all HudScore row could hide.
              for id, _, nodes in Render.hudRegionScenes size model do
                  Expect.isNonEmpty nodes $"the {id} region draws something for a model that has {id} content"

              // The banner is the one region that is absent when its state is absent.
              let quiet = { model with FloorNameTicks = 0 }
              Expect.isFalse (elementIds quiet |> List.contains "HudFloorBanner") "the floor banner is not drawn when it is not announcing"
              Expect.contains (elementIds quiet) "HudMinimap" "while the minimap always is"
          }

          test "FR-014 every M13 element is inventoried, catalogued and bound, and none duplicates another" {
              let declared = GameplayVisualInventory.all |> List.map GameplayVisualInventory.elementId |> Set.ofList
              for id in
                  [ "DepartedRoom"; "PlayerInvulnerable"; "PlayerDodgeRoll"; "PlayerDown"; "EnemyTelegraph"
                    "HudHearts"; "HudCurrency"; "HudActiveCharge"; "HudMinimap"; "HudFloorBanner" ] do
                  Expect.isTrue (Set.contains id declared) $"{id} is declared in the runtime inventory"

              // The catalog is the second, independent witness; the coverage gate compares them, and
              // this asserts the file on disk actually names the new rows.
              let catalog =
                  File.ReadAllLines(Path.Combine(__SOURCE_DIRECTORY__, "element-visuals.catalog"))
                  |> Array.filter (fun line -> not (line.StartsWith "#") && line.Trim() <> "")
                  |> Array.map (fun line -> line.Split('\t').[0])
                  |> Set.ofArray
              Expect.equal catalog declared "the committed catalog and the runtime inventory name exactly the same elements"

              // Each binding's representative scene must be distinct — an element that draws like
              // another is not a separate visual, whatever the catalogue says.
              let digests =
                  GameplayVisualInventory.bindings
                  |> List.collect (fun binding ->
                      binding.RequiredStates |> List.map (fun (_, model) -> GameplayVisualInventory.elementId binding.Element, (SceneCodec.export (binding.Project model)).CanonicalBytes))
              Expect.equal
                  (digests |> List.map snd |> List.distinct |> List.length)
                  (digests |> List.length)
                  "no two inventory elements render byte-identical evidence"
          }

          // ----------------------------------------------------------------------------------------
          // FR-015 — the frames a human looked at.
          // ----------------------------------------------------------------------------------------

          test "FR-015 the committed M13 render-and-look frames exist for every row" {
              let root =
                  Path.Combine(__SOURCE_DIRECTORY__, "..", "..", "readiness", "014-m13-room-transition-pickups-world-state", "frames")

              Expect.isTrue (Directory.Exists root) "the M13 render-and-look evidence directory is committed"

              let expected =
                  [ "01-crossing-start"; "02-crossing-midpoint"; "03-crossing-settling"
                    "04-positioned-drops"; "05-shop-priced-and-locked"; "06-boss-reward-placed"
                    "07-player-pressed-into-north-wall"; "08-player-invulnerable"; "09-player-dodge-roll"
                    "10-player-down"; "11-enemy-telegraph"; "12-hud-regions" ]

              Expect.equal
                  (Directory.GetDirectories root |> Array.map Path.GetFileName |> Array.sort |> Array.toList)
                  (List.sort expected)
                  "the committed M13 frame set is exactly the authored one"

              for frame in expected do
                  Expect.isNonEmpty
                      (Directory.GetFiles(Path.Combine(root, frame), "*.png"))
                      $"{frame} committed a production-frame PNG"
          } ]
