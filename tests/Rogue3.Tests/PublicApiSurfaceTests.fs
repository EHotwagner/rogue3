module Rogue3PublicApiSurfaceTests

open System
open System.IO
open System.Reflection
open System.Text.RegularExpressions
open Expecto

// EHotwagner/rogue3#96 — the gate for constitution principle III ("Public Surface Is Declared, Not
// Incidental") and for the `public-api` surface `.fsgg/capabilities.yml` declares at `src/**/*.fsi`
// with `maturity: block-on-ship`.
//
// Before this gate, `src/Rogue3/` held 21 compiled `.fs` modules and ZERO `.fsi` files, so every
// non-private binding was public API by accident: the declared surface was configured, empty, and
// nothing noticed. A surface gate that only counted files would have gone green the moment one
// signature existed, so the load-bearing assertion here is the LAST one — the built assembly's
// public surface never exceeds what the signatures declare. That is checked against the compiled
// artifact by reflection, not against source text, because the demotion this item relies on
// (a binding left out of the `.fsi` stops being public) happens in the compiler, and only the
// assembly can testify that it happened.
//
// Every scan is a pure function over its input so the synthetic "guard the guard" cases below can
// drive it directly — the #111 discipline already established in GovernanceTests.fs: a gate that
// cannot be shown to FAIL on a planted violation is not evidence that the violation is absent.

let private repoRoot =
    Path.GetFullPath(Path.Combine(__SOURCE_DIRECTORY__, "..", ".."))

let private srcDir = Path.Combine(repoRoot, "src", "Rogue3")
let private projectPath = Path.Combine(srcDir, "Rogue3.fsproj")

/// The `Include=` values of the project's `<Compile>` items, in compile order. Anchored on the
/// item form rather than a bare filename scan, for the #111 reason: a bare scan matches filenames
/// inside comments and inside longer names.
let compileOrder (projectText: string) =
    Regex.Matches(projectText, "<Compile\\s+Include=\"([^\"]+)\"")
    |> Seq.map (fun m -> m.Groups.[1].Value)
    |> List.ofSeq

/// The `Condition` attribute of the `<Compile>` item for `file`, or `""` when it carries none.
/// An `Exists`-guarded implementation must carry an equally guarded signature: the project is
/// documented as "durable" (deleting an adaptable helper keeps the build green), and a signature
/// left unguarded would fail the build the moment its implementation is deleted.
let compileCondition (projectText: string) (file: string) =
    let m =
        Regex.Match(projectText, "<Compile\\s+Include=\"" + Regex.Escape file + "\"([^/>]*)/>")
    if not m.Success then ""
    else
        let c = Regex.Match(m.Groups.[1].Value, "Condition=\"([^\"]*)\"")
        if c.Success then c.Groups.[1].Value else ""

/// Implementations whose signature does NOT compile immediately before them. Returns the offending
/// `.fs` names, so a failure names the file rather than only a count.
let signaturesOutOfOrder (order: string list) =
    let arr = List.toArray order
    let hasSig = arr |> Array.filter (fun f -> f.EndsWith ".fsi") |> Array.map (fun f -> f.Substring(0, f.Length - 1)) |> Set.ofArray
    arr
    |> Array.indexed
    |> Array.filter (fun (i, f) ->
        f.EndsWith ".fs"
        && Set.contains f hasSig
        && not (i > 0 && arr.[i - 1] = f + "i"))
    |> Array.map snd
    |> List.ofArray

/// Implementations in the compile order that declare no signature at all.
let implementationsWithoutSignature (order: string list) =
    let sigs = order |> List.filter (fun f -> f.EndsWith ".fsi") |> Set.ofList
    order
    |> List.filter (fun f -> f.EndsWith ".fs" && not (Set.contains (f + "i") sigs))

/// The `val` names a signature file declares at any nesting depth.
let declaredValues (signatureText: string) =
    signatureText.Split '\n'
    |> Array.choose (fun line ->
        let m = Regex.Match(line, "^\\s*val\\s+(?:private\\s+|mutable\\s+|inline\\s+)*\\(?([A-Za-z_][A-Za-z0-9_']*)\\)?")
        if m.Success then Some m.Groups.[1].Value else None)
    |> Set.ofArray

/// Non-private module-level `let` bindings in an implementation file — the surface that WOULD be
/// public if no signature constrained it.
let implementationBindings (implementationText: string) =
    implementationText.Split '\n'
    |> Array.choose (fun line ->
        let m = Regex.Match(line, "^(let|    let)\\s+(?!private\\b)(?:mutable\\s+|rec\\s+|inline\\s+)*\\(?([A-Za-z_][A-Za-z0-9_']*)\\)?")
        if m.Success then Some m.Groups.[2].Value else None)
    |> Set.ofArray

/// The module a signature file declares — `module Rogue3.X`, or the `namespace` + nested `module`
/// form the adaptable helpers (Vec2.fs, Collision.fs) use.
let declaredModule (signatureText: string) =
    let m = Regex.Match(signatureText, "^module\\s+(Rogue3\\.[A-Za-z0-9_.]+)", RegexOptions.Multiline)
    if m.Success then Some m.Groups.[1].Value
    else
        let ns = Regex.Match(signatureText, "^namespace\\s+([A-Za-z0-9_.]+)", RegexOptions.Multiline)
        let nested = Regex.Match(signatureText, "^\\s+module\\s+([A-Za-z0-9_]+)\\s*=", RegexOptions.Multiline)
        if ns.Success && nested.Success then Some(ns.Groups.[1].Value + "." + nested.Groups.[1].Value)
        else None

/// Public static members of a compiled module type, normalised back to F# binding names
/// (`get_x`/`set_x` are the property accessors a module-level value compiles to; names carrying
/// `@` are compiler-generated closures, not surface).
let publicSurfaceOf (assembly: Assembly) (moduleName: string) =
    match assembly.GetType moduleName with
    | null -> None
    | ty ->
        ty.GetMembers(BindingFlags.Public ||| BindingFlags.Static ||| BindingFlags.DeclaredOnly)
        |> Array.choose (fun m ->
            let n = m.Name
            if n.Contains "@" then None
            elif m.MemberType = MemberTypes.NestedType then None
            elif n.StartsWith "get_" || n.StartsWith "set_" then Some(n.Substring 4)
            else Some n)
        |> Set.ofArray
        |> Some

let private projectText () = File.ReadAllText projectPath
let private signatureFiles () = Directory.GetFiles(srcDir, "*.fsi") |> Array.sort
let private productAssembly = typeof<Rogue3.Model.Model>.Assembly

[<Tests>]
let publicApiSurfaceTests =
    testList "rogue3-public-api-surface" [

        // Acceptance 4, first half: the configured surface is non-empty. `.fsgg/capabilities.yml`
        // declares `public-api` at `src/**/*.fsi` and blocks on ship; before #96 that path matched
        // nothing, which is the exact condition this assertion exists to make impossible.
        test "the declared public-api surface is non-empty and every signature names its module" {
            let capabilities =
                File.ReadAllText(Path.Combine(repoRoot, ".fsgg", "capabilities.yml"))

            Expect.stringContains capabilities "src/**/*.fsi" "capabilities.yml still declares the public-api surface at src/**/*.fsi"

            let sigs = signatureFiles ()
            Expect.isGreaterThan sigs.Length 0 "the configured src/**/*.fsi public-api surface matches at least one file"

            for path in sigs do
                let text = File.ReadAllText path
                Expect.isSome (declaredModule text) $"{Path.GetFileName path} declares the module whose surface it constrains"
        }

        // Acceptance 2: every compiled module carries a signature, so no module can quietly go back
        // to publishing its whole implementation.
        test "every compiled implementation declares a signature" {
            let order = compileOrder (projectText ())
            let missing = implementationsWithoutSignature order
            let missingText = String.Join(", ", missing)

            Expect.isEmpty missing $"every <Compile> implementation has a sibling .fsi (missing: {missingText})"

            for file in order do
                Expect.isTrue
                    (File.Exists(Path.Combine(srcDir, file)))
                    $"{file} named in compile order exists on disk"
        }

        // Acceptance 4, second half: signature/source compile order. F# requires the signature to
        // precede its implementation; "immediately before" is the stronger form the issue asks for,
        // and it is what keeps the pair readable as one unit.
        test "every signature compiles immediately before its implementation" {
            let order = compileOrder (projectText ())
            let outOfOrder = signaturesOutOfOrder order
            let outOfOrderText = String.Join(", ", outOfOrder)

            Expect.isEmpty outOfOrder $"each .fsi is the compile item directly before its .fs (offenders: {outOfOrderText})"
        }

        // Guard the guard (#111 discipline): the scans above must be demonstrably capable of
        // failing, or their green tells us nothing.
        test "the compile-order scans fail on a planted violation" {
            let planted =
                String.concat "\n" [
                    "<Project>"
                    "  <ItemGroup>"
                    "    <!-- a comment mentioning Model.fsi and Model.fs must not be scanned -->"
                    "    <Compile Include=\"Model.fsi\" />"
                    "    <Compile Include=\"Render.fs\" />"
                    "    <Compile Include=\"Model.fs\" />"
                    "    <Compile Include=\"View.fs\" />"
                    "  </ItemGroup>"
                    "</Project>"
                ]

            let order = compileOrder planted
            Expect.equal order [ "Model.fsi"; "Render.fs"; "Model.fs"; "View.fs" ] "the anchored scan reads compile items in order and ignores the comment"

            Expect.equal (signaturesOutOfOrder order) [ "Model.fs" ] "a signature separated from its implementation is reported"
            Expect.equal (implementationsWithoutSignature order) [ "Render.fs"; "View.fs" ] "implementations with no signature are reported"

            // And the same scans pass on the well-formed arrangement, so the failure above is
            // about the violation rather than about the scan rejecting everything.
            let wellFormed =
                String.concat "\n" [
                    "<Project><ItemGroup>"
                    "  <Compile Include=\"Model.fsi\" />"
                    "  <Compile Include=\"Model.fs\" />"
                    "</ItemGroup></Project>"
                ]
            Expect.isEmpty (signaturesOutOfOrder (compileOrder wellFormed)) "a signature directly before its implementation is accepted"
            Expect.isEmpty (implementationsWithoutSignature (compileOrder wellFormed)) "a covered implementation is accepted"
        }

        // The project is documented as "durable": deleting an adaptable helper (Vec2.fs,
        // Collision.fs, GameShell.fs) keeps the build green because its compile item is
        // Exists-guarded. The signature must be guarded on the EXISTENCE OF ITS IMPLEMENTATION, not
        // on its own — a signature that guards on `Exists('Vec2.fsi')` survives the deletion of
        // `Vec2.fs`, and F# then rejects the orphan with FS0240 ("The signature file 'P.Vec2' does
        // not have a corresponding implementation file").
        //
        // This assertion shipped its first version inverted: it asserted the signature's condition
        // was the implementation's with the filename REWRITTEN to `.fsi`, which is exactly the
        // self-guarding form that breaks durability. It was green over a false statement of its own
        // declared subject. Measured on a scratch copy of this tree with `Collision.fs` deleted:
        // self-guarding gives FS0039 + FS0240, implementation-guarding gives FS0039 alone. FS0039
        // is the pre-existing consequence of Model.fs genuinely calling `Collision.step`; FS0240 is
        // the orphan this condition governs, and only the correct form removes it.
        test "a guarded implementation's signature is guarded on that implementation" {
            let project = projectText ()

            for file in compileOrder project |> List.filter (fun f -> f.EndsWith ".fs") do
                let implementationCondition = compileCondition project file
                let signatureCondition = compileCondition project (file + "i")

                Expect.equal
                    signatureCondition
                    implementationCondition
                    $"{file}i carries {file}'s condition VERBATIM, so the signature disappears with the implementation it constrains"

                // Stated separately and positively, because equality alone would also be satisfied
                // if both items were rewritten to guard on the signature.
                Expect.isFalse
                    (signatureCondition.Contains(file + "i", StringComparison.Ordinal))
                    $"{file}i is not guarded on its own existence — that guard survives the deletion of {file} and orphans the signature (FS0240)"
        }

        // Guard the guard: the condition scan must reject the self-guarding form this repair fixed,
        // or its green says nothing about the durability promise it claims to enforce.
        test "the condition scan rejects a signature guarded on itself" {
            let selfGuarded =
                String.concat "\n" [
                    "<Project><ItemGroup>"
                    "  <Compile Include=\"Vec2.fsi\" Condition=\"Exists('Vec2.fsi')\" />"
                    "  <Compile Include=\"Vec2.fs\" Condition=\"Exists('Vec2.fs')\" />"
                    "</ItemGroup></Project>"
                ]

            Expect.equal (compileCondition selfGuarded "Vec2.fs") "Exists('Vec2.fs')" "the implementation's condition is read"
            Expect.equal (compileCondition selfGuarded "Vec2.fsi") "Exists('Vec2.fsi')" "the signature's condition is read"
            Expect.notEqual
                (compileCondition selfGuarded "Vec2.fsi")
                (compileCondition selfGuarded "Vec2.fs")
                "the self-guarding form is NOT verbatim inheritance — the shipped assertion once treated it as such"
            Expect.isTrue
                ((compileCondition selfGuarded "Vec2.fsi").Contains("Vec2.fsi", StringComparison.Ordinal))
                "the self-guard is detectable by the same check the live assertion applies"

            let correct =
                String.concat "\n" [
                    "<Project><ItemGroup>"
                    "  <Compile Include=\"Vec2.fsi\" Condition=\"Exists('Vec2.fs')\" />"
                    "  <Compile Include=\"Vec2.fs\" Condition=\"Exists('Vec2.fs')\" />"
                    "</ItemGroup></Project>"
                ]
            Expect.equal
                (compileCondition correct "Vec2.fsi")
                (compileCondition correct "Vec2.fs")
                "the corrected form is accepted"
            Expect.isFalse
                ((compileCondition correct "Vec2.fsi").Contains("Vec2.fsi", StringComparison.Ordinal))
                "the corrected form does not name the signature in its own guard"
        }

        // Acceptance 3 and the issue's verification clause, checked against the COMPILED artifact:
        // "a planted public implementation binding absent from its signature does not enter the
        // declared API". Every module is its own live subject — the pruning this item performed left
        // real non-private `let` bindings out of the signatures, and each one must have been demoted.
        test "a public implementation binding absent from its signature does not enter the declared API" {
            let mutable subjects = 0

            for path in signatureFiles () do
                let signatureText = File.ReadAllText path
                let implementationPath = path.Substring(0, path.Length - 1)
                let declared = declaredValues signatureText

                match declaredModule signatureText with
                | None -> failtestf "%s declares no module" (Path.GetFileName path)
                | Some moduleName ->
                    match publicSurfaceOf productAssembly moduleName with
                    | None -> failtestf "%s is not a type in the built product assembly" moduleName
                    | Some publicSurface ->
                        // The load-bearing invariant: nothing is public that the signature did not declare.
                        let undeclared = Set.difference publicSurface declared
                        let undeclaredText = String.Join(", ", undeclared)
                        let signatureName = Path.GetFileName path
                        Expect.isEmpty
                            undeclared
                            $"{moduleName}: the built assembly publishes only what {signatureName} declares (undeclared public: {undeclaredText})"

                        // And the gate has a live subject: bindings written as public `let` in the
                        // implementation, left out of the signature, really did stop being public.
                        let omitted =
                            Set.difference (implementationBindings (File.ReadAllText implementationPath)) declared
                        subjects <- subjects + Set.count (Set.intersect omitted (Set.difference omitted publicSurface))

                        for binding in Set.intersect omitted publicSurface do
                            failtestf "%s.%s is a public `let` absent from the signature yet still public in the assembly" moduleName binding

            Expect.isGreaterThan
                subjects 0
                "at least one non-private implementation binding is held out of the declared API — without a live subject this assertion could pass on an empty set"
        }

        // The converse direction, so the pair of checks pins the surface from both sides: the gate
        // must reject an assembly that publishes more than its signature declares.
        test "the surface comparison fails on a planted undeclared public binding" {
            let declared = declaredValues "module Rogue3.Example\n\nval encode: value: obj -> string\nval digest: value: obj -> string\n"
            Expect.equal declared (Set.ofList [ "encode"; "digest" ]) "declared values are read from the signature text"

            let plantedPublic = Set.ofList [ "encode"; "digest"; "appendEscaped" ]
            Expect.isNonEmpty (Set.difference plantedPublic declared) "an assembly publishing a binding the signature omits is reported as undeclared"

            Expect.isEmpty (Set.difference declared declared) "a surface equal to its declaration is accepted"

            // The implementation scan must see public `let` bindings and skip `let private` ones,
            // or the "live subject" count above could be satisfied by noise.
            let bindings =
                implementationBindings "let encode (v: obj) = \"\"\nlet private appendEscaped b v = ()\nlet digest v = \"\"\n"
            Expect.equal bindings (Set.ofList [ "encode"; "digest" ]) "private bindings are not counted as would-be public surface"
        }
    ]
