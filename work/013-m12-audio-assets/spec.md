---
schemaVersion: 1
workId: 013-m12-audio-assets
title: M12 Audio Assets
stage: specify
changeTier: tier1
status: specified
publicOrToolFacingImpact: true
---

# M12 Audio Assets Specification

Prose status: specified

## User Value
A player hears Hollow Depths - every sound and music cue the game already requests plays instead of resolving to silence. At `059e993` the game requests its cues correctly and `assets/audio/` does not exist in the tree at all, so the entire cue set is silent and the shipped build says so on stderr before the first frame.

## Scope
- SB-001: Ship a real asset for every cue id AudioCues.fs requests, derived from one declaration rather than a hand-written list, plus a gate that fails when a requested id has no playable asset; no new cues, no mixing, no licensed content.
- SB-002: One declaration of the cue id set, consumed by the cue map, the asset generator and the guard test alike, is in scope as a consequence of SB-001: a second hand-maintained list is the same defect one layer down.
- SB-003: Deterministic programmatic synthesis of the WAV assets from a committed script is in scope, and is the shipping route for v1. Nothing is downloaded and no third-party licence enters the tree.
- SB-004: Making the running product find its own assets is in scope: today the resolver reads a path relative to the process working directory only, so the product is silent whenever it is launched from anywhere but one directory.

## Non-Goals
- SB-005: Mixing, buses, fades, ducking, 3D positional audio and per-bus volume UI are out. This work makes the existing requests audible; it does not extend the audio surface.
- SB-006: New cues are out. The cue map at `059e993` defines the obligation; adding a cue is a separate change.
- SB-007: Replacing the synthesized assets with authored or licensed audio is out. That is a content decision, not an availability defect.
- SB-008: M13 and M14 roadmap rows are out.
- SB-009: No regression of M8 requested-cue expectations, M10 determinism and replay, or the M11 playability route. Audio assets add no simulation state, so no determinism, workload or UI-route digest may move for this reason.

## User Stories
- US-001 (P1): As a player, when I boot Hollow Depths I hear the title theme, and when I fire, take a hit, kill something, open a door or descend a floor I hear that happen, instead of playing in silence.
- US-002 (P1): As a player, the music follows where I am - the title screen, each floor, the shop, a boss room, and the game-over and victory endings each have their own loop, and every one of them is audible.
- US-003 (P1): As a maintainer, if I add a cue to `AudioCues.fs` and forget its asset, the suite goes red naming the id, instead of shipping green with a silent cue.
- US-004 (P2): As a reviewer, I can read how each committed asset was produced and re-derive it byte for byte from the repository, without trusting an opaque binary.
- US-005 (P2): As a player, the game is audible wherever it is launched from, not only when the process working directory happens to be the one directory the assets sit beside.

## Acceptance Scenarios
- AC-001 [US-003] [FR-001]: Given the product's cue id declaration, when the guard test enumerates the ids it must cover, then the enumeration is read from that declaration - the same values `AudioCues.fs` builds its `SoundId`/`TrackId` values from - and no cue id list is written into the test. (The test may name an individual id to pin an authored spelling, for example that `floorTheme 1` is still `floor-1-theme`; what it may not do is enumerate the obligation by hand.)
- AC-002 [US-001] [FR-002]: Given the declared cue id set, when each id is resolved through `Rogue3.AudioCues.resolver` - the exact value `Program.fs` passes to `OpenAlBackend.create` - then every one returns `Some` bytes, and none returns `None`.
- AC-003 [US-001] [FR-003]: Given the bytes each id resolves to, when they are parsed by the shipped `FS.GG.Audio.Host.Wav.tryParse`, then every one parses, reports `FormatTag` PCM, exactly one channel and 16 bits per sample, and carries a non-empty data chunk - so an empty or non-PCM placeholder file cannot satisfy the gate.
- AC-004 [US-003] [FR-004]: Given the guard test passing, when any single asset file is removed or truncated, then the guard fails and its message names the missing id - demonstrated by the test asserting resolution of a deliberately unknown id fails by the same predicate the declared ids pass.
- AC-005 [US-003] [FR-005]: Given every `.fs` file under `src/Rogue3/` with its comments stripped, when they are scanned for `SoundId`/`TrackId`/`sfx` applied to a string, then no plain literal is found at all, no interpolated string is found, and no `sprintf`/`String.Format` construction is found - so a cue id written into product source, whether or not it happens to be declared, and whether or not it is assembled at run time, reds the suite.
- AC-006 [US-002] [FR-006]: Given the parameterized floor-theme family `floor-<n>-theme`, when it is evaluated at every integer a run can reach and beyond it, then every result is a member of the declared track set; and given the shipped public cue map `AudioCues.replaceWithCurrentMusic`, when it is driven at every one of those indices, then every track it requests is a member of the declared track set too, so no floor index can request an undeclared track by either route.
- AC-007 [US-004] [FR-007]: Given the committed generator script, when it is run, then it writes exactly one `.wav` per declared id, and re-running it reproduces every file byte for byte; and the guard asserts both directions of "and no others" against the committed directory - every declared id has a file, and no file in `assets/audio/` is undeclared.
- AC-008 [US-005] [FR-008]: Given the built product started from a working directory that is not the assets' parent, when the resolver is asked for a declared id, then it still returns the asset bytes, because resolution also probes the directory the product's own assembly runs from.
- AC-009 [US-001] [FR-009]: Given the shipped build launched as a player launches it, when the window opens and the title screen requests its music, then no `FS.GG.Audio.Host: ... did not resolve to an asset` line is printed, and the audio backend reports itself device-backed rather than substituted.
- AC-010 [US-001] [FR-010]: Given the M8 requested-cue expectations, the M10 determinism and replay obligations and the M11 playability route, when the full suite runs after this change, then all of them pass unchanged and no authored digest moves.

## Functional Requirements
- FR-001: The product MUST declare its cue id set once, in compiled product source, and `AudioCues.fs` MUST build every `SoundId`/`TrackId` it requests from that declaration; the guard test MUST enumerate its obligation from the same declaration rather than from a list written into the test. (covers AC-001)
- FR-002: Every id in the declared set MUST resolve to `Some` bytes through `Rogue3.AudioCues.resolver`, the same resolver value the production launch hands to `OpenAlBackend.create`. (covers AC-002)
- FR-003: The bytes each declared id resolves to MUST parse through the shipped `FS.GG.Audio.Host.Wav.tryParse` as PCM, mono, 16 bits per sample, with a non-empty data chunk. (covers AC-003)
- FR-004: The guard MUST fail closed: an id that resolves to nothing MUST make the suite red and MUST be named in the failure, and the guard MUST demonstrate its own discriminating power against an id with no asset. (covers AC-004)
- FR-005: No cue id MAY be written into product source outside the declaration; a comment-stripped scan of every `src/Rogue3/*.fs` MUST find no cue id string literal at all, no interpolated cue id, and no `sprintf`-assembled cue id. (covers AC-005)
- FR-006: The parameterized `floor-<n>-theme` family MUST be total over every integer floor index - clamped into the declared range - so no reachable or unreachable floor index can request a track that is not declared; and the SHIPPED cue map, not only the declaration, MUST be driven across that range, so a cue map that stops consulting the declaration is caught. (covers AC-006)
- FR-007: Every committed asset MUST be produced by a committed, deterministic generator script that writes exactly one file per declared id and reproduces each file byte for byte on re-run. (covers AC-007)
- FR-008: Asset resolution MUST succeed for a product launched from any working directory, by probing the running assembly's own directory in addition to the working directory, with the assets copied to the build output. (covers AC-008)
- FR-009: The shipped build MUST be launched as a player launches it and MUST print no unresolved-asset diagnostic, and the audio backend it constructs MUST be reported as device-backed rather than a silent substitution. (covers AC-009)
- FR-010: The existing M8 audio, M10 determinism and replay, and M11 playability obligations MUST stay green, and no authored workload or UI-route digest may move for this work item. (covers AC-010)

## Ambiguities
- AMB-001: Where the single cue id declaration lives - inside `AudioCues.fs` itself, or in a separate dependency-free module that `AudioCues.fs` and an `fsi` generator script can both consume.
- AMB-002: How many floor themes the declared set contains, given `floor-<n>-theme` is parameterized by an unbounded integer.
- AMB-003: Whether generated binary WAVs are committed to a repository with no LFS, or generated at build time from the script.
- AMB-004: What "resolves to a real asset" means as a gate - a file existing, or bytes the shipped parser accepts as playable.
- AMB-005: What counts as proof of audibility at launch, given the host cannot record the machine's audio output.
- AMB-006: Whether the guard belongs in the existing `M8AudioTests.fs` or in its own test file.

## Public Or Tool-Facing Impact
- Adds a compiled source file to the product, changes `AudioCues.fs`, adds committed binary content plus a build item that copies it to the output directory, adds a committed generator script and an evidence command. No framework package API changes. No model, floor or render change, therefore no authored digest movement.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd clarify --work 013-m12-audio-assets`.
