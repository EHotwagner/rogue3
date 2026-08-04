// The DECLARED public surface of this module (EHotwagner/rogue3#96, constitution principle III:
// "Public Surface Is Declared, Not Incidental"). A binding absent from this file is private to
// the implementation and is NOT product API -- adding one here is a contracted (Tier 1) change.
module Rogue3.FloorGeneration

type RoomType =
    | Combat
    | Treasure
    | Shop
    | Boss
    | Secret
    | SuperSecret
    | Start

type DoorState =
    | Open
    | LockedKey
    | BossDoor
    | HiddenWall

type DoorDirection =
    | North
    | East
    | South
    | West

type EnemySpawnKind =
    | Grub
    | Maggot
    | Spitter
    | Fly
    | Charger
    | Turret
    | Caster
    | Brute

type Door =
    {
      ToRoom: int
      Direction: DoorDirection
      State: DoorState
    }

type EnemyAnchor =
    {
      Kind: EnemySpawnKind
      Cell: FS.GG.Game.Core.Cell
      Threat: int
    }

type Interior =
    {
      TemplateId: int
      Obstacles: FS.GG.Game.Core.Cell list
      EnemyAnchors: EnemyAnchor list
      ThreatBudget: int
      ThreatSpent: int
    }

type Fixture =
    | Trapdoor
    | ItemPedestal of Entities.ItemDefinition
    | BossReward of Entities.ItemDefinition
    | ShopStock of Entities.ShopSlot list
    | ConsumableReward of Entities.PickupKind

type FloorRoom =
    {
      Id: int
      Cell: FS.GG.Game.Core.Cell
      RoomType: RoomType
      Cleared: bool
      Visited: bool
      Hidden: bool
      Interior: Interior
      Doors: Door list
      Fixtures: Fixture list

      /// Obstacles destroyed while the player was in this room. Durable floor state so that
      /// re-entering a room (§14.15) does not resurrect what a shot or bomb already removed.
      DestroyedObstacles: Set<int>
    }
type Floor =
    {
      Index: int
      Seed: uint64
      Rooms: Map<int,FloorRoom>
      Graph: Map<int,int list>
      CurrentRoom: int
      MapRevealed: Set<int>
      PendingSecrets: Map<struct (int * int),int>
      RoomBudget: int
    }

type GenerationResult =
    {
      Floor: Floor
      LayoutRng: FS.GG.Game.Core.Rng
      ItemPool: Entities.ItemPool
    }

val generateWithPool:
  runSeed: uint64 ->
    floorIndex: int -> initialItemPool: Entities.ItemPool -> GenerationResult

val generate: runSeed: uint64 -> floorIndex: int -> GenerationResult

val revealSecret: adjacentRoom: int -> secretRoom: int -> floor: Floor -> Floor

/// Record that an obstacle was destroyed in `roomId`, so a later visit rebuilds the room without it.
val recordDestroyedObstacle:
  roomId: int -> obstacleId: int -> floor: Floor -> Floor

/// Record that `roomId` is cleared. Room-clear is durable floor state (§14.5, §14.15): a cleared
/// room does not repopulate, so its clear drop is never rolled a second time.
val recordRoomCleared: roomId: int -> floor: Floor -> Floor

/// Direction travelled from `fromRoom` toward `toRoom` on the room grid. The caller uses it to
/// place the shared wall segment (and the reciprocal doorway) in room-local coordinates.
val roomDirection:
  fromRoom: int -> toRoom: int -> floor: Floor -> DoorDirection option

/// The still-hidden secret rooms reachable by bombing a wall of `roomId`, in deterministic map
/// order. Used by the §14.14 blast resolution, which must not scan the whole floor per explosion.
val pendingSecretsFrom: roomId: int -> floor: Floor -> (int * int) list

/// Unlock one reciprocal key door as a single immutable floor transition (§14.16). A malformed or
/// already-open pair is rejected without producing a half-open graph, and the caller only spends a
/// key when the returned flag is true — so re-entering an unlocked door never charges again.
val tryUnlockDoor: fromRoom: int -> toRoom: int -> floor: Floor -> Floor * bool

/// Traverse an open (or always-enterable boss) door from the current room (§14.15). The returned
/// direction is the direction travelled, so the caller can land the player at the reciprocal
/// doorway. The departed room keeps its cleared state, fixtures and doors: only the destination is
/// touched, and only to mark it visited and revealed.
val tryTraverseDoor: toRoom: int -> floor: Floor -> Floor * DoorDirection option

val clearBoss: roomId: int -> floor: Floor -> Floor
