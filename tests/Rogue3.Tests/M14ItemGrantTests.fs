module Rogue3M14ItemGrantTests

// Board item EHotwagner/rogue3#47 — "the shop takes the coins and grants nothing".
//
// The title names one call site. The defect was wider: `Model.PlayerItems` had NO production writer
// at all. Three routes in the product award an item, and at `3913c26` none of them delivered one —
//
//   * `purchaseM5ShopSlot` debited coins, emptied the offer, bumped `RunStats.ItemsFound` and stopped
//     (and for a `Consumable` offer it did not even do that: it charged and handed over nothing);
//   * the treasure room's `ItemPedestal` was drawn from the shared item pool, marked Placed, and then
//     DISCARDED by `loadM5Room`, which surfaced a reward only `if isBoss`;
//   * the `BossReward` did reach `M5Room.Reward` and was rendered, but no reducer ever consumed it.
//
// Every test here is written to fail against the tree it was written on. That is the point: a suite
// that merely asserts "the reducer returns a model" passed green through all three.
//
// The playability claims follow the M11/M13 rule — a claim about what a PLAYER can do is driven
// through `KeyChanged` + `Tick` and nothing else, because #47's ancestor defect was exactly a
// reducer that was correct and unreachable.
//
// SCOPE, updated by EHotwagner/rogue3#55. All three routes are now driven from the production input
// route end to end. The shop's leg was the last one missing: at #47's merge no key press could
// produce `InteractM5Shop`, so the shop tests below the reducer heading prove the reducer and the
// update path only. `Model.playerRoomIntentsIn` now raises the message from an interact edge at a
// placed slot, and the "Route 1b" block drives that from `KeyChanged` + `Tick` and nothing else.
//
// The consumable-fallback reward is a DIFFERENT hole and is still open: `loadM5Room` discards
// `ConsumableReward` (EHotwagner/rogue3#51), and is asserted here as still-broken.
//
// WHY #55'S "STILL BROKEN" NOTES DID NOT GO RED WHEN IT WAS FIXED. They were prose. Three files
// carried a paragraph saying `InteractM5Shop` had zero production dispatch sites, and not one of
// them asserted it, so wiring the message left every one of those paragraphs stating a falsehood
// under a green suite. The replacement below is an ASSERTION over the compiled product — the shop
// route is exercised through the input seam, so unwiring it reds this file.
//
// SECOND PASS. A fresh-context mutation critic ran 57 mutations against the first draft of this file
// and 26 survived — the fix itself could not be reverted silently, but the surface AROUND it was
// unpinned: four of six `itemModifiers` arms could be deleted outright, both tear-delay SIGN FLIPS
// survived, `roomRewardRadius` survived being widened to 400 (most of the room), and the boss
// reward could be made re-grantable on every re-entry. The assertions below are the answer to that
// run, so they lean on literal expected VALUES rather than on shape.

open Expecto
open FS.GG.Audio.Core
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

/// Walk toward `target` until `until` holds, or the budget runs out — returning the STEP COUNT as
/// well as the model, following `M11PlayabilityLegibilityTests`. The first draft of this file
/// dropped the count, and a mutation critic showed what that cost: with no assertion that the walk
/// actually happened, `roomRewardRadius` could be widened to 400 units and every test stayed green,
/// because the player was already "on" the plinth from across the room.
let private walkUntil target until budget model =
    let mutable current = model
    let mutable steps = 0
    while steps < budget && not (until current) do
        current <- steerStep target current
        steps <- steps + 1
    current, steps

let private walkTo target until budget model = walkUntil target until budget model |> fst

let private roomOfType kind (model: Model) =
    model.Floor.Rooms |> Map.values |> Seq.find (fun room -> room.RoomType = kind) |> _.Id

let private itemNamed id = Rogue3.Entities.baseItems |> List.find (fun definition -> definition.Id = id)

let private slotOffering offer price : Rogue3.Entities.ShopSlot =
    { Id = 1; Offer = offer; Price = price; KeyLocked = false }

let private rich model = { model with PlayerCurrency = { model.PlayerCurrency with Coins = 50 } }

/// Every world-space drawable bound a scene reports, so the collection point can be checked against
/// where the RENDERER actually puts the plinth rather than against a restatement of its own formula.
let private boundsOf (scene: Scene) =
    SceneInspection.inspect
        { X = -playfieldWidth; Y = -playfieldHeight; Width = playfieldWidth * 3.0; Height = playfieldHeight * 3.0 }
        scene
    |> List.filter _.Contributes
    |> List.choose (fun node ->
        match node.Bounds with
        | SceneDrawableBounds.Known bounds -> Some bounds
        | _ -> None)

/// The authored item table, written out independently of `Model.itemModifiers` so that comparing the
/// two is an assertion rather than a restatement. Four of these arms could be DELETED with a green
/// suite before this existed.
let private expectedModifiers: Map<string, StatModifier list> =
    Map.ofList
        [ "coal-heart", [ { Stat = DamageStat; Kind = Add; Value = 1.0 }; { Stat = KnockbackStat; Kind = Add; Value = 10.0 } ]
          "cracked-lens", [ { Stat = MultishotStat; Kind = Add; Value = 1.0 }; { Stat = DamageStat; Kind = Mul; Value = -0.25 } ]
          "iron-teeth", [ { Stat = DamageStat; Kind = Add; Value = 2.0 }; { Stat = TearDelayStat; Kind = Add; Value = 2.0 } ]
          "void-map", [ { Stat = RangeStat; Kind = Add; Value = 0.5 }; { Stat = ShotSpeedStat; Kind = Add; Value = 60.0 } ]
          "maggot-crown", [ { Stat = PierceStat; Kind = Add; Value = 1.0 }; { Stat = ShotRadiusStat; Kind = Add; Value = 2.0 } ]
          "choir-bell", [ { Stat = TearDelayStat; Kind = Add; Value = -2.0 }; { Stat = HomingStat; Kind = Add; Value = 0.3 } ] ]

let private statsOf id = recomputePlayerStats [ { Id = id; Modifiers = expectedModifiers.[id] } ]

// ----------------------------------------------------------------------------------------------
// Board item #55 fixtures — a REAL shop room, reached the way a run reaches one.
// ----------------------------------------------------------------------------------------------

/// Let go of the steering. `walkUntil` leaves movement keys held, and a player who is still
/// accelerating drifts off the plinth between the key press and the step that reads it.
let private releaseMovement model =
    [ 'W'; 'A'; 'S'; 'D' ] |> List.fold (fun current letter -> setKey letter false current) model

/// Press interact and let the fixed step read it. `Model.playerRoomIntentsIn` fires on the RISING
/// edge, so the release is part of the gesture rather than a tidy-up.
let private pressInteract model = model |> setKey 'E' true |> tick |> setKey 'E' false |> tick

/// Hold interact down across `ticks` steps without ever releasing it.
let private holdInteract ticks model =
    let held = setKey 'E' true model
    [ 1 .. ticks ] |> List.fold (fun current _ -> tick current) held

/// Floor two of the boot seed, which is the first floor that has a shop at all — `FloorGeneration`
/// only adds `RoomKind.Shop` to its special set when `floorIndex >= 2`. Stated as a fixture rather
/// than as a comment, because "a player can reach a shop" is false on floor one by construction and
/// a test that quietly used floor one would be proving the wrong thing.
let private shopFloor =
    FloorGeneration.generateWithPool boot.RunSeed 2 (Rogue3.Entities.itemPool [])

let private shopRoomId =
    shopFloor.Floor.Rooms |> Map.values |> Seq.find (fun room -> room.RoomType = Shop) |> _.Id

/// A model standing in that shop room, reached through the production room-entry message.
let private inShop coins =
    { boot with
        FloorIndex = 2
        Floor = shopFloor.Floor
        M5ItemPool = shopFloor.ItemPool
        PlayerCurrency = { boot.PlayerCurrency with Coins = coins } }
    |> update (EnterM5Room shopRoomId)
    |> fst

/// Steer onto a NAMED slot until the purchase route's own sensor reports that slot, then stop
/// steering. The slot id is load-bearing: the stock stands in a row, so a walk to the far slot
/// passes through the reach of the near ones, and stopping at "any slot" silently tests a different
/// purchase than the one the test set up.
let private standAtSlot slotId (at: Vec2) model =
    let arrived, steps =
        walkUntil at (fun current -> (shopSlotUnderPlayer current |> Option.map (fst >> _.Id)) = Some slotId) 900 model
    releaseMovement arrived, steps

/// The first pair of placed slots, IN SLOT ORDER, close enough together that one player position
/// stands inside both reaches, and a point four tenths of the way from the second-listed slot toward
/// the first. Returns `(nearId, farId, standPoint)`.
///
/// Taking the pair in slot order is load-bearing twice over: the NEARER slot is the one listed
/// second, so "nearest wins" cannot be satisfied by "first in the list wins"; and having two slots in
/// reach is what makes a per-step repeat visible, since the first purchase empties the slot it bought
/// and a correctly-gated press has nothing further to take.
let private straddledPair (model: Model) =
    let placed = List.zip (model.M5ShopSlots |> List.map _.Id) (shopSlotPositions model)
    let reach = shopSlotRadius + playerRadius
    let (farId, farAt), (nearId, nearAt) =
        List.allPairs placed placed
        |> List.find (fun ((idA, a), (idB, b)) ->
            let gap = magnitude (sub a b)
            idA < idB && gap > 0.0 && gap < reach * 2.0)
    let gap = magnitude (sub farAt nearAt)
    let towards = scale (1.0 / gap) (sub farAt nearAt)
    nearId, farId, add nearAt (scale (gap * 0.4) towards)

/// A room stocked past the authored candidate list, so `placeRoomFixtures` falls through to its
/// LATTICE. Two things only that lattice can produce are needed below: slots close enough together
/// that one player position reaches two of them (it steps by `obstacleClearance`, and the interact
/// reach is `shopSlotRadius + playerRadius` on each side), and a slot placed straight onto the
/// trapdoor — the fallback arm never runs `placementAccepts`, so it does not honour the hatch
/// exclusion the authored arm enforces.
let private crowdedShop coins =
    let slots: Rogue3.Entities.ShopSlot list =
        List.init 169 (fun index -> { Id = index; Offer = Rogue3.Entities.ShopOffer.Consumable Rogue3.Entities.PickupKind.Key; Price = 1; KeyLocked = false })
    let blocking =
        placeRoomFixtures [] 12
        |> List.mapi (fun index at -> Rogue3.Entities.obstacle Rogue3.Entities.ObstacleKind.Rock index |> Rogue3.Entities.obstacleAt at)
    { inShop coins with M5ShopSlots = slots; M5Obstacles = blocking }

let private readyElements model =
    Render.renderedElements model |> List.filter (fun element -> element.ElementId = "ShopSlotReady")

/// Every string a scene draws, and every stroke/fill colour it uses. A prompt that a player must be
/// able to READ has to be asserted on its words and its colour, not on "the two scenes differ" —
/// that comparison is satisfied by any incidental difference, and it let a mutant that made both
/// prompts say `E  BUY` survive.
let rec private nodeTexts (node: SceneNode) =
    match node with
    | Group scenes -> scenes |> List.collect (fun scene -> scene.Nodes |> List.collect nodeTexts)
    | Text(_, value, _)
    | SizedText(_, value, _, _) -> [ value ]
    | Translate(_, scene) -> scene.Nodes |> List.collect nodeTexts
    | _ -> []

let private sceneTexts (scene: Scene) = scene.Nodes |> List.collect nodeTexts

let rec private nodeColors (node: SceneNode) =
    match node with
    | Group scenes -> scenes |> List.collect (fun scene -> scene.Nodes |> List.collect nodeColors)
    | Translate(_, scene) -> scene.Nodes |> List.collect nodeColors
    | Text(_, _, paint)
    | SizedText(_, _, _, paint) -> [ paint ]
    | Circle(_, _, paint) -> [ paint ]
    | Rectangle(_, paint) -> [ paint ]
    | _ -> []

let private sceneColors (scene: Scene) =
    scene.Nodes |> List.collect nodeColors |> List.distinct

/// A point in the doorway on `direction`, pulled just inside the room — M11's helper, because the
/// walk that reaches a shop room is the same walk that crosses any other door.
let private doorwayTarget direction =
    let wall = wallMidpoint direction
    match direction with
    | North -> vec2 wall.Vx (wall.Vy + 4.0)
    | South -> vec2 wall.Vx (wall.Vy - 4.0)
    | West -> vec2 (wall.Vx + 4.0) wall.Vy
    | East -> vec2 (wall.Vx - 4.0) wall.Vy

[<Tests>]
let m14ItemGrantTests =
    testList
        "M14 item grants"
        [

          // --------------------------------------------------------------------------------------
          // The mapping. `Entities.ItemDefinition` is content; `StatModifier` is player state.
          // Nothing joined them before this item, which is why no grant route could be written.
          // --------------------------------------------------------------------------------------

          test "every authored item maps to exactly the modifiers it declares" {
              Expect.equal
                  (Rogue3.Entities.baseItems |> List.map _.Id |> List.sort)
                  (expectedModifiers |> Map.toList |> List.map fst |> List.sort)
                  "the expected table covers the whole pool and nothing else"
              for item in Rogue3.Entities.baseItems do
                  Expect.equal (itemModifiers item) expectedModifiers.[item.Id] $"{item.Id} maps to its authored modifiers"
          }

          test "each item's recomputed stat line is the exact value it claims" {
              // VALUES, not shape. `Expect.isNonEmpty` plus "differs from base" was the first draft,
              // and it let four of six arms be deleted and both tear-delay signs be flipped.
              Expect.equal (statsOf "coal-heart").Damage 4.5 "coal-heart adds a flat point of damage"
              Expect.equal (statsOf "coal-heart").Knockback 50.0 "and ten knockback"

              Expect.equal (statsOf "cracked-lens").Multishot 2 "cracked-lens adds a second shot"
              Expect.equal (statsOf "cracked-lens").Damage 2.625 "at three quarters damage"

              Expect.equal (statsOf "iron-teeth").Damage 5.5 "iron-teeth is the heavy hitter"
              Expect.equal (statsOf "void-map").Range 2.1 "void-map extends range"
              Expect.equal (statsOf "void-map").ShotSpeed 480.0 "and shot speed"
              Expect.equal (statsOf "maggot-crown").Pierce 1 "maggot-crown pierces one extra enemy"
              Expect.equal (statsOf "maggot-crown").ShotRadius 7.0 "with a fatter shot"
              Expect.equal (statsOf "choir-bell").Homing 0.3 "choir-bell homes"

              // DIRECTION, asserted separately from the table so a sign flip fails even if someone
              // "fixes" the table to match. `recomputePlayerStats` derives FireRate = 30 / tearDelay,
              // so the sign of a TearDelayStat modifier is inverted relative to how it reads.
              Expect.isLessThan (statsOf "iron-teeth").FireRate basePlayerStats.FireRate "iron-teeth trades rate of fire for damage"
              Expect.isGreaterThan (statsOf "choir-bell").FireRate basePlayerStats.FireRate "choir-bell buys rate of fire"
              Expect.floatClose Accuracy.high (statsOf "choir-bell").FireRate 3.0 "exactly 30/10"
              Expect.floatClose Accuracy.high (statsOf "iron-teeth").FireRate (30.0 / 14.0) "exactly 30/14"
          }

          test "an unauthored item is never a silent no-op, at any quality" {
              // Totality. An item added to the pool later must not regress to granting nothing —
              // that would be this board item's own defect, one level down.
              let unknown quality : Rogue3.Entities.ItemDefinition =
                  { Id = "not-authored"; Quality = quality; Tags = Set.ofList [ "shop" ] }
              Expect.isNonEmpty (itemModifiers (unknown 2)) "an unauthored id still resolves to a modifier"
              // Quality-scaled: 0.5 + 0.5*q added to the 3.5 base.
              Expect.equal (recomputePlayerStats [ playerItemOf (unknown 0) ]).Damage 4.0 "quality 0 still grants half a point"
              Expect.equal (recomputePlayerStats [ playerItemOf (unknown 3) ]).Damage 5.5 "quality 3 grants two"
              // `max 0` on the quality: a negative-quality item must not SUBTRACT damage.
              Expect.equal (recomputePlayerStats [ playerItemOf (unknown -4) ]).Damage 4.0 "a negative quality is clamped, not subtracted"
          }

          test "the stat clamps hold against an absurd item" {
              // `recomputePlayerStats` had ZERO production callers at 3913c26 — the only code that
              // called it was the dead reducer #20 removed. This change puts it on the live path for
              // the first time, so its clamps are now load-bearing and a mutation critic found three
              // of them unpinned. An item is content and content changes, so the bounds are what
              // stand between a bad table entry and an unplayable run.
              let absurd modifiers = recomputePlayerStats [ { Id = "absurd"; Modifiers = modifiers } ]

              Expect.equal (absurd [ { Stat = TearDelayStat; Kind = Add; Value = -1000.0 } ]).FireRate 15.0 "fire rate is capped however fast the item claims to be"
              Expect.equal (absurd [ { Stat = TearDelayStat; Kind = Add; Value = 1000.0 } ]).FireRate 0.7 "and floored however slow"
              Expect.equal (absurd [ { Stat = PierceStat; Kind = Add; Value = -5.0 } ]).Pierce 0 "pierce cannot go negative"
              Expect.equal (absurd [ { Stat = BounceStat; Kind = Add; Value = -5.0 } ]).Bounce 0 "nor bounce"
              Expect.equal (absurd [ { Stat = MultishotStat; Kind = Add; Value = 500.0 } ]).Multishot 12 "multishot is capped"
              Expect.equal (absurd [ { Stat = DamageStat; Kind = Mul; Value = -10.0 } ]).Damage 0.5 "damage has a floor"
              Expect.equal (absurd [ { Stat = ShotSpeedStat; Kind = Add; Value = 100000.0 } ]).ShotSpeed 900.0 "shot speed is capped"
              Expect.equal (absurd [ { Stat = ShotSpeedStat; Kind = Add; Value = -100000.0 } ]).ShotSpeed 150.0 "and floored"
              Expect.equal (absurd [ { Stat = RangeStat; Kind = Add; Value = 100.0 } ]).Range 4.0 "range is capped"
              Expect.equal (absurd [ { Stat = SpeedMultiplierStat; Kind = Add; Value = -100.0 } ]).SpeedMultiplier -0.5 "the player can always still move"

              // A non-finite modifier must not poison the whole stat line.
              Expect.equal (absurd [ { Stat = DamageStat; Kind = Add; Value = nan } ]).Damage basePlayerStats.Damage "a NaN modifier contributes nothing"
              Expect.equal (absurd [ { Stat = DamageStat; Kind = Add; Value = infinity } ]).Damage basePlayerStats.Damage "and neither does an infinite one"
          }

          test "grantItem is the only writer, so ItemsFound can never disagree with PlayerItems" {
              // The board item's invariant, asserted over a SEQUENCE rather than one call, because
              // the failure it replaces was a counter and an inventory maintained in separate places.
              let granted = Rogue3.Entities.baseItems |> List.fold (fun model item -> grantItem item model) boot

              Expect.equal granted.PlayerItems.Length Rogue3.Entities.baseItems.Length "every grant appended exactly one item"
              Expect.equal granted.RunStats.ItemsFound granted.PlayerItems.Length "the counter tracks the inventory exactly"
              Expect.equal
                  (granted.PlayerItems |> List.map _.Id)
                  (Rogue3.Entities.baseItems |> List.map _.Id)
                  "in acquisition order"
              // The whole pool at once, as a literal: damage is (3.5 + 1 + 2) x 0.75 = 4.875 —
              // base, plus coal-heart's +1 and iron-teeth's +2, all through cracked-lens's x0.75.
              Expect.equal granted.PlayerStats.Damage 4.875 "the whole inventory folds into one damage figure"
              Expect.equal granted.PlayerStats.Multishot 2 "and one multishot figure"
              Expect.equal granted.PlayerStats.Pierce 1 "and one pierce figure"
          }

          test "the item-granted cue is declared, distinct, and reachable through the production map" {
              // M12's lesson: a cue keyed to something nothing dispatches is silence. The REMOVED
              // `RecordItemFound` was exactly that and carried the only `item-pickup` cue, so the new
              // event has to prove it crosses `AudioCues.forTransition`, not merely that it lands in
              // `model.AudioEvents`.
              let cuesFor event =
                  AudioCues.forTransition (Tick fixedDt) boot { boot with AudioEvents = [ event ] }
                  |> List.choose (function FS.GG.Audio.Core.PlaySfx(FS.GG.Audio.Core.SoundId id, volume) -> Some(id, volume) | _ -> None)

              Expect.equal (cuesFor AudioEvent.ItemGranted) [ "item-pickup", 0.85 ] "a granted item asks for the item-pickup cue"

              // Reflection, so adding an AudioEvent case WITHOUT cueing it fails here.
              let allEvents =
                  Reflection.FSharpType.GetUnionCases typeof<AudioEvent>
                  |> Array.map (fun case -> Reflection.FSharpValue.MakeUnion(case, [||]) :?> AudioEvent)

              // A PR reviewer disproved the first version of this guard's claim by experiment: it
              // said an event missing from the hand-maintained lists in `M8AudioTests` and
              // `M12AudioAssetTests` would fail here, and it would not — this test builds its own
              // case array and never reads those lists, so a new case WITH a cue sailed past all
              // three. Asserting the case count is what actually makes the claim true: adding a case
              // fails HERE, and this message is where the author is told the other two lists exist.
              // (Claiming a protection without exercising it is the failure mode this repository
              // keeps repeating, so it is not one to plant in a test comment.)
              Expect.equal
                  allEvents.Length 8
                  "AudioEvent case count changed: cue it above, and add it to the hand-maintained event lists in M8AudioTests.fs and M12AudioAssetTests.fs, which this guard cannot see"

              for event in allEvents do
                  match cuesFor event with
                  | [ _, volume ] -> Expect.isGreaterThan volume 0.0 $"{event} is audible"
                  | other -> failtest $"{event} maps to {other.Length} cues, expected exactly one"
              let ids = allEvents |> Array.collect (cuesFor >> List.map fst >> List.toArray)
              Expect.equal (ids |> Array.distinct |> Array.length) ids.Length "no two fixed-step events share a cue"

              let granted = grantItem (itemNamed "coal-heart") boot
              Expect.contains granted.AudioEvents AudioEvent.ItemGranted "and the grant raises it"
          }

          // --------------------------------------------------------------------------------------
          // Route 1 — the shop REDUCER. The call site named in the item's title.
          //
          // These dispatch `InteractM5Shop` directly, and are deliberately kept that way: they pin
          // the arithmetic of a purchase without a walk in the way. What they do NOT prove is that a
          // player can raise the message — that claim lives in "Route 1b" below and is driven from
          // `KeyChanged` + `Tick`. Said plainly here so this block does not imply a guarantee it has
          // not earned; that conflation is what let EHotwagner/rogue3#55 sit green behind #47.
          // --------------------------------------------------------------------------------------

          test "buying an item hands the item over" {
              let item = itemNamed "coal-heart"
              let model = { rich boot with M5ShopSlots = [ slotOffering (Rogue3.Entities.ShopOffer.Item item) 13 ] }
              let bought = update (InteractM5Shop 1) model |> fst

              Expect.equal bought.PlayerCurrency.Coins 37 "the coins are taken"
              Expect.equal (bought.PlayerItems |> List.map _.Id) [ "coal-heart" ] "and the item is handed over"
              Expect.equal bought.PlayerItems.Head.Modifiers expectedModifiers.["coal-heart"] "with its authored modifiers"
              Expect.equal bought.PlayerStats.Damage 4.5 "the recomputed damage includes the item's modifier"
              Expect.equal bought.PlayerStats.Knockback 50.0 "and so does knockback"
              Expect.equal bought.RunStats.ItemsFound bought.PlayerItems.Length "the counter and the inventory agree"
          }

          test "buying a CONSUMABLE hands the consumable over" {
              // Not in the item's title, and broken the same way: the reducer charged for a key, a
              // bomb or a heart and applied none of them. Routed through the same `applyFloorPickup`
              // a floor drop uses, so the caps are the shared ones.
              let buy offer =
                  let model = { rich boot with M5ShopSlots = [ slotOffering offer 5 ] }
                  update (InteractM5Shop 1) model |> fst

              let keyed = buy (Rogue3.Entities.ShopOffer.Consumable Rogue3.Entities.PickupKind.Key)
              Expect.equal keyed.PlayerCurrency.Keys (boot.PlayerCurrency.Keys + 1) "a bought key is received"
              Expect.equal keyed.PlayerCurrency.Coins 45 "and paid for"

              let bombed = buy (Rogue3.Entities.ShopOffer.Consumable Rogue3.Entities.PickupKind.Bomb)
              Expect.equal bombed.PlayerCurrency.Bombs (boot.PlayerCurrency.Bombs + 1) "a bought bomb is received"

              let souled = buy (Rogue3.Entities.ShopOffer.Consumable Rogue3.Entities.PickupKind.SoulHeart)
              Expect.equal souled.PlayerHealth.SoulHalfHearts 2 "a bought soul heart is received"

              // `Entities.generateShop` really does stock these two, and the first draft covered
              // neither: `HalfRedHeart` at 3 and `Coin3` at 3.
              let hurt = { rich boot with PlayerHealth = { boot.PlayerHealth with RedHalfHearts = 2 } }
              let healed =
                  update (InteractM5Shop 1) { hurt with M5ShopSlots = [ slotOffering (Rogue3.Entities.ShopOffer.Consumable Rogue3.Entities.PickupKind.HalfRedHeart) 3 ] }
                  |> fst
              Expect.equal healed.PlayerHealth.RedHalfHearts 3 "a bought half red heart heals exactly one half"

              // A consumable is not an item: it must not inflate the item counter or the inventory.
              Expect.isEmpty keyed.PlayerItems "a consumable does not enter the item inventory"
              Expect.equal keyed.RunStats.ItemsFound 0 "nor the item counter"
          }

          test "a BOUGHT coin is not income, and does not pay a run-score bonus" {
              // Found by a fresh-context critic on this change. Routing the consumable through
              // `applyFloorPickup` is right for the EFFECT and wrong for the ACCOUNTING: that function
              // credits `RunStats.CoinsCollected` because a coin on the floor is income, and
              // `runScore` pays `CoinsCollected * 5`. The shop's pool-exhausted fallback offer is
              // exactly `Coin3` priced at 3, so the first draft minted 15 score per slot for a
              // net-zero transaction — repeatable on every drained floor.
              let model =
                  { boot with
                      PlayerCurrency = { boot.PlayerCurrency with Coins = 10 }
                      M5ShopSlots = [ slotOffering (Rogue3.Entities.ShopOffer.Consumable Rogue3.Entities.PickupKind.Coin3) 3 ] }
              let bought = update (InteractM5Shop 1) model |> fst

              Expect.equal bought.PlayerCurrency.Coins 10 "three coins out, three coins in, so the purse is unchanged"
              Expect.equal bought.RunStats.CoinsCollected model.RunStats.CoinsCollected "and nothing is recorded as collected"
              Expect.equal (runScore bought.RunStats) (runScore model.RunStats) "so the run score does not move"

              let single =
                  update (InteractM5Shop 1) { model with M5ShopSlots = [ slotOffering (Rogue3.Entities.ShopOffer.Consumable Rogue3.Entities.PickupKind.Coin1) 1 ] }
                  |> fst
              Expect.equal single.PlayerCurrency.Coins 10 "a one-coin offer is the same wash"
              Expect.equal single.RunStats.CoinsCollected model.RunStats.CoinsCollected "and is likewise not income"
          }

          test "a refused or unknown purchase grants nothing and charges nothing" {
              let item = itemNamed "iron-teeth"
              let model =
                  { boot with
                      PlayerCurrency = { boot.PlayerCurrency with Coins = 2 }
                      M5ShopSlots = [ slotOffering (Rogue3.Entities.ShopOffer.Item item) 13 ] }
              let refused = update (InteractM5Shop 1) model |> fst

              Expect.equal refused.PlayerCurrency.Coins 2 "the coins stay"
              Expect.isEmpty refused.PlayerItems "and no item is granted"
              Expect.equal refused.PlayerStats basePlayerStats "and no stat moves"
              Expect.equal refused.RunStats.ItemsFound 0 "and nothing is counted"

              // The `| None -> model` arm: a slot id that is not stocked. Untested in the first
              // draft, so a mutant that granted an item for an unknown id passed green.
              let unknown = update (InteractM5Shop 99) (rich boot) |> fst
              Expect.isEmpty unknown.PlayerItems "an unstocked slot id grants nothing"
              Expect.equal unknown.RunStats.ItemsFound 0 "and counts nothing"
              Expect.equal unknown.PlayerCurrency (rich boot).PlayerCurrency "and charges nothing"
          }

          // --------------------------------------------------------------------------------------
          // Route 1b — the shop, FROM A KEY PRESS. Board item EHotwagner/rogue3#55.
          //
          // Everything above this heading dispatches `InteractM5Shop`. Everything below it dispatches
          // ONLY `KeyChanged` and `Tick`, so unwiring `Model.playerRoomIntentsIn`'s shop branch reds
          // this block — which is the whole point, and exactly what the three paragraphs of prose
          // this replaces could not do.
          // --------------------------------------------------------------------------------------

          test "floor one has no shop at all, so reaching one means descending" {
              // The reachability claim would be vacuous if it were made about a floor that cannot
              // have a shop, and it would be WRONG to imply a player meets a shop on floor one.
              // `FloorGeneration` adds `RoomKind.Shop` to the special set only when `floorIndex >= 2`.
              Expect.isEmpty
                  (boot.Floor.Rooms |> Map.values |> Seq.filter (fun room -> room.RoomType = Shop) |> Seq.toList)
                  "the boot floor presents no shop room"
              Expect.hasLength
                  (shopFloor.Floor.Rooms |> Map.values |> Seq.filter (fun room -> room.RoomType = Shop) |> Seq.toList)
                  1
                  "floor two presents exactly one"
          }

          test "the shop room is reachable through crossable doors, and arrives stocked" {
              // The M11 FR-009 shape: prove a route of crossable doors exists, WALK the first hop so
              // "reachable" means walked rather than asserted, and dispatch `TraverseDoor` for the
              // rest — that message is what the walk itself raises, proved in M11.
              let start = shopFloor.Floor.CurrentRoom
              let rec path visited roomId =
                  if roomId = shopRoomId then Some [ roomId ]
                  else
                      shopFloor.Floor.Rooms.[roomId].Doors
                      |> List.filter (fun door -> door.State = Open || door.State = BossDoor)
                      |> List.filter (fun door -> not (Set.contains door.ToRoom visited))
                      |> List.tryPick (fun door -> path (Set.add door.ToRoom visited) door.ToRoom |> Option.map (fun rest -> roomId :: rest))

              let route =
                  path (Set.singleton start) start
                  |> Option.defaultWith (fun () -> failtest "the shop room is reachable from the floor's start room")
              Expect.isGreaterThan route.Length 1 "and it is not the room the player starts in"

              let atStart = { boot with FloorIndex = 2; Floor = shopFloor.Floor; M5ItemPool = shopFloor.ItemPool } |> update (EnterM5Room start) |> fst
              let firstHop = route |> List.item 1
              let firstDirection =
                  atStart.Floor.Rooms.[start].Doors |> List.find (fun door -> door.ToRoom = firstHop) |> _.Direction
              let walked, steps = walkUntil (doorwayTarget firstDirection) (fun model -> model.Floor.CurrentRoom = firstHop) 900 atStart
              Expect.equal walked.Floor.CurrentRoom firstHop "the first hop is WALKED into, not dispatched"
              Expect.isGreaterThan steps 1 "and it took real movement"

              let arrived =
                  route
                  |> List.skip 2
                  |> List.fold (fun model roomId -> update (TraverseDoor roomId) model |> fst) (releaseMovement walked)

              Expect.equal arrived.Floor.CurrentRoom shopRoomId "the walk reaches the shop room"
              Expect.isNonEmpty arrived.M5ShopSlots "which arrives stocked"
              Expect.isNonEmpty
                  (arrived.M5ShopSlots |> List.filter (fun slot -> slot.Offer <> Rogue3.Entities.ShopOffer.Empty))
                  "with something actually for sale"
          }

          test "the slot the purchase route senses is the slot the RENDERER draws, in both axes" {
              // The shop half of the pedestal's drift guard. A slot bought somewhere other than where
              // it is drawn is a slot a player cannot find, and `Render` computes its placement list
              // with a DIFFERENT count argument (it adds the reward plinth), so the prefix property
              // that makes the two agree has to be asserted rather than assumed.
              let model = inShop 50
              let sensed = shopSlotPositions model
              Expect.hasLength sensed model.M5ShopSlots.Length "one sensed position per stocked slot"

              let drawn =
                  Render.renderedElements model
                  |> List.filter (fun element -> element.ElementId = "ShopItem")
                  |> List.map (fun element ->
                      let bounds = boundsOf element.Scene
                      let left = bounds |> List.map _.X |> List.min
                      let right = bounds |> List.map (fun b -> b.X + b.Width) |> List.max
                      let top = bounds |> List.map _.Y |> List.min
                      let bottom = bounds |> List.map (fun b -> b.Y + b.Height) |> List.max
                      (left + right) / 2.0, top, bottom)

              Expect.hasLength drawn sensed.Length "the renderer draws one plinth per sensed slot"
              for (at: Vec2), (centreX, top, bottom) in List.zip sensed drawn do
                  Expect.isTrue (abs (centreX - at.Vx) < 6.0) $"slot sensed at x {at.Vx} is drawn at {centreX}"
                  // The drawn extent runs from the stock ABOVE the sensed point to the price label
                  // BELOW it, so the y claim is that the sensed point sits inside a plinth-sized
                  // envelope around it — not that it coincides with an edge. Both ends are bounded, so
                  // a mutant that widened the drawing (or moved the sensor off the fixture) fails.
                  Expect.isTrue (at.Vy >= top && at.Vy <= bottom) $"slot sensed at y {at.Vy} lies inside the drawn extent {top}..{bottom}"
                  Expect.isTrue (at.Vy - top < 40.0) $"the stock is drawn just above the sensed y {at.Vy} (top {top})"
                  Expect.isTrue (bottom - at.Vy < 60.0) $"and the price label just below it (bottom {bottom})"
          }

          test "a player walks to a slot, presses interact, and receives what was charged for" {
              // THE item. Only `KeyChanged` and `Tick` are dispatched from here to the assertion.
              let model = inShop 50
              let slot, at =
                  List.zip model.M5ShopSlots (shopSlotPositions model)
                  |> List.find (fun (slot, _) -> match slot.Offer with Rogue3.Entities.ShopOffer.Item _ -> not slot.KeyLocked | _ -> false)
              let item = match slot.Offer with Rogue3.Entities.ShopOffer.Item value -> value | other -> failtest $"expected an item offer, got {other}"

              let standing, steps = standAtSlot slot.Id at model
              Expect.isGreaterThan steps 1 "the player actually WALKED to the stock"
              Expect.equal (shopSlotUnderPlayer standing |> Option.map (fst >> _.Id)) (Some slot.Id) "and is standing at the slot they walked to"
              Expect.isTrue (shopSlotAffordable standing slot) "which they can afford"
              Expect.isEmpty standing.PlayerItems "and they hold nothing yet"

              let bought = pressInteract standing

              Expect.equal (bought.PlayerItems |> List.map _.Id) [ item.Id ] "pressing interact hands the item over"
              Expect.equal bought.PlayerCurrency.Coins (50 - slot.Price) "and takes exactly the marked price"
              Expect.equal bought.PlayerItems.Head.Modifiers expectedModifiers.[item.Id] "with its authored modifiers"
              Expect.equal bought.PlayerStats (statsOf item.Id) "and the stat line those modifiers produce"
              Expect.notEqual bought.PlayerStats basePlayerStats "which is not the base line any more"
              Expect.equal bought.RunStats.ItemsFound 1 "counted exactly once"
              Expect.equal
                  (bought.M5ShopSlots |> List.find (fun other -> other.Id = slot.Id) |> _.Offer)
                  Rogue3.Entities.ShopOffer.Empty
                  "and the slot is emptied"
          }

          test "a bought CONSUMABLE arrives through the same key press" {
              let model = inShop 50
              let slot, at =
                  List.zip model.M5ShopSlots (shopSlotPositions model)
                  |> List.find (fun (slot, _) -> match slot.Offer with Rogue3.Entities.ShopOffer.Consumable _ -> true | _ -> false)
              let kind = match slot.Offer with Rogue3.Entities.ShopOffer.Consumable value -> value | other -> failtest $"expected a consumable, got {other}"

              let standing, _ = standAtSlot slot.Id at model
              let bought = pressInteract standing

              Expect.equal bought.PlayerCurrency.Coins (50 - slot.Price) "the price is taken"
              // Compare against what the SHARED pickup rule does to the same starting model, so this
              // asserts the effect landed without restating `applyFloorPickup`'s table here.
              let expected = applyFloorPickup kind { standing with PlayerCurrency = { standing.PlayerCurrency with Coins = 50 - slot.Price } }
              Expect.equal bought.PlayerHealth expected.PlayerHealth "and the consumable's effect on health is the floor-pickup effect"
              Expect.equal bought.PlayerCurrency expected.PlayerCurrency "and on the purse"
              Expect.isEmpty bought.PlayerItems "a consumable is not an item"

              // The comparison above is against the shared rule, which is right, but on its own it is
              // satisfied by the rule doing NOTHING — and a consumable that lands nothing is exactly
              // the defect below. Pin that this fixture actually moves the player, so the assertions
              // above are comparing two changes rather than two nil effects.
              Expect.notEqual (bought.PlayerHealth, bought.PlayerCurrency.Keys, bought.PlayerCurrency.Bombs)
                  (standing.PlayerHealth, standing.PlayerCurrency.Keys, standing.PlayerCurrency.Bombs)
                  "the bought consumable changed the player, so this test is not comparing two no-ops"
          }

          test "the shop refuses a consumable that would land NOTHING, rather than charging for it" {
              // Found by a critic, in ordinary play, after the suite was green. `Entities.purchase`
              // asks only whether the player can PAY. `applyFloorPickup` silently no-ops at a cap —
              // `addCurrency` stops at 99, `healRed` stops at the container count — so a heart bought
              // at full health took the coins, emptied the offer, returned an identical player and
              // played the acquisition cue, while the prompt had said `E  BUY` in the affordable
              // colour. `#55` is what let a key press reach it and what made the loss permanent: the
              // stock write-back means the slot does not come back on re-entry.
              //
              // `Entities.generateShop` stocks exactly HalfRedHeart, Key, Bomb and SoulHeart, so a
              // full-health player standing at a heart slot is not a contrived state.
              let capped: (string * Rogue3.Entities.PickupKind * (Model -> Model)) list =
                  [ "a heart at full health", Rogue3.Entities.PickupKind.HalfRedHeart, id
                    "a key at the currency cap", Rogue3.Entities.PickupKind.Key,
                        (fun m -> { m with PlayerCurrency = { m.PlayerCurrency with Keys = 99 } })
                    "a bomb at the currency cap", Rogue3.Entities.PickupKind.Bomb,
                        (fun m -> { m with PlayerCurrency = { m.PlayerCurrency with Bombs = 99 } }) ]

              for label, kind, atCap in capped do
                  let slot: Rogue3.Entities.ShopSlot =
                      { Id = 0; Offer = Rogue3.Entities.ShopOffer.Consumable kind; Price = 3; KeyLocked = false }
                  let standing = atCap { inShop 50 with M5ShopSlots = [ slot ] }

                  Expect.isFalse (shopSlotAffordable standing slot) $"{label} is not on offer"
                  Expect.equal (shopSlotRefusal standing slot) (Some "FULL") $"{label} is refused for the reason a player can act on"

                  let pressed = update (InteractM5Shop 0) standing |> fst
                  Expect.equal pressed.PlayerCurrency standing.PlayerCurrency $"{label} costs nothing"
                  Expect.equal pressed.PlayerHealth standing.PlayerHealth $"{label} changes no health"
                  Expect.equal (pressed.M5ShopSlots |> List.map _.Offer) [ slot.Offer ] $"{label} is still on the plinth afterwards"

                  let cues =
                      AudioCues.forTransition (InteractM5Shop 0) standing pressed
                      |> Audio.interpret |> _.Requested
                      |> List.choose (function PlaySfx(SoundId id, _) -> Some id | _ -> None)
                  Expect.isEmpty cues $"{label} does not sound like a completed purchase"

              // The control, and the guarantee the refusal must not break: the SAME slot at the same
              // price sells the moment it has somewhere to land.
              let heart: Rogue3.Entities.ShopSlot =
                  { Id = 0; Offer = Rogue3.Entities.ShopOffer.Consumable Rogue3.Entities.PickupKind.HalfRedHeart; Price = 3; KeyLocked = false }
              let hurt =
                  { inShop 50 with
                      M5ShopSlots = [ heart ]
                      PlayerHealth = { (inShop 50).PlayerHealth with RedHalfHearts = 2 } }
              Expect.isTrue (shopSlotAffordable hurt heart) "a hurt player can buy the same heart"
              let healed = update (InteractM5Shop 0) hurt |> fst
              Expect.equal healed.PlayerCurrency.Coins (hurt.PlayerCurrency.Coins - 3) "and pays for it"
              Expect.equal healed.PlayerHealth.RedHalfHearts 3 "and is healed by it"

              // The shop's pool-exhausted fallback is `Coin3` priced at 3, so at the coin cap the
              // sale is net zero but NOT a no-op: the payment makes room for the coins it returns.
              // This is why the landing test is made after payment, and it is the case a check made
              // before payment would wrongly refuse.
              let coins: Rogue3.Entities.ShopSlot =
                  { Id = 0; Offer = Rogue3.Entities.ShopOffer.Consumable Rogue3.Entities.PickupKind.Coin3; Price = 3; KeyLocked = false }
              let flush = { inShop 99 with M5ShopSlots = [ coins ] }
              Expect.isTrue (shopSlotAffordable flush coins) "the Coin3 fallback still sells at the coin cap"
              Expect.equal (update (InteractM5Shop 0) flush |> fst).PlayerCurrency.Coins 99 "and leaves the purse where it was"
          }

          test "buying one slot does not MOVE the plinths the player is not standing at" {
              // A surviving mutant found this hole: `shopSlotPositions` counts `M5ShopSlots.Length`,
              // and counting only the still-stocked slots instead passes every other test in this
              // file. It would also make every remaining plinth slide sideways the instant a
              // neighbour was bought, under the player's feet and under the renderer at once — and
              // because sensor and renderer read the SAME function, they would agree with each other
              // while both lied about where the shop is. Agreement is not correctness; the positions
              // have to be pinned as STABLE across a purchase, which nothing else here does.
              let model = inShop 50
              let before = shopSlotPositions model
              let target =
                  model.M5ShopSlots
                  |> List.find (fun slot -> slot.Offer <> Rogue3.Entities.ShopOffer.Empty && not slot.KeyLocked)
              let bought = update (InteractM5Shop target.Id) model |> fst
              Expect.notEqual (bought.M5ShopSlots |> List.map _.Offer) (model.M5ShopSlots |> List.map _.Offer) "the purchase really happened"
              Expect.equal (shopSlotPositions bought) before "every slot is still drawn and sensed where it was"

              // And the renderer agrees, at the position rather than merely in count — the same
              // property one level out, read through the production scene rather than the helper.
              // Only the slots that were NOT bought are compared: the bought one legitimately redraws
              // as a bare plinth with no stock and no price label, so its bounds are supposed to
              // change. Its NEIGHBOURS are what must not move.
              let untouched (m: Model) =
                  Render.renderedElements m
                  |> List.filter (fun element -> element.ElementId = "ShopItem")
                  |> List.indexed
                  |> List.filter (fun (index, _) -> List.tryItem index m.M5ShopSlots |> Option.map _.Id <> Some target.Id)
                  |> List.map (fun (_, element) -> boundsOf element.Scene)
              Expect.isNonEmpty (untouched bought) "there is a neighbouring slot to compare"
              Expect.equal (untouched bought) (untouched model) "and the renderer draws the untouched slots in the same places"
          }

          test "a bought slot stays bought when the player leaves and comes back" {
              // Found by making the purchase reachable. `loadM5Room` re-reads a room's stock from
              // `FloorGeneration.ShopStock` on every entry, and nothing wrote the emptied offers back
              // — so walking out and back in restored the whole shop. While `InteractM5Shop` had no
              // production dispatch site that was a latent bug; the moment a key press reaches it, it
              // is an item engine bounded only by the coin cap. Same durability rule as the reward
              // plinth (`withoutRewardFixture`) and the smashed pot (§14.15).
              let model = inShop 99
              let slot, at =
                  List.zip model.M5ShopSlots (shopSlotPositions model)
                  |> List.find (fun (slot, _) -> match slot.Offer with Rogue3.Entities.ShopOffer.Item _ -> not slot.KeyLocked | _ -> false)
              let standing, _ = standAtSlot slot.Id at model
              let bought = pressInteract standing
              Expect.equal bought.PlayerItems.Length 1 "the item is bought once"

              Expect.isTrue
                  (bought.Floor.Rooms.[bought.Floor.CurrentRoom].Fixtures
                   |> List.exists (function
                       | ShopStock stock -> stock |> List.exists (fun other -> other.Id = slot.Id && other.Offer = Rogue3.Entities.ShopOffer.Empty)
                       | _ -> false))
                  "the FLOOR record shows the slot emptied, not just the loaded room"

              let revisited = update (EnterM5Room bought.Floor.CurrentRoom) bought |> fst
              Expect.equal
                  (revisited.M5ShopSlots |> List.find (fun other -> other.Id = slot.Id) |> _.Offer)
                  Rogue3.Entities.ShopOffer.Empty
                  "re-entering the shop finds that slot still sold"
              Expect.isNonEmpty
                  (revisited.M5ShopSlots |> List.filter (fun other -> other.Offer <> Rogue3.Entities.ShopOffer.Empty))
                  "while the slots that were NOT bought are still for sale"

              let standingAgain, _ = walkUntil at (fun current -> magnitude (sub current.PlayerPosition at) < shopSlotRadius) 900 revisited
              let again = pressInteract (releaseMovement standingAgain)
              Expect.equal again.PlayerItems.Length 1 "and pressing interact on the sold slot grants nothing further"
              Expect.equal again.PlayerCurrency.Coins bought.PlayerCurrency.Coins "and charges nothing further"
          }

          test "walking across the whole shop buys nothing without a press" {
              // The reason a slot is INTERACT and not walk-on. A player pathing through a shop must
              // not be charged: walk-on would let them bankrupt themselves by moving.
              let model = inShop 50
              let positions = shopSlotPositions model
              let strolled =
                  positions
                  |> List.fold (fun current at -> walkTo at (fun _ -> false) 400 current) model
                  |> releaseMovement

              Expect.isTrue
                  (positions |> List.exists (fun at -> magnitude (sub strolled.PlayerPosition at) < shopSlotRadius + playerRadius))
                  "the stroll really did end standing on a slot"
              Expect.equal strolled.PlayerCurrency model.PlayerCurrency "and not one coin was spent"
              Expect.equal strolled.M5ShopSlots model.M5ShopSlots "the stock is untouched"
              Expect.isEmpty strolled.PlayerItems "and nothing was granted"
          }

          test "pressing interact away from the stock buys nothing" {
              let model = inShop 50
              let at = shopSlotPositions model |> List.head
              let standoff = shopSlotRadius + playerRadius + 60.0
              let spot = vec2 (if at.Vx > playfieldWidth / 2.0 then at.Vx - standoff else at.Vx + standoff) at.Vy
              let parked = walkTo spot (fun _ -> false) 400 model |> releaseMovement

              Expect.isNone (shopSlotUnderPlayer parked) "the player is outside every slot's reach"
              let pressed = pressInteract parked
              Expect.equal pressed.PlayerCurrency model.PlayerCurrency "and pressing interact spends nothing"
              Expect.equal pressed.M5ShopSlots model.M5ShopSlots "and empties nothing"
              Expect.isEmpty pressed.PlayerItems "and grants nothing"
          }

          test "a held key, and a multi-step host frame, each buy exactly ONE slot" {
              // `advanceSim` drains up to five fixed steps per host frame and rotates
              // `Input.Previous` only after the loop, which is why `interactPressed` takes
              // `isFirstStep`. Drop that argument and one press raises the intent on every step of
              // the frame. Both halves are driven here: a key held for sixty steps, and a single
              // frame whose `dt` is large enough to drain several steps at once.
              let model = inShop 99
              let slot, at =
                  List.zip model.M5ShopSlots (shopSlotPositions model)
                  |> List.find (fun (slot, _) -> not slot.KeyLocked && slot.Offer <> Rogue3.Entities.ShopOffer.Empty)
              let standing, _ = standAtSlot slot.Id at model
              let emptyCount (m: Model) =
                  m.M5ShopSlots |> List.filter (fun other -> other.Offer = Rogue3.Entities.ShopOffer.Empty) |> List.length

              let held = holdInteract 60 standing
              Expect.equal (emptyCount held) (emptyCount standing + 1) "sixty steps of a held key empty exactly one slot"
              Expect.equal held.PlayerCurrency.Coins (99 - slot.Price) "and charge exactly one price"

              // One host frame, several fixed steps inside it.
              let burst = standing |> setKey 'E' true |> fun m -> update (Tick(fixedDt * 4.0)) m |> fst
              Expect.equal (emptyCount burst) (emptyCount standing + 1) "a four-step frame also empties exactly one slot"
              Expect.equal burst.PlayerCurrency.Coins (99 - slot.Price) "and charges exactly one price"
          }

          test "a refused purchase changes nothing, and the player is TOLD why" {
              // The acceptance's second half. `Entities.purchase` returns ok=false and
              // `purchaseM5ShopSlot` returns the model unchanged, so a refusal moves no currency, no
              // offer, no counter and no audio — before this it was indistinguishable from pressing
              // interact at empty floor.
              let model = inShop 0
              let slot, at =
                  List.zip model.M5ShopSlots (shopSlotPositions model)
                  |> List.find (fun (slot, _) -> not slot.KeyLocked && slot.Offer <> Rogue3.Entities.ShopOffer.Empty)
              Expect.isFalse (shopSlotAffordable model slot) "a penniless player cannot afford it"

              let standing, _ = standAtSlot slot.Id at model
              let refused = pressInteract standing

              Expect.equal refused.PlayerCurrency standing.PlayerCurrency "nothing is charged"
              Expect.equal refused.M5ShopSlots standing.M5ShopSlots "nothing is emptied"
              Expect.isEmpty refused.PlayerItems "nothing is granted"
              Expect.equal refused.RunStats.ItemsFound 0 "and nothing is counted"

              // PERCEIVABLE. The refused frame must exist and must not be the affordable frame.
              match readyElements refused with
              | [ element ] ->
                  let affordable =
                      { refused with PlayerCurrency = { refused.PlayerCurrency with Coins = 99; Keys = 9 } }
                      |> readyElements
                      |> List.exactlyOne
                  Expect.isNonEmpty (boundsOf element.Scene) "the refused prompt actually draws something"

                  // WORDS. The refusal names the price the player is short of, and does not offer a
                  // purchase. Asserting only `Scene <> Scene` is not enough: a prompt that says
                  // `E  BUY` in both states differs in stroke width and passes that.
                  Expect.equal (sceneTexts affordable.Scene) [ "E  BUY" ] "the affordable prompt offers the purchase"
                  Expect.equal (sceneTexts element.Scene) [ $"NEED %d{slot.Price}c" ] "the refused prompt names what is missing instead"

                  // COLOUR. A player reading at a glance sees the halo before the words.
                  Expect.notEqual (sceneColors element.Scene) (sceneColors affordable.Scene) "and the two are drawn in different colours"

                  // A KEY-LOCKED slot cannot be paid for in coins at all, so its refusal must not
                  // quote a coin price the player could never spend.
                  // The boot player starts holding a key, so the keyless state has to be stated.
                  match model.M5ShopSlots |> List.tryFind _.KeyLocked with
                  | Some locked ->
                      let at = List.item (model.M5ShopSlots |> List.findIndex (fun other -> other.Id = locked.Id)) (shopSlotPositions model)
                      let keyless = { model with PlayerPosition = at; PlayerCurrency = { model.PlayerCurrency with Keys = 0 } }
                      Expect.isFalse (shopSlotAffordable keyless locked) "a keyless player cannot take a key-locked slot"
                      Expect.equal (sceneTexts (readyElements keyless |> List.exactlyOne).Scene) [ "NEED KEY" ] "a key-locked slot asks for a key, not for coins"
                      // And with a key in hand it is offered, at no coin cost — the price on the
                      // plinth is not what a key-locked slot charges.
                      let keyed = { keyless with PlayerCurrency = { keyless.PlayerCurrency with Keys = 1 } }
                      Expect.equal (sceneTexts (readyElements keyed |> List.exactlyOne).Scene) [ "E  BUY" ] "a key in hand turns the same slot into an offer"
                      let spent = pressInteract keyed
                      Expect.equal spent.PlayerCurrency.Keys 0 "buying it spends the key"
                      Expect.equal spent.PlayerCurrency.Coins keyed.PlayerCurrency.Coins "and no coins, which the player has none of"
                  | None -> failtest "this shop was expected to stock a key-locked slot"
              | other -> failtest $"expected exactly one shop prompt at a refused slot, got {other.Length}"
          }

          test "the prompt appears exactly where the purchase route would fire, and nowhere else" {
              // Sensor and affordance from ONE predicate. If these could disagree the product would
              // either promise a purchase it refuses to raise, or hide one it would.
              let model = inShop 50
              let positions = shopSlotPositions model

              let away = { model with PlayerPosition = vec2 (playfieldWidth - 80.0) 120.0 }
              Expect.isNone (shopSlotUnderPlayer away) "standing in a corner senses no slot"
              Expect.isEmpty (readyElements away) "and draws no prompt"

              for at in positions do
                  let on = { model with PlayerPosition = at }
                  Expect.isSome (shopSlotUnderPlayer on) $"standing on {at} senses a slot"
                  Expect.hasLength (readyElements on) 1 $"and draws exactly one prompt at {at}"

                  // REACH, both ways, because "the sensor fires when the player is dead centre" is a
                  // claim a radius of half a unit also satisfies — and the walk always lands dead
                  // centre, so nothing else here would notice. The plinth is drawn 52 units wide, so
                  // a player standing at its drawn edge must still be able to buy.
                  let plinthEdge = { model with PlayerPosition = vec2 (at.Vx + 26.0) at.Vy }
                  Expect.isSome (shopSlotUnderPlayer plinthEdge) $"standing at the drawn edge of {at} still senses it"
                  let beyond = { model with PlayerPosition = vec2 (at.Vx + shopSlotRadius + playerRadius + 4.0) at.Vy }
                  Expect.isNone (shopSlotUnderPlayer beyond) $"standing past the reach of {at} does not"

              // An EMPTIED slot is a bare plinth: no prompt, and no message raised.
              let soldOut = { model with M5ShopSlots = model.M5ShopSlots |> List.map (fun slot -> { slot with Offer = Rogue3.Entities.ShopOffer.Empty }) }
              let onEmpty = { soldOut with PlayerPosition = List.head positions }
              Expect.isNone (shopSlotUnderPlayer onEmpty) "an emptied slot is not something to interact with"
              Expect.isEmpty (readyElements onEmpty) "and shows no prompt"
              Expect.equal (pressInteract onEmpty).PlayerCurrency onEmpty.PlayerCurrency "and pressing there charges nothing"
          }

          test "with two slots in reach at once, the press buys the NEARER one" {
              // The tie between two slots, which the authored candidate row (140 units apart, against
              // a 33-unit reach) can never produce but the fallback lattice can: it steps by
              // `obstacleClearance`, which is narrower than two reaches. Without the nearest-first
              // rule this resolves to whichever slot happens to come first in `M5ShopSlots`, which is
              // a purchase the player did not aim at.
              let model = crowdedShop 50
              let nearId, farId, between = straddledPair model
              let positions = List.zip (model.M5ShopSlots |> List.map _.Id) (shopSlotPositions model) |> Map.ofList
              let reach = shopSlotRadius + playerRadius
              let standing = { model with PlayerPosition = between }

              Expect.isTrue (magnitude (sub between positions.[nearId]) < magnitude (sub between positions.[farId])) "the stand point really is nearer the second-listed slot"
              Expect.isTrue (magnitude (sub between positions.[farId]) <= reach) "and the first-listed slot is genuinely in reach too"
              Expect.isTrue (magnitude (sub between positions.[nearId]) <= reach) "as is the nearer one"
              Expect.equal (shopSlotUnderPlayer standing |> Option.map (fst >> _.Id)) (Some nearId) "the sensor answers with the nearer slot, not the first-listed one"

              // And the same through the production route: exactly one slot is emptied, that one.
              let bought = pressInteract standing
              let emptiedIds =
                  bought.M5ShopSlots
                  |> List.filter (fun slot -> slot.Offer = Rogue3.Entities.ShopOffer.Empty)
                  |> List.map _.Id
              Expect.equal emptiedIds [ nearId ] "and the press empties that slot and no other"
              Expect.isTrue (nearId > farId) "the nearer slot really is the LATER one in slot order"
          }

          test "the rebindable interact COMMAND buys once per press, not once per fixed step" {
              // `interactPressed`'s `isFirstStep` argument exists for this arm and only this arm: the
              // raw-key arm is self-gating because `advanceSim` passes an empty pressed-set after the
              // first step, but the `Commands` comparison stays true for every step of the frame.
              // Drop `isFirstStep` and one press of a rebound interact button buys on all of them.
              // Nothing else in this file exercises the command arm, so nothing else notices.
              let model = crowdedShop 99
              // Stand where TWO slots are in reach, so a per-step repeat has somewhere to go. With
              // one slot underfoot a repeat is invisible: the first purchase empties it.
              let _, _, between = straddledPair model
              let commanding =
                  { model with
                      PlayerPosition = between
                      Input = { model.Input with Current = { model.Input.Current with Commands = Set.singleton "active" } } }
              // One host frame worth four fixed steps.
              let pressed = update (Tick(fixedDt * 4.0)) commanding |> fst
              let emptied = pressed.M5ShopSlots |> List.filter (fun slot -> slot.Offer = Rogue3.Entities.ShopOffer.Empty) |> List.length
              Expect.equal emptied 1 "a four-step frame under a held command buys exactly one slot"
              Expect.equal pressed.PlayerCurrency.Coins 98 "and charges exactly one price"
          }

          test "no AUTHORED slot placement can contest the trapdoor" {
              // The first half of the disambiguation, and the reassuring half: `placementAccepts`
              // rejects any candidate inside `trapdoorContains`, and every authored candidate row
              // sits far enough below the hatch that the interact reach cannot span the gap. Measured
              // rather than asserted from the rule, because the rule is about the CENTRE and the
              // sensor is about a radius.
              let reach = shopSlotRadius + playerRadius
              let nearestHatchPoint (at: Vec2) =
                  let x = max (trapdoorCenter.Vx - trapdoorHalfWidth) (min at.Vx (trapdoorCenter.Vx + trapdoorHalfWidth))
                  let y = max (trapdoorCenter.Vy - trapdoorHalfHeight) (min at.Vy (trapdoorCenter.Vy + trapdoorHalfHeight))
                  magnitude (sub at (vec2 x y))
              // Twelve is the whole authored candidate list; asking for more falls back to the
              // lattice, which is the pathological case the next test covers.
              for at in placeRoomFixtures [] 12 do
                  Expect.isGreaterThan (nearestHatchPoint at) reach $"an authored placement at {at} is out of interact reach of the hatch"
          }

          test "when a slot DOES contest the trapdoor, the press buys rather than descends" {
              // The pathological case, and it is genuinely reachable: `placeRoomFixtures`'s fallback
              // lattice does NOT run `placementAccepts`, so once the authored candidates are exhausted
              // it will place a fixture straight onto the hatch. Index 168 of that lattice is
              // (617, 341), inside `trapdoorContains`. A room stocked that heavily is not something
              // `Entities.generateShop` produces — it makes three slots — but the branch has to hold
              // for it, because resolving the tie toward the trapdoor would make the slot unbuyable
              // and cost the player the floor as well.
              let roomId = shopFloor.Floor.CurrentRoom
              let contested =
                  { crowdedShop 50 with
                      M5Room = { (inShop 50).M5Room with Trapdoor = true }
                      Floor =
                        { shopFloor.Floor with
                            CurrentRoom = roomId
                            Rooms =
                              shopFloor.Floor.Rooms
                              |> Map.add roomId { shopFloor.Floor.Rooms.[roomId] with Fixtures = shopFloor.Floor.Rooms.[roomId].Fixtures @ [ Trapdoor ] } } }

              let onHatch =
                  shopSlotPositions contested
                  |> List.tryFind trapdoorContains
                  |> Option.defaultWith (fun () -> failtest "the fallback lattice really does place a slot on the hatch")
              let both = { contested with PlayerPosition = onHatch }

              Expect.isTrue (canDescend both) "the press satisfies the descent guard"
              Expect.isSome (shopSlotUnderPlayer both) "and the shop sensor at the same time"

              let pressed = pressInteract both
              Expect.equal pressed.FloorIndex both.FloorIndex "the contested press does NOT descend"
              Expect.equal pressed.PlayerCurrency.Keys (both.PlayerCurrency.Keys + 1) "it buys the key it was standing on"
              Expect.equal pressed.PlayerCurrency.Coins (both.PlayerCurrency.Coins - 1) "and pays for it"

              // The control: take the stock away and the SAME press descends, so the guard is what
              // decided, not the absence of a trapdoor.
              let hatchOnly = { both with M5ShopSlots = [] }
              Expect.equal (pressInteract hatchOnly).FloorIndex (both.FloorIndex + 1) "with no stock underfoot the same press descends"
          }

          test "a slot the player cannot AFFORD does not swallow the descent" {
              // The other half of the tie-break, and the half the first draft got wrong. The sensor
              // deliberately answers for an unaffordable slot — the `NEED 13c` prompt is drawn from
              // it — so gating the descent on "is a shop intent present" handed the press to a
              // purchase that then refused it and changed nothing. On a hatch beside stock they could
              // not afford, the player pressed interact forever and neither bought nor descended.
              //
              // Reached here the same way the test above reaches its contested state, through the
              // fallback lattice of EHotwagner/rogue3#69; the difference is the purse.
              let roomId = shopFloor.Floor.CurrentRoom
              let contested coins =
                  { crowdedShop coins with
                      M5Room = { (inShop coins).M5Room with Trapdoor = true }
                      Floor =
                        { shopFloor.Floor with
                            CurrentRoom = roomId
                            Rooms =
                              shopFloor.Floor.Rooms
                              |> Map.add roomId { shopFloor.Floor.Rooms.[roomId] with Fixtures = shopFloor.Floor.Rooms.[roomId].Fixtures @ [ Trapdoor ] } } }

              // `crowdedShop` stocks every slot at one coin, so a purse of zero refuses all of them.
              let broke = contested 0
              let onHatch =
                  shopSlotPositions broke
                  |> List.tryFind trapdoorContains
                  |> Option.defaultWith (fun () -> failtest "the fallback lattice really does place a slot on the hatch")
              let stuck = { broke with PlayerPosition = onHatch }

              // The precondition: both predicates are live, and the purchase is the one that cannot
              // complete. If either of these stops holding the test below is proving nothing.
              Expect.isTrue (canDescend stuck) "the press satisfies the descent guard"
              let sensed =
                  shopSlotUnderPlayer stuck
                  |> Option.defaultWith (fun () -> failtest "the shop sensor answers at the same point")
              Expect.isFalse (shopSlotAffordable stuck (fst sensed)) "and the slot it senses is one the player cannot buy"

              let pressed = pressInteract stuck
              Expect.equal pressed.FloorIndex (stuck.FloorIndex + 1) "the refused press falls through to the descent"
              Expect.equal pressed.PlayerCurrency.Coins stuck.PlayerCurrency.Coins "and buys nothing on the way"

              // The control, and the guarantee the fix must not break: make the SAME slot affordable
              // and the purchase takes the press back, exactly as the test above requires.
              let rich = { contested 50 with PlayerPosition = onHatch }
              let bought = pressInteract rich
              Expect.equal bought.FloorIndex rich.FloorIndex "an affordable slot still wins the tie"
              Expect.equal bought.PlayerCurrency.Coins (rich.PlayerCurrency.Coins - 1) "and is what the press paid for"
          }

          test "the purchase a player triggers is AUDIBLE, which a message-keyed cue was not" {
              // The `floor-descend` lesson, one fixture over. A purchase now resolves inside a fixed
              // step, so `AudioCues.forTransition` is called with `Tick` and never sees
              // `InteractM5Shop`. A cue keyed to the message would be audible only to a test.
              let acquisitionCues =
                  [ AudioCueIds.itemPickup; AudioCueIds.pickupCoin; AudioCueIds.pickupKey; AudioCueIds.pickupBomb; AudioCueIds.pickupHeart ]
              let model = inShop 50
              let itemSlot, itemAt =
                  List.zip model.M5ShopSlots (shopSlotPositions model)
                  |> List.find (fun (slot, _) -> match slot.Offer with Rogue3.Entities.ShopOffer.Item _ -> not slot.KeyLocked | _ -> false)
              let standing, _ = standAtSlot itemSlot.Id itemAt model

              let pressed = setKey 'E' true standing
              let after = tick pressed
              Expect.notEqual after.M5ShopSlots standing.M5ShopSlots "the tick really did complete a purchase"

              let requested =
                  AudioCues.forTransition (Tick fixedDt) pressed after
                  |> Audio.interpret
                  |> _.Requested
                  |> List.choose (function PlaySfx(SoundId id, _) -> Some id | _ -> None)
              Expect.isNonEmpty requested "the transition a player produces requests a sound"

              // And exactly one acquisition cue, not two: `AudioEvent.ItemGranted` and the offer diff
              // both describe the same purchase.
              let acquisition = requested |> List.filter (fun id -> List.contains id acquisitionCues)
              Expect.equal acquisition [ AudioCueIds.itemPickup ] "one bought item requests exactly one item-pickup cue"

              // A bought CONSUMABLE is the half that has no `AudioEvent` behind it, so it is audible
              // only because the offer diff is read on `Tick`.
              let consumableSlot, consumableAt =
                  List.zip model.M5ShopSlots (shopSlotPositions model)
                  |> List.find (fun (slot, _) -> match slot.Offer with Rogue3.Entities.ShopOffer.Consumable _ -> true | _ -> false)
              let atConsumable, _ = standAtSlot consumableSlot.Id consumableAt model
              let consumablePressed = setKey 'E' true atConsumable
              let consumableAfter = tick consumablePressed
              Expect.notEqual consumableAfter.M5ShopSlots atConsumable.M5ShopSlots "the consumable was bought"
              let consumableRequested =
                  AudioCues.forTransition (Tick fixedDt) consumablePressed consumableAfter
                  |> Audio.interpret
                  |> _.Requested
                  |> List.choose (function PlaySfx(SoundId id, _) -> Some id | _ -> None)
                  |> List.filter (fun id -> List.contains id acquisitionCues)
              Expect.hasLength consumableRequested 1 "a bought consumable requests exactly one acquisition cue"

              // A REFUSED purchase requests none of them, so the two are distinguishable at the sink.
              let poor = inShop 0
              let poorSlot, poorAt =
                  List.zip poor.M5ShopSlots (shopSlotPositions poor)
                  |> List.find (fun (slot, _) -> not slot.KeyLocked && slot.Offer <> Rogue3.Entities.ShopOffer.Empty)
              let poorStanding, _ = standAtSlot poorSlot.Id poorAt poor
              let poorPressed = setKey 'E' true poorStanding
              let poorAfter = tick poorPressed
              Expect.equal poorAfter.M5ShopSlots poorStanding.M5ShopSlots "the refused press really did buy nothing"
              let poorRequested =
                  AudioCues.forTransition (Tick fixedDt) poorPressed poorAfter
                  |> Audio.interpret
                  |> _.Requested
                  |> List.choose (function PlaySfx(SoundId id, _) -> Some id | _ -> None)
                  |> List.filter (fun id -> List.contains id acquisitionCues)
              Expect.isEmpty poorRequested "a refused purchase requests no acquisition cue"

              // ROOM CHANGE, the reason the cue is not a bare offer diff. Shop slot ids are 0..2 in
              // every shop `Entities.generateShop` builds, so a transition that swaps one room's stock
              // for another's looks — id by id — exactly like a purchase. The cue must stay silent.
              let elsewhere =
                  { model with
                      Floor = { model.Floor with CurrentRoom = model.Floor.CurrentRoom + 1 }
                      M5ShopSlots = model.M5ShopSlots |> List.map (fun slot -> { slot with Offer = Rogue3.Entities.ShopOffer.Empty }) }
              let crossingRequested =
                  AudioCues.forTransition (Tick fixedDt) model elsewhere
                  |> Audio.interpret
                  |> _.Requested
                  |> List.choose (function PlaySfx(SoundId id, _) -> Some id | _ -> None)
                  |> List.filter (fun id -> List.contains id acquisitionCues)
              Expect.isEmpty crossingRequested "a room change that replaces the stock cues no purchase"

              // Same for a DESCENT, which changes the floor as well as the room.
              let descended = { elsewhere with FloorIndex = model.FloorIndex + 1; Floor = model.Floor }
              let descendRequested =
                  AudioCues.forTransition (Tick fixedDt) model descended
                  |> Audio.interpret
                  |> _.Requested
                  |> List.choose (function PlaySfx(SoundId id, _) -> Some id | _ -> None)
                  |> List.filter (fun id -> List.contains id acquisitionCues)
              Expect.isEmpty descendRequested "and neither does a descent"
          }

          // --------------------------------------------------------------------------------------
          // Route 2 — the treasure pedestal.
          // --------------------------------------------------------------------------------------

          test "reward fixtures survive room load on the floors a real run actually reaches" {
              // The regression guard for `loadM5Room`'s `if isBoss then reward else None`. That line
              // threw away the pedestal for EVERY treasure room on every floor, after the generator
              // had already consumed the item from the shared pool.
              //
              // The pool is CARRIED ACROSS FLOORS in production — `DescendFloor` feeds
              // `model.M5ItemPool` into `FloorGeneration.generateWithPool` — and it holds six items
              // (`Entities.baseItems`). A sweep that calls `FloorGeneration.generate` per floor hands
              // every floor a FRESH full pool and so reports item rewards a real run never sees. A
              // fresh-context critic measured the difference on the boot seed: per-floor generation
              // yields 12 item rewards and 0 fallbacks, the chained pool yields 4 and 8.
              let mutable itemRewards = 0
              let mutable discardedFallbacks = 0
              // `boot.RunSeed` IS 0xC0FFEE, so listing it again would have been the same run twice.
              for seed in [ boot.RunSeed; 0xA55AUL; 0xBEEF1234UL ] do
                  let mutable pool = Rogue3.Entities.itemPool []
                  for floorIndex in 1 .. 6 do
                      let generated = FloorGeneration.generateWithPool seed floorIndex pool
                      pool <- generated.ItemPool
                      let model =
                          { boot with
                              RunSeed = seed
                              FloorIndex = floorIndex
                              Floor = generated.Floor
                              M5ItemPool = generated.ItemPool }
                      for room in generated.Floor.Rooms |> Map.values do
                          match room.Fixtures |> List.tryPick (function ItemPedestal item | BossReward item -> Some item | _ -> None) with
                          | Some awarded ->
                              itemRewards <- itemRewards + 1
                              Expect.equal
                                  ((loadM5Room room.Id model).M5Room.Reward |> Option.map _.Id)
                                  (Some awarded.Id)
                                  $"seed {seed} floor {floorIndex} room {room.Id}: the item fixture reaches the live room"
                          | None ->
                              if room.Fixtures |> List.exists (function ConsumableReward _ -> true | _ -> false) then
                                  discardedFallbacks <- discardedFallbacks + 1
                                  Expect.isNone
                                      (loadM5Room room.Id model).M5Room.Reward
                                      $"seed {seed} floor {floorIndex} room {room.Id}: the consumable fallback is still discarded (EHotwagner/rogue3#51)"

              Expect.isGreaterThan itemRewards 0 "the chained sweep still finds item rewards to check"
              // Pinning this is the point: once the six-item pool drains, the generator emits
              // `ConsumableReward`, which `loadM5Room` still swallows through its `|_->None` arm. That
              // is EHotwagner/rogue3#51 — out of #47's scope because presenting it needs `Render.fs`.
              // Stating it here means the day #51 lands this test goes red and says so, rather than
              // the sweep quietly starting to cover a case it never covered.
              Expect.isGreaterThan
                  discardedFallbacks 0
                  "a real run drains the pool, and those rooms still present nothing (EHotwagner/rogue3#51)"
          }

          test "the collection point is the point the RENDERER draws, in both axes" {
              // Collection and rendering must not drift: a reward collected somewhere other than
              // where it is drawn is a reward a player cannot find. The first draft compared
              // `roomRewardPosition` against a restatement of its own body, which proves nothing —
              // so this goes through `Render.renderedElements`, the way M13 pins the shop row.
              let slots, _, _ = Rogue3.Entities.generateShop (FS.GG.Game.Core.Rng.ofSeed 0xA55AUL) (Rogue3.Entities.itemPool [])
              let model = { boot with M5ShopSlots = slots; M5Room = { boot.M5Room with Reward = Some(itemNamed "void-map") } }
              let at = roomRewardPosition model |> Option.defaultWith (fun () -> failtest "the reward has a collection point")

              match Render.renderedElements model |> List.filter (fun element -> element.ElementId = "RoomReward") with
              | [ element ] ->
                  let bounds = boundsOf element.Scene
                  let centreX = ((bounds |> List.map _.X |> List.min) + (bounds |> List.map (fun b -> b.X + b.Width) |> List.max)) / 2.0
                  let bottomY = bounds |> List.map (fun b -> b.Y + b.Height) |> List.max
                  Expect.isTrue (abs (centreX - at.Vx) < 6.0) $"the plinth is drawn at the collection x {at.Vx} (drawn at {centreX})"
                  // The plinth is drawn standing ON its position, so the collection point is at its
                  // foot rather than its centre. Pinning the y at all is what the first draft lacked.
                  Expect.isTrue (abs (bottomY - at.Vy) < 24.0) $"and at the collection y {at.Vy} (drawn foot at {bottomY})"
              | other -> failtest $"expected exactly one reward element, got {other.Length}"
          }

          test "a player walks onto the pedestal and takes the item" {
              // The whole point of the board item, driven end to end through the production input
              // route. Against the tree this was written on the pedestal was not even present.
              let treasure = roomOfType Treasure boot
              let loaded = loadM5Room treasure { boot with Floor = { boot.Floor with CurrentRoom = treasure } }
              let pedestal = loaded.M5Room.Reward |> Option.defaultWith (fun () -> failtest "the treasure room presents a reward")
              let at = roomRewardPosition loaded |> Option.defaultWith (fun () -> failtest "the reward has a position")

              let taken, steps = walkUntil at (fun model -> not model.PlayerItems.IsEmpty) 600 loaded
              Expect.isGreaterThan steps 1 "the player actually had to WALK there, rather than starting on it"
              Expect.equal (taken.PlayerItems |> List.map _.Id) [ pedestal.Id ] "walking onto the plinth grants the pedestal item"
              Expect.equal taken.PlayerItems.Head.Modifiers expectedModifiers.[pedestal.Id] "with its authored modifiers"
              Expect.equal taken.PlayerStats (statsOf pedestal.Id) "and the stat line those modifiers produce"
              Expect.notEqual taken.PlayerStats basePlayerStats "which is not the base line any more"
              Expect.equal taken.RunStats.ItemsFound 1 "counted exactly once"
              Expect.isNone taken.M5Room.Reward "the plinth is empty afterwards"

              // Standing on the spot must not re-grant: the reward is removed in the step it is
              // applied, the same rule `collectFloorPickups` follows for a coin.
              let lingered = walkTo at (fun _ -> false) 120 taken
              Expect.equal lingered.PlayerItems.Length 1 "lingering on the empty plinth grants nothing further"
              Expect.equal lingered.RunStats.ItemsFound 1 "and counts nothing further"
          }

          test "standing away from the plinth takes nothing" {
              // The negative half, and the reason it exists: with no distance assertion anywhere,
              // `roomRewardRadius` survived being widened from 20 to 400 units — a grab from most of
              // a 1280-wide room — because every other test only ever walks TOWARD the plinth.
              let treasure = roomOfType Treasure boot
              let loaded = loadM5Room treasure { boot with Floor = { boot.Floor with CurrentRoom = treasure } }
              let at = roomRewardPosition loaded |> Option.defaultWith (fun () -> failtest "the reward has a position")

              // Park well outside the collection circle but comfortably inside the room.
              let standoff = roomRewardRadius + playerRadius + 60.0
              let spot = vec2 (if at.Vx > playfieldWidth / 2.0 then at.Vx - standoff else at.Vx + standoff) at.Vy
              let parked = walkTo spot (fun _ -> false) 240 loaded

              Expect.isGreaterThan
                  (magnitude (sub parked.PlayerPosition at))
                  (roomRewardRadius + playerRadius)
                  "the player really is outside the collection circle"
              Expect.isEmpty parked.PlayerItems "and standing there takes nothing"
              Expect.equal parked.RunStats.ItemsFound 0 "and counts nothing"
              Expect.isSome parked.M5Room.Reward "the reward is still on its plinth"
          }

          test "a taken reward does not come back when the room is re-entered" {
              // Durable FLOOR state, the `recordDestroyedObstacle` rule (§14.15). Without the fixture
              // removal the item would be re-granted on every visit — an infinite stat engine.
              let treasure = roomOfType Treasure boot
              let loaded = loadM5Room treasure { boot with Floor = { boot.Floor with CurrentRoom = treasure } }
              let at = roomRewardPosition loaded |> Option.defaultWith (fun () -> failtest "the reward has a position")
              let taken = walkTo at (fun model -> not model.PlayerItems.IsEmpty) 600 loaded

              Expect.isFalse
                  (taken.Floor.Rooms.[treasure].Fixtures |> List.exists (function ItemPedestal _ | BossReward _ -> true | _ -> false))
                  "the floor record no longer carries the reward fixture"

              let revisited = loadM5Room treasure taken
              Expect.isNone revisited.M5Room.Reward "re-entering presents no reward"
              let relingered = walkTo at (fun _ -> false) 240 revisited
              Expect.equal relingered.PlayerItems.Length 1 "and standing on the old spot grants nothing"
              Expect.equal relingered.RunStats.ItemsFound 1 "and counts nothing"
          }

          test "an uncleared NON-boss room still yields its pedestal on sight" {
              // `roomRewardCollectable` is `Reward.IsSome && (not IsBoss || Cleared)`. Dropping the
              // `not IsBoss` disjunct survived mutation, because `FloorGeneration` births Treasure and
              // Shop rooms already `Cleared = true` — so the docstring's "a pedestal may be taken on
              // sight" was true by accident, not by test. Force the uncleared case explicitly.
              let treasure = roomOfType Treasure boot
              let loaded = loadM5Room treasure { boot with Floor = { boot.Floor with CurrentRoom = treasure } }
              let uncleared = { loaded with M5Room = { loaded.M5Room with IsBoss = false; Cleared = false } }
              Expect.isTrue (roomRewardCollectable uncleared) "an uncleared ordinary room does not guard its pedestal"

              let at = roomRewardPosition uncleared |> Option.defaultWith (fun () -> failtest "the reward has a position")
              let taken = walkTo at (fun model -> not model.PlayerItems.IsEmpty) 600 uncleared
              Expect.equal taken.PlayerItems.Length 1 "and it can be walked onto and taken"
          }

          // --------------------------------------------------------------------------------------
          // Route 3 — the boss reward.
          // --------------------------------------------------------------------------------------

          test "the boss reward cannot be taken until the boss is down, and can be taken after" {
              let bossRoom = roomOfType Boss boot
              let loaded = loadM5Room bossRoom { boot with Floor = { boot.Floor with CurrentRoom = bossRoom } }
              Expect.isSome loaded.M5Room.Reward "the boss room presents its reward"
              Expect.isFalse (roomRewardCollectable loaded) "but a live boss guards it"

              let at = roomRewardPosition loaded |> Option.defaultWith (fun () -> failtest "the reward has a position")
              let guarded = walkTo at (fun model -> not model.PlayerItems.IsEmpty) 600 loaded
              Expect.isEmpty guarded.PlayerItems "walking onto a guarded plinth takes nothing"
              Expect.equal guarded.RunStats.ItemsFound 0 "and counts nothing"

              // Clear the room the way the product does — `damageM5Boss` runs BOTH halves, the live
              // room's `bossCleared` and the floor record's `clearBoss`, and it is the second that
              // lays down the trapdoor fixture asserted at the end of this test.
              let cleared =
                  { guarded with
                      M5Room = Rogue3.Entities.bossCleared guarded.M5Room.Reward guarded.M5Room
                      Floor = FloorGeneration.clearBoss bossRoom guarded.Floor
                      M5Boss = None }
              Expect.isTrue (roomRewardCollectable cleared) "a cleared boss room releases its reward"
              let reward = cleared.M5Room.Reward |> Option.defaultWith (fun () -> failtest "the reward survives the clear")
              let takenAt = roomRewardPosition cleared |> Option.defaultWith (fun () -> failtest "the reward has a position")
              let taken = walkTo takenAt (fun model -> not model.PlayerItems.IsEmpty) 600 cleared

              Expect.equal (taken.PlayerItems |> List.map _.Id) [ reward.Id ] "the boss reward is granted"
              Expect.equal taken.PlayerItems.Head.Modifiers expectedModifiers.[reward.Id] "with its authored modifiers"
              Expect.equal taken.PlayerStats (statsOf reward.Id) "and the stat line those modifiers produce"
              Expect.equal taken.RunStats.ItemsFound taken.PlayerItems.Length "the counter and the inventory agree"

              // The BOSS half of the durability rule. Only the treasure route re-entered in the first
              // draft, so `withoutRewardFixture` filtering `ItemPedestal` ALONE survived mutation:
              // the boss reward was re-presented and re-granted on every re-entry, unbounded.
              Expect.isFalse
                  (taken.Floor.Rooms.[bossRoom].Fixtures |> List.exists (function ItemPedestal _ | BossReward _ -> true | _ -> false))
                  "the floor record no longer carries the boss reward fixture"
              let revisited = loadM5Room bossRoom taken
              Expect.isNone revisited.M5Room.Reward "re-entering the boss room presents no reward"
              let relingered = walkTo takenAt (fun _ -> false) 240 revisited
              Expect.equal relingered.PlayerItems.Length 1 "and standing on the old spot grants nothing further"

              // Taking the prize must not strip the way OUT. `withoutRewardFixture` filters
              // `Floor.Rooms[..].Fixtures`, and `trapdoorPresent` reads that same list — a PR
              // reviewer showed that widening the filter to include `Trapdoor` passes the whole
              // suite green while softlocking the run: the player takes the boss reward and can
              // never descend again. The code was already right; nothing asserted it.
              Expect.isTrue
                  (taken.Floor.Rooms.[bossRoom].Fixtures |> List.contains Trapdoor)
                  "collecting the boss reward leaves the trapdoor fixture on the floor record"
              Expect.isTrue (trapdoorPresent taken) "so the room still depicts a trapdoor"
          }

          test "a descent does not carry the departed floor's reward onto the new one" {
              // `DescendFloor` clears every other room-local carry-over; the reward is now COLLECTABLE,
              // so a stale one could grant the previous floor's item at the new floor's plinth.
              // Production always pairs `DescendFloor` with `EnterM5Room 0`, but `PerformanceEvidence`
              // dispatches it alone, so this must not rest on the pairing.
              let bossRoom = roomOfType Boss boot
              let staged =
                  { boot with Floor = { boot.Floor with CurrentRoom = bossRoom } }
                  |> loadM5Room bossRoom
              // A descent is guarded by the state it DEPICTS (§M11): the floor record must carry the
              // trapdoor fixture, the loaded room must agree, and the player must be standing on it.
              let staged =
                  { staged with
                      M5Room = Rogue3.Entities.bossCleared staged.M5Room.Reward staged.M5Room
                      M5Boss = None
                      Floor = FloorGeneration.clearBoss bossRoom staged.Floor
                      PlayerPosition = trapdoorCenter }
              Expect.isSome staged.M5Room.Reward "the staged boss room is holding a reward"
              Expect.isTrue (canDescend staged) "and the staged room really does permit a descent"

              let descended = update DescendFloor staged |> fst
              Expect.isGreaterThan descended.FloorIndex staged.FloorIndex "the descent happened"
              Expect.isNone descended.M5Room.Reward "and it did not carry the departed floor's reward with it"
          }
        ]
