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

let run target =
    match target with
    | "Dev"
    | "GeneratedGuidanceCheck"
    | "TemplateDrift" -> writeLog target
    | "EvidenceGraph" ->
        let exitCode = runGeneratedEvidence "EvidenceGraph"
        if exitCode <> 0 then
            failwithf "EvidenceGraph failed with exit code %d; see readiness/evidence-graph.md" exitCode
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
    | "Verify" ->
        // ADR-0056 §Decision.2: fail closed BEFORE any other audit work — a lifecycle-less sdd tree
        // is not a completable feature, so the merge-gate audit must not even begin.
        assertLifecycleSupplied ()
        [ "Dev"; "GeneratedGuidanceCheck"; "TemplateDrift" ]
        |> List.iter writeLog
        let graphExitCode = runGeneratedEvidence "EvidenceGraph"
        if graphExitCode <> 0 then
            failwithf "EvidenceGraph failed with exit code %d; see readiness/evidence-graph.md" graphExitCode
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
    + "  Test     The first real compile: `dotnet test` + Release expected-workload performance evidence (audit-free).\n"
    + "           A fresh game scaffold fails until all five Placeholder workloads drive rogue3-authored state/messages;\n"
    + "           run PerformanceEvidence, review each definitionDigest, then acknowledge it as Authored.\n"
    + "  PerformanceIntent emits the Contracts 7.x declaration for the SDD performanceIntent block.\n"
    + "  PerformanceCriticRequest emits the exact provenance, cost inventory, raw evidence, host facts,\n"
    + "           rubric version and digest a fresh-context representativeness critic must review.\n"
    + "  Verify   Runs the merge-gate audit (EvidenceGraph -> EvidenceAudit) first — the audit hard-blocks\n"
    + "           until every task is [X] — then runs the tests. Use only when the feature is complete.\n"
    + "           The first Verify on a fresh scaffold fails until you generate the headless evidence baseline\n"
    + "           (readiness/layout-evidence.txt + headless-scene-evidence.txt) and author performance workloads.\n"
    + "           A linked performance-debt issue permits baseline capture but never satisfies acceptance.\n\n"
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
