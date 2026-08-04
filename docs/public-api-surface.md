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

446 declarations across 21 modules, classified by who actually names them.

**A reference here means a reference in CODE.** The scan strips `//`, `///` and nested `(* … *)`
comments (preserving string literals) before looking, because `Rogue3.fsproj`'s own comment records
what happens otherwise: *"the `Visibility.Segment` and `Visibility.VisibilityPolygon` mentions in
Vec2.fs named types no code ever constructed, and reading them as call sites is what made a dead
module look adopted. Those Vec2.fs comments were corrected with this change; **count code
references, not grep hits**."* (#19, #28.) The first version of this table counted grep hits and was
wrong in exactly that way — see the note under the table.

| module | declared | named by other product modules | named only by tests/scripts | type vocabulary | not named outside its module |
|---|---:|---:|---:|---:|---:|
| `Rogue3.AudioCueIds` | 28 | 27 | 1 | 0 | 0 |
| `Rogue3.AudioCues` | 5 | 5 | 0 | 0 | 0 |
| `Rogue3.AudioSynthesis` | 2 | 0 | 2 | 0 | 0 |
| `Rogue3.Collision` | 12 | 2 | 0 | 4 | 6 |
| `Rogue3.Determinism` | 3 | 3 | 0 | 0 | 0 |
| `Rogue3.Entities` | 59 | 44 | 8 | 7 | 0 |
| `Rogue3.EvidenceCommands` | 20 | 12 | 6 | 1 | 1 |
| `Rogue3.FloorGeneration` | 21 | 14 | 1 | 6 | 0 |
| `Rogue3.GameShell` | 23 | 13 | 6 | 4 | 0 |
| `Rogue3.GameplayVisualInventory` | 9 | 2 | 4 | 3 | 0 |
| `Rogue3.LayoutEvidence` | 9 | 9 | 0 | 0 | 0 |
| `Rogue3.M7Ui` | 5 | 5 | 0 | 0 | 0 |
| `Rogue3.Model` | 142 | 64 | 52 | 20 | 6 |
| `Rogue3.PerformanceEvidence` | 17 | 3 | 8 | 6 | 0 |
| `Rogue3.ProfileStore` | 7 | 2 | 3 | 2 | 0 |
| `Rogue3.Program` | 22 | 0 | 16 | 2 | 4 |
| `Rogue3.Render` | 30 | 9 | 14 | 4 | 3 |
| `Rogue3.Replay` | 6 | 0 | 5 | 1 | 0 |
| `Rogue3.Geometry` (`Vec2.fs`) | 16 | 12 | 0 | 1 | 3 |
| `Rogue3.View` | 1 | 1 | 0 | 0 | 0 |
| `Rogue3.WindowOptions` | 9 | 9 | 0 | 0 | 0 |
| **total** | **446** | **236** | **126** | **61** | **23** |

> **Correction (round 1 of independent review).** The first version of this table scanned raw source,
> so a module member mentioned in a comment counted as a call site. It credited `Rogue3.Program` with
> 2 product consumers; the true number is **0**, and it cannot be otherwise: `Program.fsi` and
> `Program.fs` are the last two compile items and F# has no forward references, so no module under
> `src/Rogue3/` is able to name anything in `Rogue3.Program`. Re-deriving the whole column with
> comments stripped moved five figures, not one: product references 263 → 236, type vocabulary
> 51 → 61, and the last column 6 → 23. Every row sums to its `declared` count.

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
- **not named outside its module** — no *other* file names it. That is the predicate the scan
  actually measures: it compares each declaration against every consumer file **except the one that
  defines it**. Most of these have live call sites *inside* their own module; a few have none at all.
  Enumerated individually below, with the intra-module call-site count, because the difference
  between "used, but only privately" and "used by nothing" is the whole decision this column informs
  — and a bare count cannot carry it.

`Rogue3.Program` is worth reading twice. It is the executable entry point, and **none** of its 22
declarations is named by another product module — nor can one be, since it compiles last. Its
contract exists for two other audiences: the .NET runtime (`main`, the `[<EntryPoint>]`) and the test
suite, which drives the production route through `update` (13 call sites), `initialModel` (26),
`interactiveHost` (9), `generatedHost` (8) and the layout-evidence functions. An entry point should
contract little, and this one now contracts 22 declarations instead of the 36 it exposed before.

### The 23 declarations no other file names, in full

The right-hand column is the **intra-module** call-site count: references inside the declaring file
itself, with comments stripped, string literals blanked, and the declaration's own line excluded.

| module | declaration | intra-module call sites | what it is |
|---|---|---:|---|
| `Rogue3.Collision` | `contact` | 1 | adaptable-helper API; used inside `Collision.fs` |
| `Rogue3.Collision` | `sweptContact` | 1 | adaptable-helper API; used inside `Collision.fs` |
| `Rogue3.Collision` | `collide` | 1 | adaptable-helper API; used inside `Collision.fs` |
| `Rogue3.Collision` | `resolve` | 1 | adaptable-helper API; used inside `Collision.fs` |
| `Rogue3.Collision` | `slideCircle` | 1 | adaptable-helper API; used inside `Collision.fs` |
| `Rogue3.Collision` | `step` | 0 | adaptable-helper API, declared for the consumer who adapts it |
| `Rogue3.Geometry` | `toPoint`, `toRect`, `ofSimRectCenter` | 0 | adaptable-helper API, declared for the consumer who adapts it |
| `Rogue3.Program` | `main` | 0 | the `[<EntryPoint>]` — the .NET runtime calls it, no source does |
| `Rogue3.Program` | `init`, `mapKey` | 0 | **re-export aliases** (`let init = Rogue3.Model.init`); named by no code |
| `Rogue3.Program` | `parseWindowBehavior` | 1 | used inside `Program.fs` |
| `Rogue3.Model` | `bombRadius` | 4 | used inside `Model.fs` |
| `Rogue3.Model` | `movePaddle` | 2 | used inside `Model.fs` — including the `update` arm for `MovePaddle` |
| `Rogue3.Model` | `playerRoomIntentsIn` | 2 | used inside `Model.fs` |
| `Rogue3.Model` | `placementAccepts` | 1 | used inside `Model.fs` |
| `Rogue3.Model` | `collectRoomReward` | 1 | used inside `Model.fs` |
| `Rogue3.Model` | `shotSpeed` | 0 | **named by no code anywhere** — the only genuinely dead declaration |
| `Rogue3.Render` | `renderedElementsIn` | 2 | used inside `Render.fs` |
| `Rogue3.Render` | `roomWallsScene` | 1 | used inside `Render.fs` |
| `Rogue3.Render` | `shopSlotReadyScene` | 1 | used inside `Render.fs` |
| `Rogue3.EvidenceCommands` | `retireWithdrawnDisplayMode` | 1 | used inside `EvidenceCommands.fs` |

So of the 23: **fifteen carry live intra-module call sites**; seven have no local use either — the
four adaptable-helper entries declared for a consumer who does not exist yet, `main`, which the
runtime calls, and the two re-export aliases; and **exactly one — `Model.shotSpeed` — is named by
nothing at all**.

This corrects a claim the previous revision made per-declaration: it labelled ten of these "no code
reference", of which nine in fact have live intra-module call sites. Round 1 of review fixed a scan
that counted comment mentions as call sites (over-stating the *product* column); this revision fixes
the opposite error in the same family — discarding intra-module call sites and publishing the result
as "named by no code anywhere" (over-stating *this* column). The numbers themselves were never
affected: the scan always measured "not named outside its own module", every row still sums, and the
446 total and 236/126/61/23 split are unchanged. What was wrong was what the column was **called**.

None of these is pruned here. They are the trustworthy narrowing list precisely *because* the column
now says what it measured: an intra-module helper is a candidate for `private`, a re-export alias is a
candidate for deletion, and `shotSpeed` is dead code. Acting on that list narrows the declared
contract, which is a Tier 1 change belonging in its own reviewed step rather than in a documentation
repair.

## What is deliberately not pruned

`src/Rogue3/Vec2.fs` (`Rogue3.Geometry`) and `src/Rogue3/Collision.fs` are **consumer-owned,
adaptable** helpers: their compile items are `Exists`-guarded so deleting them keeps the build green,
and their own doc comments say "THIS FILE IS YOURS TO ADAPT". Their signatures declare the whole
helper API rather than only this product's current call sites, because the surface exists to be
reused by whoever adapts them. Narrowing them to today's usage would narrow a contract whose purpose
is to be broader than today's usage.

Their signature compile items carry their **implementation's** `Condition`, verbatim:

```xml
<Compile Include="Collision.fsi" Condition="Exists('Collision.fs')" />
<Compile Include="Collision.fs"  Condition="Exists('Collision.fs')" />
```

Both guards name the **`.fs`**. That is the whole point, and it is easy to get backwards: a signature
guarded on its own existence (`Exists('Collision.fsi')`) survives the deletion of `Collision.fs`, and
F# rejects the orphan with `FS0240: The signature file 'Rogue3.Collision' does not have a
corresponding implementation file`. This wiring shipped backwards in the first draft of #96 and was
corrected in review.

Measured on a scratch copy of this tree with `src/Rogue3/Collision.fs` deleted:

| wiring | `dotnet build` errors |
|---|---|
| signature guarded on itself | `FS0039` **and `FS0240`** |
| signature guarded on its implementation | `FS0039` only |

`FS0039` ("Collision is not defined") is pre-existing and unrelated to this wiring — `Model.fs`
genuinely calls `Collision.clampCircleInside` and `Collision.sweepCircle`, so this helper is adopted
and is not actually deletable today. `FS0240` is the orphan-signature failure this condition governs,
and only the correct form removes it. The gate asserts the corrected form and rejects the backwards
one.

## How it is enforced

`tests/Rogue3.Tests/PublicApiSurfaceTests.fs` (test list `rogue3-public-api-surface`) is durable in
the `GovernanceTests` sense — it reads `Rogue3.fsproj`, the signature files and the **built
assembly**, never the model or the view — so a scaffold-model swap leaves it compiling and passing.

1. the configured `src/**/*.fsi` surface is non-empty and every signature names its module;
2. every compiled implementation declares a signature;
3. every signature is the compile item **directly** before its implementation;
4. a signature carries its implementation's `Condition` verbatim, and never guards on itself;
5. **the built assembly publishes no module-level binding its signature does not declare** — checked
   by reflection, because the demotion happens in the compiler and source text cannot testify to it;
6. the scans fail on planted violations (the `#111` "guard the guard" discipline), including the
   backwards `Condition` that assertion 4 itself once asserted.

Assertion 5 is the load-bearing one, and it catches a failure the compiler does **not**. Removing a
single `<Compile Include="Program.fsi" />` item leaves `dotnet build` reporting **zero errors** while
11 bindings silently return to the public API; the gate fails and names all 11.

### What this gate does not catch

Adding a **new** public `let` or `type` to a `.fs` that its `.fsi` does not declare is invisible here:
the compiler simply demotes it, the build stays green, and the assembly never publishes it — so there
is nothing for assertion 5 to find. That is F# semantics working as intended, not a hole in the gate.
The consequence worth knowing is that a signature file also hides such a binding from *later modules
in the same assembly*, so a branch that adds one and uses it elsewhere fails its own build with
`FS0039` rather than merging silently.

## Changing the surface

1. Edit the module's `.fsi`. Adding a declaration widens the contract; removing one is a breaking
   change for anything that named it.
2. Run `dotnet build Rogue3.slnx -c Release` — the compiler rejects a signature that disagrees with
   its implementation.
3. Run `dotnet test Rogue3.slnx -c Release` — the surface gate re-measures the built assembly.
4. Update this document's table and the work item's SDD evidence in the same change.

## Re-measuring the unconstrained surface

To see what the modules *would* expose with no signature constraining them — the inventory step this
item used — ask the compiler for its own inferred signature.

**Do this in a scratch copy, and strip the signatures and their compile items first.** `--allsigs`
writes an inferred `.fsi` **beside each `.fs`, overwriting whatever is there**. Run against the tree
as it stands, it destroys all 21 committed signatures (`21 files changed, 1530 insertions`, the
declared surface inflating 446 → 825) and the next build fails *inside* the regenerated signature
files — `AudioSynthesis.fsi(100,35): error FS0001/FS0267/FS0837`, because the generator emits
`[<Literal>] val private TrackSeconds: float = 4` with an `int` literal for a `float`. The signatures
also cannot be regenerated while they are still in compile order, since each would be checked against
itself.

```sh
# from a scratch copy of the repository -- NEVER the working tree
rm -f src/Rogue3/*.fsi
python3 - <<'PY'
import re
p = "src/Rogue3/Rogue3.fsproj"
s = open(p).read()
open(p, "w").write(re.sub(r'^[ \t]*<Compile Include="[^"]+\.fsi"[^/>]*/>[ \t]*\n', "", s, flags=re.M))
PY
dotnet build src/Rogue3/Rogue3.fsproj -c Release -p:OtherFlags="--allsigs" --no-incremental
```

That yields 21 inferred signatures declaring **568 non-private** and 257 private declarations. It is
ground truth for what *exists* and **not** a proposal for what to contract: emitting it verbatim
would make the declared surface a rubber stamp. Of its 568, 446 are contracted here and 122 were held
out.

## Related

- `.fsgg/constitution.md` — principle III, and the Tier 1 change classification.
- `.fsgg/capabilities.yml` — the `public-api` surface declaration and its `block-on-ship` maturity.
- `work/016-declare-public-api-signature-files/` — the SDD record for this boundary.
- `docs/api-surface/` — unrelated: the vendored `.fsi` baselines of the **FS.GG dependency
  packages**, not this product's own surface.
