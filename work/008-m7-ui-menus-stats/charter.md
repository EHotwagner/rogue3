---
schemaVersion: 1
workId: 008-m7-ui-menus-stats
title: "M7 UI, menus and stats"
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

# M7 UI, menus and stats Charter

## Identity
- M7 completes Hollow Depths' player-facing shell and information surfaces: HUD, menus/settings, persistent preference requests, run/lifetime statistics, and start-run difficulty latching.

## Principles
- Keep all state transitions pure and deterministic; use the generic FS.GG game shell and typed Controls; make interactions prove themselves through the retained host; keep persistence as requested values without claiming unavailable profile-file I/O; preserve bounded update/view cost.

## Scope Boundaries
- In: all five M7 roadmap rows and AC #13. Out: M8 audio cues/playback, M9 terminal screens/unlock evaluation/atomic profile-file backend, M10 full acceptance sweep, and every stretch goal.

## Policy Pointers
- Constitution I-VIII; source specification sections 3, 4.6-4.10, 7.1-7.5, 8, 9, 12-14; performance intent in `src/Rogue3/PerformanceEvidence.fs`.

## Lifecycle Notes
- Tier 1 product UI/state expansion. M7 owns pure `MetaProfile` settings/stat values and record-only persistence requests; M9 still owns the debounced atomic platform-app-data file backend and end-of-run profile fold.
