---
schemaVersion: 1
workId: 016-declare-public-api-signature-files
title: Declare Public Api Signature Files
stage: plan
changeTier: tier1
status: planned
sourceSpec: work/016-declare-public-api-signature-files/spec.md
sourceClarifications: work/016-declare-public-api-signature-files/clarifications.md
sourceChecklist: work/016-declare-public-api-signature-files/checklist.md
publicOrToolFacingImpact: true
---

# Declare Public Api Signature Files Plan

Prose status: planned

## Source Snapshot
- spec: work/016-declare-public-api-signature-files/spec.md sha256:38c34f969d53dd8b1fa360d56546cc9149ad18bb5e7a4481e236ae67c0b0c509 schemaVersion:1
- clarifications: work/016-declare-public-api-signature-files/clarifications.md sha256:93b5773c38658f6b42dc72f118a431d47acb50a3934b96dd13c8669a33c39858 schemaVersion:1
- checklist: work/016-declare-public-api-signature-files/checklist.md sha256:075fae3bebb790eebcefb2d501be46e1079174788b06fa3fe13820b0bb65164a schemaVersion:1

## Plan Scope
- Work item 016-declare-public-api-signature-files is planned from the current specification, clarification, and checklist facts.
- Requirement count: 5.
- Clarification decision count: 0.
- Checklist result count: 5.

## Plan Decisions
- PD-001 [AC-001] [FR-001] complete: Emit one signature per compiled module by building with
  `--allsigs`, which writes the compiler's inferred `.fsi` beside each `.fs`. That output is ground
  truth for what the module currently exposes, so the starting point is measured rather than
  guessed. Wire each signature into `Rogue3.fsproj` as the compile item directly before its
  implementation, copying the implementation's `Condition` verbatim so `Exists`-guarded adaptable
  helpers keep the project durable.
- PD-002 [AC-002] [FR-002] complete: Gate the surface against the BUILT ASSEMBLY, not the source.
  `tests/Rogue3.Tests/PublicApiSurfaceTests.fs` reflects over each module type's public static
  members and asserts the set is a subset of what that module's `.fsi` declares. Source text cannot
  establish this: the demotion of an undeclared binding happens in the compiler, so only the
  compiled artifact can testify that it happened.
- PD-003 [AC-003] [FR-003] complete: Prune the inferred signature to the surface real consumers
  name, in three passes -- drop every `private` binding; drop non-private `val`s no other file
  references; drop types that neither a consumer nor a retained signature in the same file names.
  Types are pruned last and most cautiously because a record is built by field name and a union by
  case name, so an absent type NAME is not evidence of an unused type. The compiler is the oracle
  throughout: anything wrongly pruned fails the build of the product, its tests, or its scripts.
  A "reference" is a reference in CODE: the scan strips `//`, `///` and nested `(* … *)` comments
  first (preserving string literals). Review round 1 found this missing — the pruning and the
  published inventory both counted comment mentions, the `#19`/`#28` "count code references, not grep
  hits" error `Rogue3.fsproj` warns about. Correcting it moved the inventory's product-reference
  total from 263 to 236 and moved 17 declarations into the last column, which now holds 23.
  Review round 2 corrected what that column is CALLED: it compares each declaration against every
  consumer file EXCEPT its own, so it means "not named outside its module", never "named by no code
  anywhere" — 15 of the 23 carry live intra-module call sites, and exactly one (`Model.shotSpeed`) is
  named by nothing at all. The counts were unaffected; only the predicate's description was wrong.
  All 23 are enumerated with their intra-module call-site counts in `docs/public-api-surface.md` and
  left in place: acting on them narrows the declared contract, which is a Tier 1 change of its own.
- PD-004 [AC-004] [FR-004] complete: Change no `.fs` implementation file and leave every existing
  test unedited, so the pre-existing suite is an untouched control. If a binding a real consumer
  names were hidden, that suite fails to compile -- which makes "277 pre-existing tests still pass"
  direct evidence that the pruning removed only surface no consumer names.
- PD-005 [AC-005] [FR-005] complete: Prove the gate can fail. Every scan is a pure function over
  its input so synthetic violations can drive it directly (the `#111` "guard the guard" discipline),
  and the whole gate is additionally run against a real planted regression: removing one
  `<Compile Include="Program.fsi" />` item from the live project. That case is the reason the gate
  exists -- `dotnet build` stays green with zero errors while 11 bindings return to the public API.

## Contract Impact
- PC-001 [PD-001] public surface: this work item DEFINES the `public-api` surface
  `.fsgg/capabilities.yml` declares at `src/**/*.fsi` (`owner: platform`,
  `maturity: block-on-ship`), which matched zero files before it. The change is Tier 1 and
  deliberately compatibility-REDUCING for in-assembly consumers: 122 of 568 inferred non-private
  declarations stop being public. It is compatibility-preserving for every consumer that exists --
  the product, its tests and its scripts -- which is what the untouched suite in PD-004 measures.

## Verification Obligations
- VO-001 [PD-001] [PC-001] semanticTest: `dotnet build Rogue3.slnx -c Release` and
  `dotnet test Rogue3.slnx -c Release` are green, with the pre-existing 277 tests unmodified and 8
  new gate tests (285 total).
- VO-002 [PD-002] [PC-001] semanticTest: reflection over the built `Rogue3.dll` shows, for all 21
  modules, that the public member set equals the declared set (no undeclared public members).
- VO-003 [PD-005] [PC-001] negativeControl: with `<Compile Include="Program.fsi" />` removed,
  `dotnet build` reports 0 errors AND the gate reports the 11 bindings that returned to the public
  API -- recorded so the gate's green is legible as a measurement rather than an absence.
- VO-004 [PD-001] [PC-001] toolGate: `python3 scripts/check-audit-bindings.py` and its `--selftest`
  both exit 0, the two project-file edits having been excused in this cycle's own ledger.

## Performance Intent
No performance intent is declared for this work item.

## Migration Posture
- PM-001 [PC-001] diagnoseOnly: no migration is required -- no `.fs` implementation, no persisted
  format, and no on-disk artifact of the running product changes. A consumer inside this assembly
  that reached for one of the 122 removed declarations would now fail to COMPILE rather than fail at
  runtime, which is the intended and immediate diagnostic; no such consumer exists in the tree.

## Generated View Impact
- GV-001 [PD-001] workModel: this work item's SDD-owned views (`work-model.json`, `analysis.json`,
  `verify.json`, `ship.json`) refresh from these plan sources and stay UNTRACKED, per `.gitignore`'s
  `readiness/*/*` rule — "transient: ignore by role, never commit", regenerable with `fsgg-sdd
  refresh`. The one durable exception it names, `!readiness/*/ship-verdict.json` (ADR-0026), is
  committed, and the observed test run is force-added as evidence exactly as items 009 and 015 did.
  The product's own generated views are unaffected: the `--allsigs` output was an authoring INPUT and
  is not committed as a generated view, so no generator digest in this repository has to track it.
  `docs/api-surface/` holds the vendored baselines of the FS.GG dependency packages and is untouched.

## Accepted Deferrals
No accepted plan deferrals recorded.

## Planning Findings
No blocking planning findings recorded.

## Advisory Notes
- Optional Governance pointers remain compatibility facts only.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd tasks --work 016-declare-public-api-signature-files`.
