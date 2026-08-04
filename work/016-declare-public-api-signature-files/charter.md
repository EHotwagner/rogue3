---
schemaVersion: 1
workId: 016-declare-public-api-signature-files
title: Declare The Rogue3 Product API With Signature Files
stage: charter
changeTier: tier1
status: chartered
policyPointers:
  - .fsgg/sdd.yml
  - .fsgg/agents.yml
  - .fsgg/policy.yml
  - .fsgg/capabilities.yml
  - .fsgg/tooling.yml
---

# Declare The Rogue3 Product API With Signature Files Charter

## Identity
- Work id: `016-declare-public-api-signature-files`
- Lifecycle stage: charter
- Status: chartered
- Closes board item `EHotwagner/rogue3#96`: `src/Rogue3/` contained 21 compiled `.fs` modules and
  **zero** `.fsi` files, so the product exposed every non-private implementation binding as
  incidental public API.
- This is not an undocumented preference. `.fsgg/capabilities.yml` declares a `public-api` surface
  at `src/**/*.fsi` owned by platform at `maturity: block-on-ship`, and `.fsgg/constitution.md`
  principle III ("Public Surface Is Declared, Not Incidental") requires the public surface of a
  module to be declared explicitly in signature files where the language supports them.
- The preceding fifteen work items (`work/001-…` through `work/015-…`) all carry complete SDD
  artifacts and `shipReady` verdicts, and many classify their change as Tier 1 while naming
  additions to the public `Model`, `Entities`, `Render`, replay, input and audio surfaces. The SDD
  workflow was followed; the signature boundary those specs repeatedly promised was never
  established. A configured surface that matches no file is the failure this item closes.

## Principles

- **The compiler is the inventory, and the consumers are the contract.** What the product *exposes*
  is a fact only the compiler can state, so the inventory starts from the compiler's own inferred
  signature (`--allsigs`) rather than from reading source. What the product *contracts* is a
  different fact, decided by what the executable, the tests, and the scripts actually name. The
  first is ground truth for the second's starting point and nothing more.

- **Do not mechanically expose the accidental surface.** Emitting the inferred signature verbatim
  would satisfy "every module has a `.fsi`" while changing nothing about what is public — the
  configured surface would stop being empty and start being a rubber stamp. The acceptance
  criterion is explicit that implementation-only helpers stay private or absent, so every retained
  declaration is retained because something outside its module names it.

- **A binding absent from a signature must actually stop being public, and only the assembly can
  say so.** The demotion this item relies on happens in the compiler, not in the text of the `.fsi`.
  A gate that reads source could confirm a file exists and a name is missing from it while the
  built product still published that name. The load-bearing check therefore runs against the
  compiled artifact by reflection.

- **A gate that cannot be shown to fail is not evidence.** This repository already learned that at
  `#111`, where a bare-substring compile-order scan passed on a tree it should have rejected, and
  the remedy was a synthetic case proving the naive scan is fooled. Every scan added here is a pure
  function over its input so a planted violation can drive it, and the whole gate is additionally
  demonstrated against a real planted regression in the live project file.

- **Test and evidence conveniences are distinguished, not silently blessed.** The acceptance asks
  the inventory to separate contracted members from test/evidence conveniences. Members that only
  the test project or the evidence scripts name are recorded as such in the public-surface
  documentation rather than being quietly promoted to product API or quietly deleted out from under
  a passing suite.

- **The signature is a contract, so changing it is a contracted change.** Each `.fsi` carries a
  header saying that a binding absent from it is not product API and that adding one is a Tier 1
  change. The gate makes that statement enforceable rather than aspirational.

## Scope Boundaries

- **In scope:** signature files for every compiled module under `src/Rogue3/`, their compile-order
  wiring in `Rogue3.fsproj`, the public-api surface gate in `tests/Rogue3.Tests/`, the
  public-surface documentation, this work item's SDD artifacts, and the audit-binding exception
  ledger entry the two project-file edits require.
- **Out of scope:** any change to product behaviour. No `.fs` implementation file is edited by this
  item; the observable behaviour of the game, its evidence commands and its replay determinism are
  unchanged, and the existing suite must pass unmodified as the evidence of that.
- **Out of scope:** widening or narrowing what the product *does* — only what it *declares*.
- **Out of scope:** `docs/api-surface/`, which holds the vendored `.fsi` baselines of the FS.GG
  dependency packages, not this product's own surface.
- Keep SDD lifecycle ownership separate from optional Governance enforcement.

## Policy Pointers
- SDD policy comes from `.fsgg/sdd.yml` and `.fsgg/agents.yml`.
- Governance files are optional compatibility pointers and are not evaluated by this command.
- The declared surface itself is governed by `.fsgg/capabilities.yml` (`public-api`,
  `src/**/*.fsi`, `owner: platform`, `maturity: block-on-ship`).

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd specify --work 016-declare-public-api-signature-files`.
