# Evidence graph

Synthesized in-process by the FS.GG.UI.Build engine (EvidenceGraph) over the generated
product's readiness surface. The graph reflects the artifacts that exist at gate time.
Absent OPTIONAL artifacts (interactive launch/image/window/…, profile-dependent) are not
failures. The required headless baseline (layout + scene evidence) MUST be present, however —
its absence is a product-evidence defect (evidence-output-contract.md §EvidenceGraph).

- readiness files present: 100
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
- `readiness/014-m13-room-transition-pickups-world-state/frames/01-crossing-start/sha256-9908c115de8b87f4b6f35de05d07e5cf7e9f7daa8550020a0e2943e1ee173842.png`
- `readiness/014-m13-room-transition-pickups-world-state/frames/02-crossing-midpoint/reference-evidence.md`
- `readiness/014-m13-room-transition-pickups-world-state/frames/02-crossing-midpoint/sha256-4a6182d462a525d4f818f610626f8517ca2ec426d46c50c94278511ea79f84a7.png`
- `readiness/014-m13-room-transition-pickups-world-state/frames/03-crossing-settling/reference-evidence.md`
- `readiness/014-m13-room-transition-pickups-world-state/frames/03-crossing-settling/sha256-194e82764d3b57bf2899235da39bdb40014d13e901cad881132fcb2a83a4363e.png`
- `readiness/014-m13-room-transition-pickups-world-state/frames/04-positioned-drops/reference-evidence.md`
- `readiness/014-m13-room-transition-pickups-world-state/frames/04-positioned-drops/sha256-1c7aad730006480b977ab463ddd6dc9ecf95e50e9979e41f2946c688aa80ad87.png`
- `readiness/014-m13-room-transition-pickups-world-state/frames/05-shop-priced-and-locked/reference-evidence.md`
- `readiness/014-m13-room-transition-pickups-world-state/frames/05-shop-priced-and-locked/sha256-69eec58294d69b85ef1001b7cb732869ccf923ec8389c4bfc62dedc90efb3545.png`
- `readiness/014-m13-room-transition-pickups-world-state/frames/06-boss-reward-placed/reference-evidence.md`
- `readiness/014-m13-room-transition-pickups-world-state/frames/06-boss-reward-placed/sha256-7c1c9dc549341765d8a49fe376ba987c6685dc13a5a1fcd4824a5da89158e2ed.png`
- `readiness/014-m13-room-transition-pickups-world-state/frames/07-player-pressed-into-north-wall/reference-evidence.md`
- `readiness/014-m13-room-transition-pickups-world-state/frames/07-player-pressed-into-north-wall/sha256-ce01671d493462c7fc26fd25cfe7ae375e9c98601f634f684f6eb204f30eeb2a.png`
- `readiness/014-m13-room-transition-pickups-world-state/frames/08-player-invulnerable/reference-evidence.md`
- `readiness/014-m13-room-transition-pickups-world-state/frames/08-player-invulnerable/sha256-7b0b80ccf4e7494b57ff3791f7bf8433be71a9c91598c9964d6acfdfdb18f3b3.png`
- `readiness/014-m13-room-transition-pickups-world-state/frames/09-player-dodge-roll/reference-evidence.md`
- `readiness/014-m13-room-transition-pickups-world-state/frames/09-player-dodge-roll/sha256-629a67b6031f581e996acca5ac436df2ee842da4f36569579e9248960bef5b07.png`
- `readiness/014-m13-room-transition-pickups-world-state/frames/09-player-dodge-roll/sha256-b459432cb29fbfa8b041a0826bdf3aa49efffd14b3292c732e802c977d6221ad.png`
- `readiness/014-m13-room-transition-pickups-world-state/frames/10-player-down/reference-evidence.md`
- `readiness/014-m13-room-transition-pickups-world-state/frames/10-player-down/sha256-f24a872d1da47f3696962f734a0be2d245da2f6c3305a82248fdb401fb56c767.png`
- `readiness/014-m13-room-transition-pickups-world-state/frames/11-enemy-telegraph/reference-evidence.md`
- `readiness/014-m13-room-transition-pickups-world-state/frames/11-enemy-telegraph/sha256-650cf92d3cd72247b0de3de924abbb704f31b3ee0f94ddff58363e930b8b2152.png`
- `readiness/014-m13-room-transition-pickups-world-state/frames/11-enemy-telegraph/sha256-b804a6dae43583a8b2fe0b3c3ef037673772c8491eca51b4d94823e2b45dfad8.png`
- `readiness/014-m13-room-transition-pickups-world-state/frames/12-hud-regions/reference-evidence.md`
- `readiness/014-m13-room-transition-pickups-world-state/frames/12-hud-regions/sha256-746950c512d54a61ed6cbefb953ae0e7f92f6dfbcf155b0e2f1cdcae9b2bacdd.png`
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
