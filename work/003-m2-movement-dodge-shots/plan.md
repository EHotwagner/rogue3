---
schemaVersion: 1
workId: 003-m2-movement-dodge-shots
title: M2 Movement Dodge Shots
stage: plan
changeTier: tier1
status: planned
sourceSpec: work/003-m2-movement-dodge-shots/spec.md
sourceClarifications: work/003-m2-movement-dodge-shots/clarifications.md
sourceChecklist: work/003-m2-movement-dodge-shots/checklist.md
publicOrToolFacingImpact: true
---

# M2 Movement Dodge Shots Plan

Prose status: planned

## Source Snapshot
- spec: work/003-m2-movement-dodge-shots/spec.md sha256:64de6f9976fd1d4f8d9eed4992d7ccdd70e88593005275be33e4a9237ecee87b schemaVersion:1
- clarifications: work/003-m2-movement-dodge-shots/clarifications.md sha256:b9be1777fbd10ff864f3f435170e7bdb5d127cb8c15adda847d00f0ca76b78ac schemaVersion:1
- checklist: work/003-m2-movement-dodge-shots/checklist.md sha256:ecf191a4218b8d34bd7dd42646d1c1550d1328b4674d3446bc2ac5702b688804 schemaVersion:1

## Plan Scope
- Extend the replaceable product model, collision policy, focused tests, and representative production-route workloads while retaining the durable shell/governance spine.
- Declare movement/dodge/projectile records and pure helpers in `Rogue3.Model`; reuse the packaged geometry vocabulary and product-owned `Collision.slideCircle` rather than adding rigid bodies.
- Keep M3 combat state out: the projectile step consumes ordered hit ids only to spend pierce and prevent repeat spending.

## Plan Decisions
- PD-001 [AC-001] [FR-001] [DEC-004] complete: Add finite vector clamp/approach helpers and integrate velocity toward normalized input times effective movement speed with fixed 2400/3000 per-second rates.
- PD-002 [AC-001] [FR-002] complete: Store `PlayerStats`, clamp effective speed to 120..540, and clamp ordinary control velocity after every approach without altering active roll impulse semantics.
- PD-003 [AC-002] [FR-003] [DEC-004] complete: Convert the player to packaged `Circle`/`Rect` values at `Collision.sweepCircle`, casting the centre against radius-expanded walls and resolving X then Y in stable obstacle order at radius 13.
- PD-004 [AC-003] [FR-004] [DEC-001] complete: Add roll elapsed/cooldown/i-frame timers; edge-trigger Dodge applies 460 along move or facing, decays toward control during 0.45 seconds, blocks fire for 0.40 seconds, and gates starts for 0.90 seconds.
- PD-005 [AC-004] [FR-005] complete: Replace retained spawn intents with live `Shot` state carrying a snapshot of current damage/fireRate/speed/range/radius/knockback/pierce/bounce/homing values and inherited velocity.
- PD-006 [AC-004] [FR-006] complete: Generate `multishot` directions evenly over a centered 18-degree fan with a single-shot zero offset and deterministic increasing shot ids.
- PD-007 [AC-005] [FR-007] [DEC-002] [DEC-003] complete: Integrate age/distance and swept wall contacts, decrement remaining bounces, and spend ordered distinct-hit allowances tracked by shot-local hit-id sets; remove exhausted shots.
- PD-008 [AC-005] [FR-008] complete: Select nearest targets by squared distance then stable id and rotate velocity by a signed angle clamped to homing*360 degrees*fixedDt while preserving magnitude and finite range termination.
- PD-009 [AC-006] [FR-009] complete: Preserve M0/M1 update ordering and host seams, add deterministic focused regressions/production journeys, refresh all five workload digests, and retain exact structural counters for actors, shots, steps, collisions, and homing queries.

## Contract Impact
- PC-001 [PD-001] [PD-002] [PD-004] publicSurface: `Rogue3.Model` exposes `PlayerStats`, dodge state/timers, movement constants, and pure fixed-step movement transitions.
- PC-002 [PD-003] frameworkSurface: framework: FS.GG.Game.Core#Geometry.circleAabbContact and framework: FS.GG.Game.Core#Geometry.segmentAabbHit mediate product-owned `Collision.sweepCircle` without a duplicate geometry type.
- PC-003 [PD-005] [PD-006] [PD-007] [PD-008] publicSurface: `Rogue3.Model` exposes live `Shot`, target/hit seam values, `spawnShots`, and `stepShots` for deterministic acceptance tests.

## Verification Obligations
- VO-001 [PD-001] [PD-002] [PC-001] acceptanceTest: Fixed-step tests cover cardinal/diagonal acceleration, friction, speed clamps, non-finite safety, and no overshoot.
- VO-002 [PD-003] [PC-002] acceptanceTest: Tests cover radius-13 room clamping, obstacle X/Y sliding, stable order, and supported per-step sweep behavior.
- VO-003 [PD-004] [PC-001] acceptanceTest: Tests cover activation direction, exact i-frame/roll/cooldown boundaries, reactivation gate, fire lockout, and multi-step catches.
- VO-004 [PD-005] [PD-006] [PC-003] acceptanceTest: Upstream AC #4 verifies exactly three directions at -9/0/+9 and current-stat snapshots plus velocity inheritance and fire-rate cadence.
- VO-005 [PD-007] [PD-008] [PC-003] acceptanceTest: Upstream AC #10 and focused tests cover age/range, leaving/bouncing, distinct pierce hits, homing turn cap/tie-break, finite values, and guaranteed range termination.
- VO-006 [PD-009] releaseAudit: Focused/full Release TRX, generated Test/Verify, all five representative bounded-headless workloads, exact performance counters, and SDD verify/ship must pass without synthetic evidence.

## Performance Intent
No performance intent is declared for this work item.

## Migration Posture
- PM-001 [PC-001] additiveThenReplace: Retain existing public input/helper compatibility where tests depend on it, but replace the M1 `ShotSpawn` intent history with bounded live projectile state and counters.
- PM-002 [PC-002] compatibleGeometry: Keep product `Vec2` internally and convert at the existing collision boundary; do not add a second package or rigid-body world.

## Generated View Impact
- GV-001 [PD-009] workModel: Refresh analysis, normalized work model, agent guidance, verify, ship, and the committed ship verdict from current authored sources.
- GV-002 [PD-009] performanceEvidence: Regenerate performance intent/evidence and acknowledge every changed journey-bound definition digest after reviewing workload distinction and exact counters.

## Accepted Deferrals
No accepted plan deferrals recorded.

## Planning Findings
No blocking planning findings recorded.

## Advisory Notes
- The pinned host input gaps from M1 remain release obligations and are not evidence for or against pure M2 mechanics.
- No remote exists, so external PR critic, protected-branch marker, merge, and post-merge verification are unavailable and must not be fabricated.
- The tool-owned `Performance Intent` row remains none because M2 does not duplicate the producer contract; this work consumes active product declaration `PI-GENERATED-GAME` from `src/Rogue3/PerformanceEvidence.fs` and `readiness/performance-intent.yml`, including five workloads, exact structural bounds, bounded-headless capability, and compositor caveat.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd tasks --work 003-m2-movement-dodge-shots`.
