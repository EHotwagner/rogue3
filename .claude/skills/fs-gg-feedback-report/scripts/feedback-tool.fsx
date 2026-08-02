#load "FeedbackReportTool.fs"

open System
open System.IO
open FsGgFeedbackReportTool

let fail messages =
    for message in messages do
        eprintfn "feedback-tool: %s" message

    exit 1

let parseOptions (args: string array) =
    let rec loop index options =
        if index >= args.Length then
            options
        elif not (args.[index].StartsWith "--") then
            fail [ sprintf "expected --option, got '%s'" args.[index] ]
        elif index + 1 >= args.Length then
            fail [ sprintf "missing value for %s" args.[index] ]
        else
            loop (index + 2) (Map.add (args.[index].Substring 2) args.[index + 1] options)

    loop 0 Map.empty

let required options name =
    match Map.tryFind name options with
    | Some value when not (String.IsNullOrWhiteSpace value) -> value
    | _ -> fail [ sprintf "missing --%s" name ]

let requiredList options name =
    required options name
    |> fun value -> value.Split(';')
    |> Array.map (fun value -> value.Trim())
    |> Array.toList

// ---------------------------------------------------------------------------
// selftest
//
// The validator is the thing being trusted, so prove it still FAILS a stale
// binding before trusting the verdict it gives about a real report. Every case
// builds a throwaway workspace on disk and drives
// `validateActionabilityAuditDetailed` over it.
//
// The mutants these cases exist to catch, each one a plausible way to "fix"
// rogue3#40 wrongly:
//   1. drop the digest comparison entirely             -> staleNonExempt
//   2. exempt every *.audit.json as well               -> staleOtherAudit
//   3. match the exemption on the LOCATOR TEXT         -> traversingLocator
//   4. skip the citation silently                      -> notBound reporting
//   5. relativise against a non-canonical root         -> symlinkedRoot
//   6. exempt the whole scripts/ directory             -> staleSiblingScript
// ---------------------------------------------------------------------------

let private reportTemplate (locatorLine: string) =
    String.Join(
        "\n",
        [ "---"
          "feedbackSchema: 2"
          "date: 2026-08-02"
          "workspace: selftest"
          "cycle: selftest"
          "lane: none"
          "toolVersion: n/a"
          "commit: selftest"
          "---"
          ""
          "## §4 Findings"
          ""
          "#### §4.1 selftest finding"
          ""
          "- **Kind:** defect"
          "- **Impact:** selftest"
          "- **Expected:** selftest"
          "- **Observed:** selftest"
          sprintf "- **Evidence:** %s" locatorLine
          "- **Version:** n/a"
          "- **Owner:** selftest"
          "- **Recurrence:** new"
          "- **Avoidable cost:** none"
          "- **Disposition:** accepted"
          ""
          "## §5 Did not exercise"
          ""
          "None observed."
          "" ]
    )

/// One `checkedEvidence` entry; `sha256 = None` omits the field entirely.
let private evidenceJson (locator: string) (sha256: string option) =
    let digestField =
        match sha256 with
        | Some value -> sprintf ",\n          \"sha256\": \"%s\"" value
        | None -> ""

    sprintf
        "        {\n          \"locator\": \"%s\",\n          \"result\": \"verified\"%s\n        }"
        locator
        digestField

let private auditJson (reportRelative: string) (reportSha: string) (evidence: string list) =
    sprintf
        """{
  "auditSchema": 1,
  "report": "%s",
  "reportSha256": "%s",
  "criticMode": "fresh-context-subagent",
  "criticPromptVersion": "actionability-v1",
  "findings": [
    {
      "id": "§4.1",
      "status": "actionable",
      "missingFacts": [],
      "checkedEvidence": [
%s
      ],
      "confidenceLimits": []
    }
  ]
}"""
        reportRelative
        reportSha
        (String.Join(",\n", evidence))

let private writeFile (path: string) (text: string) =
    Directory.CreateDirectory(Path.GetDirectoryName path: string) |> ignore
    File.WriteAllText(path, text)

/// One citation: the locator text, and the digest the audit PINS for it.
let private cite (locator: string) (sha256: string option) = locator, sha256

/// Build a workspace whose §4.1 cites `locators`, and validate it.
let private runCase (root: string) (locators: (string * string option) list) =
    let reportRelative = "feedback/selftest.md"
    let reportPath = Path.Combine(root, "feedback", "selftest.md")
    let locatorLine = locators |> List.map fst |> String.concat "; "
    let reportText = reportTemplate locatorLine
    writeFile reportPath reportText

    let auditText =
        auditJson reportRelative (sha256Text reportText) [ for locator, sha in locators -> evidenceJson locator sha ]

    validateActionabilityAuditDetailed root (Path.GetFullPath reportPath) reportText auditText

let private ledgerBody (salt: string) =
    sprintf "{\n  \"grandfatherSchema\": 1,\n  \"entries\": [],\n  \"salt\": \"%s\"\n}\n" salt

/// Seed a fresh workspace: the ledger, an ordinary source file, a sibling
/// script, and another audit. Returns their pinned digests.
let private seedWorkspace (root: string) (ledgerSalt: string) =
    let ledger = Path.Combine(root, "scripts", "audit-binding-exceptions.json")
    let source = Path.Combine(root, "src", "Thing.fs")
    let sibling = Path.Combine(root, "scripts", "check-audit-bindings.py")
    let otherAudit = Path.Combine(root, "feedback", "audits", "other.audit.json")

    writeFile ledger (ledgerBody ledgerSalt)
    writeFile source "let thing = 1\n"
    writeFile sibling "# checker\n"
    writeFile otherAudit "{ \"auditSchema\": 1 }\n"

    {| ledger = sha256Text (File.ReadAllText ledger)
       source = sha256Text (File.ReadAllText source)
       sibling = sha256Text (File.ReadAllText sibling)
       otherAudit = sha256Text (File.ReadAllText otherAudit) |}

let private staleDigest = String.replicate 64 "a"

let private selftest () =
    let failures = ResizeArray<string>()
    let mutable total = 0

    let check (name: string) (condition: bool) =
        total <- total + 1

        if not condition then
            failures.Add name

    let temp =
        Path.Combine(Path.GetTempPath(), "feedback-tool-selftest-" + Guid.NewGuid().ToString("n"))

    Directory.CreateDirectory temp |> ignore

    let newRoot name =
        let root = Path.Combine(temp, name)
        Directory.CreateDirectory root |> ignore
        root

    try
        // --- the exemption itself -------------------------------------------
        let root = newRoot "ledger-stale"
        let pins = seedWorkspace root "one"

        let stale =
            runCase root [ cite "file:scripts/audit-binding-exceptions.json" (Some staleDigest) ]

        check
            "a STALE digest on the excuse ledger is not an error"
            (List.isEmpty stale.errors)

        check
            "the ledger citation is reported as not bound, not silently dropped"
            (stale.notBound |> List.exists (fun c -> c.locator = "file:scripts/audit-binding-exceptions.json"))

        check
            "the not-bound citation names the finding it came from"
            (stale.notBound |> List.forall (fun c -> c.findingId = "§4.1"))

        check
            "the not-bound citation carries a reason"
            (stale.notBound |> List.forall (fun c -> not (String.IsNullOrWhiteSpace c.reason)))

        let fresh =
            runCase root [ cite "file:scripts/audit-binding-exceptions.json" (Some pins.ledger) ]

        check "a FRESH digest on the excuse ledger is also accepted" (List.isEmpty fresh.errors)

        check
            "a fresh ledger citation is STILL reported as not bound"
            (fresh.notBound.Length = 1)

        let noDigest = runCase root [ cite "file:scripts/audit-binding-exceptions.json" None ]

        check
            "the ledger citation does not require a sha256 it promises never to compare"
            (List.isEmpty noDigest.errors)

        // --- the fixed point ------------------------------------------------
        // The whole point of rogue3#40: rewriting the ledger (what every
        // `--grandfather` run does) must not change the verdict.
        let verdicts =
            [ for salt in [ "two"; "three"; "four" ] do
                  writeFile
                      (Path.Combine(root, "scripts", "audit-binding-exceptions.json"))
                      (ledgerBody salt)

                  let result =
                      runCase root [ cite "file:scripts/audit-binding-exceptions.json" (Some pins.ledger) ]

                  yield List.isEmpty result.errors ]

        check
            "rewriting the ledger three times never changes the verdict"
            (verdicts = [ true; true; true ])

        // --- the exemption is NOT a class ------------------------------------
        let root = newRoot "non-exempt"
        let pins = seedWorkspace root "one"

        let staleSource = runCase root [ cite "file:src/Thing.fs" (Some staleDigest) ]

        check
            "a stale digest on an ORDINARY file is still an error"
            (staleSource.errors
             |> List.exists (fun e -> e.Contains "digest is stale" && e.Contains "src/Thing.fs"))

        check "an ordinary file is never reported as not bound" (List.isEmpty staleSource.notBound)

        let freshSource = runCase root [ cite "file:src/Thing.fs" (Some pins.source) ]
        check "a fresh digest on an ordinary file still passes" (List.isEmpty freshSource.errors)

        let staleOtherAudit =
            runCase root [ cite "file:feedback/audits/other.audit.json" (Some staleDigest) ]

        check
            "a stale digest on ANOTHER audit is still an error -- audits are deliberately NOT exempt"
            (staleOtherAudit.errors |> List.exists (fun e -> e.Contains "digest is stale"))

        check
            "another audit is never reported as not bound"
            (List.isEmpty staleOtherAudit.notBound)

        let staleSibling =
            runCase root [ cite "file:scripts/check-audit-bindings.py" (Some staleDigest) ]

        check
            "a stale digest on a SIBLING script under scripts/ is still an error -- the exemption is one path, not a directory"
            (staleSibling.errors |> List.exists (fun e -> e.Contains "digest is stale"))

        let missingDigest = runCase root [ cite "file:src/Thing.fs" None ]

        check
            "an ordinary file citation still requires a sha256"
            (missingDigest.errors |> List.exists (fun e -> e.Contains "needs sha256"))

        // --- the exemption is decided on the RESOLVED path -------------------
        let traversing =
            runCase
                root
                [ cite "file:feedback/../scripts/audit-binding-exceptions.json" (Some staleDigest) ]

        check
            "a TRAVERSING locator onto the ledger is recognised as the ledger -- the exemption reads the resolved path, not the locator text"
            (List.isEmpty traversing.errors && traversing.notBound.Length = 1)

        // --- existence is still checked --------------------------------------
        let root = newRoot "missing-ledger"
        seedWorkspace root "one" |> ignore
        File.Delete(Path.Combine(root, "scripts", "audit-binding-exceptions.json"))

        let missingLedger =
            runCase root [ cite "file:scripts/audit-binding-exceptions.json" (Some staleDigest) ]

        check
            "a citation onto a MISSING ledger is still an error -- only the digest has no fixed point, existence does"
            (missingLedger.errors |> List.exists (fun e -> e.Contains "is missing"))

        // --- symlinked workspace root ----------------------------------------
        // Relativising a realpath'd target against a root that still contains a
        // symlinked component yields `../real/...`, which matches no exemption
        // -- and the whole thing silently switches off.
        let linkHost = newRoot "symlink"
        let realRoot = Path.Combine(linkHost, "real")
        Directory.CreateDirectory realRoot |> ignore
        seedWorkspace realRoot "one" |> ignore
        let linkRoot = Path.Combine(linkHost, "link")

        let linked =
            try
                Directory.CreateSymbolicLink(linkRoot, realRoot) |> ignore
                true
            with _ ->
                false

        if linked then
            let throughLink =
                runCase linkRoot [ cite "file:scripts/audit-binding-exceptions.json" (Some staleDigest) ]

            check
                "the ledger is exempt through a SYMLINKED workspace root"
                (List.isEmpty throughLink.errors && throughLink.notBound.Length = 1)

            let throughLinkOrdinary = runCase linkRoot [ cite "file:src/Thing.fs" (Some staleDigest) ]

            check
                "an ordinary file is still checked through a symlinked workspace root"
                (throughLinkOrdinary.errors |> List.exists (fun e -> e.Contains "digest is stale"))
        else
            eprintfn "feedback-tool: selftest: symlink cases skipped (cannot create symlinks here)"

        // --- the exemption cannot hide OTHER errors ---------------------------
        let root = newRoot "other-errors"
        seedWorkspace root "one" |> ignore

        let escaping =
            runCase root [ cite "file:../outside.txt" (Some staleDigest) ]

        check
            "a locator escaping the workspace is still an error"
            (escaping.errors |> List.exists (fun e -> e.Contains "workspace-relative"))

        // --- back-compatible wrapper -----------------------------------------
        let root = newRoot "wrapper"
        seedWorkspace root "one" |> ignore
        let reportPath = Path.Combine(root, "feedback", "selftest.md")

        let detailed =
            runCase root [ cite "file:scripts/audit-binding-exceptions.json" (Some staleDigest) ]

        let reportText = File.ReadAllText reportPath

        let auditText =
            auditJson
                "feedback/selftest.md"
                (sha256Text reportText)
                [ evidenceJson "file:scripts/audit-binding-exceptions.json" (Some staleDigest) ]

        let wrapped =
            validateActionabilityAudit root (Path.GetFullPath reportPath) reportText auditText

        check
            "the errors-only wrapper agrees with the detailed result"
            (wrapped = detailed.errors)

        for failure in failures do
            eprintfn "feedback-tool: selftest FAILED: %s" failure

        if failures.Count = 0 then
            printfn "feedback-tool: selftest: %d/%d cases passed" total total
            0
        else
            eprintfn "feedback-tool: selftest: %d of %d cases FAILED" failures.Count total
            1
    finally
        try
            Directory.Delete(temp, true)
        with _ ->
            ()

let argv = fsi.CommandLineArgs |> Array.skip 1

match argv with
| [| "selftest" |] -> exit (selftest ())
| [| "digest"; path |] ->
    if not (File.Exists path) then
        fail [ sprintf "file not found: %s" path ]

    File.ReadAllText path |> sha256Text |> printfn "%s"
| [| "validate"; path; "--audit"; auditPath |] ->
    if not (File.Exists path) then
        fail [ sprintf "report not found: %s" path ]

    if not (File.Exists auditPath) then
        fail [ sprintf "audit not found: %s" auditPath ]

    let reportText = File.ReadAllText path

    let audit =
        validateActionabilityAuditDetailed
            (Directory.GetCurrentDirectory())
            (Path.GetFullPath path)
            reportText
            (File.ReadAllText auditPath)

    let errors = validateReportText reportText @ audit.errors

    // Report the citations this validator deliberately did not check, on BOTH
    // the green and the red path. A silently skipped citation is
    // indistinguishable from a checked one, which is how the unsatisfiable
    // binding stayed invisible in the first place.
    if not (List.isEmpty audit.notBound) then
        printfn
            "feedback-tool: %d citation(s) NOT BOUND -- reported rather than checked:"
            audit.notBound.Length

        for citation in audit.notBound do
            printfn "  %s %s" citation.findingId citation.locator
            printfn "    %s" citation.reason

    if List.isEmpty errors then
        printfn "feedback-tool: valid actionability-bound schema-v2 report: %s" path

        if not (List.isEmpty audit.notBound) then
            printfn
                "feedback-tool: %d citation(s) were not checked (listed above)."
                audit.notBound.Length
    else
        fail errors
| [| "validate"; _ |] ->
    fail [ "validate requires --audit <feedback/audits/report.audit.json>" ]
| [| "validate-checkpoints"; path |] ->
    let errors = validateCheckpointFile path

    if List.isEmpty errors then
        printfn "feedback-tool: valid checkpoint file: %s" path
    else
        fail errors
| args when args.Length > 0 && args.[0] = "validate-checkpoint-state" ->
    let options = parseOptions args.[1..]
    let root = Map.tryFind "root" options |> Option.defaultValue (Directory.GetCurrentDirectory())
    let cycle = required options "cycle"
    let errors = validateCheckpointState root cycle

    if List.isEmpty errors then
        printfn "feedback-tool: valid checkpoint state for cycle %s" cycle
    else
        fail errors
| args when args.Length > 0 && args.[0] = "activate" ->
    let options = parseOptions args.[1..]
    let root = Map.tryFind "root" options |> Option.defaultValue (Directory.GetCurrentDirectory())

    try
        let path =
            appendZeroEventActivation
                root
                (required options "cycle")
                (requiredList options "phases")
                (requiredList options "evidence")
                (required options "reason")

        printfn "feedback-tool: recorded zero-event activation: %s" path
    with ex ->
        fail [ ex.Message ]
| args when args.Length > 0 && args.[0] = "checkpoint" ->
    let options = parseOptions args.[1..]
    let root = Map.tryFind "root" options |> Option.defaultValue (Directory.GetCurrentDirectory())

    try
        let path =
            appendCheckpoint
                root
                (required options "cycle")
                (required options "phase")
                (required options "surface")
                (required options "kind")
                (required options "summary")
                (required options "evidence")
                (required options "cost")
                (required options "owner")

        printfn "feedback-tool: appended checkpoint: %s" path
    with :? ArgumentException as ex ->
        fail [ ex.Message ]
| _ ->
    fail
        [ "usage:"
          "  feedback-tool.fsx -- checkpoint --cycle ID --phase PHASE --surface ID --kind KIND --summary TEXT --evidence TEXT --cost TEXT --owner TEXT [--root PATH]"
          "  feedback-tool.fsx -- activate --cycle ID --phases \"PHASE;PHASE\" --evidence \"LOCATOR;LOCATOR\" --reason TEXT [--root PATH]"
          "  feedback-tool.fsx -- digest <text-file>"
          "  feedback-tool.fsx -- validate feedback/<report>.md --audit feedback/audits/<report>.audit.json"
          "  feedback-tool.fsx -- validate-checkpoints feedback/checkpoints/<cycle>.jsonl"
          "  feedback-tool.fsx -- validate-checkpoint-state --cycle ID [--root PATH]"
          "  feedback-tool.fsx -- selftest" ]
