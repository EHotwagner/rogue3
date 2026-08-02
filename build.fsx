open System
open System.Diagnostics
open System.IO
open System.Reflection
open System.Text.RegularExpressions

// Feature 043 (FR-013): generated projects run the EvidenceGraph / EvidenceAudit gates
// IN-PROCESS through the published FS.GG.UI.Build engine. No Python or shell audit scripts
// are copied into or executed by a generated scaffold; the only retained external process is
// `dotnet test`.
//
// Feature 064 (FR-004 / research R1): there is NO versioned engine reference directive here.
// F# script reference arguments must be string literals, so the engine version cannot be
// interpolated. Instead this script reads the SINGLE source of version truth —
// `<FsGgUiVersion>` in Directory.Packages.props — at runtime, loads the matching, already
// `dotnet restore`-d engine assembly from the NuGet global-packages folder, and invokes the
// generated-evidence façade by reflection (so no typed `open` pins a version). The result:
// exactly ONE literal FS.GG.UI version value in the whole generated project, and a consumer
// upgrade is a single edit to <FsGgUiVersion> + `dotnet restore` — libraries AND the build
// engine move together. See docs/UPGRADING.md.

let path parts = Path.Combine(Array.ofList parts)

let targetFromArgs args =
    let rec loop values =
        match values with
        | "-t" :: target :: _
        | "--target" :: target :: _
        | "target" :: target :: _ -> target
        | _ :: rest -> loop rest
        | [] -> "Dev"

    loop args

/// #57: set on the CHILD run of `SelfTest`'s self-probe only. Declared here, far above the target,
/// because `writeLog` below has to know about it.
let private selfTestInjectVar = "FSGG_SELFTEST_INJECT_FAILURE"

/// Set on every spawned child INDEPENDENTLY of the inject flag. Two separate variables look
/// redundant and are not: deleting the single line that sets the inject flag would otherwise make
/// the child spawn its own child, without bound — measured at 15 live `dotnet fsi` processes and
/// climbing before this existed. A depth marker the same edit does not remove turns that runaway
/// into an immediate, cheap red: the child declines to probe, so it passes, and the parent's
/// exit-code channel reports that a run which must fail did not.
let private selfTestDepthVar = "FSGG_SELFTEST_DEPTH"

let private selfTestInjecting () =
    match Environment.GetEnvironmentVariable selfTestInjectVar with
    | null
    | "" -> false
    | _ -> true

/// True for ANY run this script spawned, however it was spawned. Only a top-level run probes.
let private selfTestIsChild () =
    selfTestInjecting ()
    || match Environment.GetEnvironmentVariable selfTestDepthVar with
       | null
       | "" -> false
       | _ -> true

let writeLog target =
    // #57: a self-probe CHILD must leave no completion marker in the parent's tree. The child is a
    // run that is REQUIRED to fail, and a mutation that makes it pass anyway would otherwise leave
    // `readiness/logs/SelfTest.txt` saying "completed" while the parent exits non-zero — a forged
    // green marker, written by the very run whose dishonesty the parent is about to report.
    if selfTestInjecting () then
        printfn "%s completed for generated rogue3 (self-probe child; no marker written)" target
    else
        Directory.CreateDirectory("readiness/logs") |> ignore
        File.WriteAllText(Path.Combine("readiness", "logs", target + ".txt"), $"{target} completed for generated rogue3.{Environment.NewLine}")
        printfn "%s completed for generated rogue3" target

// ADR-0056 §Decision.2: the fail-closed half of the sdd-lane guard. The `sdd` lane (the default)
// emits the rogue3 only and expects an external SDD lifecycle owner (fsgg-sdd) to re-supply the
// lifecycle; the one file that distinguishes the byte-identical sdd/none trees —
// the rogue3-root lifecycle-scaffolding-pending.md — is present only when `--lifecycle sdd` was
// chosen. (It formerly lived under `readiness/`, but that is an SDD-owned tree the provider may not
// write under the orchestrated fsgg-sdd flow — see #954.) While it is present, the readiness/doctor
// gate stays RED (this raises, which fails Verify): a lifecycle-less rogue3 cannot pass the
// merge-gate audit. `none` (no sentinel) and `spec-kit` (no sentinel) never trip it. The stock
// `dotnet build`/Directory.Build.props path only WARNS ("sdd warns"); the fail-closed verdict lives
// here so it does not break the smoke build/test lane.
let private lifecycleGuardSentinel = "lifecycle-scaffolding-pending.md"

let private assertLifecycleSupplied () =
    // The message avoids the literal `rogue3`/`rogue3` tokens (this file is not copyOnly, so the
    // template symbols rewrite them to the scaffolded name); `tree` keeps it name-stable.
    if File.Exists lifecycleGuardSentinel then
        failwithf
            "readiness/doctor: lifecycle scaffolding not yet supplied (scaffolded with --lifecycle sdd, the default) — failing closed (ADR-0056). Run `fsgg-sdd` to re-supply it (clears %s), or re-scaffold with `--lifecycle none` if a lifecycle-less tree is deliberate."
            lifecycleGuardSentinel

let tryWriteTextLog (filePath: string) (content: string) =
    try
        let directory = Path.GetDirectoryName filePath

        if not (String.IsNullOrWhiteSpace directory) then
            Directory.CreateDirectory directory |> ignore

        File.WriteAllText(filePath, content)
        None
    with ex ->
        Some $"unreadable readiness log: {filePath}; diagnostics={ex.Message}"

// ----- engine binding: resolve <FsGgUiVersion> at runtime (FR-004, R1) -----

let private fsSkiaUiVersion () =
    let propsPath = path [ Directory.GetCurrentDirectory(); "Directory.Packages.props" ]

    if not (File.Exists propsPath) then
        failwithf "Cannot resolve the FS.GG.UI engine version: %s is missing." propsPath

    let m = Regex.Match(File.ReadAllText propsPath, "<FsGgUiVersion>([^<]+)</FsGgUiVersion>")

    if m.Success then
        m.Groups.[1].Value.Trim()
    else
        failwithf "Cannot resolve <FsGgUiVersion> from %s; it is the single source of FS.GG.UI version truth." propsPath

let private nugetPackagesRoot () =
    match Environment.GetEnvironmentVariable "NUGET_PACKAGES" with
    | null -> path [ Environment.GetFolderPath Environment.SpecialFolder.UserProfile; ".nuget"; "packages" ]
    | "" -> path [ Environment.GetFolderPath Environment.SpecialFolder.UserProfile; ".nuget"; "packages" ]
    | dir -> dir

// Probe the NuGet global-packages cache for an assembly by simple name, preferring net10.0.
// The engine's transitive dependency closure (Fake.Core, YamlDotNet, FSharp.SystemTextJson,
// DiffPlex, FS.GG.UI.SkillSupport, …) is restored into this cache; Assembly.LoadFrom of the
// engine alone does not bring them, so we resolve each on demand at invoke time.
let private probeCachedAssembly (nugetPackages: string) (simpleName: string) : string option =
    let packageDir = path [ nugetPackages; simpleName.ToLowerInvariant() ]

    if not (Directory.Exists packageDir) then
        None
    else
        Directory.GetDirectories packageDir
        |> Array.collect (fun versionDir ->
            Directory.GetFiles(versionDir, simpleName + ".dll", SearchOption.AllDirectories)
            |> Array.filter (fun f -> f.Replace('\\', '/').Contains "/lib/"))
        |> Array.sortByDescending (fun f -> if f.Replace('\\', '/').Contains "/net10.0/" then 1 else 0)
        |> Array.tryHead

// Restore the pinned engine (+ its dependency closure) into the global cache when absent, using
// a throwaway project under TEMP so default/user NuGet config resolution applies — that has the
// local feed for in-repo framework development and nuget.org for a published consumer. The exact
// <FsGgUiVersion> is restored (not "latest"), so the engine and libraries stay in lock-step.
let private restoreEngine (version: string) =
    let tmp = path [ Path.GetTempPath(); "fsskia-engine-restore-" + version ]
    Directory.CreateDirectory tmp |> ignore
    let proj = path [ tmp; "engine-restore.fsproj" ]

    File.WriteAllText(
        proj,
        "<Project Sdk=\"Microsoft.NET.Sdk\">\n"
        + "  <PropertyGroup>\n    <TargetFramework>net10.0</TargetFramework>\n    <ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally>\n  </PropertyGroup>\n"
        + sprintf "  <ItemGroup>\n    <PackageReference Include=\"FS.GG.UI.Build\" Version=\"%s\" />\n  </ItemGroup>\n" version
        + "</Project>\n")

    let psi = ProcessStartInfo("dotnet", sprintf "restore \"%s\"" proj)
    psi.RedirectStandardOutput <- true
    psi.RedirectStandardError <- true
    psi.UseShellExecute <- false
    psi.WorkingDirectory <- tmp

    match (try Process.Start psi |> Option.ofObj with _ -> None) with
    | None -> ()
    | Some p ->
        let outTask = p.StandardOutput.ReadToEndAsync()
        let errTask = p.StandardError.ReadToEndAsync()
        p.WaitForExit()
        outTask.Result |> ignore
        errTask.Result |> ignore

let private engineAssembly =
    lazy
        (let version = fsSkiaUiVersion ()
         let nugetPackages = nugetPackagesRoot ()
         // NuGet lowercases package-id folders in the global-packages cache.
         let dll = path [ nugetPackages; "fs.gg.ui.build"; version; "lib"; "net10.0"; "FS.GG.UI.Build.dll" ]

         if not (File.Exists dll) then
             restoreEngine version

         if not (File.Exists dll) then
             failwithf
                 "FS.GG.UI.Build %s could not be restored to %s. Ensure the version exists on a configured feed (`dotnet restore`)."
                 version
                 dll

         // R1: idiomatic simplicity yields to the #r-literal constraint here — bind the
         // property-resolved engine assembly at runtime so the engine moves with the single
         // version value, and resolve its dependency closure from the same global cache.
         AppDomain.CurrentDomain.add_AssemblyResolve (
             ResolveEventHandler(fun _ args ->
                 let simple = System.Reflection.AssemblyName(args.Name).Name

                 match probeCachedAssembly nugetPackages simple with
                 | Some path -> Assembly.LoadFrom path
                 | None -> null))

         Assembly.LoadFrom dll)

let private runGeneratedEvidence (target: string) : int =
    let assembly = engineAssembly.Value
    let runnerType = assembly.GetType("FS.GG.UI.Build.Evidence.GeneratedRunner")

    if isNull runnerType then
        failwith "FS.GG.UI.Build.Evidence.GeneratedRunner not found in the resolved engine assembly."

    let runMethod = runnerType.GetMethod("run")

    if isNull runMethod then
        failwith "FS.GG.UI.Build.Evidence.GeneratedRunner.run not found in the resolved engine assembly."

    runMethod.Invoke(null, [| box target; box (Directory.GetCurrentDirectory()) |]) :?> int

// ---------------------------------------------------------------------------
// #26, then #56: an evidence roll-up may only be PUBLISHED by a run that sensed at
// least everything the previously published one already records.
//
// The EvidenceGraph emitter enumerates whatever is on disk under `readiness/`, and
// part of that tree is regenerable output no clean checkout carries: the fsgg-sdd
// products excluded by `.gitignore:8`, which only the checkout that ran the lifecycle
// holds, and `readiness/logs/*.txt`, which this very run writes. So the list the
// emitter publishes is a property of the CHECKOUT, not of the repository. The emitter
// ships in the FS.GG.UI.Build engine package, so this repository cannot change its
// enumeration logic — it can only change the input tree, the emission order, or what
// it does with the result.
//
// #26 measured the damage while `readiness/evidence-graph.md` was TRACKED: a full
// Verify on a clean worktree sensed less than the committed graph recorded, rewrote
// it with the smaller number, and exited 0 — so a worker following the documented
// instructions committed an artifact asserting that evidence had disappeared. #26's
// fix, this rule, stopped that: a run that sensed a SUPERSET publishes normally, a run
// that sensed LESS restores the previous bytes exactly and names every input it could
// not see.
//
// #56 took the root cause the same rule left standing. Refusing to publish left the
// graph FROZEN — at `715bef9` it recorded 102 sensed files, 12 of which exist in no
// clean checkout, so every ordinary run refused forever, while the same frozen graph
// OMITTED four tracked files that do exist. A tracked roll-up that no checkout can
// reproduce is the defect; a rule that keeps it byte-stable only made the failure
// quieter. `readiness/evidence-graph.md` is therefore NO LONGER TRACKED (see
// `.gitignore` and rogue3#56), along with the two performance artifacts this rule was
// deliberately never extended to, which move because they record MEASUREMENTS rather
// than because an input was missing. Their field lists are NOT the same, and saying so
// once cost the previous cycle a critic finding: `readiness/performance-evidence.json`
// carries `p50Ms`/`p95Ms`/`p99Ms`, `allocatedBytes`, the input/receipt/artifact digests
// over them and a `compositionAuthority` MVID that changes whenever the assembly is
// rebuilt (see `src/Rogue3/PerformanceEvidence.fs`), while
// `readiness/m7-ui-performance.json` carries measured `p95Ms`/`p99Ms` and nothing else —
// no `p50Ms`, no `allocatedBytes`, no `compositionAuthority` anywhere in the document.
// Measured over one gate run at `715bef9`: 39 differing leaves against `origin/main` for
// the first, 8 for the second. `readiness/performance-critic-request.json` joins them
// because it digests the first.
//
// The rule is KEPT, with a narrower and honestly smaller job. It can no longer falsify
// a committed artifact, because there is none. What it still does:
//
//   * On a checkout that already has a graph ON DISK from an earlier run, it refuses
//     to let a later, narrower run silently shrink it. Nothing in git protects that
//     file any more — `git checkout` cannot bring it back — so the in-checkout copy is
//     now the ONLY copy, and losing entries from it is unrecoverable rather than merely
//     wrong.
//   * It names every input the run could not sense, which is the diagnostic #26 was
//     really about and is the only place that list is printed.
//   * On a genuinely fresh checkout there is no previous graph, so the first emission
//     publishes unconditionally, with no committed number to be measured against.
//
// What #56 did NOT fix, stated here because the obvious summary of it is wrong. The graph
// is still not reproducible from a checkout: it is a function of the checkout AND of what
// has run in it. `Verify` emits the graph after `TemplateDrift`/`GeneratedGuidanceCheck`
// have written their logs but before `Test`/`PerformanceIntent`/`PerformanceEvidence` and
// `writeLog "Verify"` write theirs, so run n absorbs run n−1's outputs and a standalone
// `-t EvidenceGraph` senses a different set again — measured on one checkout of one
// commit: first Verify 94 sensed files, second Verify 101. #26 measured the same shape
// from the other side (96 under Verify, 94 standalone). The ratchet is intact; what
// changed is its audience, from the repository to one working copy, and that is enough to
// satisfy #56's acceptance (no run produces a committable diff) without satisfying the
// word "reproducible". Emission ORDER is the issue's root cause 2 and belongs to the
// engine emitter; it is untaken.
//
// The bounded route out of a refusal is therefore no longer `FSGG_EVIDENCE_GRAPH_PUBLISH=1`
// alone: deleting `readiness/evidence-graph.md` and re-running gives a graph derived from
// this checkout at that point in the run. The override is kept for the case where a
// lifecycle checkout wants to publish a smaller graph deliberately without deleting first.
//
// The trade this makes, named rather than implied: under #26 a stale graph was visible as
// a tracked-file diff a reviewer could see. Now the only signal is stderr inside a run
// that exits 0, and `git status` is clean by construction. A partial clean of the ignored
// tree (`rm -rf readiness/logs`) leaves a checkout whose every subsequent Verify refuses,
// quietly and greenly, until someone deletes the graph.
//
// `readiness/evidence-audit.md` is still TRACKED and still unguarded — deliberately,
// and it is the control case that makes the diagnosis above falsifiable. It records a
// verdict and a node count with no per-file enumeration, so nothing in it varies with
// which readiness outputs the checkout happens to hold; it comes back byte-identical
// from every run. Tracked roll-ups are not the problem — roll-ups that enumerate
// irreproducible things are. Note that on a refusal `EvidenceAudit` then reads the
// RESTORED graph, which is the previous complete emission rather than this run's
// partial one.
//
// A refusal does not fail the gate. Failing instead would make Verify permanently red
// in every worktree — which is how a gate gets ignored.
// ---------------------------------------------------------------------------

let private evidenceGraphPath = Path.Combine("readiness", "evidence-graph.md")

let private evidenceGraphPublishVariable = "FSGG_EVIDENCE_GRAPH_PUBLISH"

let private sensedSectionHeading = "## Sensed readiness files"

type private EvidenceGraphPublication =
    /// This run sensed everything the published graph records; the fresh graph stands.
    | Published
    /// This run sensed less, but publishing anyway was explicitly requested.
    | PublishedSmaller of dropped: string list
    /// This run sensed less; the previously published bytes were restored.
    | Restored of dropped: string list
    /// The PUBLISHED graph has no sensed-file section, so there is nothing to compare
    /// against and the rule abstains rather than guessing. Kept distinct from
    /// `Published`: silence here would be the same defect one level up.
    | Unevaluatable of reason: string

/// The `- \`readiness/…\`` bullets of the sensed-file section, and ONLY that section
/// — `None` when the section is absent. Scoping matters: the counters are unquoted
/// bullets, the evidence-node rows are pipe-delimited table cells, and a future
/// emitter that lists what it could NOT sense under some other heading must not have
/// those bullets counted as sensed.
let private sensedReadinessFiles (markdown: string) : Set<string> option =
    let lines = markdown.Split('\n') |> Array.map (fun line -> line.Trim())

    lines
    |> Array.tryFindIndex (fun line -> String.Equals(line, sensedSectionHeading, StringComparison.OrdinalIgnoreCase))
    |> Option.map (fun start ->
        lines
        |> Array.skip (start + 1)
        |> Array.takeWhile (fun line -> not (line.StartsWith "## "))
        |> Array.choose (fun line ->
            if line.StartsWith "- `" && line.EndsWith "`" && line.Length > 4 then
                Some(line.Substring(3, line.Length - 4))
            else
                None)
        |> Set.ofArray)

let private evidenceGraphPublishRequested () =
    match Environment.GetEnvironmentVariable evidenceGraphPublishVariable with
    | null -> false
    | value ->
        match value.Trim().ToLowerInvariant() with
        | ""
        | "0"
        | "false"
        | "no" -> false
        | _ -> true

/// Applies the superset rule to the graph now on disk at `graphPath`.
/// `previouslyPublished` is a copy of that file as it stood before this emission,
/// and a refusal restores it with a file copy — so the encoding, any byte-order
/// mark and the original line endings survive untouched, which re-serializing the
/// text through a writer would not guarantee. (A copy is also what keeps this
/// script free of the binary writers the `Verify redirected output is clean text`
/// governance scan forbids.)
///
/// The comparison is over SETS, not counts: an emission that drops two inputs and
/// gains two others has the same cardinality and must still be refused.
let private applyEvidenceGraphPublicationRule (graphPath: string) (previouslyPublished: string) (publishSmaller: bool) =
    // An emission that exited 0 but left no graph — or left one with no sensed
    // section — has sensed NOTHING, which is the rule's own worst case rather than
    // an IO error to rethrow at the caller. Restoring then also puts the file back.
    let emitted =
        if File.Exists graphPath then
            sensedReadinessFiles (File.ReadAllText graphPath) |> Option.defaultValue Set.empty
        else
            Set.empty

    match sensedReadinessFiles (File.ReadAllText previouslyPublished) with
    | None -> Unevaluatable $"the published graph has no `{sensedSectionHeading}` section to compare against"
    | Some published ->
        let dropped = Set.difference published emitted |> Set.toList

        if List.isEmpty dropped then
            Published
        elif publishSmaller then
            PublishedSmaller dropped
        else
            File.Copy(previouslyPublished, graphPath, true)
            Restored dropped

/// The operator-facing account of a publication decision, as lines. Returned rather
/// than printed so the wording is testable — an unasserted diagnostic is how the
/// `PRESENT but not sensed` distinction below would quietly stop being made.
let private evidenceGraphPublicationReport (graphPath: string) publication =
    match publication with
    | Published -> []
    | Unevaluatable reason -> [ $"EvidenceGraph: publication rule ABSTAINED — {reason}; {graphPath} was left as the emitter wrote it." ]
    | PublishedSmaller dropped ->
        [ $"EvidenceGraph: publishing a graph that sensed {List.length dropped} fewer input(s) than {graphPath} already records, because {evidenceGraphPublishVariable} is set:" ]
        @ [ for entry in dropped -> $"EvidenceGraph:   - {entry}" ]
    | Restored dropped ->
        [ $"EvidenceGraph: this checkout could not sense {List.length dropped} input(s) that {graphPath} already records, so the freshly emitted graph was NOT published and the previous bytes were restored:" ]
        @ [ for entry in dropped ->
                // A dropped entry that is nonetheless present on disk is a different
                // and worse fault than an absent one, so never collapse the two.
                let fate =
                    if File.Exists entry then
                        "PRESENT but not sensed"
                    else
                        "absent from this checkout"

                $"EvidenceGraph:   - {entry} ({fate})" ]
        // Nothing here consults git, so neither of these lines may assert that the
        // dropped entries are untracked — it says what is USUALLY true and points at
        // the per-entry note, which is the only part actually observed.
        @ [ $"EvidenceGraph: a dropped input is usually a regenerable readiness output this checkout does not carry — an fsgg-sdd product only the lifecycle checkout holds, or a log this run writes after emitting the graph. An entry marked PRESENT above is neither, and is a different fault. Publish deliberately from a tree that does hold them with {evidenceGraphPublishVariable}=1."
            $"EvidenceGraph: {graphPath} now holds the PREVIOUSLY published bytes, not this run's, so it is exactly as stale as it already was — which is the trade: a stale record beats a freshly falsified one."
            // #56 untracked this artifact, so `git checkout` no longer resets it and a
            // refusal could otherwise pin a checkout to an emission it can never re-derive.
            // Naming the escape here keeps the route to green bounded and local.
            $"EvidenceGraph: since rogue3#56 this file is NOT tracked, so git cannot reset it — the bytes above are this checkout's own earlier emission and are now its only copy. To re-derive the graph from THIS tree alone, delete {graphPath} and re-run; the next emission has nothing to be smaller than and publishes." ]

/// #56: the publication rule matches `## Sensed readiness files`, a heading written by
/// the ENGINE package rather than by this repository, so an engine rename would leave the
/// rule abstaining forever behind a green gate. #26 pinned that literal in `SelfTest`
/// against the COMMITTED graph — which #56 untracked, so on a fresh checkout that pin has
/// nothing to read and announces a vacuous pass. This reads what the emitter JUST wrote
/// instead: it fires in every checkout, on every gate run, and it cannot go vacuous,
/// because a successful emission that produced no readable graph is itself the fault.
///
/// It WARNS rather than failing. The rule's response to an unreadable graph is to abstain
/// or to restore, neither of which loses anything now that the artifact is a run output;
/// failing here would red the gate over an engine upgrade with no local remedy.
let private emittedGraphSectionWarning (graphPath: string) : string list =
    if not (File.Exists graphPath) then
        [ $"EvidenceGraph: {graphPath} does not exist after an emission that exited 0, so there is no graph for the publication rule — or for EvidenceAudit — to read." ]
    else
        match sensedReadinessFiles (File.ReadAllText graphPath) with
        | Some sensed when Set.isEmpty sensed ->
            [ $"EvidenceGraph: the freshly emitted {graphPath} has a `{sensedSectionHeading}` section listing NOTHING, so the publication rule can never refuse anything on this tree." ]
        | Some _ -> []
        | None ->
            [ $"EvidenceGraph: the freshly emitted {graphPath} carries no `{sensedSectionHeading}` section, so the publication rule has nothing to compare and will ABSTAIN on every future run. An engine-side rename of that heading looks exactly like this — re-pin the literal in build.fsx." ]

/// Runs one emission under the rule. `emit` is a parameter, not a hard-wired call,
/// so `SelfTest` can drive this whole path: the rule being INSTALLED is exactly as
/// load-bearing as the rule being correct, and a test that only exercises the
/// predicate cannot tell the difference.
let private runEvidenceGraphEmission (graphPath: string) (publishSmaller: bool) (emit: unit -> int) =
    let previouslyPublished =
        if File.Exists graphPath then
            let snapshot = path [ Path.GetTempPath(); "rogue3-evidence-graph-" + Guid.NewGuid().ToString("N") + ".md" ]
            File.Copy(graphPath, snapshot, true)
            Some snapshot
        else
            None

    try
        try
            let exitCode = emit ()

            if exitCode <> 0 then
                failwithf "EvidenceGraph failed with exit code %d; see %s" exitCode graphPath

            match previouslyPublished with
            // Nothing has been published yet, so this emission cannot falsify anything.
            | None -> Published
            | Some snapshot -> applyEvidenceGraphPublicationRule graphPath snapshot publishSmaller
        with _ ->
            // The snapshot is about to be deleted by the `finally`. A fault anywhere
            // after the emitter started leaves the graph in an unknown state, so put
            // the published bytes back BEFORE the only copy of them disappears —
            // taking a backup and then discarding it exactly when it is needed was
            // the shape of this whole defect.
            match previouslyPublished with
            | Some snapshot ->
                try
                    File.Copy(snapshot, graphPath, true)
                with _ ->
                    ()
            | None -> ()

            reraise ()
    finally
        match previouslyPublished with
        | Some snapshot ->
            try
                File.Delete snapshot
            with _ ->
                ()
        | None -> ()

/// Wraps an emitter so the heading check reads what the EMITTER wrote.
///
/// The ordering is the whole point and it is subtle enough to be worth a named
/// function: the check must run after the emitter returns and BEFORE the publication
/// rule, which may restore the previous bytes over the emitted ones. Run afterwards, it
/// would read the restored copy while claiming to describe this run's output — a report
/// about bytes other than the ones it names, which is the shape of the defect #26 exists
/// to stop.
///
/// `emit` and `warn` are parameters rather than hard-wired calls so `SelfTest` can drive
/// this composition. #26's own lesson, in this same file, is that the predicate being
/// correct and the predicate being INSTALLED are two claims and a suite that proves the
/// first says nothing about the second: three mutants of the runner each reinstated the
/// original defect with a fully green suite.
///
/// `warn` is a SINK and a sink can be neutered. A PR reviewer proved it: passing `ignore`
/// here left every gate green and made the production check unobservable for every input,
/// because `warn` is its only consumer. That is why nothing above `evidenceGraphRun`
/// passes a sink at all — the warnings are a RETURNED VALUE there, which is #26's own
/// remedy for the publication report ("returned rather than printed so the wording is
/// testable") applied to the thing that reports on it.
let private emitWithHeadingCheck (graphPath: string) (emit: unit -> int) (warn: string -> unit) () =
    let exitCode = emit ()

    // A failed emission is reported by the caller, which raises; warning about the
    // graph it did not finish writing would bury that under noise about a file whose
    // state nobody has claimed anything about.
    if exitCode = 0 then
        emittedGraphSectionWarning graphPath |> List.iter warn

    exitCode

/// Everything `Verify` does for the evidence graph except call the engine, as a value.
/// The ONLY injected parameter is the emitter; the sink is internal, so there is no
/// production sink to replace with `ignore`, and the operator-facing lines are a returned
/// `string list` that `SelfTest` reads directly rather than a side effect it has to
/// intercept. Warnings precede the publication report because a rule that abstained or
/// restored everything is explained by the heading warning, and the explanation is
/// useless printed after the consequence.
let private evidenceGraphRun (emit: unit -> int) : string list =
    let warnings = ResizeArray<string>()

    let publication =
        runEvidenceGraphEmission
            evidenceGraphPath
            (evidenceGraphPublishRequested ())
            (emitWithHeadingCheck evidenceGraphPath emit warnings.Add)

    List.ofSeq warnings @ evidenceGraphPublicationReport evidenceGraphPath publication

let private runEvidenceGraph () =
    evidenceGraphRun (fun () -> runGeneratedEvidence "EvidenceGraph")
    |> List.iter (eprintfn "%s")

// A redirected pipe reaches EOF when the LAST writer closes it, and that is not necessarily the
// child we started: every grandchild inherits the same write handles. MSBuild's worker nodes are
// exactly that case — `dotnet build`/`dotnet run` spawn them as children with `/nodeReuse:true`, and
// they deliberately SURVIVE the command that spawned them so the next build can reuse them. So a
// command that has already exited 0 can leave stdout/stderr open indefinitely, and `ReadToEnd` on
// those pipes then blocks forever. Because the console echo, the readiness log write and the
// exit-code check all come AFTER that read, the observable symptom is total silence and a zero-byte
// log from a target that actually finished. Hence: collect incrementally, so nothing the command
// produced is ever lost, and bound the post-exit drain, so a surviving pipe holder can delay EOF but
// can never delay us.
let private postExitDrainSeconds = 10.0

let runProcess (target: string) (fileName: string) (arguments: string) =
    Directory.CreateDirectory("readiness/logs") |> ignore
    let logPath = Path.Combine("readiness", "logs", target + ".txt")
    let startInfo = ProcessStartInfo(fileName, arguments)
    startInfo.RedirectStandardOutput <- true
    startInfo.RedirectStandardError <- true
    startInfo.UseShellExecute <- false
    startInfo.WorkingDirectory <- Directory.GetCurrentDirectory()

    let proc =
        try
            Process.Start(startInfo) |> Option.ofObj
        with ex ->
            failwithf "%s failed command launch: %s %s; diagnostics=%s" target fileName arguments ex.Message

    use proc =
        match proc with
        | Some proc -> proc
        | None -> failwithf "%s failed command launch: %s %s" target fileName arguments

    // Collect both streams concurrently and incrementally. Concurrently because reading one stream
    // to end before the other deadlocks when the child fills the other pipe; incrementally because
    // an abandoned drain must still yield everything received up to that point.
    // One bounded loss is inherent to reading by line: a final line the child leaves UNTERMINATED is
    // only flushed when the reader sees EOF, which is precisely what an abandoned drain never sees.
    // A normally-draining command keeps it (verified), and the abandoned case still keeps every
    // complete line — against the previous behavior, which kept nothing and never returned at all.
    let outBuffer = Text.StringBuilder()
    let errBuffer = Text.StringBuilder()

    let collect (buffer: Text.StringBuilder) (line: string) =
        if not (isNull line) then
            lock buffer (fun () -> buffer.AppendLine line |> ignore)

    proc.OutputDataReceived.Add(fun received -> collect outBuffer received.Data)
    proc.ErrorDataReceived.Add(fun received -> collect errBuffer received.Data)
    proc.BeginOutputReadLine()
    proc.BeginErrorReadLine()

    // The timeout overload waits for the PROCESS only. The no-argument `WaitForExit()` additionally
    // waits for the asynchronous readers to reach EOF, which is the unbounded wait being avoided, so
    // it is used below only under a deadline.
    while not (proc.WaitForExit 500) do
        ()

    let drainedInTime =
        Threading.Tasks.Task
            .Run(fun () -> proc.WaitForExit())
            .Wait(TimeSpan.FromSeconds postExitDrainSeconds)

    let stdout = lock outBuffer (fun () -> outBuffer.ToString())

    // The abandoned-drain notice is reported on the error channel so the single existing path puts it
    // in BOTH the console echo and readiness/logs/<target>.txt.
    let drainDiagnostic =
        if drainedInTime then
            ""
        else
            sprintf
                "%s: stdout/stderr stayed open %.0fs after the command exited with code %d — a surviving grandchild (MSBuild `/nodeReuse:true` worker nodes do this) still holds the inherited handles. Every COMPLETE line the command produced is above; a final line it left unterminated is only flushed at EOF, so that one line alone may be missing. The exit code is authoritative.%s"
                target
                postExitDrainSeconds
                proc.ExitCode
                Environment.NewLine

    let stderr = lock errBuffer (fun () -> errBuffer.ToString()) + drainDiagnostic

    let output = stdout + stderr

    match tryWriteTextLog logPath output with
    | Some diagnostic -> failwithf "%s failed readiness log write; %s" target diagnostic
    | None -> ()

    printf "%s" output

    if output.IndexOf("NU1603", StringComparison.OrdinalIgnoreCase) >= 0 then
        failwithf "%s failed package-resolution: NU1603 fallback is not authoritative generated-rogue3 evidence" target

    if proc.ExitCode <> 0 then
        failwithf "%s failed with exit code %d; see %s" target proc.ExitCode logPath

// `Run` launches the INTERACTIVE product, and it is the one pass-through that must not redirect.
// Capturing an interactive launch buys nothing — readiness/logs/Run.txt is not part of the evidence
// graph — and costs everything: the product's console is withheld until it exits, so a window that
// is up and running is indistinguishable from a hang, and the inherited pipes are what stalled the
// target in the first place. Inheriting the console instead makes `-t Run` behave exactly like the
// `dotnet run --project src/<Name>` it wraps: output is live, and there is no pipe to strand.
// FSGG_RUN_TIMEOUT_SECONDS bounds an unattended launch (a positive number of seconds arms the
// watchdog); unset, the product runs for as long as the operator keeps it open.
let private interactiveWatchdog () =
    match Environment.GetEnvironmentVariable "FSGG_RUN_TIMEOUT_SECONDS" with
    | null
    | "" -> None
    | raw ->
        match Double.TryParse(raw, Globalization.NumberStyles.Float, Globalization.CultureInfo.InvariantCulture) with
        | true, seconds when seconds > 0.0 -> Some seconds
        | _ ->
            failwithf
                "FSGG_RUN_TIMEOUT_SECONDS must be a positive number of seconds; got %s"
                raw

let runInteractive (target: string) (fileName: string) (arguments: string) =
    Directory.CreateDirectory("readiness/logs") |> ignore
    let logPath = Path.Combine("readiness", "logs", target + ".txt")
    let startInfo = ProcessStartInfo(fileName, arguments)
    startInfo.UseShellExecute <- false
    startInfo.WorkingDirectory <- Directory.GetCurrentDirectory()

    let watchdog = interactiveWatchdog ()

    printfn
        "%s: launching %s %s — console inherited, so output below is the product's own and is live%s"
        target
        fileName
        arguments
        (match watchdog with
         | Some seconds -> sprintf " (watchdog %.0fs)" seconds
         | None -> "")

    let started = DateTime.UtcNow

    let proc =
        try
            Process.Start(startInfo) |> Option.ofObj
        with ex ->
            failwithf "%s failed command launch: %s %s; diagnostics=%s" target fileName arguments ex.Message

    use proc =
        match proc with
        | Some proc -> proc
        | None -> failwithf "%s failed command launch: %s %s" target fileName arguments

    let exited =
        match watchdog with
        | None ->
            while not (proc.WaitForExit 500) do
                ()

            true
        | Some seconds -> proc.WaitForExit(int (min seconds 86400.0 * 1000.0))

    let elapsed = DateTime.UtcNow - started

    let record note =
        let log =
            sprintf
                "%s: %s %s%s%s ran %.1fs; %s%s"
                target
                fileName
                arguments
                Environment.NewLine
                "console inherited (not captured) — the product wrote straight to this terminal;"
                elapsed.TotalSeconds
                note
                Environment.NewLine

        match tryWriteTextLog logPath log with
        | Some diagnostic -> failwithf "%s failed readiness log write; %s" target diagnostic
        | None -> ()

    if not exited then
        try
            proc.Kill true
        with _ ->
            ()

        record "watchdog expired before the product exited, so it was terminated"

        failwithf
            "%s failed: %s %s neither exited nor was closed within the FSGG_RUN_TIMEOUT_SECONDS watchdog (%.1fs); the product was terminated. See %s"
            target
            fileName
            arguments
            elapsed.TotalSeconds
            logPath

    record (sprintf "exit code %d" proc.ExitCode)

    if proc.ExitCode <> 0 then
        failwithf "%s failed with exit code %d; see %s" target proc.ExitCode logPath

    printfn "%s completed for generated rogue3 (exit code 0 after %.1fs)" target elapsed.TotalSeconds

let runGeneratedTests () =
    runProcess "Test" "dotnet" "test tests/Rogue3.Tests/Rogue3.Tests.fsproj -m:1 --disable-build-servers"
    printfn "Test completed for generated rogue3"

// Feature 212 (R3): name-agnostic locators so the new pass-through targets (and the verb wrapper)
// need no literal <Name>. The rogue3 root holds exactly one root solution and exactly one src
// project; both are discovered at runtime so the same script works for any scaffolded name.
let private singleRootSolution () =
    match Directory.GetFiles(Directory.GetCurrentDirectory(), "*.slnx") with
    | [| f |] -> Path.GetFileName f
    | [||] -> failwith "No root *.slnx found in the rogue3 root (Feature 212 root solution missing)."
    | many -> failwithf "Expected exactly one root *.slnx; found %d." many.Length

let private singleSrcProject () =
    let srcRoot = path [ Directory.GetCurrentDirectory(); "src" ]

    match (if Directory.Exists srcRoot then Directory.GetDirectories srcRoot else [||]) with
    | [| d |] -> Path.GetFileName d
    | [||] -> failwith "No src/<project> directory found in the rogue3 root."
    | many -> failwithf "Expected exactly one src/<project>; found %d." many.Length

let runPerformanceEvidence () =
    let project = singleSrcProject ()
    runProcess "PerformanceEvidence" "dotnet" (sprintf "run -c Release --project src/%s -- --performance-evidence readiness/performance-evidence.json" project)

let runPerformanceCriticRequest () =
    let project = singleSrcProject ()
    runProcess "PerformanceCriticRequest" "dotnet" (sprintf "run -c Release --project src/%s -- --performance-critic-request readiness/performance-critic-request.json" project)

let runPerformanceIntent () =
    let project = singleSrcProject ()
    runProcess "PerformanceIntent" "dotnet" (sprintf "run -c Release --project src/%s -- --performance-intent readiness/performance-intent.yml" project)

// ---------------------------------------------------------------------------
// Issue #34: the three merge-gate steps that announced success without checking.
//
// `Dev`, `GeneratedGuidanceCheck` and `TemplateDrift` all routed to `writeLog`,
// so `Verify` reported green for three things it never read. A no-op that
// announces success is worse than an absent step, because it is counted.
//
// `TemplateDrift` and `GeneratedGuidanceCheck` now perform a real check that
// fails on a tree that violates it, and `SelfTest` proves each one fails on a
// PLANTED violation before CI trusts its verdict — the discipline the
// audit-binding checker already applies to itself. `Dev` is no longer counted by
// `Verify`: it is honestly a completion marker for the dev loop, the help banner
// has always said so, and giving it a check it does not need would only restore
// the same false coverage in a different shape.
//
// SCOPE, and why it is drawn here. `TemplateDrift` compares materialized kit
// files against the digests pinned in the two skill manifests. It deliberately
// does NOT read `.fsgg/scaffold-provenance.json`:
//   * `producedPaths` records what the generator PRODUCED, not what should exist
//     now — three helper modules listed there were deliberately deleted (#19,
//     #28), and nothing distinguishes "produced then removed" from "produced and
//     still present", so a drift verdict over it would be false;
//   * `mirroredPaths`/`driverPaths` pin digests for skills this repository owns
//     and edits locally, so many of them are already legitimately stale. Checking
//     them would make the gate red on a correct tree, which is the failure mode
//     that trained everyone to ignore gates in the first place.
//     #34 wrote "12" here, measured at `d8d0024`. Re-measured at `09895b9` it is
//     **18** of the 110 sha-bearing entries (producedPaths 4, mirroredPaths 4,
//     driverPaths 10); every pinned file is present, so all 18 are content drift.
//     The count is deliberately no longer stated as a constant in this comment —
//     it grows every time the repository edits a driver skill, which is exactly
//     why provenance cannot be the oracle. Recompute it rather than trust a
//     number in a comment; a stale figure here is how this one got to 18.
// The skill manifests are the honest source for what they name: they are
// re-pinned when kit files are re-materialized. They name only 32 of 95, which is
// what #46 below is about.
// ---------------------------------------------------------------------------

let private lowerHex (bytes: byte array) = (Convert.ToHexString bytes).ToLowerInvariant()

// ---------------------------------------------------------------------------
// #62 (4): a digest that cannot throw.
//
// `fileDigest` was `File.ReadAllBytes |> SHA256`, and a kit file the gate can SEE
// but cannot OPEN — mode bits set to 000, a lock, a symlink whose target moved between the
// `File.Exists` test and the read — threw an unhandled exception out of the
// target: "Stopped due to error", NO violation line, and no report on the one
// tree the gate most needs to report on. That directly contradicts the doctrine
// this file states three times about its other input, the manifest `sha256`:
// truncating it blindly would crash the gate on the tree it exists to describe.
//
// So every read that feeds a verdict is TOTAL: it returns either a value or a
// violation line. The seams, enumerated by grepping this file for `File.ReadAll`
// and `Directory.Get*` rather than by reading it (#62's own lesson — a worker
// guarded the seam an issue named and none of the ones it did not):
//   * `tryDigest`           — all five digest sites (manifest agreement, manifest
//                             entry, ledger entry, mirror, `computeKitPins`);
//   * `tryReadJson`         — both skill manifests, the ledger, and
//                             `.fsgg/scaffold-provenance.json`, which
//                             `templateDriftViolations` reads FIRST and which was
//                             equally able to abort the run;
//   * `kitTreeScan`         — the two tree walks;
//   * `generatedGuidanceViolations` — `.fsgg/agents.yml`.
// A malformed or unreadable input is a VIOLATION here, never an absence: an
// oracle that vanishes silently is the no-op defect #34 was filed for.
// ---------------------------------------------------------------------------

/// A digest, or the reason there isn't one. The `Error` is a fragment, not a whole line: each
/// call site prefixes the name it knows the file by, so the violation reads in that pass's terms.
let private tryDigest (filePath: string) : Result<string, string> =
    try
        File.ReadAllBytes filePath
        |> System.Security.Cryptography.SHA256.HashData
        |> lowerHex
        |> Ok
    with ex ->
        Error $"cannot be read ({ex.GetType().Name}), so nothing is checking its bytes"

/// The text of a JSON input, parsed, or the reason it could not be. Both failure modes — a file
/// that will not open and a file that will not parse — are the same thing to a gate: an oracle it
/// cannot consult, which must be reported rather than treated as an empty one.
let private tryReadJson (filePath: string) : Result<System.Text.Json.JsonDocument, string> =
    try
        Ok(System.Text.Json.JsonDocument.Parse(File.ReadAllText filePath))
    with ex ->
        Error $"cannot be read as JSON ({ex.GetType().Name}), so nothing it declares is being checked"

/// #62 (1) and (2): the target of a symbolic link, or `None` for a real file or directory.
/// `LinkTarget` is non-null for exactly the reparse points that make a path stand in for bytes the
/// repository does not store; it is what distinguishes "this tree has 190 files" from "this tree
/// has 95 files and a link that makes them look like 190".
let private linkTarget (fullPath: string) : string option =
    let info: FileSystemInfo =
        if Directory.Exists fullPath then
            DirectoryInfo(fullPath) :> FileSystemInfo
        else
            FileInfo(fullPath) :> FileSystemInfo

    match info.LinkTarget with
    | null -> None
    | target -> Some target

/// Digests are shown truncated. A manifest is an INPUT, so its `sha256` may be malformed;
/// truncating it blindly would crash the gate on the one tree it most needs to report on.
let private shortDigest (digest: string) =
    if String.IsNullOrEmpty digest then "<none>"
    elif digest.Length <= 12 then digest
    else digest.Substring(0, 12)

/// Hex case is not part of a digest's identity. Comparing ordinally would turn a manifest
/// re-emitted in upper case into 96 violations on a tree where nothing has drifted — the
/// red-on-a-correct-tree failure this gate exists to avoid.
let private digestEquals (a: string) (b: string) =
    String.Equals(a, b, StringComparison.OrdinalIgnoreCase)

/// One YAML scalar, as generated files actually write them: `x`, `"x"`, `'x'`, or `x # comment`.
/// A parser that only accepts the bare form reports a missing guidance target on a correct tree.
let private yamlScalar (raw: string) =
    let withoutComment =
        match raw.IndexOf(" #", StringComparison.Ordinal) with
        | -1 -> raw
        | i -> raw.Substring(0, i)

    let trimmed = withoutComment.Trim()

    if trimmed.Length >= 2
       && ((trimmed.StartsWith "\"" && trimmed.EndsWith "\"")
           || (trimmed.StartsWith "'" && trimmed.EndsWith "'")) then
        trimmed.Substring(1, trimmed.Length - 2)
    else
        trimmed

/// The scaffolded profile, read from the one place that records it, plus the reason it could not be
/// read where that applies. `materializes-when` is evaluated against this, so a kit file that is
/// absent BECAUSE this profile does not take it is not reported as drift.
///
/// #62 (4): this is the FIRST thing `templateDriftViolations` reads, and it used to parse the file
/// unguarded — an unreadable or malformed `scaffold-provenance.json` aborted the whole target
/// before any check ran, which is the same defect the item records for `fileDigest` reached one
/// input earlier. It now reports, and the caller falls through to the `declares no profile`
/// violation it already had.
let private scaffoldProfile (root: string) : string option * string option =
    let provenance = path [ root; ".fsgg"; "scaffold-provenance.json" ]

    if not (File.Exists provenance) then
        None, None
    else
        match tryReadJson provenance with
        | Error reason -> None, Some $".fsgg/scaffold-provenance.json: {reason}"
        | Ok doc ->
            use doc = doc

            // Every shape test is a ValueKind test rather than a `GetString()` that trusts the file:
            // provenance is an INPUT like the manifest, and `"effectiveParameters": {}` threw the
            // same unhandled exception the malformed-JSON case did.
            let asString (element: System.Text.Json.JsonElement) =
                if element.ValueKind = System.Text.Json.JsonValueKind.String then element.GetString() else null

            let profile =
                match doc.RootElement.TryGetProperty "effectiveParameters" with
                | true, parameters when parameters.ValueKind = System.Text.Json.JsonValueKind.Array ->
                    parameters.EnumerateArray()
                    |> Seq.tryPick (fun p ->
                        if p.ValueKind <> System.Text.Json.JsonValueKind.Object then
                            None
                        else
                            match p.TryGetProperty "key", p.TryGetProperty "value" with
                            | (true, key), (true, value) when asString key = "profile" ->
                                // A non-string `value` is NOT a profile. Returning `Some null` here
                                // would satisfy the caller's `profile.IsNone` test and then match
                                // nothing, which is a declared profile that silently excludes every
                                // skill — worse than the violation the `None` path already reports.
                                match asString value with
                                | null -> None
                                | declared -> Some declared
                            | _ -> None)
                | _ -> None

            profile, None

/// `always` | `profile == x` | `profile in [a, b, c]`. An expression this cannot read is
/// reported rather than skipped: silently ignoring a condition is how the no-op arose.
let private materializesHere (profile: string option) (expression: string) =
    let expr = yamlScalar expression

    if expr = "" || expr = "always" then
        Ok true
    else
        let equality = Regex.Match(expr, @"^profile\s*==\s*(\S+)$")
        let membership = Regex.Match(expr, @"^profile\s+in\s+\[(.*)\]$")

        if equality.Success then
            Ok(profile = Some(yamlScalar equality.Groups.[1].Value))
        elif membership.Success then
            let allowed =
                membership.Groups.[1].Value.Split(',')
                |> Array.map yamlScalar
                |> Array.filter (fun s -> s <> "")

            Ok(match profile with
               | Some p -> Array.contains p allowed
               | None -> false)
        else
            Error expr

let private skillManifests = [ ".agents"; ".claude" ]

// ---------------------------------------------------------------------------
// #46: the generated manifest pins 32 of the 95 files it materializes, so the
// check #34 shipped covered 64 of 190 kit files and its own oracle was pinned by
// nothing that this gate reads. #46 records two surviving evasion routes. ONE of
// them was still live when this was written; the other had been closed by
// accident in the meantime, and the difference matters:
//
//   1. edit any kit file no manifest names — 63 of the 95 under `.agents/skills`,
//      including every `fs-gg-sdd-*` skill and 7 of `work-board`'s 8 files (its
//      `SKILL.md` IS named; its references, agents and scripts are not). MEASURED at
//      `09895b9`: appending one line to
//      `.agents/skills/work-board/references/deep-detail.md` leaves
//      `check-audit-bindings.py` at exit 0 and every other gate green. Genuinely
//      open, and closed here.
//   2. mutate a pinned kit file and delete its entry from BOTH manifests. #46
//      says this is green on every gate; it is NOT, as of `09895b9`. Two merged
//      audits — `feedback/audits/2026-08-02-Rogue3-10.audit.json` and `-11` —
//      cite `file:.agents/skills/skill-manifest.json` with a sha256, so the same
//      mutation exits 1 from `check-audit-bindings.py`. #46's claim was true when
//      it was filed and went stale when #45's and #40's own audits merged.
//      That guard is real but INCIDENTAL — it is a side effect of two cycles
//      happening to cite the manifest as evidence, exactly the "guarded by
//      accident" shape #34 recorded for 7 of 32 kit files — it lives only in CI
//      rather than in `./fake.sh -t TemplateDrift`, `--grandfather` clears it,
//      and it covers `.agents/` only: NO audit binds
//      `.claude/skills/skill-manifest.json`. The coverage rule below makes it
//      first-class, local and intentional instead.
//
// The upstream fix — widen the generated manifest — is not available here: the
// manifest is generated (FS.GG.SDD.Artifacts 0.32.0 emitted it), so a local
// widening is not durable and would be lost whenever the generator re-emits it.
// So this takes #46's second acceptance option, and takes it in the strong form.
// `TemplateDrift` now ENUMERATES both kit trees and reports every file that no
// pin covers, and a repository-owned ledger, `scripts/kit-pins.json`, pins the
// complement the generated manifest omits — so the uncovered set is both
// reported by the gate and empty on this tree.
//
// Why a ledger and not an allow-list. #46 offers "the manifest widened first or
// an explicit allow-list". An allow-list of the 63 would make the gate green
// while guarding nothing, which is the shape of the defect #34 fixed. Digests
// cost the same to author and actually hold.
//
// Why the COMPLEMENT and not all 95. Route 2 is closed by the coverage rule, not
// by a second digest: deleting an entry from both manifests removes the file's
// only pin, and an unpinned kit file is now itself a violation. Pinning all 95
// would duplicate 32 digests that must then be re-pinned in two places, which is
// how a ledger goes stale and a gate goes red on a correct tree.
//
// What pins the ledger. Not this gate — a digest file cannot pin itself, the
// same fixed point `scripts/audit-binding-exceptions.json` is exempted from in
// `check-audit-bindings.py`. It is pinned OUT of band, by the audit-binding gate,
// PROVIDED this cycle's audit ships in the same pull request as this file:
// `feedback/audits/2026-08-02-Rogue3-15.audit.json` cites `scripts/kit-pins.json`
// and BOTH manifests as `file:` evidence, and `.github/workflows/audit-bindings.yml`
// — which, unlike the FAKE `Verify` target, really does run on every pull request
// — then reports an edit to any of the three as a stale binding.
//
// That is stated as a condition because it IS one. A reviewer reading this file at
// a commit where the audit is not yet present is looking at a ledger pinned by
// nothing; a critic caught exactly that on the first commit of this branch, which
// is why the paragraph is worded this way. Verify rather than believe — run the
// audit-binding checker and look for the ledger among its bindings:
//     scripts/check-audit-bindings.py --json | grep kit-pins
// (the interpreter prefix is omitted deliberately: a governance scan in
// tests/Rogue3.Tests/GovernanceTests.fs forbids that token anywhere in this file)
//
// Note also what is and is not new here, stated carefully, because this is the
// THIRD claim of absence in this area to turn out wrong when someone finally ran
// the grep (the other two are recorded above and on #46):
//   * `.agents/skills/skill-manifest.json` already had a live pin, from the two
//     audits named above.
//   * BOTH manifests are also pinned in `.fsgg/scaffold-provenance.json`
//     (`producedPaths` and `mirroredPaths`, both at `7bf2f301c0a1`) — but both
//     files are actually `a9e86b4ec1b6`, so those pins are stale, and nothing
//     reads them anyway: this check deliberately does not, for the reason given
//     in the scope comment far above.
// So the honest statement is NOT "the first pin" but: the first pin on
// `.claude/skills/skill-manifest.json` that anything CHECKS, the first pin of any
// kind on the ledger, and — unlike the two audit bindings — a citation made
// deliberately for that purpose rather than as a by-product of citing evidence
// for an unrelated finding.
//
// It is not tamper-PROOF: a worker who edits a kit file, re-pins the ledger and
// grandfathers the binding gets green. It is tamper-EVIDENT, which is the honest
// ceiling for an oracle that lives in the same tree as its subject, and every
// step of that sequence is a reviewable diff rather than the single invisible
// line append that passed before.
// ---------------------------------------------------------------------------

let private kitPinsRelative = "scripts/kit-pins.json"

/// The one tree EITHER oracle may pin. A pin outside it would be neither checked for
/// coverage nor mirrored, so accepting one would let a digest list quietly become a
/// general-purpose digest file whose entries nothing enumerates.
let private kitSourcePrefix = ".agents/skills/"

/// #62 (3): the boundary, as ONE predicate.
///
/// The ledger refused a pin outside `.agents/skills/` and the generated manifest did not, though
/// both feed the same `pinnedSources` set and the same mirror pass. Adding
/// `"resolvablePath": "README.md"` to both manifests was green: the digest matched, so the entry
/// looked like coverage while naming a file no coverage pass enumerates and no mirror pass mirrors
/// — the same overstatement the ledger's boundary exists to prevent, one oracle over.
///
/// It is a predicate rather than two copies of a condition because that asymmetry is the whole
/// defect: the rule the file STATED ("the one tree the ledger may pin", and the mirror pass's
/// assumption that every pin is a `.agents/` source) was enforced in one place out of two. A shared
/// predicate makes the two oracles disagree UNCONSTRUCTIBLE rather than merely unlikely.
///
/// `..` is refused for the same reason and not as a security boundary: `.agents/skills/../../x`
/// satisfies the prefix while naming a file outside the enumerated tree.
let private withinKitSources (relative: string) =
    relative.StartsWith(kitSourcePrefix, StringComparison.Ordinal)
    && not (relative.Split([| '/'; '\\' |]) |> Array.contains "..")

/// Every file under `<owner>/skills`, workspace-relative with forward slashes, PLUS one line per
/// entry this walk refuses to count. Enumerated from DISK, not from git: a file injected into the
/// kit is exactly the case this must see, and `SelfTest` fixtures are not repositories.
///
/// #62 (1) and (2), and why this is a hand-written walk rather than `Directory.GetFiles(…,
/// AllDirectories)`:
///
///   * `.NET`'s recursive enumeration FOLLOWS directory symlinks and reports nothing about them.
///     With `.claude/skills` a link to `.agents/skills` the gate printed `190 of 190 … plus 95
///     mirror(s)` for 95 distinct files; the mirror pass degenerated into comparing every file with
///     itself, and `the two skill manifests disagree` became structurally incapable of firing. No
///     drift hides — an edit still shows up, twice — but 190 is precisely the number #46 exists to
///     make trustworthy.
///   * A single kit FILE replaced by a link to identical bytes outside the repository was counted
///     as covered by digest alone. Its provenance has left the tree and a fresh clone dangles.
///
/// A link is therefore REPORTED and NOT COUNTED, at any depth, in either tree. Not counting it is
/// half the fix: leaving it in the total would keep the denominator describing a file this
/// repository does not store.
///
/// The walk is also total (#62 item 4): a directory that will not list is a violation line, not an
/// unhandled `UnauthorizedAccessException` out of the target.
let private kitTreeScan (root: string) (owner: string) : string list * string list =
    let files = ResizeArray<string>()
    let problems = ResizeArray<string>()
    let relativeTo (full: string) = (Path.GetRelativePath(root, full)).Replace('\\', '/')

    let reportLink (full: string) (target: string) =
        problems.Add
            $"{relativeTo full} is a symbolic link (-> {target}), not something this repository stores: its bytes live outside the tree these digests pin, so it is reported rather than counted as covered"

    let rec walk (dir: string) =
        let entries =
            try
                Ok(Directory.GetFileSystemEntries(dir, "*", SearchOption.TopDirectoryOnly) |> Array.sort)
            with ex ->
                Error ex

        match entries with
        | Error ex ->
            problems.Add $"{relativeTo dir}: cannot be listed ({ex.GetType().Name}), so nothing under it is being checked"
        | Ok entries ->
            for entry in entries do
                match linkTarget entry with
                | Some target -> reportLink entry target
                | None -> if Directory.Exists entry then walk entry else files.Add(relativeTo entry)

    /// The walk yields depth-first, and the caller this most matters to is `computeKitPins`, which
    /// WRITES `scripts/kit-pins.json` in the order it receives. `Directory.GetFiles(…,
    /// AllDirectories)` returned a flat array that was then sorted on the RELATIVE path, and the two
    /// orders are not the same wherever a directory and a file share a prefix: `.agents/skills/x/a`
    /// sorts before `.agents/skills/x.md` (`/` > `.`), but a depth-first walk emits the directory's
    /// contents first. Three such pairs exist in this repository's kit today, so without this sort
    /// `KitPins` rewrites the ledger with identical digests in a different order — a gratuitous diff
    /// on a file four merged audits bind, for no change at all. Measured, not reasoned: the first
    /// version of this walk moved six lines.
    let sorted (values: ResizeArray<string>) = values |> List.ofSeq |> List.sort

    // The two roots the item names, checked BEFORE the walk: `.claude` itself may be the link, in
    // which case `.claude/skills` is a perfectly ordinary directory reached through it.
    let ownerRoot = path [ root; owner ]
    let treeRoot = path [ root; owner; "skills" ]

    match (if Directory.Exists ownerRoot then linkTarget ownerRoot else None) with
    | Some target -> reportLink ownerRoot target
    | None ->
        if Directory.Exists treeRoot then
            match linkTarget treeRoot with
            | Some target -> reportLink treeRoot target
            | None -> walk treeRoot

    sorted files, sorted problems

/// The files only, for the callers that want the enumeration and not the refusals. Every caller
/// that produces VIOLATIONS uses `kitTreeScan` — dropping the problems is legitimate only where
/// something else is already reporting them.
let private kitTreeFiles (root: string) (owner: string) = fst (kitTreeScan root owner)

/// The ledger as `(path, sha256)` pairs, or a violation line explaining why it could
/// not be read. A missing or malformed ledger is REPORTED, never treated as "no pins
/// to check" — an oracle that vanishes silently is the no-op defect one level up.
let private readKitPins (root: string) : Result<(string * string) list, string> =
    let file = path [ root; kitPinsRelative ]

    if not (File.Exists file) then
        // Names the remedy, because this is the one violation a tree can reach WITHOUT anything
        // being wrong with the kit — a checkout that never had the ledger. Reporting it without a
        // way out is how a gate becomes something people switch off.
        Error $"{kitPinsRelative}: the kit pin ledger is missing, so nothing pins the kit files the generated manifest does not name — run `./fake.sh build -t KitPins` to create it, then review the pins it writes"
    else
        match tryReadJson file with
        | Error reason -> Error $"{kitPinsRelative}: {reason}"
        | Ok doc ->
            use doc = doc

            match doc.RootElement.TryGetProperty "pins" with
            // The ValueKind test is not decoration: `"pins": {}` used to reach `EnumerateArray`
            // and throw, which the old blanket `with` then reported as unparseable JSON — a true
            // verdict for the wrong reason, on a file that parses perfectly.
            | true, pins when pins.ValueKind = System.Text.Json.JsonValueKind.Array ->
                pins.EnumerateArray()
                |> Seq.map (fun pin ->
                    let read (name: string) =
                        // `TryGetProperty` throws on a non-object, so `"pins": [ "x" ]` was another
                        // parseable file the old blanket `with` reported as unparseable. A
                        // shapeless entry reads as pinning nothing, which is exactly what it does.
                        if pin.ValueKind <> System.Text.Json.JsonValueKind.Object then
                            null
                        else
                            match pin.TryGetProperty name with
                            | true, v when v.ValueKind = System.Text.Json.JsonValueKind.String -> v.GetString()
                            | _ -> null

                    (read "path", read "sha256"))
                |> List.ofSeq
                |> Ok
            | _ -> Error $"{kitPinsRelative}: no `pins` array to check"

/// The ledger content this tree implies: every file under `.agents/skills` that the manifest does
/// not PIN, at its current digest — not merely every file it does not NAME, which is a different
/// and smaller set whenever an entry has lost its `sha256`. Shared by the `KitPins` target and by
/// `SelfTest`, so the remedy the gate prints is the one the tests exercise, and so `KitPins` can
/// actually clear a coverage violation caused by a digestless manifest entry.
///
/// #62 (4): returns the pins AND the files it could not digest, rather than throwing out of the
/// remedy the gate tells people to run. A file omitted here is not silently dropped — `KitPins`
/// prints it and exits non-zero, and the next `TemplateDrift` reports it as covered by nothing.
let private computeKitPins (root: string) (manifestPinned: Set<string>) : (string * string) list * string list =
    let pins = ResizeArray<string * string>()
    let unpinnable = ResizeArray<string>()

    for relative in kitTreeFiles root ".agents" do
        if not (manifestPinned.Contains relative) then
            match tryDigest (path [ root; relative ]) with
            | Ok digest -> pins.Add(relative, digest)
            | Error reason -> unpinnable.Add $"{relative}: {reason}"

    List.ofSeq pins, List.ofSeq unpinnable

/// The digest the `.agents` manifest pins for each entry that carries one. PINS ONLY — the set of
/// paths the manifest merely NAMES is deliberately not returned.
///
/// That is the whole design of this function, and it is the answer to a fault this file hit twice.
/// An entry keeping its `resolvablePath` but losing its `sha256` is named and not pinned; counting
/// the name as coverage made the gate print `190 of 190 … 0 uncovered` while that file and its
/// mirror were digest-checked by nothing, with a drifted mirror going unmentioned. The tree stays
/// red either way — the manifest pass reports the entry — so nothing hid behind green; the NUMBER
/// was wrong, which is what #46 exists to stop.
///
/// An earlier fix returned both sets with a comment saying which one coverage may use. A reviewer
/// argued the comment was the wrong instrument, and was right: a rule a maintainer must READ is how
/// the fault came back the second time, in the commit that fixed the first. Not returning the names
/// makes keying coverage on them UNCONSTRUCTIBLE rather than merely discouraged.
///
/// #62 (3): an entry outside `.agents/skills/` is not returned either, for the same reason and by
/// the same predicate the ledger uses. `pinnedSources` and the mirror pass are both built from what
/// this returns, so admitting `README.md` here would have credited a file that no coverage pass
/// enumerates and no mirror exists for.
///
/// #62 (4): an unreadable or malformed manifest yields NO pins here and is reported by
/// `templateDriftViolations`, which reads the same file through the same total reader. It used to
/// throw out of this function — before the violation pass had emitted a single line.
let private agentsManifestPins (root: string) =
    let pins = ResizeArray<string * string>()
    let manifest = path [ root; ".agents"; "skills"; "skill-manifest.json" ]

    if File.Exists manifest then
        match tryReadJson manifest with
        | Error _ -> ()
        | Ok doc ->
            use doc = doc

            match doc.RootElement.TryGetProperty "skills" with
            | true, skills when skills.ValueKind = System.Text.Json.JsonValueKind.Array ->
                for skill in skills.EnumerateArray() do
                    let read (name: string) =
                        if skill.ValueKind <> System.Text.Json.JsonValueKind.Object then
                            null
                        else
                            match skill.TryGetProperty name with
                            | true, v when v.ValueKind = System.Text.Json.JsonValueKind.String -> v.GetString()
                            | _ -> null

                    let relative = read "resolvablePath"
                    let pinned = read "sha256"

                    if not (String.IsNullOrWhiteSpace relative)
                       && not (String.IsNullOrWhiteSpace pinned)
                       && withinKitSources relative then
                        pins.Add(relative, pinned)
            | _ -> ()

    pins

let private renderKitPins (pins: (string * string) list) =
    let entries =
        pins
        |> List.map (fun (relative, digest) -> $"    {{ \"path\": \"{relative}\", \"sha256\": \"{digest}\" }}")
        |> String.concat ",\n"

    "{\n"
    + "  \"schemaVersion\": 1,\n"
    + "  \"note\": \"Repository-owned digests for the kit files .agents/skills/skill-manifest.json does not name. Read by TemplateDrift in build.fsx; regenerate with ./fake.sh build -t KitPins after a DELIBERATE kit edit. This file cannot pin itself; it is pinned only for as long as some audit under feedback/audits/ cites it - check with: scripts/check-audit-bindings.py --json | grep kit-pins\",\n"
    + "  \"pins\": [\n"
    + entries
    + "\n  ]\n}\n"

/// Rewrites the ledger from the tree and returns what it pinned. THE one write path: the `KitPins`
/// target and `SelfTest`'s fixture re-pin both go through here, so a mutation to the remedy is a
/// mutation to the thing the tests exercise. They used to be parallel implementations, and a
/// reviewer's mutant that made the TARGET write an empty ledger survived every case because only
/// the fixture's copy was ever run.
let private writeKitPins (root: string) =
    let manifestPins = agentsManifestPins root
    let pins, unpinnable = computeKitPins root (manifestPins |> Seq.map fst |> Set.ofSeq)
    let target = path [ root; kitPinsRelative ]
    Directory.CreateDirectory(Path.GetDirectoryName target: string) |> ignore
    File.WriteAllText(target, renderKitPins pins)
    pins, unpinnable

/// Every materialized kit file matches the digest its manifest pins. Returns one line per
/// violation; an empty list is a genuine pass.
let templateDriftViolations (root: string) =
    let violations = ResizeArray<string>()
    let profile, provenanceProblem = scaffoldProfile root

    match provenanceProblem with
    | Some problem -> violations.Add problem
    | None -> ()

    if profile.IsNone then
        violations.Add ".fsgg/scaffold-provenance.json declares no `profile`, so `materializes-when` cannot be evaluated"

    // #62 (1) and (2): the two kit trees, walked once each, with every symlink and every directory
    // that will not list reported instead of followed. Done FIRST so a reader sees why a total
    // moved before reading the total.
    let sourceFiles, sourceProblems = kitTreeScan root ".agents"
    let mirrorFiles, mirrorProblems = kitTreeScan root ".claude"
    violations.AddRange sourceProblems
    violations.AddRange mirrorProblems

    // The manifests are byte-identical by construction, so requiring them to agree means tampering
    // has to be done twice, identically, to go unnoticed.
    //
    // #34 shipped this comment saying "nothing else in the tree pins them … and no audit binds
    // either", and called the residual — an edit applied to BOTH manifests — unclosed. BOTH halves
    // of that are now wrong, and leaving the words would misdescribe the code directly below them:
    //   * `feedback/audits/2026-08-02-Rogue3-{10,11}.audit.json` cite
    //     `file:.agents/skills/skill-manifest.json` with a sha256, so the audit-binding gate has
    //     pinned that file since those audits merged. It was already false when written.
    //   * #46's `scripts/kit-pins.json` now pins `.agents/skills/skill-manifest.json` outright, and
    //     the mirror pass holds `.claude/skills/skill-manifest.json` to the same digest — so an
    //     edit applied to both manifests IS reported, and the agreement check below is no longer
    //     the only thing standing between this gate and its own oracle.
    // The agreement check is kept regardless: it names the ONE-sided edit precisely ("edited
    // alone"), which the digest pins would otherwise report as two unexplained drifts.
    let manifestPaths = skillManifests |> List.map (fun owner -> owner, path [ root; owner; "skills"; "skill-manifest.json" ])

    match manifestPaths |> List.filter (snd >> File.Exists) with
    | [ (ownerA, a); (ownerB, b) ] ->
        // #62 (4): BOTH digests are attempted and BOTH failures reported. Reading one and letting it
        // throw took the agreement check, the two manifest passes below it and every other check in
        // this function down with it.
        let digestOrReport (owner: string) (file: string) =
            match tryDigest file with
            | Ok digest -> Some digest
            | Error reason ->
                violations.Add $"{owner}/skills/skill-manifest.json: {reason}"
                None

        match digestOrReport ownerA a, digestOrReport ownerB b with
        | Some da, Some db when not (digestEquals da db) ->
            violations.Add $"the two skill manifests disagree: {ownerA}/skills/skill-manifest.json is {shortDigest da}, {ownerB}/skills/skill-manifest.json is {shortDigest db} — one of them has been edited alone"
        | _ -> ()
    | _ -> ()

    for owner in skillManifests do
        let manifest = path [ root; owner; "skills"; "skill-manifest.json" ]
        let manifestName = $"{owner}/skills/skill-manifest.json"

        if not (File.Exists manifest) then
            violations.Add $"{manifestName}: pinned skill manifest is missing"
        else
            // #62 (4): the gate's PRIMARY oracle was parsed unguarded, so a manifest that would not
            // open or would not parse crashed the target with no violation line — while the LEDGER,
            // the secondary oracle, had been reporting both since #46. The asymmetry was the same
            // shape as item 3's: a rule enforced on one of two inputs.
            match tryReadJson manifest with
            | Error reason -> violations.Add $"{manifestName}: {reason}"
            | Ok parsed ->
                use doc = parsed

                match doc.RootElement.TryGetProperty "skills" with
                | true, skills when skills.ValueKind = System.Text.Json.JsonValueKind.Array ->
                    for skill in skills.EnumerateArray() do
                        let read (name: string) =
                            if skill.ValueKind <> System.Text.Json.JsonValueKind.Object then
                                null
                            else
                                match skill.TryGetProperty name with
                                | true, v when v.ValueKind = System.Text.Json.JsonValueKind.String -> v.GetString()
                                | _ -> null

                        let id = read "id"
                        let relative = read "resolvablePath"
                        let pinned = read "sha256"
                        let condition = match read "materializes-when" with | null -> "always" | c -> c

                        if String.IsNullOrWhiteSpace relative || String.IsNullOrWhiteSpace pinned then
                            violations.Add $"{manifestName}: skill `{id}` pins no resolvablePath/sha256"
                        elif not (withinKitSources relative) then
                            // #62 (3). The ledger has refused this since #46; the manifest did not,
                            // so `"resolvablePath": "README.md"` in both manifests was GREEN — a
                            // digest that matches, over a file no coverage pass enumerates and no
                            // mirror pass mirrors. The manifest is generated, so reaching this line
                            // means either the generator changed its contract or someone edited both
                            // copies by hand; either way the gate must say so rather than count it.
                            violations.Add $"{manifestName}: skill `{id}` pins `{relative}`, which is outside {kitSourcePrefix} — the only tree this gate enumerates, so a pin there is checked by nothing that counts coverage"
                        else
                            match materializesHere profile condition with
                            | Error expr -> violations.Add $"{manifestName}: skill `{id}` has an unreadable `materializes-when` expression: {expr}"
                            | Ok false ->
                                // NOT a silent skip. If the condition says this profile does not take
                                // the skill, the file must be ABSENT — otherwise flipping a condition
                                // is a one-line way to park arbitrary drift outside the digest check.
                                if File.Exists(path [ root; relative ]) then
                                    violations.Add $"{manifestName}: skill `{id}` is present at {relative} but `materializes-when` ({condition}) excludes this profile, so nothing pins it"
                            | Ok true ->
                                let target = path [ root; relative ]

                                if not (File.Exists target) then
                                    violations.Add $"{manifestName}: skill `{id}` should be materialized here but {relative} is missing"
                                else
                                    match tryDigest target with
                                    | Error reason -> violations.Add $"{manifestName}: {relative} {reason}"
                                    | Ok actual ->
                                        if not (digestEquals actual pinned) then
                                            violations.Add $"{manifestName}: {relative} has drifted — pinned {shortDigest pinned}, found {shortDigest actual}"
                | _ -> violations.Add $"{manifestName}: no `skills` array to check"

    let manifestPins = agentsManifestPins root

    // #46: the repository-owned complement. Checked exactly like a manifest entry, so a drifted
    // `work-board/references/deep-detail.md` — evasion route 1, and the cheapest one — now reads
    // the same as a drifted `fs-gg-collision/SKILL.md`.
    let kitPins =
        match readKitPins root with
        | Error reason ->
            violations.Add reason
            []
        | Ok pins ->
            [ for (relative, pinned) in pins do
                if String.IsNullOrWhiteSpace relative || String.IsNullOrWhiteSpace pinned then
                    violations.Add $"{kitPinsRelative}: an entry pins no path/sha256"
                elif not (withinKitSources relative) then
                    // Out-of-tree pins are refused rather than checked: nothing enumerates them for
                    // coverage, so accepting one would let the ledger grow entries that look like
                    // guarantees and are not. The condition itself now lives in `withinKitSources`,
                    // shared with the manifest pass — #62 (3) was this boundary existing here and
                    // nowhere else.
                    violations.Add $"{kitPinsRelative}: `{relative}` is outside {kitSourcePrefix}, which this ledger does not pin"
                else
                    let target = path [ root; relative ]

                    if not (File.Exists target) then
                        // A pin whose file is gone is how a deletion hides: the coverage pass below
                        // only sees files that EXIST, so removing a kit file would otherwise be silent.
                        violations.Add $"{kitPinsRelative}: {relative} is pinned but missing from this tree"
                    else
                        match tryDigest target with
                        | Error reason -> violations.Add $"{kitPinsRelative}: {relative} {reason}"
                        | Ok actual ->
                            if not (digestEquals actual pinned) then
                                violations.Add $"{kitPinsRelative}: {relative} has drifted — pinned {shortDigest pinned}, found {shortDigest actual}"

                        // Yielded whether or not the bytes could be read. A file the ledger pins IS
                        // pinned; if it cannot be read, the line above says so. Dropping it here
                        // would make the coverage pass report it as "pinned by nothing", which is
                        // false, and would silently drop its mirror from the mirror pass.
                        yield relative, pinned ]

    // #46 COVERAGE, the check the item asks for by name: every file the kit materializes is pinned
    // by something, and the ones that are not are named in the gate's own output rather than in an
    // issue. This is also what closes evasion route 2 — deleting an entry from both manifests does
    // not hide a mutated file, it strips the file of its only pin and lands it here.
    let pinnedSources =
        Seq.append (manifestPins |> Seq.map fst) (kitPins |> Seq.map fst) |> Set.ofSeq

    for relative in sourceFiles do
        if not (pinnedSources.Contains relative) then
            violations.Add
                $"coverage: {relative} is materialized in the kit but neither skill manifest nor {kitPinsRelative} pins it, so nothing would report it drifting — run `./fake.sh build -t KitPins` to pin it"

    // Both manifests pin `.agents/...` paths, but the kit is MIRRORED into `.claude/...` and the
    // copies are byte-identical by construction. Without this pass a drifted mirror is invisible:
    // no manifest names it, and provenance's `mirroredPaths` cannot be used as an oracle (§4.3 —
    // its driver pins are legitimately stale). So the mirror is held to the SAME pin as its source,
    // now over the ledger's paths as well as the manifest's — otherwise widening coverage on the
    // `.agents` side would have left all 63 of the new mirrors unguarded.
    for (relative, pinned) in Seq.append (manifestPins :> seq<string * string>) (Seq.ofList kitPins) do
        if relative.StartsWith(".agents/", StringComparison.Ordinal) then
            let mirrored = ".claude/" + relative.Substring(".agents/".Length)
            let mirroredFull = path [ root; mirrored ]

            // The mirror is only required where the SOURCE is materialized here; deleting a
            // mirror is drift, not a provider choice, or `rm -rf .claude/skills` would pass.
            if File.Exists(path [ root; relative ]) then
                if not (File.Exists mirroredFull) then
                    violations.Add $"mirror: {relative} is materialized but its mirrored copy {mirrored} is missing"
                else
                    match tryDigest mirroredFull with
                    | Error reason -> violations.Add $"mirror: {mirrored} {reason}"
                    | Ok actual ->
                        if not (digestEquals actual pinned) then
                            violations.Add $"mirror: {mirrored} differs from the digest pinned for {relative} — pinned {shortDigest pinned}, found {shortDigest actual}"

    // The mirror pass walks SOURCES, so a file added only to `.claude/skills` is named by nothing it
    // iterates. Enumerating the mirror tree too is what makes the 190 a real total rather than
    // 95 plus an assumption.
    //
    // The second arm exists because a reviewer caught the first draft COUNTING an uncovered mirror
    // without NAMING it: the summary said "2 uncovered" while only one `coverage:` line was
    // emitted. #46's acceptance is that the uncovered set is visible in the gate's output, so a
    // file that the denominator counts as uncovered has to appear by name, not by arithmetic.
    for mirrored in mirrorFiles do
        let source = ".agents/" + mirrored.Substring(".claude/".Length)

        if not (File.Exists(path [ root; source ])) then
            violations.Add $"coverage: {mirrored} mirrors no source at {source}, so no pin covers it"
        elif not (pinnedSources.Contains source) then
            violations.Add $"coverage: {mirrored} is pinned by nothing, because its source {source} is pinned by nothing"

    List.ofSeq violations

/// What `TemplateDrift` covers on this tree, as one line. #46's acceptance is that the uncovered
/// set is visible in the GATE's output; a green run reports zero uncovered, and this says so with
/// the denominator rather than leaving a reader to infer full coverage from silence.
/// The uncovered count is DERIVED from the violation list rather than recomputed, so the counter
/// and the enumeration cannot disagree — not "are checked to agree", but cannot.
///
/// The first version recomputed coverage from its own name-membership test, and a reviewer produced
/// a tree where it printed `0 uncovered` while the enumeration below it named two files as pinned
/// by nothing: `readKitPins` had returned an entry whose `sha256` was absent, which
/// `templateDriftViolations` rejects and the summary's own set-building did not. That is the exact
/// overstatement #46 was filed about, reproduced inside the fix for it — twice, in opposite
/// directions. Deriving is the only version that cannot come back.
let templateDriftCoverage (root: string) =
    let violations = templateDriftViolations root
    let uncovered = violations |> List.filter (fun v -> v.StartsWith "coverage: ") |> List.length

    // #62 (1) and (2): the denominator counts what this repository STORES. A symlinked kit root or
    // kit file is excluded by `kitTreeScan` and reported by `templateDriftViolations`, so
    // `.claude/skills -> .agents/skills` now prints `95 of 95 … plus 0 mirror(s)` beside a line
    // naming the link, instead of `190 of 190 … plus 95 mirror(s)` for 95 distinct files.
    let sources = kitTreeFiles root ".agents"
    let mirrors = kitTreeFiles root ".claude"
    let total = List.length sources + List.length mirrors
    let covered = total - uncovered

    // The breakdown is attribution: it says WHICH oracle covers what. `covered` above no longer
    // comes from it, so it cannot move the total — but it must still not NAME an oracle for a file
    // that oracle does not pin. Both halves therefore key on the same validated sets the
    // enumeration uses. A reviewer found the first version keying `byLedger` on the raw ledger, so
    // the line could report `63 by scripts/kit-pins.json` four lines above a `coverage:` line
    // naming one of those 63 as pinned by nothing: the manifest half had been fixed and its mirror
    // image left behind.
    let manifestPins = agentsManifestPins root
    let manifestPinned = manifestPins |> Seq.map fst |> Set.ofSeq

    // This repeats the enumeration's validation MINUS `File.Exists`, and that difference is
    // deliberate: a pin whose file is absent contributes no enumerated file, so it can neither be
    // counted nor named, and `templateDriftViolations` reports it separately as `pinned but
    // missing`. Adding the existence test here would be harmless today and is the kind of drift
    // that made these two computations disagree twice, so the asymmetry is stated rather than left
    // for the next reader to re-derive.
    let ledgerPinned =
        match readKitPins root with
        | Ok pins ->
            pins
            |> List.filter (fun (relative, pinned) ->
                not (String.IsNullOrWhiteSpace relative)
                && not (String.IsNullOrWhiteSpace pinned)
                && withinKitSources relative)
            |> List.map fst
            |> Set.ofList
        | Error _ -> Set.empty
    let byManifest = sources |> List.filter manifestPinned.Contains |> List.length
    let byLedger = sources |> List.filter (fun s -> not (manifestPinned.Contains s) && ledgerPinned.Contains s) |> List.length

    $"TemplateDrift: {covered} of {total} kit files pinned — {byManifest} source(s) by the generated manifest, "
    + $"{byLedger} by {kitPinsRelative}, plus {covered - byManifest - byLedger} mirror(s); "
    + $"{uncovered} uncovered. Manifest entries carrying a digest: {manifestPins.Count}."

/// The agents `.fsgg/agents.yml` declares each have their guidance target present, and where
/// generated guidance exists for one agent it exists for all of them
/// (`requireEquivalentClaudeAndCodexBehavior`). Generated guidance is a projection, never a
/// second source of truth, so this checks presence and symmetry rather than content.
let generatedGuidanceViolations (root: string) =
    let violations = ResizeArray<string>()
    let agentsFile = path [ root; ".fsgg"; "agents.yml" ]

    // #62 (4), the neighbouring seam. The item names `fileDigest`, but the same defect class — an
    // unhandled filesystem exception aborting a gate with no violation line — was one unguarded
    // `File.ReadAllLines` away in the target that runs beside it. Clearing the mode bits on
    // `.fsgg/agents.yml`
    // stopped `GeneratedGuidanceCheck` dead. Enumerating the producers of a defect class rather
    // than the ones an issue happens to name is the whole point of fixing it here.
    let read =
        if not (File.Exists agentsFile) then
            Error ".fsgg/agents.yml is missing, so the declared agent inventory cannot be checked"
        else
            try
                Ok(File.ReadAllLines agentsFile)
            with ex ->
                Error $".fsgg/agents.yml cannot be read ({ex.GetType().Name}), so the declared agent inventory cannot be checked"

    match read with
    | Error reason ->
        violations.Add reason
        List.ofSeq violations
    | Ok lines ->
        let agents = ResizeArray<string * string option * string option>()
        let mutable current = None

        let flush () =
            match current with
            | Some (id, guidance, generated) -> agents.Add(id, guidance, generated)
            | None -> ()

        // Only the `agents:` block declares agents. Without this, any later section of a
        // generated agents.yml that happens to use `- id:` items (a `commands:` list, say)
        // is read as a phantom agent and reds the gate on a correct tree.
        let mutable inAgents = false

        for line in lines do
            if Regex.IsMatch(line, @"^agents:\s*$") then
                inAgents <- true
            elif Regex.IsMatch(line, @"^\S") then
                flush ()
                current <- None
                inAgents <- false

            if inAgents then
                // Scalars are captured to end of line and then unquoted/de-commented, so
                // `guidancePath: "CLAUDE.md"` and `guidancePath: CLAUDE.md # target` both resolve.
                let idMatch = Regex.Match(line, @"^\s*-\s*id:\s*(.+?)\s*$")
                let guidanceMatch = Regex.Match(line, @"^\s*guidancePath:\s*(.+?)\s*$")
                let generatedMatch = Regex.Match(line, @"^\s*generatedRoot:\s*(.+?)\s*$")

                if idMatch.Success then
                    flush ()
                    current <- Some(yamlScalar idMatch.Groups.[1].Value, None, None)
                elif guidanceMatch.Success then
                    current <- current |> Option.map (fun (i, _, g) -> i, Some(yamlScalar guidanceMatch.Groups.[1].Value), g)
                elif generatedMatch.Success then
                    current <- current |> Option.map (fun (i, gu, _) -> i, gu, Some(yamlScalar generatedMatch.Groups.[1].Value))

        flush ()

        let requireEquivalence =
            lines |> Array.exists (fun l ->
                let m = Regex.Match(l, @"^\s*requireEquivalentClaudeAndCodexBehavior:\s*(.+?)\s*$")
                m.Success && (let v = (yamlScalar m.Groups.[1].Value).ToLowerInvariant() in v = "true" || v = "yes"))

        if agents.Count = 0 then
            violations.Add ".fsgg/agents.yml declares no agents, so nothing pins the guidance targets"

        for (id, guidance, _) in agents do
            match guidance with
            | None -> violations.Add $"agent `{id}` declares no guidancePath"
            | Some relative ->
                let target = path [ root; relative ]

                if not (File.Exists target) then
                    violations.Add $"agent `{id}` declares guidancePath {relative}, which does not exist"
                elif (FileInfo target).Length = 0L then
                    violations.Add $"agent `{id}` guidance {relative} is empty"

        // Symmetry: generated guidance is per work id, and must never exist for one agent only.
        if requireEquivalence && agents.Count > 1 then
            let leaves =
                agents
                |> Seq.choose (fun (_, _, generated) -> generated)
                |> Seq.map (fun g -> g.TrimEnd('/').Split('/') |> Array.last)
                |> List.ofSeq

            let readinessRoot = path [ root; "readiness" ]

            if Directory.Exists readinessRoot then
                for workDir in Directory.GetDirectories readinessRoot do
                    let commands = path [ workDir; "agent-commands" ]

                    if Directory.Exists commands then
                        let present =
                            Directory.GetDirectories commands
                            |> Array.map Path.GetFileName
                            |> Set.ofArray

                        for leaf in leaves do
                            if not (present.Contains leaf) then
                                let work = Path.GetFileName workDir
                                violations.Add $"readiness/{work}/agent-commands has generated guidance but none for `{leaf}`"

        List.ofSeq violations

let private runViolationCheck target (violations: string list) =
    if List.isEmpty violations then
        writeLog target
    else
        for violation in violations do
            eprintfn "%s: %s" target violation

        failwithf "%s failed: %d violation(s); see the lines above." target violations.Length

let private currentRoot () = Directory.GetCurrentDirectory()

/// #46: the coverage line is printed on EVERY run, green or red, and before the verdict. A gate
/// that speaks only when it fails leaves a reader to infer full coverage from silence — which is
/// precisely the misreading of a green `TemplateDrift` that #46 was filed to stop.
///
/// Defined ABOVE `SelfTest` so a case can drive the real target and assert the line actually
/// reaches stdout. It sat below, untested, and a mutant that simply never printed survived.
let private runTemplateDrift () =
    printfn "%s" (templateDriftCoverage (currentRoot ()))
    runViolationCheck "TemplateDrift" (templateDriftViolations (currentRoot ()))

// ---------------------------------------------------------------------------
// #57: SelfTest's RESULT must not be inferable from what SelfTest PRINTS.
//
// Every mutant that defeated this gate before #57 worked the same way: it left
// the printed transcript looking like a pass. Deleting `failures <- failures + 1`
// printed every FAIL and still exited 0; rewriting `expect`'s failure branch to
// `printfn "  ok   %s"` printed 111 ok lines and exited 0, which also defeated
// #52's ok-count == case-count CI guard. No guard that reads stdout can survive
// that class, because stdout is the thing being forged.
//
// So the verdict is carried on two channels that a transcript cannot fake:
//
//   1. a STRUCTURED result file (`selftest-result.json`) written from the same
//      recorded case list the verdict is computed from, and
//   2. the true PROCESS EXIT CODE of a child `SelfTest` run that this run
//      spawns with one deliberately-false case injected (`FSGG_SELFTEST_INJECT_FAILURE`).
//
// The child carries whatever mutation the parent carries. So a mutation that
// disarms the failure path disarms it in the child too — and the child, which
// is KNOWN to contain a failing case, then exits 0 and records 0 failures.
// That is the signal. It is checked by a direct `failwith` that is NOT routed
// through `expect`, so disarming `expect` cannot disarm the check on `expect`.
// ---------------------------------------------------------------------------

// `selfTestInjectVar` / `selfTestInjecting` are declared near `writeLog`, which needs them. Its
// presence means two things: inject one known-false case, and do not spawn a further child
// (otherwise the probe would recurse without bound).

/// Where the child is told to write its structured result, so the probe reads a file it named
/// rather than scraping the child's stdout — and so a child run never overwrites the parent's own
/// structured result. (The child writes no `readiness/logs/*.txt` at all; see `writeLog`.)
let private selfTestResultVar = "FSGG_SELFTEST_RESULT_PATH"

/// The injected case's description, shared by the child that RECORDS it and the parent that
/// requires to find it. One literal, so the two halves cannot drift apart silently.
let private selfTestInjectedDescription =
    "INJECTED failing case (the probe's control: this run MUST report a failure)"

let private selfTestResultPath () =
    match Environment.GetEnvironmentVariable selfTestResultVar with
    | null
    | "" -> path [ "readiness"; "logs"; "selftest-result.json" ]
    | explicit -> explicit

let private jsonString (value: string) =
    let builder = Text.StringBuilder()
    builder.Append '"' |> ignore

    for ch in value do
        match ch with
        | '"' -> builder.Append "\\\"" |> ignore
        | '\\' -> builder.Append "\\\\" |> ignore
        | '\n' -> builder.Append "\\n" |> ignore
        | '\r' -> builder.Append "\\r" |> ignore
        | '\t' -> builder.Append "\\t" |> ignore
        | c when c < ' ' -> builder.Append(sprintf "\\u%04x" (int c)) |> ignore
        | c -> builder.Append c |> ignore

    builder.Append('"').ToString()

/// The structured result. `cases`/`failures` are DERIVED from the recorded case list rather than
/// tracked in a counter alongside it, so there is no counter to silence: to report a false result
/// you have to falsify the recorded outcome of a case, which is what the probe below detects.
///
/// `verdict` is the WHOLE target's outcome, not just the case tally, and `probed` records whether
/// the self-probe actually ran. Both exist because a reader — and the CI guard — is told to trust
/// this file over the transcript, so it must not be able to say `failures: 0` about a run that
/// failed for a reason the tally never sees (a repository-clean raise), nor about a run that never
/// checked itself at all.
let private writeSelfTestResult (recorded: (string * bool) list) (verdict: string) (detail: string) (probed: bool) =
    let failed = recorded |> List.filter (snd >> not) |> List.map fst
    let target = selfTestResultPath ()
    let full = Path.GetFullPath target
    Directory.CreateDirectory(Path.GetDirectoryName full) |> ignore

    let body =
        String.concat
            "\n"
            [ "{"
              "  \"schemaVersion\": 2,"
              sprintf "  \"verdict\": %s," (jsonString verdict)
              sprintf "  \"detail\": %s," (jsonString detail)
              sprintf "  \"probed\": %b," probed
              sprintf "  \"cases\": %d," (List.length recorded)
              sprintf "  \"failures\": %d," (List.length failed)
              sprintf "  \"injected\": %b," (selfTestInjecting ())
              "  \"failed\": ["
              (failed |> List.map (fun d -> "    " + jsonString d) |> String.concat ",\n")
              "  ]"
              "}"
              "" ]

    File.WriteAllText(full, body)
    full

/// Reads one integer field out of the child's structured result. Deliberately NOT a read of the
/// child's stdout: the whole point is a channel the transcript cannot forge.
let private selfTestResultField (contents: string) (field: string) =
    let m = Text.RegularExpressions.Regex.Match(contents, sprintf "\"%s\"\\s*:\\s*(-?\\d+)" field)

    if m.Success then
        Some(int m.Groups[1].Value)
    else
        None

/// #57: the probe. Runs THIS script's `SelfTest` again, in a child process, with one extra case
/// that is false by construction, and requires the child to have noticed.
///
/// `expectedCases`/`expectedFailures` are the parent's own recorded totals. The child runs the
/// parent's cases plus exactly the one injected case, so the expected child totals are
/// `parent + 1` on BOTH counters — an expected-case-count assertion that is DERIVED rather than a
/// bare literal, which is what #57 asks for (and what keeps #54-style case-set changes from
/// needing a magic number updated by hand).
/// Bounds the probe. The merge gate must not be able to hang: a wedged child would otherwise burn
/// the whole CI job timeout with no diagnosis. ~45x the ~2s the child really takes, and deliberately
/// not more: this deadline is also the last line of defence against a runaway spawn chain, which
/// grows one nested process every couple of seconds until it trips.
let private selfTestProbeTimeoutMs = 90_000

let private probeSelfTestFailurePath () =
    let resultPath =
        path [ Path.GetTempPath(); "rogue3-selftest-probe-" + Guid.NewGuid().ToString("N") + ".json" ]

    let startInfo = ProcessStartInfo("dotnet", "fsi build.fsx -t SelfTest")
    startInfo.UseShellExecute <- false
    startInfo.RedirectStandardOutput <- true
    startInfo.RedirectStandardError <- true
    startInfo.WorkingDirectory <- currentRoot ()
    startInfo.Environment[selfTestInjectVar] <- "1"
    startInfo.Environment[selfTestResultVar] <- resultPath
    // Independent of the line above, on purpose — see `selfTestDepthVar`.
    startInfo.Environment[selfTestDepthVar] <- "1"

    let exitCode, childOut =
        // `Process.Start` THROWS when `dotnet` cannot be resolved — it does not return null — so the
        // launch failure has to be caught, not pattern-matched away. (`Option.ofObj` alone left the
        // crafted message below unreachable.)
        use proc =
            try
                match Process.Start startInfo |> Option.ofObj with
                | Some proc -> proc
                | None -> failwith "SelfTest self-probe could not launch `dotnet fsi build.fsx -t SelfTest`."
            with ex ->
                failwithf "SelfTest self-probe could not launch `dotnet fsi build.fsx -t SelfTest`: %s" ex.Message
        // Both streams drained concurrently: reading one to end before the other deadlocks as soon
        // as the child fills the pipe we are not reading.
        let outTask = proc.StandardOutput.ReadToEndAsync()
        let errTask = proc.StandardError.ReadToEndAsync()

        // DEADLINE, not the no-argument overload. See the note above `runProcess`: the bare
        // `WaitForExit()` additionally waits for the async readers to reach EOF, which is exactly
        // the unbounded wait this file already decided never to take without a deadline.
        if not (proc.WaitForExit selfTestProbeTimeoutMs) then
            try
                proc.Kill true
            with _ ->
                ()

            failwithf
                "SelfTest self-probe did not finish within %d ms; the child was killed. The gate cannot certify itself (#57)."
                selfTestProbeTimeoutMs

        proc.ExitCode, outTask.Result + errTask.Result

    try
        // CHANNEL 1 — the child's true process exit code.
        if exitCode = 0 then
            failwithf
                "SelfTest's own failure path is DISARMED: a child run carrying one INJECTED failing case exited 0. %s\n%s"
                "Whatever this run printed about itself is therefore not evidence (#57)."
                childOut

        // CHANNEL 2 — the child's structured result, which is not its transcript.
        if not (File.Exists resultPath) then
            failwithf
                "SelfTest's own failure path is unverifiable: the child run wrote no structured result to %s (#57).\n%s"
                resultPath
                childOut

        let contents = File.ReadAllText resultPath

        let childFailures =
            match selfTestResultField contents "failures" with
            | Some value -> value
            | None -> failwithf "The child SelfTest result at %s records no `failures` (#57).\n%s" resultPath childOut

        // Deliberately NOT arithmetic on totals. Requiring `child = parent + 1` couples the gate to
        // the two runs having identical case SETS, and they legitimately need not: the child runs
        // with extra environment, redirected stdio, and a tree the parent has already written its
        // result into, so any environment-conditional case makes an honest run red with a message
        // that blames the accounting. What actually matters is narrower and stronger — the child
        // must have NOTICED THE ONE CASE that cannot pass — so that is what is asserted.
        if childFailures < 1 then
            failwithf
                "SelfTest cannot count its own failures: the injected child recorded %d, and it contains a case that cannot pass (#57).\n%s"
                childFailures
                childOut

        if not (contents.Contains selfTestInjectedDescription) then
            failwithf
                "SelfTest did not report its own INJECTED failing case: %s is absent from the child's recorded failures at %s (#57).\n%s"
                (jsonString selfTestInjectedDescription)
                resultPath
                childOut
    finally
        try
            if File.Exists resultPath then File.Delete resultPath
        with _ ->
            ()

// ---------------------------------------------------------------------------
// #34: the checker proves itself before the gate trusts it.
//
// Each case PLANTS one violation in a synthetic tree and requires the check to
// report it. A check that cannot fail is the defect this target exists to stop
// coming back, so `SelfTest` is what makes a green `TemplateDrift` mean
// something. It also asserts both checks are clean on THIS repository, so the
// gate cannot be tightened into permanent redness without someone noticing here
// first.
// ---------------------------------------------------------------------------

let private writeFile (filePath: string) (contents: string) =
    Directory.CreateDirectory(Path.GetDirectoryName filePath: string) |> ignore
    File.WriteAllText(filePath, contents)

/// A digest of a file a fixture has just written. #62 removed the throwing `fileDigest` so no
/// verdict path can reach one; a fixture that cannot read what it wrote a line earlier is a broken
/// test rather than a finding, so this one fails LOUDLY instead of returning a violation nobody
/// would be asserting on.
let private fixtureDigest (filePath: string) =
    match tryDigest filePath with
    | Ok digest -> digest
    | Error reason -> failwithf "SelfTest fixture: %s %s" filePath reason

/// The fixture's stand-in for the 63 kit files the generated manifest does not name. Named after a
/// real one: `#46` records appending a line to this exact path as the cheapest surviving evasion.
let private unnamedRelative = ".agents/skills/work-board/references/deep-detail.md"

/// Rewrites the fixture's kit pin ledger from its current tree, through the SAME writer the
/// `KitPins` target uses. #46: the remedy the gate prints has to be the remedy the tests exercise,
/// or "run KitPins" is advice nothing has ever checked.
let private repinFixture (root: string) = writeKitPins root |> ignore

/// A minimal but structurally faithful tree: one always-materialized skill, one that this
/// profile does not take, both manifests, the agent inventory and its two guidance targets.
let private plantFixture (root: string) =
    let skillBody = "# kit skill\n"
    let skillRelative = ".agents/skills/fs-gg-kit/SKILL.md"
    writeFile (path [ root; skillRelative ]) skillBody
    // the mirrored twin the kit materializes alongside it, byte-identical by construction
    writeFile (path [ root; ".claude/skills/fs-gg-kit/SKILL.md" ]) skillBody
    let digest = fixtureDigest (path [ root; skillRelative ])
    // A skill whose condition MATCHES this profile by equality. Without it, a mutation that
    // makes the `profile == x` branch always answer "no" is invisible: the only equality entry
    // in the fixture would be one that is absent anyway.
    let equalRelative = ".agents/skills/fs-gg-equal/SKILL.md"
    writeFile (path [ root; equalRelative ]) skillBody
    writeFile (path [ root; ".claude/skills/fs-gg-equal/SKILL.md" ]) skillBody

    let manifest =
        $"""{{
  "schemaVersion": 1,
  "skills": [
    {{ "id": "fs-gg-kit", "scope": "product", "sha256": "{digest}",
       "resolvablePath": "{skillRelative}", "materializes-when": "always" }},
    {{ "id": "fs-gg-equal", "scope": "product", "sha256": "{digest}",
       "resolvablePath": "{equalRelative}", "materializes-when": "profile == game" }},
    {{ "id": "fs-gg-samples", "scope": "product", "sha256": "{digest}",
       "resolvablePath": ".agents/skills/fs-gg-samples/SKILL.md",
       "materializes-when": "profile == sample-pack" }}
  ]
}}
"""

    for owner in skillManifests do
        writeFile (path [ root; owner; "skills"; "skill-manifest.json" ]) manifest

    writeFile
        (path [ root; ".fsgg"; "scaffold-provenance.json" ])
        """{ "schemaVersion": 1, "effectiveParameters": [ { "key": "profile", "value": "game" } ] }"""

    writeFile
        (path [ root; ".fsgg"; "agents.yml" ])
        "schemaVersion: 1\nagents:\n  - id: claude\n    guidancePath: CLAUDE.md\n    generatedRoot: readiness/{workId}/agent-commands/claude\n  - id: codex\n    guidancePath: AGENTS.md\n    generatedRoot: readiness/{workId}/agent-commands/codex\npolicy:\n  requireEquivalentClaudeAndCodexBehavior: true\n"

    writeFile (path [ root; "CLAUDE.md" ]) "# guidance\n"
    writeFile (path [ root; "AGENTS.md" ]) "# guidance\n"

    // #46: a kit file NO manifest entry names, which is what 63 of this repository's 95 really are.
    // Without one in the fixture every new case would be exercised only against manifest-named
    // files — the exact blind spot the item records, reproduced inside its own regression suite.
    let unnamedBody = "# a reference no manifest names\n"
    writeFile (path [ root; unnamedRelative ]) unnamedBody
    writeFile (path [ root; ".claude/skills/work-board/references/deep-detail.md" ]) unnamedBody

    // Written LAST, from the finished tree: the ledger pins the complement, and the manifests are
    // part of that complement, so it cannot be built before they exist.
    repinFixture root
    skillRelative

let private runSelfTest () =
    let sandbox = path [ Path.GetTempPath(); "rogue3-selftest-" + Guid.NewGuid().ToString("N") ]

    // #57: ONE record per case, holding the case's outcome — not two counters running alongside a
    // transcript. `cases` and `failures` are derived from this list at the verdict, so there is no
    // counter whose deletion silences a failure, and the printing below is a REPORT of the record
    // rather than the thing the verdict is computed from.
    let recorded = ResizeArray<string * bool>()

    let expect description condition =
        recorded.Add(description, condition)

        if condition then
            printfn "  ok   %s" description
        else
            eprintfn "  FAIL %s" description

    // #57: the one case that is false by construction, present only in the child run the probe
    // spawns. A run that cannot report THIS as a failure cannot report any failure, and the parent
    // checks that from outside, on the child's exit code and structured result.
    if selfTestInjecting () then
        expect selfTestInjectedDescription false

    let freshFixture () =
        let root = path [ sandbox; Guid.NewGuid().ToString("N") ]
        Directory.CreateDirectory root |> ignore
        root, plantFixture root

    try
        // The gate must be green where it ships, or it is useless as a gate.
        expect "TemplateDrift is clean on this repository" (List.isEmpty (templateDriftViolations (currentRoot ())))
        expect "GeneratedGuidanceCheck is clean on this repository" (List.isEmpty (generatedGuidanceViolations (currentRoot ())))

        // #26 pinned the ENGINE-written `## Sensed readiness files` heading against the
        // COMMITTED graph, so an engine rename would fail here rather than leave the rule
        // abstaining forever behind a green gate. #56 untracked that graph, so on a fresh
        // checkout the pin has nothing to read: the two cases below still run
        // unconditionally, but where the file is absent they can only announce that they
        // proved nothing. A case that can go vacuous is not a pin, so the real drift check
        // moved to `emittedGraphSectionWarning`, which reads what the emitter just wrote
        // and is exercised by planted fixtures further down. These two are kept as a free
        // extra assertion over whatever graph THIS checkout last emitted.
        let lastEmittedGraph = path [ currentRoot (); "readiness"; "evidence-graph.md" ]
        let lastEmittedGraphExists = File.Exists lastEmittedGraph

        if not lastEmittedGraphExists then
            printfn
                "  note %s does not exist (rogue3#56 untracked it; it appears once this checkout runs EvidenceGraph or Verify) — the two heading-drift cases prove nothing here, and emittedGraphSectionWarning is what pins the heading"
                lastEmittedGraph

        let lastEmittedSensed =
            if lastEmittedGraphExists then
                sensedReadinessFiles (File.ReadAllText lastEmittedGraph)
            else
                None

        expect
            $"the evidence graph this checkout last emitted still has the `{sensedSectionHeading}` section the rule matches"
            (not lastEmittedGraphExists || lastEmittedSensed.IsSome)

        expect
            "the rule reads a non-empty sensed-file list from the evidence graph this checkout last emitted"
            (not lastEmittedGraphExists
             || (match lastEmittedSensed with
                 | Some entries -> not (Set.isEmpty entries)
                 | None -> false))

        // #56: the heading pin that CANNOT go vacuous. `emittedGraphSectionWarning` reads
        // the graph the emitter just wrote, so it runs in every checkout on every gate run.
        // Each case below plants a graph and asserts the warning fires or stays silent —
        // a mutant that returns `[]` unconditionally, or that drops the empty-section case,
        // is killed here.
        let headingRoot = path [ sandbox; "emitted-graph-heading" ]
        Directory.CreateDirectory headingRoot |> ignore
        let mutable headingCase = 0

        let plantEmitted (body: string) =
            headingCase <- headingCase + 1
            let file = path [ headingRoot; $"emitted-{headingCase}.md" ]
            File.WriteAllText(file, body)
            file

        expect
            "a graph carrying the sensed section warns about nothing"
            (List.isEmpty (
                emittedGraphSectionWarning (
                    plantEmitted $"# Evidence graph\n\n{sensedSectionHeading}\n\n- `readiness/layout-evidence.txt`\n"
                )
            ))

        let renamedHeadingWarning =
            emittedGraphSectionWarning (
                plantEmitted "# Evidence graph\n\n## Readiness files this run could see\n\n- `readiness/layout-evidence.txt`\n"
            )
            |> String.concat "\n"

        expect
            "an engine rename of the sensed-file heading is reported, not passed over"
            (renamedHeadingWarning.Contains sensedSectionHeading
             && renamedHeadingWarning.Contains "ABSTAIN")

        let emptySectionWarning =
            emittedGraphSectionWarning (plantEmitted $"# Evidence graph\n\n{sensedSectionHeading}\n\n## Something else\n")
            |> String.concat "\n"

        expect
            "a sensed section listing nothing is reported separately from a renamed one"
            (emptySectionWarning.Contains "listing NOTHING"
             && not (emptySectionWarning.Contains "ABSTAIN"))

        expect
            "an emission that exited 0 and wrote no graph at all is reported"
            ((emittedGraphSectionWarning (path [ headingRoot; "never-written.md" ]) |> String.concat "\n")
                .Contains "does not exist after an emission that exited 0")

        // #56: the cases above prove the PREDICATE. These prove it is INSTALLED, and
        // installed in the right ORDER — the distinction #26 paid a re-implementation
        // round to learn in this same file. Each drives `emitWithHeadingCheck` through
        // the real runner with a fake emitter and a capturing sink.
        //
        // The ordering case is the load-bearing one and it is built so that it can only
        // pass one way. A graph WITH the heading is published; the fake emitter then
        // overwrites it with a headingless graph; the publication rule sees an emission
        // that sensed nothing, refuses, and RESTORES the heading. So a check running
        // before the rule sees the headingless bytes and warns, and a check running
        // after it sees the restored heading and stays silent. Moving the call site out
        // of the emit callback flips this case red.
        let orderingRoot = path [ sandbox; "heading-install" ]
        Directory.CreateDirectory orderingRoot |> ignore

        // Returns the captured warnings, the publication outcome as an option (None when
        // the runner raised, which it does for a non-zero emitter), and the file's final
        // bytes. Capturing the warnings OUTSIDE the try means a raising run still reports
        // whether it warned first.
        let drive (publishedBody: string) (emittedBody: string) (emitExit: int) =
            let file = path [ orderingRoot; Guid.NewGuid().ToString("N") + ".md" ]
            File.WriteAllText(file, publishedBody)
            let captured = ResizeArray<string>()

            let outcome =
                try
                    Some(
                        runEvidenceGraphEmission
                            file
                            false
                            (emitWithHeadingCheck
                                file
                                (fun () ->
                                    File.WriteAllText(file, emittedBody)
                                    emitExit)
                                captured.Add)
                    )
                with _ ->
                    None

            List.ofSeq captured, outcome, File.ReadAllText file

        let withHeading = $"# Evidence graph\n\n{sensedSectionHeading}\n\n- `readiness/layout-evidence.txt`\n"
        let withoutHeading = "# Evidence graph\n\n## Something the engine renamed\n\n- `readiness/layout-evidence.txt`\n"

        let orderedWarnings, orderedOutcome, orderedFinalBytes = drive withHeading withoutHeading 0

        expect
            "the heading check runs on the EMITTED graph, before the rule restores over it"
            (orderedWarnings |> List.exists (fun line -> line.Contains "ABSTAIN"))

        // Without this, the case above would also pass if the rule had simply left the
        // headingless graph in place — and then it would prove nothing about ordering.
        expect
            "...and that run really did restore the heading afterwards, so a later check would have seen it"
            ((match orderedOutcome with
              | Some(Restored _) -> true
              | _ -> false)
             && orderedFinalBytes.Contains sensedSectionHeading)

        let cleanWarnings, cleanOutcome, _ = drive withHeading withHeading 0

        expect
            "a clean emission installs the check and it stays silent"
            (List.isEmpty cleanWarnings
             && (match cleanOutcome with
                 | Some Published -> true
                 | _ -> false))

        let failedWarnings, failedOutcome, _ = drive withHeading withoutHeading 3

        expect
            "a failed emission still fails the gate and warns about nothing"
            (failedOutcome = None && List.isEmpty failedWarnings)

        // `evidenceGraphRun` is the exact composition `Verify` runs, so drive THAT and
        // read its returned lines. This is what makes the heading warning observable
        // without a sink: the mutant that passed `ignore` for `warn` has nothing to
        // neuter here, because the production path takes no sink at all.
        //
        // It runs against the REAL evidenceGraphPath, so it is bracketed by a save and
        // restore of whatever this checkout has there — SelfTest must not be the thing
        // that destroys a graph, and `readiness/evidence-graph.md` is untracked since #56,
        // so git could not put it back.
        let liveGraph = evidenceGraphPath
        let liveGraphBackup = path [ sandbox; "live-graph-backup.md" ]
        let hadLiveGraph = File.Exists liveGraph

        if hadLiveGraph then
            File.Copy(liveGraph, liveGraphBackup, true)

        try
            Directory.CreateDirectory(Path.GetDirectoryName liveGraph) |> ignore
            File.WriteAllText(liveGraph, withHeading)

            let producedLines = evidenceGraphRun (fun () -> File.WriteAllText(liveGraph, withoutHeading); 0)

            expect
                "the composition Verify runs RETURNS the heading warning, with no sink to discard it"
                (producedLines |> List.exists (fun line -> line.Contains "ABSTAIN"))

            expect
                "...and returns the refusal report after it, in that order"
                (match producedLines |> List.tryFindIndex (fun l -> l.Contains "ABSTAIN"),
                       producedLines |> List.tryFindIndex (fun l -> l.Contains "NOT published") with
                 | Some warnAt, Some reportAt -> warnAt < reportAt
                 | _ -> false)

            File.WriteAllText(liveGraph, withHeading)

            expect
                "a clean run through that same composition returns nothing to print"
                (List.isEmpty (evidenceGraphRun (fun () -> File.WriteAllText(liveGraph, withHeading); 0)))
        finally
            if hadLiveGraph then
                File.Copy(liveGraphBackup, liveGraph, true)
            elif File.Exists liveGraph then
                File.Delete liveGraph

        // The cases above drive `emitWithHeadingCheck` directly, so they prove the
        // composition and NOT the production call site — `runEvidenceGraph` calls the real
        // engine and cannot be driven from here. A mutant that rewires `runEvidenceGraph`
        // to call the bare emitter and then warn AFTER the publication rule passed every
        // case above: the composition was still correct, it just was not used. This scan
        // closes that, and it is honest about what it is — the same text-scan convention
        // `GovernanceTests` already applies to this file. It proves the wiring is WRITTEN,
        // not that it runs; the cases above are what prove it behaves.
        // A SUBSTRING scan is not enough, and a reviewer proved it: binding the correct
        // wiring to `let _checked = …` and then calling the bare emitter satisfies every
        // "contains" test while the check runs on nothing. So this pins the whole body by
        // EQUALITY, whitespace-normalised. `runEvidenceGraph` is two lines; anything that
        // makes it longer is a rewiring and should have to say so here.
        //
        // The marker is anchored to a line start, so the literal below — which is indented
        // — cannot be found instead of the definition. The body runs to the first line
        // that is non-empty and not indented, so an unrelated helper declared after it
        // does not get swept in.
        let runEvidenceGraphBody =
            let source = (File.ReadAllText(path [ currentRoot (); "build.fsx" ])).Replace("\r\n", "\n")
            let marker = "\nlet private runEvidenceGraph () =\n"

            match source.IndexOf(marker, StringComparison.Ordinal) with
            | -1 -> None
            | start ->
                source.Substring(start + marker.Length).Split('\n')
                |> Array.takeWhile (fun line -> line.Trim() = "" || Char.IsWhiteSpace line.[0])
                |> String.concat " "
                |> fun body -> body.Split([| ' '; '\t' |], StringSplitOptions.RemoveEmptyEntries)
                |> String.concat " "
                |> Some

        expect
            "build.fsx still declares runEvidenceGraph at a line start, so this scan has a subject"
            runEvidenceGraphBody.IsSome

        expect
            "runEvidenceGraph is EXACTLY the wiring through evidenceGraphRun, with nothing else in it"
            (runEvidenceGraphBody = Some "evidenceGraphRun (fun () -> runGeneratedEvidence \"EvidenceGraph\") |> List.iter (eprintfn \"%s\")")

        let clean, _ = freshFixture ()
        expect "a faithful fixture is clean" (List.isEmpty (templateDriftViolations clean) && List.isEmpty (generatedGuidanceViolations clean))

        // A file that this profile does NOT take is absent, and that is not drift.
        expect
            "an unmaterialized skill is not reported as drift"
            (not (File.Exists(path [ clean; ".agents"; "skills"; "fs-gg-samples"; "SKILL.md" ]))
             && List.isEmpty (templateDriftViolations clean))

        let mutated, skillRelative = freshFixture ()
        File.AppendAllText(path [ mutated; skillRelative ], "drifted\n")
        let mutatedViolations = templateDriftViolations mutated
        expect "a mutated kit file is reported as drift" (mutatedViolations |> List.exists (fun v -> v.Contains "drifted" || v.Contains "SKILL.md"))
        expect "both manifests report the same mutated file" (mutatedViolations.Length = 2)

        // The mirrored copy is the hole a manifest-only check leaves: nothing names it.
        let mirrored, _ = freshFixture ()
        File.AppendAllText(path [ mirrored; ".claude/skills/fs-gg-kit/SKILL.md" ], "drifted\n")
        let mirroredViolations = templateDriftViolations mirrored
        expect "a drifted mirrored copy is reported" (mirroredViolations |> List.exists (fun v -> v.StartsWith "mirror:"))
        expect "a drifted mirror is reported exactly once" (mirroredViolations.Length = 1)

        let deleted, skillRelative = freshFixture ()
        File.Delete(path [ deleted; skillRelative ])
        expect "a deleted materialized kit file is reported" (templateDriftViolations deleted |> List.exists (fun v -> v.Contains "missing"))

        let unreadable, _ = freshFixture ()

        for owner in skillManifests do
            let manifestPath = path [ unreadable; owner; "skills"; "skill-manifest.json" ]
            File.WriteAllText(manifestPath, (File.ReadAllText manifestPath).Replace("\"always\"", "\"whenever the moon is right\""))

        expect "an unreadable materializes-when expression is reported, not skipped" (templateDriftViolations unreadable |> List.exists (fun v -> v.Contains "unreadable"))

        // A manifest is an INPUT: a malformed pin must be REPORTED, not crash the gate on the
        // one tree that most needs a verdict.
        let malformed, _ = freshFixture ()

        for owner in skillManifests do
            let manifestPath = path [ malformed; owner; "skills"; "skill-manifest.json" ]
            let text = File.ReadAllText manifestPath
            let firstDigest = Regex.Match(text, "\"sha256\": \"([0-9a-f]{64})\"").Groups.[1].Value
            File.WriteAllText(manifestPath, text.Replace(firstDigest, "abc"))

        expect "a malformed pinned digest is reported rather than crashing" (templateDriftViolations malformed |> List.exists (fun v -> v.Contains "abc"))

        // Evasion routes an earlier revision of these checks passed. Each is a real tree a
        // careless edit produces, not a contrived one.
        let deletedMirror, _ = freshFixture ()
        File.Delete(path [ deletedMirror; ".claude/skills/fs-gg-kit/SKILL.md" ])
        expect "a DELETED mirrored copy is reported" (templateDriftViolations deletedMirror |> List.exists (fun v -> v.Contains "mirrored copy" && v.Contains "missing"))

        let excluded, skillRelative = freshFixture ()
        File.AppendAllText(path [ excluded; skillRelative ], "drifted\n")

        for owner in skillManifests do
            let manifestPath = path [ excluded; owner; "skills"; "skill-manifest.json" ]
            File.WriteAllText(manifestPath, (File.ReadAllText manifestPath).Replace("\"always\"", "\"profile == sample-pack\""))

        expect
            "flipping materializes-when to park a present file outside the digest check is reported"
            (templateDriftViolations excluded |> List.exists (fun v -> v.Contains "excludes this profile"))

        let tampered, _ = freshFixture ()
        let oneManifest = path [ tampered; ".agents"; "skills"; "skill-manifest.json" ]
        File.WriteAllText(oneManifest, (File.ReadAllText oneManifest).Replace("\"scope\": \"product\"", "\"scope\": \"product\", \"note\": \"tampered\""))
        expect "editing ONE manifest alone is reported" (templateDriftViolations tampered |> List.exists (fun v -> v.Contains "disagree"))

        // RED-ON-A-CORRECT-TREE routes an independent reviewer found. Each of these trees has
        // NOTHING wrong with it, so each case asserts the gate stays QUIET.
        let upperHex, _ = freshFixture ()

        for owner in skillManifests do
            let manifestPath = path [ upperHex; owner; "skills"; "skill-manifest.json" ]
            let text = File.ReadAllText manifestPath
            let upper = Regex.Replace(text, "\"sha256\": \"([0-9a-f]{64})\"", fun m -> "\"sha256\": \"" + m.Groups.[1].Value.ToUpperInvariant() + "\"")
            File.WriteAllText(manifestPath, upper)

        // Re-emitting a manifest changes its bytes, and #46 makes the manifest itself a pinned kit
        // file — so a legitimate re-emission needs a re-pin. Doing it here is what keeps this case
        // about hex casing instead of quietly becoming a second copy of the oracle-pin case below.
        repinFixture upperHex
        expect "an UPPER-CASE hex pin is not drift" (List.isEmpty (templateDriftViolations upperHex))

        let quotedCondition, _ = freshFixture ()

        for owner in skillManifests do
            let manifestPath = path [ quotedCondition; owner; "skills"; "skill-manifest.json" ]
            File.WriteAllText(manifestPath, (File.ReadAllText manifestPath).Replace("\"profile == sample-pack\"", "\"profile == \\\"sample-pack\\\"\""))

        repinFixture quotedCondition
        expect "a QUOTED materializes-when value is still understood" (List.isEmpty (templateDriftViolations quotedCondition))

        let quotedGuidance, _ = freshFixture ()
        let agentsPath = path [ quotedGuidance; ".fsgg"; "agents.yml" ]
        File.WriteAllText(agentsPath, (File.ReadAllText agentsPath).Replace("guidancePath: CLAUDE.md", "guidancePath: \"CLAUDE.md\""))
        expect "a QUOTED guidancePath resolves" (List.isEmpty (generatedGuidanceViolations quotedGuidance))

        let commentedGuidance, _ = freshFixture ()
        let commentedPath = path [ commentedGuidance; ".fsgg"; "agents.yml" ]
        File.WriteAllText(commentedPath, (File.ReadAllText commentedPath).Replace("guidancePath: AGENTS.md", "guidancePath: AGENTS.md  # the codex target"))
        expect "an inline YAML comment after guidancePath is ignored" (List.isEmpty (generatedGuidanceViolations commentedGuidance))

        let extraSection, _ = freshFixture ()
        let extraPath = path [ extraSection; ".fsgg"; "agents.yml" ]
        File.AppendAllText(extraPath, "commands:\n  - id: specify\n  - id: plan\n")
        expect "`- id:` outside the agents block is not a phantom agent" (List.isEmpty (generatedGuidanceViolations extraSection))

        // Mutation survivors the reviewer found: these paths had no case at all.
        let noProfile, _ = freshFixture ()
        writeFile (path [ noProfile; ".fsgg"; "scaffold-provenance.json" ]) """{ "schemaVersion": 1, "effectiveParameters": [] }"""
        expect "a provenance with no profile is reported" (templateDriftViolations noProfile |> List.exists (fun v -> v.Contains "profile"))

        let noSha, _ = freshFixture ()

        for owner in skillManifests do
            let manifestPath = path [ noSha; owner; "skills"; "skill-manifest.json" ]
            File.WriteAllText(manifestPath, Regex.Replace(File.ReadAllText manifestPath, "\"sha256\": \"[0-9a-f]{64}\",", "", RegexOptions.None))

        expect "a manifest entry pinning no sha256 is reported" (templateDriftViolations noSha |> List.exists (fun v -> v.Contains "pins no"))

        let noSkills, _ = freshFixture ()

        for owner in skillManifests do
            writeFile (path [ noSkills; owner; "skills"; "skill-manifest.json" ]) """{ "schemaVersion": 1 }"""

        expect "a manifest with no skills array is reported" (templateDriftViolations noSkills |> List.exists (fun v -> v.Contains "no `skills`"))

        let noAgentList, _ = freshFixture ()
        writeFile (path [ noAgentList; ".fsgg"; "agents.yml" ]) "schemaVersion: 1\nagents:\npolicy:\n  requireEquivalentClaudeAndCodexBehavior: true\n"
        expect "an agents.yml declaring no agents is reported" (generatedGuidanceViolations noAgentList |> List.exists (fun v -> v.Contains "no agents"))

        let equalDrift, _ = freshFixture ()
        File.AppendAllText(path [ equalDrift; ".agents/skills/fs-gg-equal/SKILL.md" ], "drifted\n")
        expect "a drifted skill matched by `profile == <this profile>` is reported" (templateDriftViolations equalDrift |> List.exists (fun v -> v.Contains "fs-gg-equal"))

        let noManifest, _ = freshFixture ()
        File.Delete(path [ noManifest; ".agents"; "skills"; "skill-manifest.json" ])
        expect "a missing skill manifest is reported" (templateDriftViolations noManifest |> List.exists (fun v -> v.Contains "missing"))

        // -------------------------------------------------------------------
        // #46. The two evasion routes that survived #34's review, the coverage
        // rule that closes them, and the ledger that carries it. Every case
        // below is a tree that was GREEN on every gate before this change.
        // -------------------------------------------------------------------

        // ROUTE 1, the cheapest one: append a line to a kit file no manifest names.
        let unnamedDrift, _ = freshFixture ()
        File.AppendAllText(path [ unnamedDrift; unnamedRelative ], "drifted\n")

        expect
            "ROUTE 1: a drifted kit file that NO manifest names is reported"
            (templateDriftViolations unnamedDrift
             |> List.exists (fun v -> v.Contains kitPinsRelative && v.Contains "deep-detail.md" && v.Contains "drifted"))

        // and its mirror, which is the other half of the 190 and was pinned by nothing at all.
        let unnamedMirrorDrift, _ = freshFixture ()
        File.AppendAllText(path [ unnamedMirrorDrift; ".claude/skills/work-board/references/deep-detail.md" ], "drifted\n")

        expect
            "ROUTE 1: a drifted MIRROR of a kit file no manifest names is reported"
            (templateDriftViolations unnamedMirrorDrift
             |> List.exists (fun v -> v.StartsWith "mirror:" && v.Contains "deep-detail.md"))

        // ROUTE 2: mutate a pinned kit file and delete its entry from BOTH manifests. The
        // cross-manifest agreement check cannot see this — both files still agree. The coverage
        // rule catches it because the deletion strips the file of its only pin.
        let route2, route2Skill = freshFixture ()
        File.AppendAllText(path [ route2; route2Skill ], "drifted\n")

        for owner in skillManifests do
            let manifestPath = path [ route2; owner; "skills"; "skill-manifest.json" ]

            File.WriteAllText(
                manifestPath,
                Regex.Replace(File.ReadAllText manifestPath, @"\s*\{ ""id"": ""fs-gg-kit""[^}]*\},", "")
            )

        let route2Violations = templateDriftViolations route2

        expect
            "ROUTE 2: deleting a mutated file's entry from BOTH manifests is reported as uncovered"
            (route2Violations
             |> List.exists (fun v -> v.StartsWith "coverage: " && v.Contains "fs-gg-kit/SKILL.md"))

        expect
            "ROUTE 2: the two manifests still AGREE, so the agreement check alone would have passed"
            (route2Violations |> List.forall (fun v -> not (v.Contains "disagree")))

        // THE ORACLE IS PINNED NOW. Editing both manifests identically was invisible before: they
        // agreed, and nothing held a digest over either. The ledger pins them, so it is reported.
        let oracleEdit, _ = freshFixture ()

        for owner in skillManifests do
            let manifestPath = path [ oracleEdit; owner; "skills"; "skill-manifest.json" ]
            File.WriteAllText(manifestPath, (File.ReadAllText manifestPath).Replace("\"schemaVersion\": 1", "\"schemaVersion\": 1, \"tampered\": true"))

        expect
            "editing BOTH manifests identically is reported, because the ledger pins the oracle"
            (templateDriftViolations oracleEdit
             |> List.exists (fun v -> v.Contains kitPinsRelative && v.Contains "skill-manifest.json"))

        // A kit file INJECTED into the tree is covered by nothing and named by nothing. Before the
        // coverage rule there was no pass that enumerated files rather than pins, so this was free.
        let injected, _ = freshFixture ()
        writeFile (path [ injected; ".agents/skills/fs-gg-kit/backdoor.md" ]) "# added\n"
        writeFile (path [ injected; ".claude/skills/fs-gg-kit/backdoor.md" ]) "# added\n"

        let injectedViolations = templateDriftViolations injected

        expect
            "an INJECTED kit file no pin covers is reported BY NAME"
            (injectedViolations
             |> List.exists (fun v -> v.StartsWith "coverage: " && v.Contains ".agents/skills/fs-gg-kit/backdoor.md"))

        // A reviewer found the counter and the enumeration disagreeing: the summary COUNTED an
        // uncovered mirror that no `coverage:` line NAMED. #46's acceptance is that the uncovered
        // set is visible in the gate's output, so both halves of the pair must be named.
        expect
            "the uncovered MIRROR of an injected file is reported by name too, not merely counted"
            (injectedViolations
             |> List.exists (fun v -> v.StartsWith "coverage: " && v.Contains ".claude/skills/fs-gg-kit/backdoor.md"))

        expect
            "every file the coverage COUNTER calls uncovered is also NAMED by a coverage line"
            (let line = templateDriftCoverage injected
             let named = injectedViolations |> List.filter (fun v -> v.StartsWith "coverage: ") |> List.length
             line.Contains $"{named} uncovered")

        // …and the converse. A ledger entry with no `sha256` is REJECTED by the violation pass but
        // was still counted as a pin by the summary's own set-building, so the counter said
        // `0 uncovered` while the enumeration named two files. Both directions now, on one tree.
        let counterVsNames, _ = freshFixture ()

        writeFile
            (path [ counterVsNames; kitPinsRelative ])
            """{ "schemaVersion": 1, "pins": [ { "path": ".agents/skills/work-board/references/deep-detail.md" } ] }"""

        expect
            "a file the coverage ENUMERATION names is never counted as covered"
            (let named =
                templateDriftViolations counterVsNames
                |> List.filter (fun v -> v.StartsWith "coverage: ")
                |> List.length

             named > 0 && (templateDriftCoverage counterVsNames).Contains $"{named} uncovered")

        // SURVIVOR: every source-coverage case above also has a mirror, so the mirror arm masked
        // the source arm and a predicate excluding sources by path survived every case.
        let sourceOnly, _ = freshFixture ()
        writeFile (path [ sourceOnly; ".agents/skills/fs-gg-kit/references/orphan-source.md" ]) "# added\n"

        expect
            "an unpinned SOURCE with no mirror is reported on its own"
            (templateDriftViolations sourceOnly
             |> List.exists (fun v -> v.StartsWith "coverage: " && v.Contains ".agents/skills/fs-gg-kit/references/orphan-source.md"))

        // SURVIVOR: no fixture had a dot-prefixed kit file, so filtering them out of the
        // enumeration was invisible. A `.DS_Store` in the kit is drift like any other file.
        let dotFile, _ = freshFixture ()
        writeFile (path [ dotFile; ".agents/skills/fs-gg-kit/.DS_Store" ]) "junk\n"

        expect
            "a DOT-PREFIXED file in the kit tree is enumerated, not skipped"
            (templateDriftViolations dotFile
             |> List.exists (fun v -> v.StartsWith "coverage: " && v.Contains ".DS_Store"))

        // SURVIVOR: the only out-of-tree case pinned `build.fsx`, which fails EVERY candidate
        // prefix, so widening `kitSourcePrefix` to `.agents/` or dropping its trailing slash both
        // survived. This pins the boundary itself.
        //
        // The file is named so that NOTHING in the refusal wording appears in its path. The first
        // version of this case pinned `.agents/outside-the-kit.md` and asserted `v.Contains
        // "outside"` — which the DRIFT message satisfies via the filename, so both mutants still
        // passed. A substring assertion that the fixture's own name can satisfy is not a test.
        let ledgerBoundary, _ = freshFixture ()
        writeFile (path [ ledgerBoundary; ".agents/plain.md" ]) "# added\n"

        writeFile
            (path [ ledgerBoundary; kitPinsRelative ])
            """{ "schemaVersion": 1, "pins": [ { "path": ".agents/plain.md", "sha256": "0000000000000000000000000000000000000000000000000000000000000000" } ] }"""

        expect
            "a ledger pin under .agents/ but OUTSIDE .agents/skills/ is refused"
            (templateDriftViolations ledgerBoundary
             |> List.exists (fun v -> v.Contains kitPinsRelative && v.Contains "which this ledger does not pin"))

        // The trailing slash specifically: `.agents/skillsX/` must not satisfy `.agents/skills`.
        let ledgerSiblingDir, _ = freshFixture ()
        writeFile (path [ ledgerSiblingDir; ".agents/skillsX/plain.md" ]) "# added\n"

        writeFile
            (path [ ledgerSiblingDir; kitPinsRelative ])
            """{ "schemaVersion": 1, "pins": [ { "path": ".agents/skillsX/plain.md", "sha256": "0000000000000000000000000000000000000000000000000000000000000000" } ] }"""

        expect
            "a ledger pin in a SIBLING directory sharing the prefix is refused"
            (templateDriftViolations ledgerSiblingDir
             |> List.exists (fun v -> v.Contains kitPinsRelative && v.Contains "which this ledger does not pin"))

        // -------------------------------------------------------------------
        // #62. Four residuals an independent hostile-code critic found against
        // #46's candidate AFTER SelfTest, TemplateDrift and the suite were green.
        // None of them hides content drift — the critic appended a line to each of
        // the 190 kit files, one at a time, in 190 separate runs, and every one was
        // caught. They are COVERAGE OVERSTATEMENTS and ROBUSTNESS gaps: a tree
        // where the gate's own number is wrong, or where it says nothing at all.
        //
        // The cases below are ordered as the item numbers them. Each is written so
        // that DELETING the guard leaves the fixture GREEN or CRASHING, never
        // merely differently worded — the only shape of case that could have caught
        // these in the first place.
        // -------------------------------------------------------------------

        /// A `templateDriftViolations` run that reports a CRASH as a value. #62 (4)'s defect was an
        /// unhandled exception out of the target — "Stopped due to error" and NO violation line —
        /// so "did not abort" is the thing being asserted and must be assertable, not merely the
        /// absence of a dead `SelfTest` run.
        let violationsOrCrash (root: string) =
            try
                Ok(templateDriftViolations root)
            with ex ->
                Error(ex.GetType().Name)

        /// Creates a symbolic link, or announces that this platform refused. Windows without
        /// developer mode cannot make one; a case that cannot run must SAY SO rather than record a
        /// pass, which is the vacuous-green shape this whole target exists to prevent.
        let trySymlink (linkPath: string) (target: string) (directory: bool) =
            try
                if directory then
                    Directory.CreateSymbolicLink(linkPath, target) |> ignore
                else
                    File.CreateSymbolicLink(linkPath, target) |> ignore

                true
            with ex ->
                printfn
                    "  note this platform refused to create a symbolic link (%s); the #62 link cases below prove nothing here"
                    (ex.GetType().Name)

                false

        // #62 (1): `.claude/skills` a link to `.agents/skills`. The gate printed
        // `190 of 190 … plus 95 mirror(s)` for 95 distinct files, the mirror pass compared every
        // file with itself, and `the two skill manifests disagree` could not fire. Every digest
        // still matched, so this tree was GREEN before the walk learned what a link is.
        let linkedTreeRoot, _ = freshFixture ()
        Directory.Delete(path [ linkedTreeRoot; ".claude"; "skills" ], true)

        if trySymlink (path [ linkedTreeRoot; ".claude"; "skills" ]) (path [ linkedTreeRoot; ".agents"; "skills" ]) true then
            let linkedTreeViolations = templateDriftViolations linkedTreeRoot

            expect
                "a kit tree ROOT replaced by a symbolic link is reported"
                (linkedTreeViolations
                 |> List.exists (fun v -> v.Contains ".claude/skills" && v.Contains "symbolic link"))

            // The number, which is what the item is actually about. 4 real files, not 8.
            expect
                "…and the coverage total stops counting the same files twice through the link"
                (let line = templateDriftCoverage linkedTreeRoot
                 line.Contains "of 4 kit files" && line.Contains "plus 0 mirror(s)")

            // Without the link check this tree is CLEAN. Asserting the count pins that: the link
            // line is the only thing standing between this fixture and a green run.
            expect
                "…and the link is the ONLY finding, so nothing else was ever going to report it"
                (List.length linkedTreeViolations = 1)

        // The other half of (1): `.claude` itself is the link, so `.claude/skills` is a perfectly
        // ordinary directory reached through it and a check on `<owner>/skills` alone sees nothing.
        let linkedOwnerRoot, _ = freshFixture ()
        Directory.Delete(path [ linkedOwnerRoot; ".claude" ], true)

        if trySymlink (path [ linkedOwnerRoot; ".claude" ]) (path [ linkedOwnerRoot; ".agents" ]) true then
            expect
                "a kit OWNER directory replaced by a symbolic link is reported too, not just its skills/ child"
                (templateDriftViolations linkedOwnerRoot
                 |> List.exists (fun v -> v.StartsWith ".claude is a symbolic link"))

        // A link NESTED inside the tree, which `Directory.GetFiles(…, AllDirectories)` followed
        // silently. A check on the two roots alone would leave this route open — and a root check is
        // exactly what the item proposed.
        let linkedSubdirRoot, _ = freshFixture ()
        Directory.Delete(path [ linkedSubdirRoot; ".claude"; "skills"; "fs-gg-kit" ], true)

        if trySymlink
            (path [ linkedSubdirRoot; ".claude"; "skills"; "fs-gg-kit" ])
            (path [ linkedSubdirRoot; ".agents"; "skills"; "fs-gg-kit" ])
            true then
            expect
                "a symbolic link NESTED inside a kit tree is reported, not followed"
                (templateDriftViolations linkedSubdirRoot
                 |> List.exists (fun v -> v.Contains ".claude/skills/fs-gg-kit" && v.Contains "symbolic link"))

        // The tree walk feeds `computeKitPins`, which WRITES the ledger in the order it receives, so
        // the enumeration's ORDER is part of the contract and not an implementation detail. The
        // hand-written walk is depth-first and the array it replaced was sorted flat; the two differ
        // wherever a directory and a file share a prefix. This was not caught by reading — the first
        // version of the walk reordered six lines of the real `scripts/kit-pins.json`, a gratuitous
        // diff on a file four merged audits bind, discovered only by running `KitPins` and looking at
        // `git diff`. The fixture plants exactly that pair so a dropped sort cannot pass.
        let walkOrder, _ = freshFixture ()
        writeFile (path [ walkOrder; ".agents/skills/fs-gg-kit/section/nested.md" ]) "# nested\n"
        writeFile (path [ walkOrder; ".agents/skills/fs-gg-kit/section.md" ]) "# sibling\n"
        let walkOrderFiles = kitTreeFiles walkOrder ".agents"

        expect
            "the kit enumeration is SORTED, so the ledger it writes has a stable order"
            (walkOrderFiles
             |> List.contains ".agents/skills/fs-gg-kit/section.md"
             && walkOrderFiles |> List.contains ".agents/skills/fs-gg-kit/section/nested.md"
             && walkOrderFiles = List.sort walkOrderFiles)

        // …and the property that actually matters, asserted on THIS repository rather than on a
        // fixture: the committed ledger is byte-for-byte what the remedy would write today. A
        // fixture can only prove the walk agrees with itself; only the real tree can prove it still
        // agrees with the file 63 pins and four merged audit bindings already depend on. This is
        // read-only — it renders the content and compares, and writes nothing, so `Verify` still
        // leaves `git status --porcelain` empty (rogue3#56).
        expect
            "scripts/kit-pins.json is byte-for-byte what KitPins would write for this tree right now"
            (let root = currentRoot ()
             let manifestPinned = agentsManifestPins root |> Seq.map fst |> Set.ofSeq
             let pins, unpinnable = computeKitPins root manifestPinned

             List.isEmpty unpinnable
             && File.Exists(path [ root; kitPinsRelative ])
             && File.ReadAllText(path [ root; kitPinsRelative ]) = renderKitPins pins)

        // #62 (2): a kit SOURCE replaced by a link to identical bytes outside the repository. Every
        // digest matches — that is the point — so nothing but file TYPE can report it. The file's
        // provenance has left the tree and a fresh clone dangles.
        let linkedFileRoot, _ = freshFixture ()
        let outsideTheRepository = path [ sandbox; "carried-out-of-tree-" + Guid.NewGuid().ToString("N") + ".md" ]
        let carriedOut = path [ linkedFileRoot; unnamedRelative ]
        File.Copy(carriedOut, outsideTheRepository)
        File.Delete carriedOut

        if trySymlink carriedOut outsideTheRepository false then
            let linkedFileViolations = templateDriftViolations linkedFileRoot

            expect
                "a kit FILE replaced by a symbolic link out of the repository is reported"
                (linkedFileViolations
                 |> List.exists (fun v -> v.Contains "deep-detail.md" && v.Contains "symbolic link"))

            // Proves WHICH check fired. If this passed by digest the case would be asserting the
            // pre-existing drift check and would survive deleting everything #62 added.
            expect
                "…by TYPE and not by digest: the bytes behind the link are identical"
                (not (linkedFileViolations |> List.exists (fun v -> v.Contains "has drifted")))

            expect
                "…and a linked file is no longer counted toward the coverage total"
                ((templateDriftCoverage linkedFileRoot).Contains "of 7 kit files")

        /// Inserts one raw entry into BOTH skill manifests, keeping them byte-identical so the
        /// agreement check stays quiet and the case tests only what it means to.
        let plantManifestEntry (root: string) (entryJson: string) =
            for owner in skillManifests do
                let manifestPath = path [ root; owner; "skills"; "skill-manifest.json" ]
                let text = File.ReadAllText manifestPath
                File.WriteAllText(manifestPath, text.Replace("\"skills\": [", "\"skills\": [\n    " + entryJson + ","))

            // The manifests are themselves pinned by the ledger, so an edit to them is drift until
            // the ledger is rewritten. Re-pinning isolates the case to the entry it plants.
            repinFixture root

        // #62 (3): the ledger has refused a pin outside `.agents/skills/` since #46 and the
        // generated manifest did not, though both feed the same coverage set and the same mirror
        // pass. The digest below is CORRECT, so before this guard the tree was green.
        //
        // Nothing in the fixture's path or id can satisfy the assertion by accident: #46's own
        // ledger case was first written against a file called `outside-the-kit.md` and asserted
        // `Contains "outside"`, which the DRIFT message satisfied through the filename, so both
        // mutants passed. The phrase asserted here appears in one message in this file.
        let manifestBoundary, _ = freshFixture ()
        writeFile (path [ manifestBoundary; "README.md" ]) "# readme\n"
        let readmeDigest = fixtureDigest (path [ manifestBoundary; "README.md" ])

        plantManifestEntry
            manifestBoundary
            $"""{{ "id": "fs-gg-readme", "scope": "product", "sha256": "{readmeDigest}", "resolvablePath": "README.md", "materializes-when": "always" }}"""

        let manifestBoundaryViolations = templateDriftViolations manifestBoundary

        expect
            "a MANIFEST entry pinning a path outside the kit tree is refused, exactly as the ledger's is"
            (manifestBoundaryViolations
             |> List.exists (fun v -> v.Contains "skill-manifest.json" && v.Contains "the only tree this gate enumerates"))

        expect
            "…and the coverage line does not credit the generated manifest for it"
            ((templateDriftCoverage manifestBoundary).Contains "2 source(s) by the generated manifest")

        // The trailing slash, on the manifest side this time: `.agents/skillsX/` must not satisfy
        // `.agents/skills`. Given a real file at a matching digest, so removing the boundary makes
        // this fixture green rather than differently red.
        let manifestSiblingDir, _ = freshFixture ()
        writeFile (path [ manifestSiblingDir; ".agents/skillsX/plain.md" ]) "# added\n"
        let siblingDigest = fixtureDigest (path [ manifestSiblingDir; ".agents/skillsX/plain.md" ])

        plantManifestEntry
            manifestSiblingDir
            $"""{{ "id": "fs-gg-sibling", "scope": "product", "sha256": "{siblingDigest}", "resolvablePath": ".agents/skillsX/plain.md", "materializes-when": "always" }}"""

        expect
            "a MANIFEST entry in a sibling directory sharing the prefix is refused"
            (templateDriftViolations manifestSiblingDir
             |> List.exists (fun v -> v.Contains "skill-manifest.json" && v.Contains "the only tree this gate enumerates"))

        // `.agents/skills/../../x` satisfies the prefix. The refusal must come from the boundary and
        // not from the file being absent, which is why the assertion names the boundary's wording.
        let manifestEscape, _ = freshFixture ()

        plantManifestEntry
            manifestEscape
            """{ "id": "fs-gg-escape", "scope": "product", "sha256": "0000000000000000000000000000000000000000000000000000000000000000", "resolvablePath": ".agents/skills/../../escaped.md", "materializes-when": "always" }"""

        expect
            "a MANIFEST entry that escapes the kit tree with .. is refused"
            (templateDriftViolations manifestEscape
             |> List.exists (fun v -> v.Contains "skill-manifest.json" && v.Contains "the only tree this gate enumerates"))

        // #62 (4): every read that feeds a verdict returns a line instead of throwing.
        //
        // This first case needs no permissions and no platform: `File.ReadAllBytes` on a DIRECTORY
        // throws exactly as it did on a mode-000 file, so the total reader is pinned everywhere the
        // suite runs, including the platforms where the mode-bit cases below announce a skip.
        expect
            "a digest of something that cannot be read is a REASON, not an exception"
            (match tryDigest sandbox with
             | Error reason -> reason.Contains "cannot be read"
             | Ok _ -> false)

        /// Makes a path unreadable and answers whether it really became unreadable. A run as root
        /// ignores the mode entirely, and a case that quietly proves nothing is the defect this
        /// target exists to stop — so the probe READS BACK rather than trusting the mode it just set.
        let tryMakeUnreadable (target: string) =
            if OperatingSystem.IsWindows() then
                printfn "  note this platform has no POSIX mode bits; the #62 unreadable-input cases below prove nothing here"
                false
            else
                try
                    File.SetUnixFileMode(target, UnixFileMode.None)

                    try
                        File.ReadAllBytes target |> ignore
                        printfn "  note %s is still readable with its mode bits cleared (running as root?); the #62 unreadable-input cases prove nothing here" target
                        false
                    with _ ->
                        true
                with _ ->
                    false

        let restoreReadable (target: string) =
            if not (OperatingSystem.IsWindows()) then
                try
                    File.SetUnixFileMode(target, UnixFileMode.UserRead ||| UnixFileMode.UserWrite ||| UnixFileMode.UserExecute)
                with _ ->
                    ()

        // A kit file the gate can SEE but not OPEN. Before #62 this was an unhandled
        // `UnauthorizedAccessException`, "Stopped due to error", and no violation line at all.
        let unreadableSource, unreadableSkill = freshFixture ()
        let unreadableTarget = path [ unreadableSource; unreadableSkill ]

        if tryMakeUnreadable unreadableTarget then
            let outcome = violationsOrCrash unreadableSource
            restoreReadable unreadableTarget

            expect
                "an UNREADABLE kit file is reported rather than aborting the run"
                (match outcome with
                 | Error _ -> false
                 | Ok violations -> violations |> List.exists (fun v -> v.Contains unreadableSkill && v.Contains "cannot be read"))

        // The gate's PRIMARY oracle. The ledger has reported its own read failures since #46; the
        // manifest was parsed unguarded, so the tree the gate most needs to describe was the one it
        // could say nothing about.
        let unreadableManifest, _ = freshFixture ()
        let unreadableManifestPath = path [ unreadableManifest; ".agents"; "skills"; "skill-manifest.json" ]

        if tryMakeUnreadable unreadableManifestPath then
            let outcome = violationsOrCrash unreadableManifest
            restoreReadable unreadableManifestPath

            expect
                "an UNREADABLE skill manifest is reported rather than aborting the run"
                (match outcome with
                 | Error _ -> false
                 | Ok violations ->
                     violations
                     |> List.exists (fun v -> v.Contains ".agents/skills/skill-manifest.json" && v.Contains "cannot be read"))

        // A DIRECTORY that will not list. The tree walk is a filesystem call like any other, and
        // `Directory.GetFiles` threw the same exception from the same cause.
        let unlistableDir, _ = freshFixture ()
        let unlistableTarget = path [ unlistableDir; ".agents"; "skills"; "work-board"; "references" ]

        if not (OperatingSystem.IsWindows()) then
            let becameUnlistable =
                try
                    DirectoryInfo(unlistableTarget).UnixFileMode <- UnixFileMode.None

                    try
                        Directory.GetFileSystemEntries unlistableTarget |> ignore
                        false
                    with _ ->
                        true
                with _ ->
                    false

            if becameUnlistable then
                let outcome = violationsOrCrash unlistableDir

                try
                    DirectoryInfo(unlistableTarget).UnixFileMode <-
                        UnixFileMode.UserRead ||| UnixFileMode.UserWrite ||| UnixFileMode.UserExecute
                with _ ->
                    ()

                expect
                    "a kit DIRECTORY that cannot be listed is reported rather than aborting the run"
                    (match outcome with
                     | Error _ -> false
                     | Ok violations -> violations |> List.exists (fun v -> v.Contains "references" && v.Contains "cannot be listed"))

        // The neighbouring target, guarded in the same change for the same reason: #62 names
        // `fileDigest`, and a worker who guards only the seam an issue names has not fixed the
        // defect class. `.fsgg/agents.yml` was one unguarded `File.ReadAllLines` away.
        let unreadableAgents, _ = freshFixture ()
        let unreadableAgentsPath = path [ unreadableAgents; ".fsgg"; "agents.yml" ]

        if tryMakeUnreadable unreadableAgentsPath then
            let outcome =
                try
                    Ok(generatedGuidanceViolations unreadableAgents)
                with ex ->
                    Error(ex.GetType().Name)

            restoreReadable unreadableAgentsPath

            expect
                "an UNREADABLE .fsgg/agents.yml is reported rather than aborting GeneratedGuidanceCheck"
                (match outcome with
                 | Error _ -> false
                 | Ok violations -> violations |> List.exists (fun v -> v.Contains "agents.yml" && v.Contains "cannot be read"))

        // Malformed, not unreadable — the same class, reachable on every platform and with no
        // permissions involved, so these run where the mode-bit cases above cannot.
        let malformedManifest, _ = freshFixture ()

        for owner in skillManifests do
            writeFile (path [ malformedManifest; owner; "skills"; "skill-manifest.json" ]) "{ not json at all"

        expect
            "a MALFORMED skill manifest is reported rather than crashing the gate"
            (match violationsOrCrash malformedManifest with
             | Error _ -> false
             | Ok violations -> violations |> List.exists (fun v -> v.Contains "skill-manifest.json" && v.Contains "JSON"))

        let malformedProvenance, _ = freshFixture ()
        writeFile (path [ malformedProvenance; ".fsgg"; "scaffold-provenance.json" ]) "{ not json at all"

        expect
            "a MALFORMED scaffold-provenance.json is reported rather than aborting before any check runs"
            (match violationsOrCrash malformedProvenance with
             | Error _ -> false
             | Ok violations -> violations |> List.exists (fun v -> v.Contains "scaffold-provenance.json" && v.Contains "JSON"))

        // Parseable JSON of the WRONG SHAPE. `EnumerateArray` on an object throws, so before the
        // ValueKind tests these files were reported as unparseable — a true verdict for a false
        // reason, which is how a reader chases the wrong repair.
        let manifestSkillsShape, _ = freshFixture ()

        for owner in skillManifests do
            writeFile (path [ manifestSkillsShape; owner; "skills"; "skill-manifest.json" ]) """{ "schemaVersion": 1, "skills": { } }"""

        expect
            "a skill manifest whose `skills` is not an ARRAY is reported as having none, not as unparseable"
            (match violationsOrCrash manifestSkillsShape with
             | Error _ -> false
             | Ok violations -> violations |> List.exists (fun v -> v.Contains "no `skills` array"))

        let ledgerPinsShape, _ = freshFixture ()
        writeFile (path [ ledgerPinsShape; kitPinsRelative ]) """{ "schemaVersion": 1, "pins": { } }"""

        expect
            "a kit pin ledger whose `pins` is not an ARRAY is reported as having none, not as unparseable"
            (match violationsOrCrash ledgerPinsShape with
             | Error _ -> false
             | Ok violations -> violations |> List.exists (fun v -> v.Contains kitPinsRelative && v.Contains "no `pins`"))

        let ledgerScalarEntry, _ = freshFixture ()
        writeFile (path [ ledgerScalarEntry; kitPinsRelative ]) """{ "schemaVersion": 1, "pins": [ "not-an-object" ] }"""

        expect
            "a kit pin ledger entry that is not an OBJECT pins nothing, and says so"
            (match violationsOrCrash ledgerScalarEntry with
             | Error _ -> false
             | Ok violations -> violations |> List.exists (fun v -> v.Contains kitPinsRelative && v.Contains "pins no path/sha256"))

        // REVIEWER (PR #64): a manifest entry that keeps its `resolvablePath` but loses its
        // `sha256` is NAMED and not PINNED. Treating the name as coverage printed
        // `190 of 190 … 0 uncovered` while that source and its mirror were digest-checked by
        // nothing, and a drifted mirror went unmentioned. The tree is red either way — the manifest
        // pass reports the entry — so this was never a way to hide drift behind green; it was a way
        // to make the number lie, which is the thing this item exists to stop.
        let namedNotPinned, namedSkill = freshFixture ()

        for owner in skillManifests do
            let manifestPath = path [ namedNotPinned; owner; "skills"; "skill-manifest.json" ]
            let text = File.ReadAllText manifestPath
            // ONE entry loses its digest and keeps its path; the rest stay intact, so this is
            // precisely "named but not pinned" and not the existing whole-manifest `noSha` case.
            File.WriteAllText(manifestPath, Regex("\"sha256\": \"[0-9a-f]{64}\",").Replace(text, "", 1))

        File.AppendAllText(path [ namedNotPinned; ".claude/skills/fs-gg-kit/SKILL.md" ], "drifted\n")
        let namedNotPinnedViolations = templateDriftViolations namedNotPinned

        expect
            "a manifest entry NAMED but not PINNED leaves its source uncovered, and says so"
            (namedNotPinnedViolations
             |> List.exists (fun v -> v.StartsWith "coverage: " && v.Contains namedSkill))

        expect
            "…and its MIRROR is reported too, rather than silently unchecked"
            (namedNotPinnedViolations
             |> List.exists (fun v -> v.StartsWith "coverage: " && v.Contains ".claude/skills/fs-gg-kit/SKILL.md"))

        expect
            "…and the coverage line stops claiming those two files are pinned"
            (not ((templateDriftCoverage namedNotPinned).Contains "0 uncovered"))

        // SURVIVOR: a path pinned by BOTH oracles is checked twice rather than masked. Skipping the
        // ledger entry for a manifest-named file survived every case, because no fixture had one.
        let doublePinned, doubleSkill = freshFixture ()
        let doubleDigest = fixtureDigest (path [ doublePinned; doubleSkill ])

        writeFile
            (path [ doublePinned; kitPinsRelative ])
            $"""{{ "schemaVersion": 1, "pins": [ {{ "path": "{doubleSkill}", "sha256": "{doubleDigest}" }} ] }}"""

        File.AppendAllText(path [ doublePinned; doubleSkill ], "drifted\n")

        expect
            "a path pinned by BOTH the manifest and the ledger is checked by both, not masked"
            (let v = templateDriftViolations doublePinned
             (v |> List.exists (fun l -> l.Contains kitPinsRelative && l.Contains "has drifted"))
             && (v |> List.exists (fun l -> l.Contains "skill-manifest.json" && l.Contains "has drifted")))

        // REVIEWER (PR #64), the mirror image of the manifest fault above: `byLedger` keyed on the
        // RAW ledger, so a digestless ledger entry was attributed to `scripts/kit-pins.json` in the
        // same breath as a `coverage:` line naming that file as pinned by nothing. The existing
        // `ledgerNoSha` and `counterVsNames` fixtures build exactly this tree and assert only the
        // COUNT, which is why both passed. This asserts the ATTRIBUTION.
        let ledgerAttribution, _ = freshFixture ()

        writeFile
            (path [ ledgerAttribution; kitPinsRelative ])
            """{ "schemaVersion": 1, "pins": [ { "path": ".agents/skills/work-board/references/deep-detail.md" } ] }"""

        expect
            "an INVALID ledger entry is attributed to no oracle, not counted as a ledger pin"
            (let line = templateDriftCoverage ledgerAttribution
             line.Contains $"0 by {kitPinsRelative}")

        expect
            "…and the mirror count drops with it, rather than crediting a mirror of an unpinned source"
            ((templateDriftCoverage ledgerAttribution).Contains "plus 2 mirror(s)")

        // SURVIVOR: the breakdown numbers were asserted by nothing, so halving one of them passed.
        let breakdown, _ = freshFixture ()

        expect
            "the coverage line attributes each source to the oracle that actually pins it"
            (let line = templateDriftCoverage breakdown
             // fixture: fs-gg-kit + fs-gg-equal are manifest-named; the manifest and the unnamed
             // reference file are the ledger's; every present source has a mirror.
             line.Contains "2 source(s) by the generated manifest"
             && line.Contains $"2 by {kitPinsRelative}"
             && line.Contains "plus 4 mirror(s)")

        // SURVIVOR: `templateDriftCoverage` was tested, but the TARGET that prints it was not — so
        // a mutant that simply never printed the line passed, while #46's acceptance is that the
        // uncovered set is visible in the GATE's own output.
        let printed =
            let previous = Console.Out
            use captured = new StringWriter()
            Console.SetOut captured

            try
                try
                    runTemplateDrift ()
                with _ ->
                    ()
            finally
                Console.SetOut previous

            captured.ToString()

        expect "the TemplateDrift target PRINTS the coverage line, not just computes it" (printed.Contains "kit files pinned" && printed.Contains "uncovered")

        // Injected into the MIRROR tree only, where no source exists to walk from.
        let injectedMirror, _ = freshFixture ()
        writeFile (path [ injectedMirror; ".claude/skills/fs-gg-kit/orphan.md" ]) "# added\n"

        expect
            "a file added to the MIRROR tree alone is reported as mirroring no source"
            (templateDriftViolations injectedMirror
             |> List.exists (fun v -> v.Contains "orphan.md" && v.Contains "mirrors no source"))

        // Deleting an unnamed kit file outright. The coverage pass only sees files that EXIST, so
        // this has to be caught by the ledger entry outliving its file.
        let unnamedDeleted, _ = freshFixture ()
        File.Delete(path [ unnamedDeleted; unnamedRelative ])

        expect
            "DELETING a kit file no manifest names is reported"
            (templateDriftViolations unnamedDeleted
             |> List.exists (fun v -> v.Contains "deep-detail.md" && v.Contains "missing"))

        // The ledger is an INPUT, and an input that vanishes must not read as "nothing to check".
        let noLedger, _ = freshFixture ()
        File.Delete(path [ noLedger; kitPinsRelative ])
        let noLedgerViolations = templateDriftViolations noLedger

        expect
            "a MISSING kit pin ledger is reported, not treated as no pins to check"
            (noLedgerViolations |> List.exists (fun v -> v.Contains kitPinsRelative && v.Contains "missing"))

        expect
            "with the ledger gone, every file it pinned is reported as uncovered"
            (noLedgerViolations |> List.exists (fun v -> v.StartsWith "coverage: " && v.Contains "deep-detail.md"))

        let malformedLedger, _ = freshFixture ()
        writeFile (path [ malformedLedger; kitPinsRelative ]) "{ not json at all"

        expect
            "a MALFORMED kit pin ledger is reported rather than crashing the gate"
            (templateDriftViolations malformedLedger
             |> List.exists (fun v -> v.Contains kitPinsRelative && v.Contains "JSON"))

        let ledgerNoPins, _ = freshFixture ()
        writeFile (path [ ledgerNoPins; kitPinsRelative ]) """{ "schemaVersion": 1 }"""

        expect
            "a kit pin ledger with no `pins` array is reported"
            (templateDriftViolations ledgerNoPins
             |> List.exists (fun v -> v.Contains kitPinsRelative && v.Contains "no `pins`"))

        let ledgerNoSha, _ = freshFixture ()
        writeFile (path [ ledgerNoSha; kitPinsRelative ]) """{ "schemaVersion": 1, "pins": [ { "path": ".agents/skills/work-board/references/deep-detail.md" } ] }"""

        expect
            "a ledger entry pinning no sha256 is reported"
            (templateDriftViolations ledgerNoSha
             |> List.exists (fun v -> v.Contains kitPinsRelative && v.Contains "pins no path/sha256"))

        // A pin outside the kit tree is refused. Accepting one would let the ledger grow entries
        // that read as guarantees while no coverage pass ever enumerates what they cover.
        let ledgerOutside, _ = freshFixture ()
        writeFile
            (path [ ledgerOutside; kitPinsRelative ])
            """{ "schemaVersion": 1, "pins": [ { "path": "build.fsx", "sha256": "0000000000000000000000000000000000000000000000000000000000000000" } ] }"""

        expect
            "a ledger pin OUTSIDE the kit tree is refused"
            (templateDriftViolations ledgerOutside
             |> List.exists (fun v -> v.Contains kitPinsRelative && v.Contains "which this ledger does not pin"))

        // `.agents/skills/../..` satisfies the prefix test. Counting it would inflate the covered
        // total with a file no coverage pass enumerates — the overstatement this item is about.
        let ledgerEscape, _ = freshFixture ()
        writeFile
            (path [ ledgerEscape; kitPinsRelative ])
            """{ "schemaVersion": 1, "pins": [ { "path": ".agents/skills/../../escaped.md", "sha256": "0000000000000000000000000000000000000000000000000000000000000000" } ] }"""

        expect
            "a ledger pin that ESCAPES the kit tree with .. is refused"
            (templateDriftViolations ledgerEscape
             |> List.exists (fun v -> v.Contains kitPinsRelative && v.Contains "which this ledger does not pin"))

        // The remedy the gate prints has to work, or the gate is red with no way out and gets
        // ignored — the failure mode every check in this file is written to avoid.
        let repinned, _ = freshFixture ()
        File.AppendAllText(path [ repinned; unnamedRelative ], "a deliberate edit\n")
        File.AppendAllText(path [ repinned; ".claude/skills/work-board/references/deep-detail.md" ], "a deliberate edit\n")
        let beforeRepin = templateDriftViolations repinned
        repinFixture repinned

        expect
            "the KitPins remedy makes a DELIBERATELY edited kit clean again"
            (not (List.isEmpty beforeRepin) && List.isEmpty (templateDriftViolations repinned))

        // The coverage line is the item's acceptance: the uncovered set is visible in the GATE's
        // own output. Asserting the wording, not just the predicate — an unasserted diagnostic is
        // how a count silently stops being reported.
        let coverageClean, _ = freshFixture ()

        expect
            "the coverage line reports ZERO uncovered on a faithful fixture"
            ((templateDriftCoverage coverageClean).Contains "0 uncovered")

        let coverageDirty, _ = freshFixture ()
        writeFile (path [ coverageDirty; ".agents/skills/fs-gg-kit/unpinned.md" ]) "# added\n"

        expect
            "the coverage line COUNTS an uncovered kit file"
            (let line = templateDriftCoverage coverageDirty
             line.Contains "1 uncovered" && not (line.Contains "0 uncovered"))

        // The denominator is counted INDEPENDENTLY here — with `Directory.GetFiles` rather than
        // through `kitTreeFiles`, which the line itself uses — so a mutation that shrinks the
        // enumeration cannot satisfy both sides. A literal (`of 190 kit files`) would have been a
        // stronger tripwire and a worse test: re-materializing the kit with one more skill would
        // red the gate on a correct tree, which is the failure this file exists to avoid, and #52
        // is about to run these cases on every pull request.
        let realTotal =
            [ ".agents"; ".claude" ]
            |> List.sumBy (fun owner ->
                let treeRoot = path [ currentRoot (); owner; "skills" ]

                if Directory.Exists treeRoot then
                    (Directory.GetFiles(treeRoot, "*", SearchOption.AllDirectories)).Length
                else
                    0)

        expect
            "the coverage line reports the real repository's kit total, counted independently"
            (let line = templateDriftCoverage (currentRoot ())
             realTotal > 0 && line.Contains $"of {realTotal} kit files" && line.Contains "0 uncovered")

        let noGuidance, _ = freshFixture ()
        File.Delete(path [ noGuidance; "AGENTS.md" ])
        expect "a missing guidance target is reported" (generatedGuidanceViolations noGuidance |> List.exists (fun v -> v.Contains "AGENTS.md"))

        let emptyGuidance, _ = freshFixture ()
        File.WriteAllText(path [ emptyGuidance; "CLAUDE.md" ], "")
        expect "an empty guidance target is reported" (generatedGuidanceViolations emptyGuidance |> List.exists (fun v -> v.Contains "empty"))

        let lopsided, _ = freshFixture ()
        Directory.CreateDirectory(path [ lopsided; "readiness"; "015-work"; "agent-commands"; "claude" ]) |> ignore
        expect "generated guidance for one agent only is reported" (generatedGuidanceViolations lopsided |> List.exists (fun v -> v.Contains "codex"))

        let balanced, _ = freshFixture ()

        for agent in [ "claude"; "codex" ] do
            Directory.CreateDirectory(path [ balanced; "readiness"; "015-work"; "agent-commands"; agent ]) |> ignore

        expect "generated guidance for every agent is clean" (List.isEmpty (generatedGuidanceViolations balanced))

        let noAgents, _ = freshFixture ()
        File.Delete(path [ noAgents; ".fsgg"; "agents.yml" ])
        expect "a missing agent inventory is reported" (generatedGuidanceViolations noAgents |> List.exists (fun v -> v.Contains "agents.yml"))

        // -------------------------------------------------------------------
        // #26: the evidence-graph publication rule. Each case plants a graph the
        // emitter could really have produced in a checkout that cannot see the
        // whole readiness tree, and requires the rule to refuse to publish it.
        // The engine emitter is not invoked here — the rule is exercised over the
        // bytes it would have written, which is exactly the incomplete-input case.
        // -------------------------------------------------------------------

        let graphFixture (entries: string list) =
            let bullets = entries |> List.map (fun entry -> $"- `{entry}`") |> String.concat "\n"

            "# Evidence graph\n\n"
            + $"- readiness files present: {List.length entries}\n"
            + "- recognized evidence nodes: 2\n\n"
            + $"## Sensed readiness files\n\n{bullets}\n\n"
            + "## Evidence nodes\n\n| Artifact | Kind | State |\n|---|---|---|\n"
            + "| `readiness/layout-evidence.txt` | layout | present-valid |\n"

        // Two tracked roll-ups, and two gitignored logs Verify writes only AFTER
        // the graph is emitted — the shape observed on a clean worktree of 7d9d442.
        let committedEntries =
            [ "readiness/012-m11-playability-visual-legibility/ship-verdict.json"
              "readiness/evidence-audit.md"
              "readiness/logs/Dev.txt"
              "readiness/logs/Test.txt" ]

        let droppedLogs = [ "readiness/logs/Dev.txt"; "readiness/logs/Test.txt" ]
        let narrowerEntries = [ "readiness/012-m11-playability-visual-legibility/ship-verdict.json"; "readiness/evidence-audit.md" ]

        let parsedOrEmpty markdown = sensedReadinessFiles markdown |> Option.defaultValue Set.empty
        let parsed = parsedOrEmpty (graphFixture committedEntries)
        expect "every sensed bullet is read as an entry" (parsed = Set.ofList committedEntries)
        expect "the section's own counter line is not read as an entry" (parsed |> Set.forall (fun entry -> not (entry.Contains "files present")))
        expect "an evidence-node TABLE row is not read as a sensed entry" (not (parsed.Contains "readiness/layout-evidence.txt"))

        // The bullet guards, each exercised on its own so none of them is decorative.
        // These compare the WHOLE set: asserting only that the well-formed name is
        // absent would pass even when a loosened guard admits a mangled substring of
        // it, which is exactly how a dropped guard hides.
        let unterminated =
            parsedOrEmpty ((graphFixture committedEntries).Replace("- `readiness/evidence-audit.md`", "- `readiness/evidence-audit.md"))

        let withoutAudit = committedEntries |> List.filter (fun entry -> entry <> "readiness/evidence-audit.md") |> Set.ofList
        expect "a bullet with no CLOSING backtick yields no entry at all, mangled or otherwise" (unterminated = withoutAudit)

        let withPlainBullet =
            parsedOrEmpty ((graphFixture committedEntries).Replace("## Sensed readiness files\n\n", "## Sensed readiness files\n\n- readiness/plain-bullet.txt\n"))

        expect "an unquoted bullet INSIDE the sensed section is ignored, not parsed" (withPlainBullet = Set.ofList committedEntries)

        expect "an empty backtick bullet is not an entry" (parsedOrEmpty "## Sensed readiness files\n\n- ``\n" |> Set.isEmpty)

        // Section scoping: bullets outside the sensed section must not count, or an
        // emitter that lists what it COULD NOT sense would look like a superset.
        let maskedFixture =
            (graphFixture narrowerEntries).Replace(
                "## Evidence nodes",
                "## Could not sense\n\n- `readiness/logs/Dev.txt`\n- `readiness/logs/Test.txt`\n\n## Evidence nodes"
            )

        expect "backticked bullets OUTSIDE the sensed section are not counted as sensed" (parsedOrEmpty maskedFixture = Set.ofList narrowerEntries)
        expect "a graph with no sensed section at all parses as absent, not empty" ((sensedReadinessFiles "# Evidence graph\n\n- `readiness/x`\n").IsNone)

        let graphRoot = path [ sandbox; "evidence-graph" ]
        Directory.CreateDirectory graphRoot |> ignore
        let mutable graphCase = 0

        /// Publishes `previousText`, snapshots it the way the runner does, then stands
        /// in for the emitter by overwriting the file with what THIS checkout sensed.
        let plantedText (publishPrevious: string -> unit) (emittedText: string) publishSmaller =
            graphCase <- graphCase + 1
            let graphPath = path [ graphRoot; $"graph-{graphCase}.md" ]
            let published = path [ graphRoot; $"graph-{graphCase}.published.md" ]
            publishPrevious graphPath
            File.Copy(graphPath, published, true)
            File.WriteAllText(graphPath, emittedText)
            applyEvidenceGraphPublicationRule graphPath published publishSmaller, graphPath, published

        let planted previousEntries emittedEntries publishSmaller =
            plantedText (fun target -> File.WriteAllText(target, graphFixture previousEntries)) (graphFixture emittedEntries) publishSmaller

        let sameBytes a b = File.ReadAllBytes a = File.ReadAllBytes b

        let sameOutcome, _, _ = planted committedEntries committedEntries false
        expect "a graph that sensed the same inputs is published" (sameOutcome = Published)

        let widerEntries = "readiness/014-m13/critic-history.md" :: committedEntries
        let widerOutcome, widerPath, _ = planted committedEntries widerEntries false
        expect "a graph that sensed MORE is published" (widerOutcome = Published)
        expect "a published wider graph keeps the newly sensed entry" ((File.ReadAllText widerPath).Contains "critic-history.md")

        let narrowerOutcome, narrowerPath, narrowerPublished = planted committedEntries narrowerEntries false
        expect "a graph that sensed LESS names exactly the dropped inputs" (narrowerOutcome = Restored droppedLogs)
        expect "a graph that sensed LESS is restored byte for byte" (sameBytes narrowerPath narrowerPublished)

        // The shape actually observed: inputs dropped AND one gained, so a rule that
        // only looked for additions would publish it. This fixture still SHRINKS
        // (4 -> 3), so it does not by itself pin the set semantics — the equal-count
        // swap below is what does that.
        let mixedOutcome, mixedPath, mixedPublished = planted committedEntries ("readiness/014-m13/critic-history.md" :: narrowerEntries) false
        expect "dropping some inputs while gaining another is still refused" (mixedOutcome = Restored droppedLogs)
        expect "a refused graph does not keep the entry it gained" (sameBytes mixedPath mixedPublished)

        let overriddenOutcome, overriddenPath, overriddenPublished = planted committedEntries narrowerEntries true
        expect "an explicit publish request publishes the smaller graph" (overriddenOutcome = PublishedSmaller droppedLogs)
        expect "an explicitly published smaller graph is NOT restored" (not (sameBytes overriddenPath overriddenPublished))

        // The restore hands back the ORIGINAL bytes, not a re-serialization of them.
        // CRLF endings, a missing trailing newline and a byte-order mark are exactly
        // what a read-text/write-text round trip silently normalises away.
        let crlfText = (graphFixture committedEntries).Replace("\n", "\r\n").TrimEnd('\r', '\n')
        let crlfOutcome, crlfPath, crlfPublished = plantedText (fun target -> File.WriteAllText(target, crlfText)) (graphFixture narrowerEntries) false
        expect "CRLF bullets are still read as sensed entries" (crlfOutcome = Restored droppedLogs)
        expect "the restore is byte-exact, including line endings and a missing trailing newline" (sameBytes crlfPath crlfPublished)

        let bomOutcome, bomPath, bomPublished =
            plantedText (fun target -> File.WriteAllText(target, graphFixture committedEntries, Text.UTF8Encoding true)) (graphFixture narrowerEntries) false

        expect "a byte-order mark does not hide the first sensed entry" (bomOutcome = Restored droppedLogs)
        expect "the restore preserves a byte-order mark" (sameBytes bomPath bomPublished)

        // An emitter that exits 0 and writes NOTHING has dropped everything. That is
        // the rule's worst case, not an IO error for the caller to trip over.
        graphCase <- graphCase + 1
        let vanishedPath = path [ graphRoot; $"graph-{graphCase}.md" ]
        let vanishedPublished = path [ graphRoot; $"graph-{graphCase}.published.md" ]
        File.WriteAllText(vanishedPath, graphFixture committedEntries)
        File.Copy(vanishedPath, vanishedPublished, true)
        File.Delete vanishedPath
        expect
            "an emission that wrote no graph at all is treated as dropping everything"
            (applyEvidenceGraphPublicationRule vanishedPath vanishedPublished false = Restored(List.sort committedEntries))

        expect "restoring after a vanished emission puts the file back" (File.Exists vanishedPath && sameBytes vanishedPath vanishedPublished)

        // SETS, not counts. Two dropped and two gained is the same cardinality, so a
        // rule that compared sizes — or only looked for additions — publishes it.
        let swappedOutcome, swappedPath, swappedPublished =
            planted committedEntries (narrowerEntries @ [ "readiness/logs/Run.txt"; "readiness/016-later/ship-verdict.json" ]) false

        expect "an equal-COUNT emission that swapped two inputs for two others is refused" (swappedOutcome = Restored droppedLogs)
        expect "an equal-count refusal is restored byte for byte" (sameBytes swappedPath swappedPublished)

        // A published graph with no sensed section leaves nothing to compare, and the
        // rule must say so rather than silently behave like a clean publish.
        graphCase <- graphCase + 1
        let sectionlessPath = path [ graphRoot; $"graph-{graphCase}.md" ]
        let sectionlessPublished = path [ graphRoot; $"graph-{graphCase}.published.md" ]
        File.WriteAllText(sectionlessPath, "# Evidence graph\n\n- readiness files present: 0\n")
        File.Copy(sectionlessPath, sectionlessPublished, true)
        File.WriteAllText(sectionlessPath, graphFixture narrowerEntries)

        expect
            "a published graph with no sensed section makes the rule ABSTAIN, not pass"
            (match applyEvidenceGraphPublicationRule sectionlessPath sectionlessPublished false with
             | Unevaluatable _ -> true
             | _ -> false)

        // The refusal report is the only thing an operator sees. An unasserted
        // diagnostic is how the present-vs-absent distinction stops being made.
        let reportLines = evidenceGraphPublicationReport "readiness/evidence-graph.md" (Restored droppedLogs)
        let reportText = String.concat "\n" reportLines
        expect "the refusal report names every dropped input" (droppedLogs |> List.forall (fun entry -> reportText.Contains entry))
        expect "the refusal report states that nothing was published" (reportText.Contains "NOT published")
        expect "a dropped input absent from disk is reported as absent" (reportText.Contains "absent from this checkout")

        // #56: git can no longer reset this artifact, so a refusal that named no local
        // remedy would pin a checkout to an emission it cannot re-derive — a gate with no
        // bounded route to green, which is the failure rogue3#38 is a record of.
        expect
            "the refusal report names the local route back to a graph derived from this checkout"
            (reportText.Contains "NOT tracked" && reportText.Contains "delete" && reportText.Contains "re-run")

        graphCase <- graphCase + 1
        let presentEntry = path [ graphRoot; $"present-{graphCase}.txt" ]
        File.WriteAllText(presentEntry, "here\n")

        expect
            "a dropped input that IS on disk is reported differently from an absent one"
            ((evidenceGraphPublicationReport "readiness/evidence-graph.md" (Restored [ presentEntry ])
              |> String.concat "\n")
                .Contains "PRESENT but not sensed")

        expect "a clean publication reports nothing" (List.isEmpty (evidenceGraphPublicationReport "readiness/evidence-graph.md" Published))

        // The env reader decides which way the escape hatch points, and a green suite
        // that never calls it cannot tell an inverted polarity from a correct one.
        let withPublishVariable value body =
            let restore = Environment.GetEnvironmentVariable evidenceGraphPublishVariable
            Environment.SetEnvironmentVariable(evidenceGraphPublishVariable, value)

            try
                body ()
            finally
                Environment.SetEnvironmentVariable(evidenceGraphPublishVariable, restore)

        expect "an unset publish variable does NOT request publication" (withPublishVariable null evidenceGraphPublishRequested = false)
        expect "publish variable = 1 requests publication" (withPublishVariable "1" evidenceGraphPublishRequested)
        expect "publish variable = 0 does NOT request publication" (withPublishVariable "0" evidenceGraphPublishRequested = false)
        expect "publish variable = false does NOT request publication" (withPublishVariable "false" evidenceGraphPublishRequested = false)
        expect "an empty publish variable does NOT request publication" (withPublishVariable "" evidenceGraphPublishRequested = false)

        // ---- the RULE INSTALLED, not just the rule correct ----
        // Everything above proves the predicate. These drive the runner that wires it
        // into Verify, with a stub emitter, so that removing the snapshot, skipping
        // the rule, or ignoring the emitter's exit code cannot pass green.
        let drive previousEntries emittedEntries publishSmaller exitCode =
            graphCase <- graphCase + 1
            let graphPath = path [ graphRoot; $"driven-{graphCase}.md" ]
            let reference = path [ graphRoot; $"driven-{graphCase}.reference.md" ]
            File.WriteAllText(graphPath, graphFixture previousEntries)
            File.Copy(graphPath, reference, true)

            let outcome =
                runEvidenceGraphEmission graphPath publishSmaller (fun () ->
                    File.WriteAllText(graphPath, graphFixture emittedEntries)
                    exitCode)

            outcome, graphPath, reference

        let drivenOutcome, drivenPath, drivenReference = drive committedEntries narrowerEntries false 0
        expect "the runner applies the rule to a degraded emission" (drivenOutcome = Restored droppedLogs)
        expect "the runner restores the published bytes" (sameBytes drivenPath drivenReference)

        let drivenWiderOutcome, drivenWiderPath, drivenWiderReference = drive committedEntries widerEntries false 0
        expect "the runner publishes an emission that sensed more" (drivenWiderOutcome = Published)
        expect "the runner leaves a published emission alone" (not (sameBytes drivenWiderPath drivenWiderReference))

        expect
            "the runner still fails the gate when the emitter exits non-zero"
            (try
                drive committedEntries narrowerEntries false 3 |> ignore
                false
             with ex ->
                 ex.Message.Contains "exit code 3")

        // A fault after the emitter ran must not leave the degraded graph behind with
        // the only good copy deleted — the backup exists precisely for this moment.
        graphCase <- graphCase + 1
        let faultedPath = path [ graphRoot; $"faulted-{graphCase}.md" ]
        let faultedReference = path [ graphRoot; $"faulted-{graphCase}.reference.md" ]
        File.WriteAllText(faultedPath, graphFixture committedEntries)
        File.Copy(faultedPath, faultedReference, true)

        expect
            "a fault after emission is not swallowed"
            (try
                runEvidenceGraphEmission faultedPath false (fun () ->
                    File.WriteAllText(faultedPath, graphFixture narrowerEntries)
                    failwith "emitter blew up after writing")
                |> ignore

                false
             with ex ->
                 ex.Message.Contains "blew up")

        expect "a fault after emission still restores the published bytes" (sameBytes faultedPath faultedReference)

        // The runner has no published file to protect on a first emission.
        graphCase <- graphCase + 1
        let firstPath = path [ graphRoot; $"first-{graphCase}.md" ]

        expect
            "a first emission with nothing published yet is published"
            (runEvidenceGraphEmission firstPath false (fun () ->
                File.WriteAllText(firstPath, graphFixture committedEntries)
                0) = Published)

        expect "a first emission keeps what the emitter wrote" (File.Exists firstPath && (File.ReadAllText firstPath).Contains "logs/Dev.txt")
    finally
        if Directory.Exists sandbox then
            try Directory.Delete(sandbox, true) with _ -> ()

    // #57: DERIVED from the case record, at the moment of the verdict.
    let recorded = List.ofSeq recorded
    let cases = List.length recorded
    let failures = recorded |> List.filter (snd >> not) |> List.length
    let mutable probed = false

    printfn "SelfTest: %d case(s), %d failure(s)" cases failures

    // #57: the result file is written LAST, from a `finally`-equivalent on both paths, because it
    // reports the WHOLE target's verdict. Written before the assertions below, it would say
    // `failures: 0` about a run that then failed on a repository-clean raise — and the banner and
    // the CI guard both tell their reader to trust this file over the transcript, so it must never
    // be able to describe a failed run as a passing one.
    let publish verdict detail =
        let resultPath = writeSelfTestResult recorded verdict detail probed
        printfn "SelfTest: structured result written to %s — read THAT, not this transcript (#57)." resultPath

    try
        // A gate that asserted nothing is not a pass, and this run is the only thing that can say
        // so about itself: with the case record emptied, the child's record is empty too.
        if cases < 1 then
            failwith "SelfTest recorded 0 case(s) — a gate that asserts nothing is not a pass (#57)."

        // #57: prove the failure path still works before believing a zero. The child run carries
        // this run's mutations, so if the failure path is disarmed here it is disarmed there too —
        // and there it has a case that MUST fail. Skipped in the child, which would else recurse.
        if not (selfTestIsChild ()) then
            probeSelfTestFailurePath ()
            probed <- true

        // Independent of the branch above, so that rewriting `if not (selfTestIsChild ())` to
        // `if false` — ONE edit that would otherwise remove BOTH probe channels at once and leave
        // a result file byte-identical to an honest run — is itself caught.
        if not (selfTestIsChild ()) && not probed then
            failwith "SelfTest did not probe its own failure path, so its verdict is not evidence (#57)."

        if failures > 0 then
            failwithf "SelfTest failed: %d of %d case(s)." failures cases

        // #57 / M15: the repository-clean assertions above are booleans, and a boolean can be
        // rewritten to `true` in one edit. These are the same assertions carried by a RAISE instead —
        // the real checks, over the real root, with nothing catching them. To make the gate vacuous
        // over this repository you now have to edit BOTH the `expect` conditions and these, because
        // each catches the mutation that defeats the other (measured: neutering `runViolationCheck`
        // is caught by the `expect`s, and rewriting the `expect` conditions to `true` by these).
        //
        // Deliberately AFTER the case verdict: a genuinely drifted tree should report every case and
        // fail on the summary, not abort on the first violation with no transcript.
        //
        // #57 / M21: and both are evaluated over the same tree from a DIFFERENT WORKING DIRECTORY.
        // M21's whole trick is `if root = Directory.GetCurrentDirectory() then []` — an exemption
        // that fires only for the real repository, while every fixture case (which uses a temp root)
        // still passes. Normalising the comparison (`Path.GetFullPath root = Path.GetFullPath cwd`)
        // defeats a mere second SPELLING of the root. It does not defeat this: the process is
        // standing somewhere else entirely, so `root` is the repository and the current directory is
        // not, and no root-vs-cwd equality can hold however it is normalised. Both check functions
        // derive every path they touch from `root`, so relocating the process cannot change their
        // answers.
        //
        // BOTH checks, not just TemplateDrift: covering one and not the other left the identical
        // one-line exemption on `generatedGuidanceViolations` as a live gate defeat.
        let driftFromElsewhere, guidanceFromElsewhere =
            let here = Directory.GetCurrentDirectory()
            let realRoot = Path.GetFullPath here

            // A directory this code CREATES, not `Path.GetTempPath()` itself. `GetTempPath` honours
            // $TMPDIR, so `TMPDIR=$PWD` would relocate the process to the repository root and turn
            // this entire defence into a silent no-op — measured: the cwd-keyed exemption went from
            // caught to green, with drift on disk. A fresh subdirectory is a different directory
            // whatever $TMPDIR says.
            let elsewhere =
                path [ Path.GetTempPath(); "rogue3-selftest-elsewhere-" + Guid.NewGuid().ToString("N") ]

            Directory.CreateDirectory elsewhere |> ignore

            try
                Directory.SetCurrentDirectory elsewhere

                // Fail CLOSED rather than check from the wrong place: if the relocation did not
                // actually move us, the two calls below would be exactly the ones M21 exempts.
                if Path.GetFullPath(Directory.GetCurrentDirectory()) = realRoot then
                    failwith
                        "SelfTest could not evaluate the repository checks from outside the repository, so the M21 class is unguarded here (#57)."

                templateDriftViolations realRoot, generatedGuidanceViolations realRoot
            finally
                Directory.SetCurrentDirectory here

                try
                    Directory.Delete(elsewhere, true)
                with _ ->
                    ()

        // Called AFTER the directory is restored: the success path calls `writeLog`, which writes to
        // a RELATIVE path and would otherwise land in the temp dir. The coverage line is printed
        // here rather than via `runTemplateDrift` so the 190-file walk is not repeated a third time.
        printfn "%s" (templateDriftCoverage (currentRoot ()))
        runViolationCheck "TemplateDrift" driftFromElsewhere
        runViolationCheck "GeneratedGuidanceCheck" guidanceFromElsewhere

        publish "pass" ""
    with ex ->
        publish "fail" ex.Message
        reraise ()

    writeLog "SelfTest"

/// #46: the remedy the gate names when it reports an unpinned or drifted kit file. Re-pinning is a
/// deliberate, reviewable act — it rewrites a tracked ledger that the audit-binding gate holds a
/// digest over — so this prints what it did rather than doing it quietly.
let private runKitPins () =
    let pins, unpinnable = writeKitPins (currentRoot ())

    printfn
        "KitPins: pinned %d kit file(s) under %s that the generated skill manifest does not name, into %s"
        (List.length pins)
        kitSourcePrefix
        kitPinsRelative

    printfn "KitPins: review the diff — a changed pin is the record that a kit file changed on purpose, and it is what a reviewer reads instead of a silent edit."

    // #62 (4): the remedy the gate prints must not exit 0 after writing a ledger it knows is
    // incomplete. A file it could not read is named and the target fails; `TemplateDrift` would
    // otherwise report the same file as covered by nothing, one run later, with no clue why.
    if not (List.isEmpty unpinnable) then
        for line in unpinnable do
            eprintfn "  %s" line

        failwithf
            "KitPins could not digest %d kit file(s), so %s is INCOMPLETE — the files above are named in it by nothing"
            (List.length unpinnable)
            kitPinsRelative

let run target =
    match target with
    | "Dev" -> writeLog target
    | "GeneratedGuidanceCheck" -> runViolationCheck target (generatedGuidanceViolations (currentRoot ()))
    | "TemplateDrift" -> runTemplateDrift ()
    | "KitPins" -> runKitPins ()
    | "EvidenceGraph" -> runEvidenceGraph ()
    | "EvidenceAudit" ->
        let exitCode = runGeneratedEvidence "EvidenceAudit"
        if exitCode <> 0 then
            failwithf "EvidenceAudit failed with exit code %d; see readiness/evidence-audit.md" exitCode
    // Feature 212 (R3 / FR-007): pass-through build-graph targets over the single root .slnx. These
    // shell to stock `dotnet` so the governed script path and stock root path build the SAME project set
    // (FR-010, no divergence). Test/Verify below are FROZEN — their bodies are unchanged.
    | "Restore" -> runProcess "Restore" "dotnet" (sprintf "restore \"%s\"" (singleRootSolution ()))
    | "Build" -> runProcess "Build" "dotnet" (sprintf "build \"%s\"" (singleRootSolution ()))
    | "Run" -> runInteractive "Run" "dotnet" (sprintf "run --project src/%s" (singleSrcProject ()))
    | "Pack" -> runProcess "Pack" "dotnet" (sprintf "pack \"%s\" -c Release" (singleRootSolution ()))
    | "Test" ->
        runGeneratedTests ()
        runPerformanceIntent ()
        runPerformanceEvidence ()
    | "PerformanceIntent" -> runPerformanceIntent ()
    | "PerformanceEvidence" -> runPerformanceEvidence ()
    | "PerformanceCriticRequest" -> runPerformanceCriticRequest ()
    | "SelfTest" -> runSelfTest ()
    | "Verify" ->
        // ADR-0056 §Decision.2: fail closed BEFORE any other audit work — a lifecycle-less sdd tree
        // is not a completable feature, so the merge-gate audit must not even begin.
        assertLifecycleSupplied ()
        // #34: these two are REAL checks now, and they raise. `Dev` is gone from this list —
        // it is a dev-loop completion marker, not a gate step, and counting it was the lie.
        runTemplateDrift ()
        runViolationCheck "GeneratedGuidanceCheck" (generatedGuidanceViolations (currentRoot ()))
        // #26: the graph is emitted under the publication rule, so a gate run that
        // cannot see the whole readiness tree never overwrites the committed one.
        runEvidenceGraph ()
        let auditExitCode = runGeneratedEvidence "EvidenceAudit"
        if auditExitCode <> 0 then
            failwithf "EvidenceAudit failed with exit code %d; see readiness/evidence-audit.md" auditExitCode
        runGeneratedTests ()
        runPerformanceIntent ()
        runPerformanceEvidence ()
        writeLog "Verify"
        printfn "Verify completed for generated rogue3"
    | other ->
        failwithf "Unknown generated rogue3 target: %s" other

// Feature 242 (spec 242-scaffold-discoverability, §2.3): surface the load-bearing build-target
// semantics at the entry point, so a developer never mistakes a green `Dev` for a passing compile.
// The banner phrasing is kept in sync with docs/rogue3.md (a governance scan fails on drift).
// `dotnet fsi` reserves --help/-h for itself on the script path (they never reach this script), so
// the script-level trigger is the bare `help` token; ./build.sh handles --help/-h at the shell level.
// Printing help runs no target and writes no readiness/logs/*, then exits 0.
let helpBanner =
    "FS.GG.UI generated rogue3 — build targets\n"
    + "  Invoke: ./build.sh <verb> | dotnet fsi build.fsx -t <Target> | ./fake.sh -t <Target>\n\n"
    + "  Dev      A completion-marker / log-writer only — writes readiness/logs/Dev.txt. It does not compile\n"
    + "           your code; a green Dev is not evidence the build passes. Use Test for real feedback.\n"
    + "           Verify does NOT run Dev: a marker is not a gate step, and counting one was issue #34.\n"
    + "  TemplateDrift  Every materialized kit file still matches the sha256 its skill manifest pins\n"
    + "           (.agents/ and .claude/skills/skill-manifest.json), with `materializes-when` evaluated\n"
    + "           against the scaffolded profile so a skill this profile does not take is not drift.\n"
    + "           The generated manifest names only 32 of the 95 files it materializes, so scripts/kit-pins.json\n"
    + "           pins the rest and every file under .agents/skills and .claude/skills that NOTHING pins is\n"
    + "           reported by name (#46). The coverage line prints on every run, green or red.\n"
    + "  KitPins  Rewrites scripts/kit-pins.json from the current tree. Run it only after a DELIBERATE kit\n"
    + "           edit: the changed pin is the reviewable record that a kit file was meant to change.\n"
    + "  GeneratedGuidanceCheck  Every agent .fsgg/agents.yml declares has its guidancePath present and\n"
    + "           non-empty, and generated guidance never exists for one agent only.\n"
    + "  SelfTest Plants a violation for each check above and requires it to be reported, so a check that\n"
    + "           silently stopped checking fails here first. Run it before trusting a green gate.\n"
    + "           Its VERDICT is not what it prints (#57): the transcript is a report, and every mutant that\n"
    + "           ever defeated this gate worked by forging it. The result is carried by readiness/logs/\n"
    + "           selftest-result.json and by the exit code of a child SelfTest run this run spawns with one\n"
    + "           deliberately-failing case injected — a run that cannot report THAT failure reports nothing.\n"
    + "           TAMPER-EVIDENT, NOT TAMPER-PROOF. Named residuals, measured, not implied:\n"
    + "             * A check cannot certify it is still bound to the real tree. An edit that exempts any root\n"
    + "               it recognises AS this repository — by a MARKER FILE it contains, rather than by the\n"
    + "               current directory — leaves every fixture case passing and the real tree unchecked. Both\n"
    + "               checks are run from a working directory this code creates, which closes every cwd-keyed\n"
    + "               form: plain, path-normalising, and one keyed on `root is not under the temp dir` (the\n"
    + "               property every fixture relies on, so it reads as an optimisation). It does NOT close the\n"
    + "               class, and nothing here can: this is the one surviving SINGLE-EDIT defeat of the gate.\n"
    + "             * The printed ok/FAIL labels can be made to lie without changing the verdict. That is now\n"
    + "               a legibility defect rather than a gate defeat, and it is why no guard should read them.\n"
    + "             * Replacing this whole verdict block in ONE contiguous edit defeats the gate. It is not a\n"
    + "               single-LINE edit and a reviewer would see it, but it is real and is named, not hidden.\n"
    + "             * Nothing stops a PATIENT editor. Each layer above falls to its own edit; they are chosen\n"
    + "               so that no SINGLE one leaves the gate green, not so that three cannot be made together.\n"
    + "           rogue3#52's acceptance criterion — 'a PR that makes any build.fsx check unconditionally pass\n"
    + "           is red in CI' — is therefore still NOT fully met, by the first residual above.\n"
    + "           See #62 for the checker's own known blind spots (symlinked kit roots, unreadable files).\n"
    + "           Two environment variables belong to the probe and are otherwise unset: FSGG_SELFTEST_INJECT_FAILURE\n"
    + "           (inject one failing case and do not recurse) and FSGG_SELFTEST_RESULT_PATH (where to write the\n"
    + "           structured result). Both are FAIL-CLOSED if set by accident — they red the run, never green it.\n"
    + "  Test     The first real compile: `dotnet test` + Release expected-workload performance evidence (audit-free).\n"
    + "           A fresh game scaffold fails until all five Placeholder workloads drive rogue3-authored state/messages;\n"
    + "           run PerformanceEvidence, review each definitionDigest, then acknowledge it as Authored.\n"
    + "  PerformanceIntent emits the Contracts 7.x declaration for the SDD performanceIntent block.\n"
    + "  PerformanceCriticRequest emits the exact provenance, cost inventory, raw evidence, host facts,\n"
    + "           rubric version and digest a fresh-context representativeness critic must review.\n"
    + "  Verify   Runs TemplateDrift and GeneratedGuidanceCheck, then the merge-gate audit\n"
    + "           (EvidenceGraph -> EvidenceAudit) — the audit hard-blocks\n"
    + "           until every task is [X] — then runs the tests. Use only when the feature is complete.\n"
    + "           The first Verify on a fresh scaffold fails until you generate the headless evidence baseline\n"
    + "           (readiness/layout-evidence.txt + headless-scene-evidence.txt) and author performance workloads.\n"
    + "           A linked performance-debt issue permits baseline capture but never satisfies acceptance.\n"
    + "           The evidence graph is only PUBLISHED by a run that sensed at least everything the\n"
    + "           graph already on disk records (#26); a run that sensed less names what it missed and\n"
    + "           restores the previous bytes. Set FSGG_EVIDENCE_GRAPH_PUBLISH=1 to publish a smaller\n"
    + "           graph, or delete readiness/evidence-graph.md to re-derive it from this checkout.\n"
    + "           Since #56 the three roll-ups Verify rewrites — readiness/evidence-graph.md,\n"
    + "           performance-evidence.json and m7-ui-performance.json — are NOT tracked: they are run\n"
    + "           outputs no checkout can reproduce, so a green Verify leaves git status clean and you\n"
    + "           do not stage them. The tracked evidence a reviewer reads is\n"
    + "           readiness/performance-intent.yml and readiness/evidence-audit.md.\n\n"
    + "  Restore | Build | Run | Pack   Pass-through to stock `dotnet` over the single root .slnx.\n"
    + "           Run inherits this console, so the product's output is live and it stays up until the\n"
    + "           product exits; set FSGG_RUN_TIMEOUT_SECONDS to bound an unattended launch.\n\n"
    + "  Help:  ./build.sh --help   |   dotnet fsi build.fsx help   (fsi reserves --help/-h on the script path)"

let private isHelpToken (token: string) =
    match token.ToLowerInvariant() with
    | "help"
    | "--help"
    | "-h"
    | "-help"
    | "/?" -> true
    | _ -> false

let args = Environment.GetCommandLineArgs() |> Array.skip 1 |> Array.toList

if args |> List.exists isHelpToken then
    printfn "%s" helpBanner
else
    args |> targetFromArgs |> run
