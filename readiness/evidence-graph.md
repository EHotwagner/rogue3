# Evidence graph

Synthesized in-process by the FS.GG.UI.Build engine (EvidenceGraph) over the generated
product's readiness surface. The graph reflects the artifacts that exist at gate time.
Absent OPTIONAL artifacts (interactive launch/image/window/…, profile-dependent) are not
failures. The required headless baseline (layout + scene evidence) MUST be present, however —
its absence is a product-evidence defect (evidence-output-contract.md §EvidenceGraph).

- readiness files present: 28
- recognized evidence nodes: 2

## Sensed readiness files

- `readiness/001-m0-scaffold-fixed-step-loop/agent-commands/claude/commands.md`
- `readiness/001-m0-scaffold-fixed-step-loop/agent-commands/claude/guidance.json`
- `readiness/001-m0-scaffold-fixed-step-loop/agent-commands/claude/skills.md`
- `readiness/001-m0-scaffold-fixed-step-loop/agent-commands/codex/commands.md`
- `readiness/001-m0-scaffold-fixed-step-loop/agent-commands/codex/guidance.json`
- `readiness/001-m0-scaffold-fixed-step-loop/agent-commands/codex/skills.md`
- `readiness/001-m0-scaffold-fixed-step-loop/analysis.json`
- `readiness/001-m0-scaffold-fixed-step-loop/governance-handoff.json`
- `readiness/001-m0-scaffold-fixed-step-loop/ship-verdict.json`
- `readiness/001-m0-scaffold-fixed-step-loop/ship.json`
- `readiness/001-m0-scaffold-fixed-step-loop/summary.md`
- `readiness/001-m0-scaffold-fixed-step-loop/test-results/m0-focused.trx`
- `readiness/001-m0-scaffold-fixed-step-loop/test-results/m0-full.trx`
- `readiness/001-m0-scaffold-fixed-step-loop/verify.json`
- `readiness/001-m0-scaffold-fixed-step-loop/work-model.json`
- `readiness/evidence-audit.md`
- `readiness/evidence-graph.md`
- `readiness/headless-scene-evidence.txt`
- `readiness/layout-evidence.txt`
- `readiness/logs/Dev.txt`
- `readiness/logs/GeneratedGuidanceCheck.txt`
- `readiness/logs/PerformanceEvidence.txt`
- `readiness/logs/PerformanceIntent.txt`
- `readiness/logs/TemplateDrift.txt`
- `readiness/logs/Test.txt`
- `readiness/logs/Verify.txt`
- `readiness/performance-evidence.json`
- `readiness/performance-intent.yml`

## Evidence nodes

| Artifact | Kind | State |
|---|---|---|
| `readiness/layout-evidence.txt` | layout | present-valid |
| `readiness/headless-scene-evidence.txt` | scene | present-valid |

## Required baseline

_required headless baseline present (layout + scene evidence)_
