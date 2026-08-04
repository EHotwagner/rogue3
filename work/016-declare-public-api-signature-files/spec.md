---
schemaVersion: 1
workId: 016-declare-public-api-signature-files
title: Declare Public Api Signature Files
stage: specify
changeTier: tier1
status: specified
publicOrToolFacingImpact: true
---

# Declare Public Api Signature Files Specification

Prose status: specified

## User Value
A reader, a consumer and a gate can all see what the Rogue3 product contracts, because every compiled module declares its public surface in a signature file instead of publishing whatever its implementation happens to leave non-private.

## Scope
- SB-001: Signature files for every compiled module under src/Rogue3/, their compile-order wiring in src/Rogue3/Rogue3.fsproj, a public-api surface gate under tests/Rogue3.Tests/, public-surface documentation under docs/, and this work item's SDD artifacts. No .fs implementation file is edited and no product behaviour changes.

## Non-Goals
- SB-002: Do not implement later lifecycle commands or Governance enforcement in this specification.

## User Stories
- US-001 (P1): As someone reading, consuming or gating the Rogue3 product, I can tell what it contracts from its signature files, rather than inferring it from whatever each implementation happens to leave non-private.

## Acceptance Scenarios
- AC-001 [US-001] [FR-001]: Given `src/Rogue3/` compiles 21 modules, when the project is read, then each module has a `.fsi` and each `.fsi` is the compile item directly before its `.fs`.
- AC-002 [US-001] [FR-002]: Given the product is built, when the assembly's public module-level members are enumerated by reflection, then every one of them is declared in that module's signature file.
- AC-003 [US-001] [FR-003]: Given the compiler-inferred signature declares 568 non-private members, when the shipped signatures are counted, then at most 450 remain — the surface is reduced, not restated.
- AC-004 [US-001] [FR-004]: Given the pre-existing suite is unmodified, when it runs against the signature-constrained build, then every test passes, so nothing a real consumer names was made private.
- AC-005 [US-001] [FR-005]: Given a signature is removed from compile order, when `dotnet build` reports zero errors, then the gate still fails and names the bindings that silently returned to the public API.

## Functional Requirements
- FR-001: find src/Rogue3 -name '*.fsi' returns one signature per compiled module, and every signature is the compile item directly before its implementation. (Stories: US-001; Acceptance: AC-001)
- FR-002: The built Rogue3 assembly publishes no module-level binding that its module's signature file does not declare, measured by reflection over the compiled artifact rather than by reading source. (Stories: US-001; Acceptance: AC-002)
- FR-003: The accidental surface is reduced rather than restated: of 568 non-private declarations in the compiler-inferred signature, at most 450 remain declared. (Stories: US-001; Acceptance: AC-003)
- FR-004: The existing test suite passes unmodified, proving no binding a real consumer names was made private. (Stories: US-001; Acceptance: AC-004)
- FR-005: The gate fails when a signature is removed from compile order, a case in which dotnet build reports zero errors. (Stories: US-001; Acceptance: AC-005)

## Ambiguities
No material ambiguities recorded.

## Public Or Tool-Facing Impact
- This specification is an SDD lifecycle artifact and command-report contract input.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd clarify --work 016-declare-public-api-signature-files`.
