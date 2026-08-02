---
schemaVersion: 1
workId: 013-m12-audio-assets
title: M12 Audio Assets For Requested Cues
stage: charter
changeTier: tier1
status: chartered
policyPointers:
  - .fsgg/sdd.yml
  - .fsgg/agents.yml
  - .fsgg/policy.yml
  - .fsgg/capabilities.yml
  - .fsgg/tooling.yml
---

# M12 Audio Assets For Requested Cues Charter

## Identity
- Work id: `013-m12-audio-assets`
- Lifecycle stage: charter
- Status: chartered
- Make Hollow Depths audible. `src/Rogue3/AudioCues.fs` requests sound and music ids that resolve as `assets/audio/<id>.wav`, and `assets/audio/` does not exist in the tree at all — so every cue the product already asks for plays as silence. Launching the shipped build at `059e993` prints `FS.GG.Audio.Host: track 'title-theme' did not resolve to an asset — AssetResolver.ResolveTrack returned None, so every play of it is silent.` before the first frame. This work item ships the assets and the gate that notices when one goes missing.

## Principles
- The id set is **derived from source, never hand-listed**. A hand-written list of cue ids in a test or a generator is the defect one layer down: it drifts from `AudioCues.fs` and then passes green while the product is silent. One declaration is the source of truth for the cue map, the asset generator and the guard test.
- The guard resolves through the **real** resolver the product ships — `Rogue3.AudioCues.resolver`, the exact value `Program.fs` hands to `OpenAlBackend.create`. A test that stats the filesystem against a literal list would pass while the product stayed silent.
- Assets are **generated deterministically from committed code**, not downloaded. No network dependency, no third-party licence laundered into the tree, and every byte is reproducible and reviewable from a script a reviewer can read and re-run.
- Silence is a failure mode, not a default. Resolution is proven by the shipped `Wav` parser accepting the bytes as playable mono 16-bit PCM, not merely by `File.Exists`.
- The product is launched and what it prints is read. A milestone about audibility that never launched the product would repeat the failure that produced it.
- M8's requested-cue expectations, M10 determinism and the M11 playability route stay green. Audio assets add no simulation state, so no determinism digest may move for this reason.

## Scope Boundaries
- In: the two M12 roadmap rows — every sound and track id `AudioCues.fs` requests resolves to a real asset; a test fails when a requested cue id has no asset.
- In (consequential): a single source-of-truth declaration of the cue id set that `AudioCues.fs`, the generator and the guard all consume; a committed deterministic WAV synthesis script; asset copy-to-output wiring so the running product finds its assets from its own directory rather than only from the shell's working directory; a product-owned audio-asset evidence command that reports resolution through the shipped resolver and backend.
- In (consequential): a drift guard that fails when a cue id literal appears in `AudioCues.fs` outside the declaration, so the "derived from source" property cannot rot.
- Out: mixing, buses, ducking, 3D positional audio, per-bus volume UI, and any new cue the product does not already request. This milestone makes the existing requests audible; it does not extend the cue map.
- Out: replacing the synthesized placeholders with authored or licensed audio. The generator is the shipping route for v1; a later swap is a content decision, not an availability defect.
- Out: M13 and M14 rows.

## Policy Pointers
- SDD policy comes from `.fsgg/sdd.yml` and `.fsgg/agents.yml`.
- Governance files are optional compatibility pointers and are not evaluated by this command.
- Honor constitution principles I, II, III, V, VI and VIII; the FS.GG.Audio FR-005 boundary (the product owns the id -> asset mapping, the host never does) as mirrored in `docs/api-surface/Audio.Host/Host.fsi`.
- Honor the M10 determinism contract in `src/Rogue3/Determinism.fs`.

## Deferrals Received
- M11 DEC-013 deferred audio asset resolution out of `012-m11-playability-visual-legibility` as "an asset-availability fact rather than a playability or legibility defect", naming `title-theme`, `floor-1-theme`, `dodge-roll`, `player-hit` and `bomb-explosion`. This work item picks that deferral up and discharges it over the **complete** derived id set, not the five ids first observed.

## Lifecycle Notes
- Tier 1: it adds a compiled source file to the public product surface, changes `AudioCues.fs`, adds committed binary content and a build item that copies it to the output directory, and adds an evidence command.
- Assets are content, not model state: no workload or UI-route digest should move for this work item, and any that does is a signal to investigate rather than to re-derive.
- Next lifecycle action: `fsgg-sdd specify --work 013-m12-audio-assets`.
