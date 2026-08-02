# Evidence graph

Synthesized in-process by the FS.GG.UI.Build engine (EvidenceGraph) over the generated
product's readiness surface. The graph reflects the artifacts that exist at gate time.
Absent OPTIONAL artifacts (interactive launch/image/window/…, profile-dependent) are not
failures. The required headless baseline (layout + scene evidence) MUST be present, however —
its absence is a product-evidence defect (evidence-output-contract.md §EvidenceGraph).

- readiness files present: 102
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
- `readiness/012-m11-playability-visual-legibility/frames/01-start-room/reference-evidence.md`
- `readiness/012-m11-playability-visual-legibility/frames/01-start-room/sha256-e8d8c84e755aa573153a36f0bf4e4ef96ffe5f65a8f7fa313fa122422c5481c7.png`
- `readiness/012-m11-playability-visual-legibility/frames/02-all-door-states/reference-evidence.md`
- `readiness/012-m11-playability-visual-legibility/frames/02-all-door-states/sha256-c65654023b023d88de492705e51b1f5edfd4d152e23fd0909dcf46e64e0e1368.png`
- `readiness/012-m11-playability-visual-legibility/frames/03-combat-room-sealed/reference-evidence.md`
- `readiness/012-m11-playability-visual-legibility/frames/03-combat-room-sealed/sha256-20fd76d5717aa7443f1066b3a24b122a0ec9f1138eaffb02b3c462626a0f9423.png`
- `readiness/012-m11-playability-visual-legibility/frames/04-boss-room-sealed/reference-evidence.md`
- `readiness/012-m11-playability-visual-legibility/frames/04-boss-room-sealed/sha256-e1e3201a9962cb324234394111f2e55eba93505a18a33977a281d6e96fbcd5bb.png`
- `readiness/012-m11-playability-visual-legibility/frames/05-boss-cleared-trapdoor/reference-evidence.md`
- `readiness/012-m11-playability-visual-legibility/frames/05-boss-cleared-trapdoor/sha256-2e78c18e1a1e8375bc4d04adae1ee55b9ae5c2bc854fc110a807f59809b05a34.png`
- `readiness/012-m11-playability-visual-legibility/frames/06-standing-on-trapdoor/reference-evidence.md`
- `readiness/012-m11-playability-visual-legibility/frames/06-standing-on-trapdoor/sha256-9989fca28dd4ab358e11cc8f81fbb3f8718aaba272b81fdc4eac6df9243546ad.png`
- `readiness/012-m11-playability-visual-legibility/frames/07-combat-room-cleared-hidden-wall/reference-evidence.md`
- `readiness/012-m11-playability-visual-legibility/frames/07-combat-room-cleared-hidden-wall/sha256-a51324185abc1bcae06db1bb5c07284de950821c80536b3b08257e9a4b0f9c49.png`
- `readiness/012-m11-playability-visual-legibility/frames/08-key-door-in-play/reference-evidence.md`
- `readiness/012-m11-playability-visual-legibility/frames/08-key-door-in-play/sha256-8aff8474a90159911a73f44b97d74e891a6c88c70d668b70e105a284c41b75e3.png`
- `readiness/012-m11-playability-visual-legibility/frames/09-pickups-and-drops/reference-evidence.md`
- `readiness/012-m11-playability-visual-legibility/frames/09-pickups-and-drops/sha256-c2e43787edc96ad417b11cae2c6c0b53e5c764a6ebe06741d9857ff5d9e3882d.png`
- `readiness/012-m11-playability-visual-legibility/frames/10-enemy-roster/reference-evidence.md`
- `readiness/012-m11-playability-visual-legibility/frames/10-enemy-roster/sha256-05a12565189043c69555b8d5453242e831e81d1aafbd582e1505594a76df6d48.png`
- `readiness/012-m11-playability-visual-legibility/frames/11-boss-hollow-choir/reference-evidence.md`
- `readiness/012-m11-playability-visual-legibility/frames/11-boss-hollow-choir/sha256-a3021351a498fd733dc64c7e3b9cc5d6d53c0ae49cb3ba32a4da6d693837d8d4.png`
- `readiness/012-m11-playability-visual-legibility/frames/12-boss-maw/reference-evidence.md`
- `readiness/012-m11-playability-visual-legibility/frames/12-boss-maw/sha256-62e39e565317e320dc7ecff0981cb5a971bf20d976aa30f818f3fc182b74a027.png`
- `readiness/012-m11-playability-visual-legibility/frames/13-shop-and-reward/reference-evidence.md`
- `readiness/012-m11-playability-visual-legibility/frames/13-shop-and-reward/sha256-39999262503cadf9d9595cc34ba8e1e6fd89f33fded93c71be15f91f40bb081c.png`
- `readiness/012-m11-playability-visual-legibility/frames/14-projectiles-and-bombs/reference-evidence.md`
- `readiness/012-m11-playability-visual-legibility/frames/14-projectiles-and-bombs/sha256-0a24feea22ce7616c2c69f757a28a67e4254c5469324d2e76d57c06114c19afc.png`
- `readiness/012-m11-playability-visual-legibility/frames/15-particles/reference-evidence.md`
- `readiness/012-m11-playability-visual-legibility/frames/15-particles/sha256-b9b5314102b1be0c8a75fa32d79a0a4ed12285f441abecfec8913fcf64854c02.png`
- `readiness/012-m11-playability-visual-legibility/frames/16-run-result-overlay/reference-evidence.md`
- `readiness/012-m11-playability-visual-legibility/frames/16-run-result-overlay/sha256-e26caa0b83ad62b8e624c7fd36e355518af6935e6b94e26be1e87ad6659be0cd.png`
- `readiness/012-m11-playability-visual-legibility/ship-verdict.json`
- `readiness/012-m11-playability-visual-legibility/test-results/m11-release-final.trx`
- `readiness/013-m12-audio-assets/audio-asset-evidence-foreign-cwd.txt`
- `readiness/013-m12-audio-assets/audio-asset-evidence.txt`
- `readiness/013-m12-audio-assets/launch-evidence.md`
- `readiness/013-m12-audio-assets/launch-mode-evidence.txt`
- `readiness/013-m12-audio-assets/launch-output-after.txt`
- `readiness/013-m12-audio-assets/launch-output-before.txt`
- `readiness/013-m12-audio-assets/ship-verdict.json`
- `readiness/013-m12-audio-assets/test-results/m12-focused-release.trx`
- `readiness/013-m12-audio-assets/test-results/m12-full-release.trx`
- `readiness/014-m13-room-transition-pickups-world-state/analysis.json`
- `readiness/014-m13-room-transition-pickups-world-state/frames/01-crossing-start/reference-evidence.md`
- `readiness/014-m13-room-transition-pickups-world-state/frames/01-crossing-start/sha256-e09a11b36be375d7edb14651d9837ae8b323b86ce227ad4b62424e17d7861e16.png`
- `readiness/014-m13-room-transition-pickups-world-state/frames/02-crossing-midpoint/reference-evidence.md`
- `readiness/014-m13-room-transition-pickups-world-state/frames/02-crossing-midpoint/sha256-a413685764acdd21b405ebea34bdee4b572df77d21a00ed55d6b5a129b328848.png`
- `readiness/014-m13-room-transition-pickups-world-state/frames/03-crossing-settling/reference-evidence.md`
- `readiness/014-m13-room-transition-pickups-world-state/frames/03-crossing-settling/sha256-2ebcd49ba4271c3bb7573d30d6cfc26c67174b2ee70136ce1f74456295c84ae1.png`
- `readiness/014-m13-room-transition-pickups-world-state/frames/04-positioned-drops/reference-evidence.md`
- `readiness/014-m13-room-transition-pickups-world-state/frames/04-positioned-drops/sha256-56be5a7751fc3972ddded5d1642dbe6c503add983fd77a957f9a8dbea6c95ce9.png`
- `readiness/014-m13-room-transition-pickups-world-state/frames/05-shop-priced-and-locked/reference-evidence.md`
- `readiness/014-m13-room-transition-pickups-world-state/frames/05-shop-priced-and-locked/sha256-60ded15b94bd83162e0ca92f93686e4283c1edaa108d8c131c4d447aa1420571.png`
- `readiness/014-m13-room-transition-pickups-world-state/frames/06-boss-reward-placed/reference-evidence.md`
- `readiness/014-m13-room-transition-pickups-world-state/frames/06-boss-reward-placed/sha256-4b270be297586503823ffbd4a8613dd6d7730301f7cedd154e76c223c1723f1f.png`
- `readiness/014-m13-room-transition-pickups-world-state/frames/07-player-pressed-into-north-wall/reference-evidence.md`
- `readiness/014-m13-room-transition-pickups-world-state/frames/07-player-pressed-into-north-wall/sha256-bb23fc89ad8edf129fe51e0fe5d494480e68c9247f45412cefdb26dce62cc1af.png`
- `readiness/014-m13-room-transition-pickups-world-state/frames/08-player-invulnerable/reference-evidence.md`
- `readiness/014-m13-room-transition-pickups-world-state/frames/08-player-invulnerable/sha256-3f0af4517a69911edffcd1a4221318441dd91515b4683f1de3be43e6b0e5f609.png`
- `readiness/014-m13-room-transition-pickups-world-state/frames/09-player-dodge-roll/reference-evidence.md`
- `readiness/014-m13-room-transition-pickups-world-state/frames/09-player-dodge-roll/sha256-441162511f80fef3a95c8b89d03f7a0dad7bc274482aa7bb4704c7076ad2f800.png`
- `readiness/014-m13-room-transition-pickups-world-state/frames/10-player-down/reference-evidence.md`
- `readiness/014-m13-room-transition-pickups-world-state/frames/10-player-down/sha256-4462f71a44727795afb7f4f57b37abebddaebdaa1be87bb708e099dc55186d13.png`
- `readiness/014-m13-room-transition-pickups-world-state/frames/11-enemy-telegraph/reference-evidence.md`
- `readiness/014-m13-room-transition-pickups-world-state/frames/11-enemy-telegraph/sha256-2bf61cd9ea05b8d0db21dc02cff1c112a01f2ad4b808fe7e35efdadb9ddf9e33.png`
- `readiness/014-m13-room-transition-pickups-world-state/frames/12-hud-regions/reference-evidence.md`
- `readiness/014-m13-room-transition-pickups-world-state/frames/12-hud-regions/sha256-d5218a8bbc5421f0442f39cfced890945b04e6d0d2b1b8801e12ad2b347ffc2f.png`
- `readiness/014-m13-room-transition-pickups-world-state/governance-handoff.json`
- `readiness/014-m13-room-transition-pickups-world-state/ship-verdict.json`
- `readiness/014-m13-room-transition-pickups-world-state/ship.json`
- `readiness/014-m13-room-transition-pickups-world-state/test-results/m13-full-release.trx`
- `readiness/014-m13-room-transition-pickups-world-state/verify.json`
- `readiness/014-m13-room-transition-pickups-world-state/work-model.json`
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
