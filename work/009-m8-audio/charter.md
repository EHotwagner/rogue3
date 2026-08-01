---
schemaVersion: 1
workId: 009-m8-audio
title: "M8 Audio"
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

# M8 Audio Charter

## Identity
- M8 completes Hollow Depths' pure requested-audio layer: deterministic event cues, one replace-before-play music loop per context, and clamped volume/mute requests carried to the existing host sink.

## Principles
- Keep simulation/update pure and device-free; audio leaves as ordered `AudioEffect` values and is proven at the production host boundary through `Audio.interpret`.
- Preserve deterministic fixed-step behavior and existing representative performance budgets.
- Claim requested audio only; asset resolution and actual speaker playback are unavailable evidence in this checkout.

## Scope Boundaries
- In: every §10 cue, per-context title/floor/shop/boss/end music requests, stop-before-replacement policy, startup/restored volume, `[0,1]` clamping, and mute/unmute.
- Out: M9 victory/game-over state machines, permadeath/profile-file durability, M10 acceptance sweep, authored WAV assets, and claims of real speaker playback.

## Policy Pointers
- Constitution I-VIII; source specification §10; `fs-gg-audio`; producer-owned performance intent in `src/Rogue3/PerformanceEvidence.fs`.

## Lifecycle Notes
- Tier 1 product behavior over the already-pinned `FS.GG.Audio.Core`/Host packages and existing `ViewerEffect.PlayAudio` sink; no package/public framework change.
