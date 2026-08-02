module Rogue3.Tests.M4ProceduralFloorTests

open Expecto
open Rogue3.Geometry
open Rogue3.FloorGeneration
open Rogue3.Entities
open Rogue3.Model

[<Tests>]
let generationTests =
    testList "M4 procedural floors" [
        testCase "AC2 same seed and index reproduce byte-identical floor values" <| fun _ ->
            let a = generate 0xCAFEUL 4
            let b = generate 0xCAFEUL 4
            Expect.equal a b "all graph, template, spawn, fixture and RNG values replay"
            Expect.notEqual (generate 0xCAFEUL 5).Floor.Seed a.Floor.Seed "floor seeds separate indices"

        testCase "room budget is bounded and placement is connected through orthogonal doors" <| fun _ ->
            for index in 1..20 do
                let floor = (generate 77UL index).Floor
                Expect.isGreaterThanOrEqual floor.RoomBudget 8 "lower budget bound"
                Expect.isLessThanOrEqual floor.RoomBudget 20 "upper budget bound"
                Expect.equal floor.Rooms.Count floor.RoomBudget "the cap includes hidden rooms"
                floor.Graph |> Map.iter (fun id neighbors ->
                    for other in neighbors do
                        let a, b = floor.Rooms.[id].Cell, floor.Rooms.[other].Cell
                        Expect.equal (abs(a.Col-b.Col) + abs(a.Row-b.Row)) 1 "all carved doors are orthogonal")

        testCase "required special rooms, templates, and exact threat budgets are assigned" <| fun _ ->
            let generated = generate 99UL 4
            let kinds = generated.Floor.Rooms |> Seq.map (fun (KeyValue(_,r)) -> r.RoomType) |> Set.ofSeq
            for kind in [Start; Boss; Treasure; Shop; Secret; SuperSecret] do Expect.contains kinds kind "required room type"
            generated.Floor.Rooms |> Seq.iter (fun (KeyValue(_, room)) ->
                if room.RoomType = Combat || room.RoomType = Boss then
                    Expect.equal room.Interior.ThreatBudget 14 "6 + 2*floorIndex"
                    Expect.equal room.Interior.ThreatSpent room.Interior.ThreatBudget "population spends the exact integer budget"
                    Expect.isNonEmpty room.Interior.EnemyAnchors "combat content exists")

        testCase "AC14 bomb reveal atomically opens reciprocal doors and graph adjacency" <| fun _ ->
            let floor = (generate 123UL 4).Floor
            let (KeyValue(struct(adjacent, secret), _)) = floor.PendingSecrets |> Seq.head
            let revealed = revealSecret adjacent secret floor
            Expect.isFalse revealed.Rooms.[secret].Hidden "room becomes visible"
            Expect.contains revealed.Graph.[adjacent] secret "forward graph edge committed"
            Expect.contains revealed.Graph.[secret] adjacent "reverse graph edge committed"
            Expect.isTrue (revealed.Rooms.[adjacent].Doors |> List.exists (fun d -> d.ToRoom=secret && d.State=Open)) "source door committed"
            Expect.isTrue (revealed.Rooms.[secret].Doors |> List.exists (fun d -> d.ToRoom=adjacent && d.State=Open)) "secret door committed"

        testCase "boss clear creates exactly one trapdoor" <| fun _ ->
            let floor = (generate 10UL 2).Floor
            let boss = floor.Rooms |> Seq.find (fun (KeyValue(_,r)) -> r.RoomType=Boss) |> fun (KeyValue(id,_)) -> id
            let twice = floor |> clearBoss boss |> clearBoss boss
            let traps = twice.Rooms.[boss].Fixtures |> List.filter ((=) Trapdoor)
            Expect.hasLength traps 1 "clear is idempotent"

        testCase "AC18 descent carries player run state and replaces every room-local collection" <| fun _ ->
            let oldFloor = initialModel.Floor
            let carried =
                { initialModel with
                    PlayerItems = [{Id="carry";Modifiers=[]}]
                    PlayerHealth = {initialModel.PlayerHealth with SoulHalfHearts=2}
                    PlayerCurrency = {Coins=12;Keys=3;Bombs=4}
                    M5Obstacles = [ obstacle ObstacleKind.Rock 1 |> obstacleAt (vec2 200.0 200.0) ]
                    HomingTargets = [{Id=2;Position=zero}]
                    M5Enemies = [ spawn 1 1 EnemyKind.Grub zero ]
                    M5ShopSlots = [ { Id=1; Offer=ShopOffer.Consumable PickupKind.Key; Price=5; KeyLocked=false } ]
                    EnemyBullets = [{Id=1;Position=zero;Velocity=zero;Radius=1.;Damage=1;Homing=0.;AgeTicks=0}]
                    Bombs = [{Id=1;Position=zero;FuseTicks=1}] }
                // M11: descent is guarded by the trapdoor it depicts, so stage the route a player
                // takes — clear the floor's boss room and stand on the trapdoor it leaves. Re-seed the
                // room-local collections AFTER that staging: entering a cleared room replaces them, so
                // seeding first would leave the "drop" assertions below unable to fail.
                |> fun model ->
                    let bossId = model.Floor.Rooms |> Map.toList |> List.find (fun (_, room) -> room.RoomType = Boss) |> fst
                    { model with Floor = clearBoss bossId model.Floor }
                    |> update (EnterM5Room bossId) |> fst
                    |> fun staged ->
                        { staged with
                            PlayerPosition = trapdoorCenter
                            M5Obstacles = [ obstacle ObstacleKind.Rock 1 |> obstacleAt (vec2 200.0 200.0) ]
                            HomingTargets = [{Id=2;Position=zero}]
                            M5Enemies = [ spawn 1 1 EnemyKind.Grub zero ]
                            M5ShopSlots = [ { Id=1; Offer=ShopOffer.Consumable PickupKind.Key; Price=5; KeyLocked=false } ]
                            EnemyBullets = [{Id=1;Position=zero;Velocity=zero;Radius=1.;Damage=1;Homing=0.;AgeTicks=0}]
                            Bombs = [{Id=1;Position=zero;FuseTicks=1}] }
                |> fun staged ->
                    // Guard the guard: the "drop" assertions below can only fail if the collections
                    // were really populated at the moment of the descent. M10 scenario 18 has always
                    // had this check; this file only had the comment.
                    Expect.isNonEmpty staged.M5Enemies "the pre-descend state really carries live actors"
                    Expect.isNonEmpty staged.M5Obstacles "and room obstacles"
                    Expect.isNonEmpty staged.M5ShopSlots "and shop stock"
                    staged
                |> update DescendFloor |> fst
            Expect.equal carried.FloorIndex 2 "index advances"
            Expect.notEqual carried.Floor.Seed oldFloor.Seed "floor regenerates"
            Expect.equal carried.PlayerItems.Head.Id "carry" "items persist"
            Expect.equal carried.PlayerHealth.SoulHalfHearts 2 "health persists"
            Expect.equal carried.PlayerCurrency.Coins 12 "currency persists"
            Expect.isEmpty carried.M5Enemies "active enemies drop"
            Expect.isEmpty carried.M5Obstacles "room obstacles drop"
            Expect.isEmpty carried.M5ShopSlots "shop stock drops"
            Expect.isEmpty carried.HomingTargets "room targeting facts drop"
            Expect.isEmpty carried.EnemyBullets "bullets drop"
            Expect.isEmpty carried.Bombs "bombs drop"
            Expect.equal carried.Floor.CurrentRoom 0 "new START room is current"
    ]
