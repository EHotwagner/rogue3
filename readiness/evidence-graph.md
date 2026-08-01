# Evidence graph

Synthesized in-process by the FS.GG.UI.Build engine (EvidenceGraph) over the generated
product's readiness surface. The graph reflects the artifacts that exist at gate time.
Absent OPTIONAL artifacts (interactive launch/image/window/…, profile-dependent) are not
failures. The required headless baseline (layout + scene evidence) MUST be present, however —
its absence is a product-evidence defect (evidence-output-contract.md §EvidenceGraph).

- readiness files present: 32
- recognized evidence nodes: 2

## Sensed readiness files

- `readiness/001-m0-scaffold-fixed-step-loop/ship-verdict.json`
- `readiness/002-m1-input-twin-stick-control/ship-verdict.json`
- `readiness/003-m2-movement-dodge-shots/ship-verdict.json`
- `readiness/004-m3-combat-health-currency/ship-verdict.json`
- `readiness/005-m4-procedural-floor-generation/agent-commands/claude/commands.md`
- `readiness/005-m4-procedural-floor-generation/agent-commands/claude/guidance.json`
- `readiness/005-m4-procedural-floor-generation/agent-commands/claude/skills.md`
- `readiness/005-m4-procedural-floor-generation/agent-commands/codex/commands.md`
- `readiness/005-m4-procedural-floor-generation/agent-commands/codex/guidance.json`
- `readiness/005-m4-procedural-floor-generation/agent-commands/codex/skills.md`
- `readiness/005-m4-procedural-floor-generation/analysis.json`
- `readiness/005-m4-procedural-floor-generation/governance-handoff.json`
- `readiness/005-m4-procedural-floor-generation/ship-verdict.json`
- `readiness/005-m4-procedural-floor-generation/ship.json`
- `readiness/005-m4-procedural-floor-generation/summary.md`
- `readiness/005-m4-procedural-floor-generation/verify.json`
- `readiness/005-m4-procedural-floor-generation/work-model.json`
- `readiness/evidence-audit.md`
- `readiness/evidence-graph.md`
- `readiness/headless-scene-evidence.txt`
- `readiness/layout-evidence.txt`
- `readiness/logs/Dev.txt`
- `readiness/logs/GeneratedGuidanceCheck.txt`
- `readiness/logs/PerformanceCriticRequest.txt`
- `readiness/logs/PerformanceEvidence.txt`
- `readiness/logs/PerformanceIntent.txt`
- `readiness/logs/TemplateDrift.txt`
- `readiness/logs/Test.txt`
- `readiness/logs/Verify.txt`
- `readiness/performance-critic-request.json`
- `readiness/performance-evidence.json`
- `readiness/performance-intent.yml`

## Evidence nodes

| Artifact | Kind | State |
|---|---|---|
| `readiness/layout-evidence.txt` | layout | present-valid |
| `readiness/headless-scene-evidence.txt` | scene | present-valid |

## Required baseline

_required headless baseline present (layout + scene evidence)_
