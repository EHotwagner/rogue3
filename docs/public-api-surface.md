# The Rogue3 public API surface

This product declares its public surface in F# **signature files** (`.fsi`), one per compiled module
under `src/Rogue3/`, each compiled directly before the implementation it constrains.

That is not a style preference. `.fsgg/capabilities.yml` declares a `public-api` surface:

```yaml
surfaces:
  - id: public-api
    kind: package
    paths: ["src/**/*.fsi"]
    owner: platform
    maturity: block-on-ship
```

and `.fsgg/constitution.md` principle III — *"Public Surface Is Declared, Not Incidental"* — requires
the public surface of a module to be declared explicitly in signature files where the language
supports them.

Until `EHotwagner/rogue3#96`, that configured surface matched **zero files**. `src/Rogue3/` held 21
compiled `.fs` modules and no `.fsi` at all, so every non-private binding was public API by accident.

## The rule

**A binding absent from its module's `.fsi` is not product API.** It is private to the
implementation, the compiler enforces that, and `tests/Rogue3.Tests/PublicApiSurfaceTests.fs` proves
the enforcement happened by reflecting over the built assembly.

**Adding a declaration to a `.fsi` is a contracted (Tier 1) change.** Update the signature, the
tests, and this document together — principle III again: *"A contracted change that does not update
signatures, baselines, tests, and docs together is incomplete."*

## What the surface contains

446 declarations across 21 modules, classified by who actually names them:

| module | declared | named by other product modules | named only by tests/scripts | type vocabulary | unreferenced |
|---|---:|---:|---:|---:|---:|
| `Rogue3.AudioCueIds` | 28 | 27 | 1 | 0 | 0 |
| `Rogue3.AudioCues` | 5 | 5 | 0 | 0 | 0 |
| `Rogue3.AudioSynthesis` | 2 | 0 | 2 | 0 | 0 |
| `Rogue3.Collision` | 12 | 5 | 0 | 2 | 5 |
| `Rogue3.Determinism` | 3 | 3 | 0 | 0 | 0 |
| `Rogue3.Entities` | 59 | 44 | 8 | 7 | 0 |
| `Rogue3.EvidenceCommands` | 20 | 13 | 6 | 1 | 0 |
| `Rogue3.FloorGeneration` | 21 | 14 | 3 | 4 | 0 |
| `Rogue3.GameShell` | 23 | 13 | 7 | 3 | 0 |
| `Rogue3.GameplayVisualInventory` | 9 | 2 | 4 | 3 | 0 |
| `Rogue3.LayoutEvidence` | 9 | 9 | 0 | 0 | 0 |
| `Rogue3.M7Ui` | 5 | 5 | 0 | 0 | 0 |
| `Rogue3.Model` | 142 | 77 | 48 | 17 | 0 |
| `Rogue3.PerformanceEvidence` | 17 | 3 | 8 | 6 | 0 |
| `Rogue3.ProfileStore` | 7 | 2 | 3 | 2 | 0 |
| `Rogue3.Program` | 22 | 2 | 20 | 0 | 0 |
| `Rogue3.Render` | 30 | 15 | 11 | 4 | 0 |
| `Rogue3.Replay` | 6 | 0 | 5 | 1 | 0 |
| `Rogue3.Geometry` (`Vec2.fs`) | 16 | 14 | 0 | 1 | 1 |
| `Rogue3.View` | 1 | 1 | 0 | 0 | 0 |
| `Rogue3.WindowOptions` | 9 | 9 | 0 | 0 | 0 |
| **total** | **446** | **263** | **126** | **51** | **6** |

The column meanings, because they are the distinction issue #96 asked the inventory to make:

- **named by other product modules** — contracted product API. Another module under `src/Rogue3/`
  depends on it, so it is load-bearing for the product itself.
- **named only by tests/scripts** — **test and evidence conveniences.** These are declared so the
  existing suite and the evidence scripts keep compiling, but *no product code depends on them*. They
  are the honest candidates for a future narrowing (an `InternalsVisibleTo` seam, or deletion with
  their assertions re-pointed). They are recorded here rather than quietly promoted to product API.
- **type vocabulary** — a record or union whose *name* no consumer writes, because it reaches
  consumers through inference (`Entities.definition` returns `EnemyDefinition` without any caller
  naming the type). These cannot be removed: a retained signature in the same module names them.
- **unreferenced** — declared but named by nothing today. All 6 are in `Collision.fs` and `Vec2.fs`,
  the consumer-owned adaptable helpers, and are deliberate: see below.

`Rogue3.Program` is worth reading twice. It is the executable entry point, and only **2** of its 22
declarations are named by another product module. The rest are the MVU seam (`init`, `update`,
`view`, `mapKey`, `tick`), the two hosts, and the layout-evidence functions — all reached by tests
that drive the production route. An entry point should contract little, and this one now does.

## What is deliberately not pruned

`src/Rogue3/Vec2.fs` (`Rogue3.Geometry`) and `src/Rogue3/Collision.fs` are **consumer-owned,
adaptable** helpers: their compile items are `Exists`-guarded so deleting them keeps the build green,
and their own doc comments say "THIS FILE IS YOURS TO ADAPT". Their signatures declare the whole
helper API rather than only this product's current call sites, because the surface exists to be
reused by whoever adapts them. Narrowing them to today's usage would narrow a contract whose purpose
is to be broader than today's usage.

Their signature compile items carry the **same** `Condition` as their implementations, so deleting
an adaptable helper never leaves an orphan signature behind. The gate asserts this.

## How it is enforced

`tests/Rogue3.Tests/PublicApiSurfaceTests.fs` (test list `rogue3-public-api-surface`) is durable in
the `GovernanceTests` sense — it reads `Rogue3.fsproj`, the signature files and the **built
assembly**, never the model or the view — so a scaffold-model swap leaves it compiling and passing.

1. the configured `src/**/*.fsi` surface is non-empty and every signature names its module;
2. every compiled implementation declares a signature;
3. every signature is the compile item **directly** before its implementation;
4. an `Exists`-guarded implementation carries an equally guarded signature;
5. **the built assembly publishes no module-level binding its signature does not declare** — checked
   by reflection, because the demotion happens in the compiler and source text cannot testify to it;
6. the scans fail on planted violations (the `#111` "guard the guard" discipline).

Assertion 5 is the load-bearing one, and it catches a failure the compiler does **not**. Removing a
single `<Compile Include="Program.fsi" />` item leaves `dotnet build` reporting **zero errors** while
11 bindings silently return to the public API; the gate fails and names all 11.

## Changing the surface

1. Edit the module's `.fsi`. Adding a declaration widens the contract; removing one is a breaking
   change for anything that named it.
2. Run `dotnet build Rogue3.slnx -c Release` — the compiler rejects a signature that disagrees with
   its implementation.
3. Run `dotnet test Rogue3.slnx -c Release` — the surface gate re-measures the built assembly.
4. Update this document's table and the work item's SDD evidence in the same change.

To see what a module *currently* exposes without a signature constraining it — the inventory step
this item used — build with the compiler's own inferred signature and read the result:

```sh
dotnet build src/Rogue3/Rogue3.fsproj -c Release -p:OtherFlags="--allsigs" --no-incremental
```

That writes an inferred `.fsi` beside each `.fs`. It is ground truth for what *exists* and **not** a
proposal for what to contract: emitting it verbatim would make the declared surface a rubber stamp.
Of its 568 non-private declarations, 446 are contracted here and 122 were held out.

## Related

- `.fsgg/constitution.md` — principle III, and the Tier 1 change classification.
- `.fsgg/capabilities.yml` — the `public-api` surface declaration and its `block-on-ship` maturity.
- `work/016-declare-public-api-signature-files/` — the SDD record for this boundary.
- `docs/api-surface/` — unrelated: the vendored `.fsi` baselines of the **FS.GG dependency
  packages**, not this product's own surface.
