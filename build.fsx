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

let writeLog target =
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
// #26: an evidence roll-up may only be PUBLISHED by a run that sensed at least
// everything the published one already records.
//
// The EvidenceGraph emitter enumerates whatever is on disk under `readiness/`, and
// part of that tree is regenerable output no clean checkout carries. TRACKED files
// there are in every checkout and are not the problem (a `.gitignore` rule does not
// apply to a tracked file, and `.gitignore:9` re-includes `ship-verdict.json`
// anyway). The UNTRACKED ones are: the fsgg-sdd products excluded by
// `.gitignore:8`, which only the checkout that ran the lifecycle holds, and
// `readiness/logs/*.txt`, which this very run writes. So the list the emitter
// publishes is a property of the CHECKOUT, not of the repository.
//
// Measured on a clean worktree at `7d9d442`: a full Verify dropped TEN of the 102
// entries the committed graph records — five fsgg-sdd outputs under
// `readiness/014-m13-room-transition-pickups-world-state/`, and five of the seven
// `readiness/logs/*.txt` (`TemplateDrift.txt` and `GeneratedGuidanceCheck.txt` are
// written before the graph, so they survive; `Dev.txt` is never written by Verify
// at all since #34; the remaining four are written after it). It added one, giving
// 93 — then rewrote the TRACKED `readiness/evidence-graph.md` with the smaller
// number and exited 0. A worker following the documented instructions — run the
// full gate, then stage — commits an artifact asserting that evidence disappeared,
// and a reviewer reading a green Verify has no reason to open it.
//
// The emitter ships in the FS.GG.UI.Build engine package, so this repository cannot
// change its enumeration logic (it can only change the input tree or the emission
// order, which are #26's other candidate root causes). It CAN refuse to publish the
// result. A run that sensed a SUPERSET publishes normally; a run that sensed LESS
// restores the previous bytes exactly and names every input it could not see. Set
// FSGG_EVIDENCE_GRAPH_PUBLISH=1 in the checkout that legitimately holds the whole
// tree to publish a smaller graph deliberately — which is how the committed graph
// gets corrected, and it currently needs it: at `3913c26` it omits FOUR tracked
// files, and a Verify there senses 96.
//
// This is deliberately NOT extended to `readiness/performance-evidence.json` and
// `readiness/m7-ui-performance.json`, which the same run also leaves dirty. Those
// move because they record MEASUREMENTS, not because an input was missing:
// `performance-evidence.json` carries p50/p95/p99 latencies, `allocatedBytes`, and
// a composition-authority MVID that changes whenever this assembly is rebuilt (see
// `src/Rogue3/PerformanceEvidence.fs`, `provenanceDefinitionToken`);
// `m7-ui-performance.json` carries measured p95/p99 only. Re-running cannot
// reproduce them, so the superset rule has nothing to compare and would assert
// something false about them. Making those two reproducible is a different fix
// (#26's third candidate root cause — stop tracking the roll-ups) on a different
// artifact, and is out of scope here.
//
// `readiness/evidence-audit.md` is tracked and rewritten by the same run, and IS
// left unguarded — deliberately. It records a verdict and a node count, with no
// per-file enumeration, so nothing in it varies with which readiness outputs the
// checkout happens to hold; it came back byte-identical from every run measured
// here. Note that on a refusal `EvidenceAudit` then reads the RESTORED graph, which
// is the previous complete emission rather than this run's partial one.
//
// A refusal does not fail the gate. The harm #26 describes is a falsified artifact
// reaching a reviewer through a green gate; once the tree is left byte-identical
// there is nothing to be fooled by, and failing instead would make Verify
// permanently red in every worktree — which is how a gate gets ignored.
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
        @ [ $"EvidenceGraph: these are UNTRACKED readiness outputs — regenerable fsgg-sdd products only the checkout that ran the lifecycle holds, and logs this run writes after emitting the graph. Tracked readiness files are unaffected. Publish deliberately from a tree that does hold them with {evidenceGraphPublishVariable}=1."
            $"EvidenceGraph: {graphPath} now holds the PREVIOUSLY published bytes, not this run's — its counters and node table are last-complete-emission values, which is the point: a stale complete record beats a fresh falsified one." ]

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

let private runEvidenceGraph () =
    runEvidenceGraphEmission evidenceGraphPath (evidenceGraphPublishRequested ()) (fun () -> runGeneratedEvidence "EvidenceGraph")
    |> evidenceGraphPublicationReport evidenceGraphPath
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
//     and edits locally, so 12 of them are already legitimately stale. Checking
//     them would make the gate red on a correct tree, which is the failure mode
//     that trained everyone to ignore gates in the first place.
// The skill manifests are the honest source: they are re-pinned when kit files
// are re-materialized, and both are correct as this ships.
// ---------------------------------------------------------------------------

let private lowerHex (bytes: byte array) = (Convert.ToHexString bytes).ToLowerInvariant()

let private fileDigest (filePath: string) =
    File.ReadAllBytes filePath
    |> System.Security.Cryptography.SHA256.HashData
    |> lowerHex

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

/// The scaffolded profile, read from the one place that records it. `materializes-when`
/// is evaluated against this, so a kit file that is absent BECAUSE this profile does not
/// take it is not reported as drift.
let private scaffoldProfile (root: string) =
    let provenance = path [ root; ".fsgg"; "scaffold-provenance.json" ]

    if not (File.Exists provenance) then
        None
    else
        use doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText provenance)

        match doc.RootElement.TryGetProperty "effectiveParameters" with
        | true, parameters ->
            parameters.EnumerateArray()
            |> Seq.tryPick (fun p ->
                match p.TryGetProperty "key", p.TryGetProperty "value" with
                | (true, key), (true, value) when key.GetString() = "profile" -> Some(value.GetString())
                | _ -> None)
        | _ -> None

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

/// Every materialized kit file matches the digest its manifest pins. Returns one line per
/// violation; an empty list is a genuine pass.
let templateDriftViolations (root: string) =
    let violations = ResizeArray<string>()
    let profile = scaffoldProfile root

    if profile.IsNone then
        violations.Add ".fsgg/scaffold-provenance.json declares no `profile`, so `materializes-when` cannot be evaluated"

    // The manifests ARE the oracle, and nothing else in the tree pins them: provenance's own pins
    // for both are already stale, and no audit binds either. They are byte-identical by
    // construction, so requiring them to agree means tampering has to be done twice, identically,
    // to go unnoticed — a weak guarantee, but strictly better than trusting one file absolutely.
    // The residual (an edit applied to BOTH manifests still hides drift) is recorded in
    // feedback/2026-08-02-Rogue3-10.md §4.2 and filed, not papered over.
    let manifestPaths = skillManifests |> List.map (fun owner -> owner, path [ root; owner; "skills"; "skill-manifest.json" ])

    match manifestPaths |> List.filter (snd >> File.Exists) with
    | [ (ownerA, a); (ownerB, b) ] when not (digestEquals (fileDigest a) (fileDigest b)) ->
        violations.Add $"the two skill manifests disagree: {ownerA}/skills/skill-manifest.json is {shortDigest (fileDigest a)}, {ownerB}/skills/skill-manifest.json is {shortDigest (fileDigest b)} — one of them has been edited alone"
    | _ -> ()

    for owner in skillManifests do
        let manifest = path [ root; owner; "skills"; "skill-manifest.json" ]
        let manifestName = $"{owner}/skills/skill-manifest.json"

        if not (File.Exists manifest) then
            violations.Add $"{manifestName}: pinned skill manifest is missing"
        else
            use doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText manifest)

            match doc.RootElement.TryGetProperty "skills" with
            | false, _ -> violations.Add $"{manifestName}: no `skills` array to check"
            | true, skills ->
                for skill in skills.EnumerateArray() do
                    let read (name: string) =
                        match skill.TryGetProperty name with
                        | true, v -> v.GetString()
                        | _ -> null

                    let id = read "id"
                    let relative = read "resolvablePath"
                    let pinned = read "sha256"
                    let condition = match read "materializes-when" with | null -> "always" | c -> c

                    if String.IsNullOrWhiteSpace relative || String.IsNullOrWhiteSpace pinned then
                        violations.Add $"{manifestName}: skill `{id}` pins no resolvablePath/sha256"
                    else
                        match materializesHere profile condition with
                        | Error expr -> violations.Add $"{manifestName}: skill `{id}` has an unreadable `materializes-when` expression: {expr}"
                        | Ok false ->
                            // NOT a silent skip. If the condition says this profile does not take the
                            // skill, the file must be ABSENT — otherwise flipping a condition is a
                            // one-line way to park arbitrary drift outside the digest check.
                            if File.Exists(path [ root; relative ]) then
                                violations.Add $"{manifestName}: skill `{id}` is present at {relative} but `materializes-when` ({condition}) excludes this profile, so nothing pins it"
                        | Ok true ->
                            let target = path [ root; relative ]

                            if not (File.Exists target) then
                                violations.Add $"{manifestName}: skill `{id}` should be materialized here but {relative} is missing"
                            else
                                let actual = fileDigest target

                                if not (digestEquals actual pinned) then
                                    violations.Add $"{manifestName}: {relative} has drifted — pinned {shortDigest pinned}, found {shortDigest actual}"

    // Both manifests pin `.agents/...` paths, but the kit is MIRRORED into `.claude/...` and the
    // copies are byte-identical by construction. Without this pass a drifted mirror is invisible:
    // no manifest names it, and provenance's `mirroredPaths` cannot be used as an oracle (§4.3 —
    // its driver pins are legitimately stale). So the mirror is held to the SAME pin as its source.
    let agentsManifest = path [ root; ".agents"; "skills"; "skill-manifest.json" ]

    if File.Exists agentsManifest then
        use doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText agentsManifest)

        match doc.RootElement.TryGetProperty "skills" with
        | false, _ -> ()
        | true, skills ->
            for skill in skills.EnumerateArray() do
                let read (name: string) =
                    match skill.TryGetProperty name with
                    | true, v -> v.GetString()
                    | _ -> null

                let relative = read "resolvablePath"
                let pinned = read "sha256"

                if not (String.IsNullOrWhiteSpace relative)
                   && not (String.IsNullOrWhiteSpace pinned)
                   && relative.StartsWith(".agents/", StringComparison.Ordinal) then
                    let mirrored = ".claude/" + relative.Substring(".agents/".Length)
                    let mirroredFull = path [ root; mirrored ]

                    // The mirror is only required where the SOURCE is materialized here; deleting a
                    // mirror is drift, not a provider choice, or `rm -rf .claude/skills` would pass.
                    if File.Exists(path [ root; relative ]) then
                        if not (File.Exists mirroredFull) then
                            violations.Add $"mirror: {relative} is materialized but its mirrored copy {mirrored} is missing"
                        else
                            let actual = fileDigest mirroredFull

                            if not (digestEquals actual pinned) then
                                violations.Add $"mirror: {mirrored} differs from the digest pinned for {relative} — pinned {shortDigest pinned}, found {shortDigest actual}"

    List.ofSeq violations

/// The agents `.fsgg/agents.yml` declares each have their guidance target present, and where
/// generated guidance exists for one agent it exists for all of them
/// (`requireEquivalentClaudeAndCodexBehavior`). Generated guidance is a projection, never a
/// second source of truth, so this checks presence and symmetry rather than content.
let generatedGuidanceViolations (root: string) =
    let violations = ResizeArray<string>()
    let agentsFile = path [ root; ".fsgg"; "agents.yml" ]

    if not (File.Exists agentsFile) then
        violations.Add ".fsgg/agents.yml is missing, so the declared agent inventory cannot be checked"
        List.ofSeq violations
    else
        let lines = File.ReadAllLines agentsFile
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

/// A minimal but structurally faithful tree: one always-materialized skill, one that this
/// profile does not take, both manifests, the agent inventory and its two guidance targets.
let private plantFixture (root: string) =
    let skillBody = "# kit skill\n"
    let skillRelative = ".agents/skills/fs-gg-kit/SKILL.md"
    writeFile (path [ root; skillRelative ]) skillBody
    // the mirrored twin the kit materializes alongside it, byte-identical by construction
    writeFile (path [ root; ".claude/skills/fs-gg-kit/SKILL.md" ]) skillBody
    let digest = fileDigest (path [ root; skillRelative ])
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
    skillRelative

let private runSelfTest () =
    let sandbox = path [ Path.GetTempPath(); "rogue3-selftest-" + Guid.NewGuid().ToString("N") ]
    let mutable failures = 0
    let mutable cases = 0

    let expect description condition =
        cases <- cases + 1

        if condition then
            printfn "  ok   %s" description
        else
            failures <- failures + 1
            eprintfn "  FAIL %s" description

    let freshFixture () =
        let root = path [ sandbox; Guid.NewGuid().ToString("N") ]
        Directory.CreateDirectory root |> ignore
        root, plantFixture root

    try
        // The gate must be green where it ships, or it is useless as a gate.
        expect "TemplateDrift is clean on this repository" (List.isEmpty (templateDriftViolations (currentRoot ())))
        expect "GeneratedGuidanceCheck is clean on this repository" (List.isEmpty (generatedGuidanceViolations (currentRoot ())))

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

        expect "an UPPER-CASE hex pin is not drift" (List.isEmpty (templateDriftViolations upperHex))

        let quotedCondition, _ = freshFixture ()

        for owner in skillManifests do
            let manifestPath = path [ quotedCondition; owner; "skills"; "skill-manifest.json" ]
            File.WriteAllText(manifestPath, (File.ReadAllText manifestPath).Replace("\"profile == sample-pack\"", "\"profile == \\\"sample-pack\\\"\""))

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

    printfn "SelfTest: %d case(s), %d failure(s)" cases failures

    if failures > 0 then
        failwithf "SelfTest failed: %d of %d case(s)." failures cases

    writeLog "SelfTest"

let run target =
    match target with
    | "Dev" -> writeLog target
    | "GeneratedGuidanceCheck" -> runViolationCheck target (generatedGuidanceViolations (currentRoot ()))
    | "TemplateDrift" -> runViolationCheck target (templateDriftViolations (currentRoot ()))
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
        runViolationCheck "TemplateDrift" (templateDriftViolations (currentRoot ()))
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
    + "  GeneratedGuidanceCheck  Every agent .fsgg/agents.yml declares has its guidancePath present and\n"
    + "           non-empty, and generated guidance never exists for one agent only.\n"
    + "  SelfTest Plants a violation for each check above and requires it to be reported, so a check that\n"
    + "           silently stopped checking fails here first. Run it before trusting a green gate.\n"
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
    + "           committed one records (#26); a run that sensed less names what it missed and restores\n"
    + "           the previous bytes. Set FSGG_EVIDENCE_GRAPH_PUBLISH=1 to publish a smaller graph.\n\n"
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
