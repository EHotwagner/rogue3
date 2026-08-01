---
schemaVersion: 1
workId: 010-m9-win-loss-permadeath
title: M9 Win/Loss and Permadeath
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

# M9 Win/Loss and Permadeath Charter

## Identity
- Complete Hollow Depths M9 by making production combat terminate in a scored Victory or GameOver and by durably preserving only meta-progression.

## Principles
- Terminal resolution is deterministic and happens once at the production update boundary.
- Permadeath clears all run state; only a versioned MetaProfile survives.
- Filesystem I/O remains at the host boundary and uses debounced atomic replacement.

## Scope Boundaries
- In: floor-6 final-boss victory, zero-health death, score and unlock evaluation, result presentation, audio/shell routing, and real profile JSON persistence.
- Out: M10 replay/determinism acceptance work, online leaderboards, mid-run saves, and additional characters.

## Policy Pointers
- Honor constitution principles I, V, VI, and VIII; source specification sections 4.10, 7.5, 11, 13, and acceptance scenario 7.

## Lifecycle Notes
- Tier 1 product behavior and persistence-format work. Release evidence must use a caller-owned temporary directory and must not touch user data.
