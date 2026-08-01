---
schemaVersion: 1
workId: 011-m10-acceptance-determinism
title: M10 Acceptance and Determinism
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

# M10 Acceptance and Determinism Charter

## Identity
- Close Hollow Depths by proving all 24 source-specification acceptance scenarios (§14) green against the production simulation, and by making a run reproducible from a seed plus an ordered input log.

## Principles
- An acceptance claim names the scenario it discharges and drives the production `Model.update` route, never a test-only replica.
- Determinism is proven by a canonical byte encoding the product owns, not by a formatting helper whose stability is undocumented.
- Layout facts ride `LayoutRng` only; combat and drop facts ride `DropRng` only. Neither stream may observe the other.
- Missing production seams found while proving a scenario are implemented in the production model, not stubbed in the test.

## Scope Boundaries
- In: the seven M10 roadmap rows — the full §14 sweep, seeded generation byte-identity (§14.1), layout/combat stream independence (§14.2), layout-deterministic dupe-free fixture contents (§14.12), difficulty latch and sim scaling (§14.13), atomic bomb-driven secret reveal (§14.14), and seed-plus-input-log replay byte-identity (§13).
- In (deferral pickup): the acceptance sweep deferred here by M7 and M8, and the replay/determinism acceptance work deferred here by M9.
- In (production gaps discovered by the sweep): door traversal and key-door unlocking as production messages, because §14.15/§14.16 cannot be discharged without them.
- Out: every "Stretch — deferred (post-v1)" roadmap row (§15), online leaderboards, mid-run saves, additional characters, and render interpolation.

## Policy Pointers
- Honor constitution principles I, II, IV, V, VI, and VIII; source specification sections 13 and 14 in full, plus 4.8, 4.11, and 12 where the acceptance scenarios reach into them.

## Lifecycle Notes
- Tier 1 product behavior work: it adds product-owned replay and door-traversal contracts and new production messages, so signatures, tests, and evidence move together.
- This is the terminal non-deferred milestone. Every deferral aimed at M10 by an earlier milestone is discharged here or reported as a blocking finding.
- Next lifecycle action: `fsgg-sdd specify --work 011-m10-acceptance-determinism`.
