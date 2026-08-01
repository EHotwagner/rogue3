# Evidence graph

Synthesized in-process by the FS.GG.UI.Build engine (EvidenceGraph) over the generated
product's readiness surface. The graph reflects the artifacts that exist at gate time.
Absent OPTIONAL artifacts (interactive launch/image/window/…, profile-dependent) are not
failures. The required headless baseline (layout + scene evidence) MUST be present, however —
its absence is a product-evidence defect (evidence-output-contract.md §EvidenceGraph).

- readiness files present: 27
- recognized evidence nodes: 2

## Sensed readiness files

- `readiness/001-m0-scaffold-fixed-step-loop/ship-verdict.json`
- `readiness/002-m1-input-twin-stick-control/ship-verdict.json`
- `readiness/003-m2-movement-dodge-shots/ship-verdict.json`
- `readiness/004-m3-combat-health-currency/ship-verdict.json`
- `readiness/005-m4-procedural-floor-generation/ship-verdict.json`
- `readiness/006-m5-entities-bosses-rooms/implementation-critic-history.md`
- `readiness/006-m5-entities-bosses-rooms/ship-verdict.json`
- `readiness/007-m6-rendering-enemy-symbology/ship-verdict.json`
- `readiness/008-m7-ui-menus-stats/ship-verdict.json`
- `readiness/009-m8-audio/ship-verdict.json`
- `readiness/009-m8-audio/test-results/m8-full.trx`
- `readiness/010-m9-win-loss-permadeath/analysis.json`
- `readiness/evidence-audit.md`
- `readiness/evidence-graph.md`
- `readiness/headless-scene-evidence.txt`
- `readiness/layout-evidence.txt`
- `readiness/logs/Dev.txt`
- `readiness/logs/GeneratedGuidanceCheck.txt`
- `readiness/logs/M7UiPerformanceEvidence.txt`
- `readiness/logs/PerformanceEvidence.txt`
- `readiness/logs/PerformanceIntent.txt`
- `readiness/logs/TemplateDrift.txt`
- `readiness/logs/Test.txt`
- `readiness/m7-ui-performance.json`
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
