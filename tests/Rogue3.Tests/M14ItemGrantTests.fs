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
// SCOPE LIMIT, stated up front rather than buried: the PEDESTAL and BOSS routes below are driven
// from the production input route end to end. The SHOP route is not, and cannot be — no key press
// can produce `InteractM5Shop` (EHotwagner/rogue3#55), so the shop tests prove the reducer and the
// update path, NOT that a player can reach them. The consumable-fallback reward is likewise still
// discarded by `loadM5Room` (EHotwagner/rogue3#51) and is asserted here as still-broken.
//
// SECOND PASS. A fresh-context mutation critic ran 57 mutations against the first draft of this file
// and 26 survived — the fix itself could not be reverted silently, but the surface AROUND it was
// unpinned: four of six `itemModifiers` arms could be deleted outright, both tear-delay SIGN FLIPS
// survived, `roomRewardRadius` survived being widened to 400 (most of the room), and the boss
// reward could be made re-grantable on every re-entry. The assertions below are the answer to that
// run, so they lean on literal expected VALUES rather than on shape.

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
          // Route 1 — the shop. The call site named in the item's title.
          //
          // NOTE: these drive `InteractM5Shop` directly. That message has ZERO production dispatch
          // sites — no key press can produce it (EHotwagner/rogue3#55) — so unlike the pedestal and
          // boss routes below, these prove the reducer and NOT player reachability. Said plainly
          // here so the file does not imply a guarantee it has not earned.
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
