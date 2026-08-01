module Rogue3.FloorGeneration

open FS.GG.Game.Core

type RoomType = Combat | Treasure | Shop | Boss | Secret | SuperSecret | Start
type DoorState = Open | LockedKey | BossDoor | HiddenWall
type DoorDirection = North | East | South | West
type EnemySpawnKind = Grub | Maggot | Spitter | Charger | Turret | Caster | Brute

type Door =
    { ToRoom: int
      Direction: DoorDirection
      State: DoorState }

type EnemyAnchor =
    { Kind: EnemySpawnKind
      Cell: Cell
      Threat: int }

type Interior =
    { TemplateId: int
      Obstacles: Cell list
      EnemyAnchors: EnemyAnchor list
      ThreatBudget: int
      ThreatSpent: int }

type Fixture =
    | Trapdoor

type FloorRoom =
    { Id: int
      Cell: Cell
      RoomType: RoomType
      Cleared: bool
      Visited: bool
      Hidden: bool
      Interior: Interior
      Doors: Door list
      Fixtures: Fixture list }

type Floor =
    { Index: int
      Seed: uint64
      Rooms: Map<int, FloorRoom>
      Graph: Map<int, int list>
      CurrentRoom: int
      MapRevealed: Set<int>
      PendingSecrets: Map<struct (int * int), int>
      RoomBudget: int }

type GenerationResult =
    { Floor: Floor
      LayoutRng: Rng }

let private cardinal = [ { Col = 0; Row = -1 }; { Col = 1; Row = 0 }; { Col = 0; Row = 1 }; { Col = -1; Row = 0 } ]
let private addCell a b = { Col = a.Col + b.Col; Row = a.Row + b.Row }
let private cellKey c = struct (c.Col, c.Row)

let private direction a b =
    match b.Col - a.Col, b.Row - a.Row with
    | 0, -1 -> North | 1, 0 -> East | 0, 1 -> South | _ -> West

let private roomType (kind: RoomKind) =
    match kind with
    | RoomKind.Start -> Start
    | RoomKind.Boss -> Boss
    | RoomKind.Treasure -> Treasure
    | RoomKind.Shop -> Shop
    | RoomKind.Secret -> Secret
    | RoomKind.Normal -> Combat

let private threatCost = function
    | Grub | Maggot -> 1 | Spitter -> 2 | Charger | Turret -> 3 | Caster -> 4 | Brute -> 6

let private roster floorIndex =
    [ Grub; Maggot; Spitter ]
    @ (if floorIndex >= 2 then [ Charger; Turret ] else [])
    @ (if floorIndex >= 3 then [ Caster ] else [])
    @ (if floorIndex >= 4 then [ Brute ] else [])

let private populateInterior floorIndex roomType templateId rng =
    let obstacles =
        match abs templateId % 4 with
        | 0 -> [ { Col = 4; Row = 3 }; { Col = 8; Row = 3 } ]
        | 1 -> [ { Col = 3; Row = 2 }; { Col = 9; Row = 4 }; { Col = 6; Row = 3 } ]
        | 2 -> [ { Col = 3; Row = 3 }; { Col = 9; Row = 3 } ]
        | _ -> []
    let budget = if roomType = Combat || roomType = Boss then 6 + 2 * max 1 floorIndex else 0
    let pool = roster floorIndex |> List.toArray
    let mutable remaining = budget
    let mutable next = rng
    let mutable anchors = []
    let mutable slot = 0
    while remaining > 0 && slot < 32 do
        let struct (choice, advanced) = Rng.nextInt 0 (pool.Length - 1) next
        next <- advanced
        let kind = pool.[choice]
        let cost = threatCost kind
        let chosen = if cost <= remaining then kind else Grub
        let actual = threatCost chosen
        anchors <- { Kind = chosen; Cell = { Col = 2 + (slot * 3) % 9; Row = 2 + (slot * 2) % 4 }; Threat = actual } :: anchors
        remaining <- remaining - actual
        slot <- slot + 1
    { TemplateId = templateId; Obstacles = obstacles; EnemyAnchors = List.rev anchors; ThreatBudget = budget; ThreatSpent = budget - remaining }, next

let private addEdge a b graph =
    graph
    |> Map.change a (fun xs -> Some (b :: Option.defaultValue [] xs |> List.distinct |> List.sort))
    |> Map.change b (fun xs -> Some (a :: Option.defaultValue [] xs |> List.distinct |> List.sort))

let private bestSecretCell exactlyOne occupied =
    occupied
    |> Seq.collect (fun (KeyValue (_, cell)) -> cardinal |> Seq.map (addCell cell))
    |> Seq.filter (fun c -> not (Map.containsKey (cellKey c) occupied))
    |> Seq.distinct
    |> Seq.map (fun c -> c, cardinal |> List.sumBy (fun d -> if Map.containsKey (cellKey (addCell c d)) occupied then 1 else 0))
    |> Seq.filter (fun (_, count) -> if exactlyOne then count = 1 else count >= 2)
    |> Seq.sortBy (fun (c, count) -> -count, c.Row, c.Col)
    |> Seq.tryHead
    |> Option.map fst

let generate runSeed floorIndex =
    let floorIndex = max 1 floorIndex
    let seed = MapGen.floorSeed runSeed floorIndex
    let mutable rng = Rng.ofSeed seed
    let struct (noise, afterBudget) = Rng.nextInt 0 2 rng
    rng <- afterBudget
    let budget = int (System.Math.Round(7.0 + 1.6 * float floorIndex + float noise)) |> max 8 |> min 20
    let hiddenRoomCount = if floorIndex >= 3 then 2 else 1
    let baseBudget = budget - hiddenRoomCount
    let specials = [ RoomKind.Boss; RoomKind.Treasure ] @ (if floorIndex >= 2 then [ RoomKind.Shop ] else [])
    let parameters = { RoomCount = baseBudget; MaxRooms = 20; SpecialRooms = specials }
    // MapGen deliberately remains total and may return a partial walk. The product contract requires
    // the exact budget, so retry whole walks under the threaded stream, bounded at 32 attempts.
    let mutable layout = Unchecked.defaultof<FloorLayout>
    let mutable accepted = false
    let mutable attempts = 0
    while attempts < 32 && not accepted do
        let struct (candidate, afterLayout) = MapGen.floorLayout parameters rng
        rng <- afterLayout
        let occupied = candidate.Rooms |> Seq.map (fun room -> cellKey room.Cell, room.Cell) |> Map.ofSeq
        if candidate.Rooms.Length = baseBudget && (bestSecretCell false occupied |> Option.isSome) then
            layout <- candidate
            accepted <- true
        attempts <- attempts + 1
    if not accepted then
        // Deterministic bounded fallback: a comb tree. It is connected, orthogonal, collision-free,
        // and supplies enough dead ends for boss/treasure/shop without an unbounded retry loop.
        let cells = ResizeArray<Cell>()
        cells.Add { Col=0; Row=0 }
        let mutable x = 1
        while cells.Count < baseBudget do
            cells.Add { Col=x; Row=0 }
            if cells.Count < baseBudget then cells.Add { Col=x; Row=(if x % 2 = 0 then 1 else -1) }
            x <- x + 1
        let templateRooms = cells |> Seq.mapi (fun id cell -> { Cell=cell; Kind=(if id=0 then RoomKind.Start else RoomKind.Normal); TemplateId=id % 4 }) |> Seq.toArray
        let edges =
            [ for id in 1 .. templateRooms.Length-1 do
                let c = templateRooms.[id].Cell
                if c.Row = 0 then
                    let prior = templateRooms |> Array.findIndex (fun r -> r.Cell = {Col=c.Col-1;Row=0})
                    yield templateRooms.[prior].Cell, c
                else
                    let spine = templateRooms |> Array.findIndex (fun r -> r.Cell = {Col=c.Col;Row=0})
                    yield templateRooms.[spine].Cell, c ]
        let deadEnds =
            templateRooms
            |> Array.mapi (fun id r -> id,r.Cell)
            |> Array.filter (fun (_,c) -> c.Row <> 0 || c.Col = x-1)
            |> Array.sortByDescending (fun (_,c) -> abs c.Col + abs c.Row)
        specials |> List.iteri (fun index kind -> if index < deadEnds.Length then let id,_=deadEnds.[index] in templateRooms.[id] <- {templateRooms.[id] with Kind=kind})
        layout <- { Rooms=templateRooms; Adjacency=edges |> List.toArray }
    let mutable cells = layout.Rooms |> Array.mapi (fun id r -> cellKey r.Cell, (id, r.Cell, roomType r.Kind, r.TemplateId)) |> Map.ofArray
    let mutable nextId = layout.Rooms.Length
    let mutable secretIds = []
    match bestSecretCell false (cells |> Map.map (fun _ (_, c, _, _) -> c)) with
    | Some cell -> cells <- Map.add (cellKey cell) (nextId, cell, Secret, 900 + nextId) cells; secretIds <- nextId :: secretIds; nextId <- nextId + 1
    | None -> ()
    if floorIndex >= 3 then
        match bestSecretCell true (cells |> Map.map (fun _ (_, c, _, _) -> c)) with
        | Some cell -> cells <- Map.add (cellKey cell) (nextId, cell, SuperSecret, 950 + nextId) cells; secretIds <- nextId :: secretIds; nextId <- nextId + 1
        | None -> ()
    let byId = cells |> Seq.map (fun (KeyValue (_, v)) -> let id, c, t, template = v in id, (c,t,template)) |> Map.ofSeq
    let normalEdges =
        layout.Adjacency
        |> Array.choose (fun (a,b) ->
            match Map.tryFind (cellKey a) cells, Map.tryFind (cellKey b) cells with
            | Some (ai,_,_,_), Some (bi,_,_,_) -> Some(ai,bi)
            | _ -> None)
        |> Array.toList
    let mutable graph = normalEdges |> List.fold (fun g (a,b) -> addEdge a b g) (byId |> Map.map (fun _ _ -> []))
    let mutable pending = Map.empty
    for secretId in secretIds do
        let cell, _, _ = byId.[secretId]
        cardinal
        |> List.choose (fun d -> Map.tryFind (cellKey (addCell cell d)) cells)
        |> List.iter (fun (adjacentId,_,_,_) -> pending <- Map.add (struct (adjacentId, secretId)) secretId pending)
    let mutable rooms = Map.empty
    for KeyValue(id, (cell, kind, templateId)) in byId do
        let interior, afterInterior = populateInterior floorIndex kind templateId rng
        rng <- afterInterior
        let fixtures = []
        let doors =
            graph.[id]
            |> List.map (fun other ->
                let otherCell, otherKind, _ = byId.[other]
                let state = if otherKind = Boss || kind = Boss then BossDoor elif otherKind = Treasure then LockedKey else Open
                { ToRoom = other; Direction = direction cell otherCell; State = state })
        rooms <- Map.add id { Id=id; Cell=cell; RoomType=kind; Cleared=(kind=Start || kind=Treasure || kind=Shop); Visited=(kind=Start); Hidden=(kind=Secret || kind=SuperSecret); Interior=interior; Doors=doors; Fixtures=fixtures } rooms
    { Floor = { Index=floorIndex; Seed=seed; Rooms=rooms; Graph=graph; CurrentRoom=0; MapRevealed=Set.singleton 0; PendingSecrets=pending; RoomBudget=budget }
      LayoutRng = rng }

let revealSecret adjacentRoom secretRoom floor =
    if not (Map.containsKey (struct (adjacentRoom, secretRoom)) floor.PendingSecrets) then floor
    else
        let a = floor.Rooms.[adjacentRoom]
        let s = floor.Rooms.[secretRoom]
        let aDoor = { ToRoom=secretRoom; Direction=direction a.Cell s.Cell; State=Open }
        let sDoor = { ToRoom=adjacentRoom; Direction=direction s.Cell a.Cell; State=Open }
        let rooms = floor.Rooms |> Map.add adjacentRoom { a with Doors=aDoor::a.Doors } |> Map.add secretRoom { s with Hidden=false; Doors=sDoor::s.Doors }
        { floor with Rooms=rooms; Graph=addEdge adjacentRoom secretRoom floor.Graph; PendingSecrets=floor.PendingSecrets |> Map.remove (struct(adjacentRoom,secretRoom)); MapRevealed=Set.add secretRoom floor.MapRevealed }

let clearBoss roomId floor =
    match Map.tryFind roomId floor.Rooms with
    | Some room when room.RoomType = Boss ->
        let fixtures = if room.Fixtures |> List.exists ((=) Trapdoor) then room.Fixtures else room.Fixtures @ [Trapdoor]
        { floor with Rooms = Map.add roomId { room with Cleared=true; Fixtures=fixtures } floor.Rooms }
    | _ -> floor
