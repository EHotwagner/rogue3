# Evidence graph

Synthesized in-process by the FS.GG.UI.Build engine (EvidenceGraph) over the generated
product's readiness surface. The graph reflects the artifacts that exist at gate time.
Absent OPTIONAL artifacts (interactive launch/image/window/…, profile-dependent) are not
failures. The required headless baseline (layout + scene evidence) MUST be present, however —
its absence is a product-evidence defect (evidence-output-contract.md §EvidenceGraph).

- readiness files present: 41
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
- `readiness/010-m9-win-loss-permadeath/ship-verdict.json`
- `readiness/011-m10-acceptance-determinism/ship-verdict.json`
- `readiness/012-m11-playability-visual-legibility/analysis.json`
- `readiness/012-m11-playability-visual-legibility/frames/01-start-room/reference-evidence.md`
- `readiness/012-m11-playability-visual-legibility/frames/01-start-room/sha256-4f204801186b3f54da5797f7dbb5edf8a885c42bd3fc5b5bbf6a2477a4a09fe6.png`
- `readiness/012-m11-playability-visual-legibility/frames/02-all-door-states/reference-evidence.md`
- `readiness/012-m11-playability-visual-legibility/frames/02-all-door-states/sha256-c8868adf28e4756b92a92f6a09f5c8f86dadc75cae5832000c436078089f6eb8.png`
- `readiness/012-m11-playability-visual-legibility/frames/03-combat-room-sealed/reference-evidence.md`
- `readiness/012-m11-playability-visual-legibility/frames/03-combat-room-sealed/sha256-cb64d3b15b8634063c0283d81a722bafcc35be618236e1f857d4ba76fb9e3d5d.png`
- `readiness/012-m11-playability-visual-legibility/frames/04-boss-room-sealed/reference-evidence.md`
- `readiness/012-m11-playability-visual-legibility/frames/04-boss-room-sealed/sha256-8250ca5ae6d41ad1a59422894012c9a68627e652fa2710c87e63aba705a5893a.png`
- `readiness/012-m11-playability-visual-legibility/frames/05-boss-cleared-trapdoor/reference-evidence.md`
- `readiness/012-m11-playability-visual-legibility/frames/05-boss-cleared-trapdoor/sha256-163c3bc8fdf0e2f4d1e62eec9c9dd66853fe855fd3dc387441fd8718260c8c9a.png`
- `readiness/012-m11-playability-visual-legibility/frames/06-standing-on-trapdoor/reference-evidence.md`
- `readiness/012-m11-playability-visual-legibility/frames/06-standing-on-trapdoor/sha256-051fc5f363a70c577fd22e93a83fefe54e295134a57c72e0918e07491a46c674.png`
- `readiness/012-m11-playability-visual-legibility/frames/07-combat-room-cleared-hidden-wall/reference-evidence.md`
- `readiness/012-m11-playability-visual-legibility/frames/07-combat-room-cleared-hidden-wall/sha256-b140e14b73be336fd0c1202c7f2358c7f5a4811e9db8debe6ca8c8b208e921ca.png`
- `readiness/012-m11-playability-visual-legibility/frames/08-key-door-in-play/reference-evidence.md`
- `readiness/012-m11-playability-visual-legibility/frames/08-key-door-in-play/sha256-8c06cf3a88b27a830de5d1e79db7d2a80622f1da7d47c256e26aa4dc4355ec75.png`
- `readiness/evidence-audit.md`
- `readiness/evidence-graph.md`
- `readiness/headless-scene-evidence.txt`
- `readiness/layout-evidence.txt`
- `readiness/logs/Dev.txt`
- `readiness/logs/GeneratedGuidanceCheck.txt`
- `readiness/logs/TemplateDrift.txt`
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
