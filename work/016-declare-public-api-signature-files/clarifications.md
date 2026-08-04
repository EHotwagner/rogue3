---
schemaVersion: 1
workId: 016-declare-public-api-signature-files
title: Declare Public Api Signature Files
stage: clarify
changeTier: tier1
status: clarified
sourceSpec: work/016-declare-public-api-signature-files/spec.md
publicOrToolFacingImpact: true
---

# Declare Public Api Signature Files Clarifications

## Source Specification
- work/016-declare-public-api-signature-files/spec.md

## Clarification Questions
- CQ-001: The issue says "the intended public product modules". Which modules are intended?
- CQ-002: Acceptance 1 asks to "distinguish contracted members from test/evidence conveniences".
  Should members that only the test project names be removed from the public surface?
- CQ-003: How is the *intended* surface distinguished from the *accidental* one without hand-reading
  11,771 lines of implementation?
- CQ-004: Acceptance 4 asks for a gate proving "signature/source compile order remains valid". The
  F# compiler already rejects a signature that follows its implementation. What is left to gate?
- CQ-005: Two adaptable helper modules (`Vec2.fs`, `Collision.fs`) use `namespace Rogue3` plus a
  nested `module`, and their compile items are `Exists`-guarded so the project stays "durable" when
  a consumer deletes them. How do their signatures participate?

## Answers
- CA-001: All 21. Every compiled module is part of the product, and a partial answer would leave the
  configured `src/**/*.fsi` surface matching some modules and silently not others — which is the
  same failure mode as matching none, only harder to see. The issue's verification asks that the
  signatures cover "every intentionally public module", and nothing in the tree marks a compiled
  module as unintended.
- CA-002: No — they stay declared, and the distinction is *recorded* rather than enforced. The
  acceptance asks the *inventory* to distinguish them; it asks only "implementation-only helpers"
  to be private or absent, and a member the test project names is not implementation-only. Removing
  them would mean either deleting live assertions or adding an `InternalsVisibleTo` seam, both of
  which change more than this item contracts. `docs/public-api-surface.md` records which members
  have only test/evidence consumers so a later item can act on a real list.
- CA-003: By taking the compiler's inferred signature (`--allsigs`) as ground truth for what
  *exists*, then pruning it by measured cross-module reference. The compiler is then the oracle for
  correctness: anything wrongly pruned fails the build of the product, its tests or its scripts.
- CA-004: The case the compiler does not catch. A signature file present on disk but absent from
  compile order leaves the build **completely green** while its module silently republishes
  everything — verified live: removing `<Compile Include="Program.fsi" />` produced 0 build errors
  and returned 11 bindings to the public API. So the gate must read compile order and the built
  assembly, not rely on compilation succeeding.
- CA-005: Their signatures are declared whole and carry the *same* `Exists` guard as their
  implementation. An unguarded signature beside a guarded implementation would break the durability
  promise on the first deletion, leaving a signature with nothing to constrain; the gate asserts the
  guards match.

## Decisions
- CD-001 (from CA-001): Ship 21 signature files, one per compiled module.
- CD-002 (from CA-002): Retain test-consumed members in the declared surface; record their consumer
  class in `docs/public-api-surface.md`. Do not add an `InternalsVisibleTo` seam under this item.
- CD-003 (from CA-003): Derive from `--allsigs`, prune by measured reference, and treat a green
  build of product + tests + scripts as the correctness oracle.
- CD-004 (from CA-004): The gate's load-bearing assertion runs against the built assembly by
  reflection, and is demonstrated against a real planted regression rather than only synthetic input.
- CD-005 (from CA-005): Signature compile items inherit their implementation's `Condition` verbatim.

## Accepted Deferrals
- AD-001: `Rogue3.Program` still declares 20 values whose types read as `(unit -> Model * Command)`
  rather than curried parameters, because the implementation binds them as eta-reduced values. The
  signature reports this faithfully. Rewriting the implementation to improve how the signature reads
  would edit `.fs` files this item declares out of scope.
- AD-002: `Vec2.fs` and `Collision.fs` are declared whole rather than pruned. They are
  consumer-owned, adaptable, `Exists`-guarded helpers documented as "yours to adapt", so their full
  helper API is the contract a consumer adapts against; pruning them to this product's current call
  sites would narrow a surface that exists to be reused.

## Remaining Ambiguity
No blocking ambiguity remains.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd checklist --work 016-declare-public-api-signature-files`.
