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

/// The whole `validate` subcommand, as a function: prints exactly what the CLI
/// prints and returns the exit code the CLI exits with.
///
/// This is a FUNCTION, not an inline match arm, so the selftest can drive the
/// real command end to end. When this logic lived inline, nothing exercised it:
/// dropping `audit.errors` from the error list, or never printing the NOT BOUND
/// block, both left the selftest green. Both are now covered.
let validateCommand (workspaceRoot: string) (path: string) (auditPath: string) =
    if not (File.Exists path) then
        eprintfn "feedback-tool: report not found: %s" path
        1
    elif not (File.Exists auditPath) then
        eprintfn "feedback-tool: audit not found: %s" auditPath
        1
    else

    let reportText = File.ReadAllText path

    let audit =
        validateActionabilityAuditDetailed
            workspaceRoot
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

        0
    else
        for message in errors do
            eprintfn "feedback-tool: %s" message

        1

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

/// A report whose §4.n findings each declare the given evidence line.
let private reportTemplate (findings: (int * string) list) =
    let header =
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
          "## §1 Provenance and confidence"
          ""
          "None observed."
          ""
          "## §2 What worked"
          ""
          "None observed."
          ""
          "## §3 What did not"
          ""
          "None observed."
          ""
          "## §4 Findings"
          "" ]

    let block (number: int) (locatorLine: string) =
        [ sprintf "#### §4.%d selftest finding" number
          ""
          "- **Kind:** defect"
          "- **Impact:** selftest"
          // Expected and Observed must differ, or validateReportText rejects the
          // finding for not describing a delta.
          "- **Expected:** the selftest fixture's expected behaviour"
          "- **Observed:** the selftest fixture's observed behaviour"
          sprintf "- **Evidence:** %s" locatorLine
          "- **Version:** n/a"
          "- **Owner:** selftest"
          "- **Recurrence:** new"
          "- **Avoidable cost:** none"
          "- **Disposition:** accepted"
          "" ]

    // A COMPLETE schema-v2 report, not just the §4 the audit check reads. The
    // CLI runs `validateReportText` over the same text, so a partial fixture
    // would leave the whole command path failing for unrelated reasons and
    // untestable end to end.
    let footer =
        [ "## §5 Did not exercise"
          ""
          "None observed."
          ""
          "## §6 Doc-versus-behavior contradictions"
          ""
          "None observed."
          ""
          "## §7 Workarounds still in the tree"
          ""
          "None observed."
          ""
          "## §8 Friction and avoidable cost"
          ""
          "None observed."
          ""
          "## §9 Skill value and gaps"
          ""
          "None observed."
          ""
          "## §10 Outcome markers"
          ""
          "None observed."
          ""
          "## §11 Falsifiable improvements"
          ""
          "None observed."
          ""
          "## §12 Development-surface coverage"
          ""
          "| Surface | Status | Evidence and result |"
          "|---|---|---|" ]
        @ [ for surface in surfaces -> sprintf "| %s | not-exercised | selftest fixture |" surface ]
        @ [ "" ]

    String.Join(
        "\n",
        header @ (findings |> List.collect (fun (number, line) -> block number line)) @ footer
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

let private findingJson (number: int) (evidence: string list) =
    sprintf
        "    {\n      \"id\": \"§4.%d\",\n      \"status\": \"actionable\",\n      \"missingFacts\": [],\n      \"checkedEvidence\": [\n%s\n      ],\n      \"confidenceLimits\": []\n    }"
        number
        (String.Join(",\n", evidence))

let private auditJsonMulti (reportRelative: string) (reportSha: string) (findings: string list) =
    sprintf
        """{
  "auditSchema": 1,
  "report": "%s",
  "reportSha256": "%s",
  "criticMode": "fresh-context-subagent",
  "criticPromptVersion": "actionability-v1",
  "findings": [
%s
  ]
}"""
        reportRelative
        reportSha
        (String.Join(",\n", findings))

let private auditJson (reportRelative: string) (reportSha: string) (evidence: string list) =
    auditJsonMulti reportRelative reportSha [ findingJson 1 evidence ]

let private writeFile (path: string) (text: string) =
    Directory.CreateDirectory(Path.GetDirectoryName path: string) |> ignore
    File.WriteAllText(path, text)

/// One citation: the locator text, and the digest the audit PINS for it.
let private cite (locator: string) (sha256: string option) = locator, sha256

/// Write a workspace whose §4.n findings cite `findings`, returning the report
/// path, its text and the matching audit text.
let private writeCase (root: string) (findings: (string * string option) list list) =
    let reportRelative = "feedback/selftest.md"
    let reportPath = Path.Combine(root, "feedback", "selftest.md")

    let numbered = findings |> List.mapi (fun index locators -> index + 1, locators)

    let reportText =
        numbered
        |> List.map (fun (number, locators) ->
            number, locators |> List.map fst |> String.concat "; ")
        |> reportTemplate

    writeFile reportPath reportText

    let auditText =
        numbered
        |> List.map (fun (number, locators) ->
            findingJson number [ for locator, sha in locators -> evidenceJson locator sha ])
        |> auditJsonMulti reportRelative (sha256Text reportText)

    reportPath, reportText, auditText

/// Build a workspace whose §4.1 cites `locators`, and validate it.
let private runCase (root: string) (locators: (string * string option) list) =
    let reportPath, reportText, auditText = writeCase root [ locators ]
    validateActionabilityAuditDetailed root (Path.GetFullPath reportPath) reportText auditText

/// Build a workspace with several findings, and validate it.
let private runCaseMulti (root: string) (findings: (string * string option) list list) =
    let reportPath, reportText, auditText = writeCase root findings
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
    // Near misses. A PREFIX or BASENAME or SUFFIX-TEXT match would exempt these;
    // only an exact match on the resolved workspace-relative path does not.
    // Ported from check-audit-bindings.py's selftest, which tests exactly this.
    let neighbour = Path.Combine(root, "scripts", "audit-binding-exceptions.json.bak")
    let nearMiss = Path.Combine(root, "scripts", "audit-binding-exceptionsX.json")
    let cased = Path.Combine(root, "scripts", "Audit-Binding-Exceptions.json")
    // Same BASENAME and same trailing TEXT as the ledger, different directory.
    let vendored = Path.Combine(root, "vendor", "scripts", "audit-binding-exceptions.json")
    // Depth 3, so relativising against the wrong root is not accidentally equal.
    let deep = Path.Combine(root, "src", "deep", "nested", "Thing.fs")
    // rogue3#53: the ledger is now one file per cycle under a directory, and
    // THAT is the path an excuse actually lands in today.
    let cycleLedger =
        Path.Combine(root, "scripts", "audit-binding-exceptions", "item-alpha.json")
    // Near misses for the DIRECTORY. A bare `startswith` would exempt the first;
    // dropping the `.json` suffix test would exempt the second.
    let siblingDirectory =
        Path.Combine(root, "scripts", "audit-binding-exceptions-other", "x.json")

    let insideButNotJson =
        Path.Combine(root, "scripts", "audit-binding-exceptions", "notes.md")

    // The ledger DIRECTORY name somewhere else entirely: a substring match
    // rather than a path prefix would exempt it.
    let vendoredDirectory =
        Path.Combine(root, "vendor", "scripts", "audit-binding-exceptions", "x.json")

    // Case variant of the DIRECTORY. The gate compares case-sensitively on every
    // platform and this validator is required to agree; folding case here would
    // exempt a path the gate still binds.
    let casedDirectory =
        Path.Combine(root, "scripts", "Audit-Binding-Exceptions", "x.json")

    writeFile ledger (ledgerBody ledgerSalt)
    writeFile source "let thing = 1\n"
    writeFile sibling "# checker\n"
    writeFile otherAudit "{ \"auditSchema\": 1 }\n"
    writeFile neighbour "backup\n"
    writeFile nearMiss "near miss\n"
    writeFile cased "cased\n"
    writeFile vendored "vendored\n"
    writeFile deep "let deep = 1\n"
    writeFile cycleLedger (ledgerBody ledgerSalt)
    writeFile siblingDirectory "sibling directory\n"
    writeFile insideButNotJson "notes\n"
    writeFile vendoredDirectory "vendored directory\n"
    writeFile casedDirectory "cased directory\n"

    {| ledger = sha256Text (File.ReadAllText ledger)
       cycleLedger = sha256Text (File.ReadAllText cycleLedger)
       source = sha256Text (File.ReadAllText source)
       sibling = sha256Text (File.ReadAllText sibling)
       otherAudit = sha256Text (File.ReadAllText otherAudit)
       deep = sha256Text (File.ReadAllText deep) |}

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

        // --- the ledger is one file PER CYCLE (rogue3#53) --------------------
        // The gate's excuse now lands in scripts/audit-binding-exceptions/<id>.json.
        // If only the legacy path were exempt, this validator would call every
        // such citation stale and rogue3#38 would return under a new name.
        let root = newRoot "cycle-ledger"
        let pins = seedWorkspace root "one"

        let cyclePath = "file:scripts/audit-binding-exceptions/item-alpha.json"
        let staleCycle = runCase root [ cite cyclePath (Some staleDigest) ]

        check
            "a STALE digest on a PER-CYCLE ledger file is not an error"
            (List.isEmpty staleCycle.errors)

        check
            "the per-cycle ledger citation is reported as not bound"
            (staleCycle.notBound |> List.exists (fun c -> c.locator = cyclePath))

        check
            "a FRESH digest on a per-cycle ledger file is also accepted and still not bound"
            (let r = runCase root [ cite cyclePath (Some pins.cycleLedger) ]
             List.isEmpty r.errors && r.notBound.Length = 1)

        check
            "a per-cycle ledger citation does not require a sha256"
            (List.isEmpty (runCase root [ cite cyclePath None ]).errors)

        // Resolved path, not locator text -- at the new path too.
        let traversingCycle =
            runCase
                root
                [ cite "file:feedback/../scripts/audit-binding-exceptions/item-alpha.json" (Some staleDigest) ]

        check
            "a TRAVERSING locator onto a per-cycle ledger file is exempt too"
            (List.isEmpty traversingCycle.errors && traversingCycle.notBound.Length = 1)

        // Rewriting ONE cycle's file must not change the verdict for a citation
        // onto it -- the rogue3#40 fixed point, at the path excuses now use.
        let cycleVerdicts =
            [ for salt in [ "two"; "three"; "four" ] do
                  writeFile
                      (Path.Combine(root, "scripts", "audit-binding-exceptions", "item-alpha.json"))
                      (ledgerBody salt)

                  yield List.isEmpty (runCase root [ cite cyclePath (Some pins.cycleLedger) ]).errors ]

        check
            "rewriting a per-cycle ledger file three times never changes the verdict"
            (cycleVerdicts = [ true; true; true ])

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

        // --- NARROWNESS: the exemption is ONE exact resolved path -------------
        // Without these, a prefix match, a basename match, a suffix-text match or
        // a case-insensitive match all widen the exemption and still pass.
        // Ported from check-audit-bindings.py's selftest, which tests the same.
        let root = newRoot "narrowness"
        seedWorkspace root "one" |> ignore

        let stillBound (locator: string) (name: string) =
            let result = runCase root [ cite locator (Some staleDigest) ]

            check
                name
                (result.errors |> List.exists (fun e -> e.Contains "digest is stale")
                 && List.isEmpty result.notBound)

        stillBound
            "file:scripts/audit-binding-exceptions.json.bak"
            "a NEIGHBOUR of the ledger (.bak) is still bound -- a prefix match would exempt it"

        stillBound
            "file:scripts/audit-binding-exceptionsX.json"
            "a NEAR-MISS ledger name is still bound -- a prefix match would exempt it"

        stillBound
            "file:vendor/scripts/audit-binding-exceptions.json"
            "the same BASENAME in another directory is still bound -- a basename or suffix-text match would exempt it"

        stillBound
            "file:scripts/Audit-Binding-Exceptions.json"
            "a CASE variant is still bound -- the gate compares case-sensitively on every platform, so this validator must too"

        stillBound
            "file:scripts/audit-binding-exceptions-other/x.json"
            "a DIRECTORY whose name merely starts with the ledger's is still bound -- a bare startswith would exempt it"

        stillBound
            "file:scripts/audit-binding-exceptions/notes.md"
            "a NON-.json file inside the ledger directory is still bound -- dropping the suffix test would exempt it"

        stillBound
            "file:vendor/scripts/audit-binding-exceptions/x.json"
            "the ledger DIRECTORY name in another directory is still bound -- a substring match would exempt it"

        stillBound
            "file:scripts/Audit-Binding-Exceptions/x.json"
            "a CASE variant of the ledger DIRECTORY is still bound -- the gate compares case-sensitively on every platform, so this validator must too"

        let deepOrdinary = runCase root [ cite "file:src/deep/nested/Thing.fs" (Some staleDigest) ]

        check
            "a DEEP ordinary path is still checked -- a wrongly-rooted relativise is not equal at depth 3"
            (deepOrdinary.errors |> List.exists (fun e -> e.Contains "digest is stale"))

        // --- the reported path is the RESOLVED path, and is asserted ----------
        // Without this nothing pins the field the exemption decision is made on,
        // so reporting the locator text, or a constant, or "" all pass.
        let root = newRoot "reported-path"
        seedWorkspace root "one" |> ignore

        let traversingReport =
            runCase
                root
                [ cite "file:feedback/../scripts/audit-binding-exceptions.json" (Some staleDigest) ]

        check
            "the reported path is the RESOLVED workspace-relative path, not the locator text"
            (traversingReport.notBound
             |> List.forall (fun c -> c.path = "scripts/audit-binding-exceptions.json"))

        check
            "the reported locator is preserved verbatim, so a reader can find the citation"
            (traversingReport.notBound
             |> List.forall (fun c ->
                 c.locator = "file:feedback/../scripts/audit-binding-exceptions.json"))

        // --- not-bound reporting is per citation, deduplicated ----------------
        let root = newRoot "dedup"
        seedWorkspace root "one" |> ignore

        let twoFindings =
            runCaseMulti
                root
                [ [ cite "file:scripts/audit-binding-exceptions.json" (Some staleDigest) ]
                  [ cite "file:scripts/audit-binding-exceptions.json" (Some staleDigest) ] ]

        check
            "TWO findings citing the ledger report TWO not-bound citations, one each"
            (twoFindings.notBound.Length = 2
             && (twoFindings.notBound |> List.map (fun c -> c.findingId) |> List.distinct |> List.length) = 2)

        let twoSpellings =
            runCase
                root
                [ cite "file:scripts/audit-binding-exceptions.json" (Some staleDigest)
                  cite "file:feedback/../scripts/audit-binding-exceptions.json" (Some staleDigest) ]

        check
            "ONE finding citing the ledger two ways reports both, keyed on the locator"
            (twoSpellings.notBound.Length = 2)

        // --- END TO END through the real CLI ----------------------------------
        // The cases above call the library directly. Nothing exercised the
        // command wiring, so dropping audit.errors from the CLI's error list, or
        // never printing the NOT BOUND block, both stayed green.
        let root = newRoot "cli"
        let pins = seedWorkspace root "one"

        let runCli (findings: (string * string option) list list) =
            let reportPath, _, auditText = writeCase root findings
            let auditPath = Path.Combine(root, "feedback", "audits", "selftest.audit.json")
            writeFile auditPath auditText
            let stdout = new StringWriter()
            let stderr = new StringWriter()
            let previousOut = Console.Out
            let previousError = Console.Error

            try
                Console.SetOut stdout
                Console.SetError stderr
                let code = validateCommand root reportPath auditPath
                code, string stdout, string stderr
            finally
                Console.SetOut previousOut
                Console.SetError previousError

        let code, out, _ =
            runCli [ [ cite "file:scripts/audit-binding-exceptions.json" (Some staleDigest) ] ]

        check "CLI: a ledger citation exits 0" (code = 0)

        check
            "CLI: a ledger citation PRINTS the NOT BOUND block"
            (out.Contains "NOT BOUND" && out.Contains "file:scripts/audit-binding-exceptions.json")

        check
            "CLI: a green run still says how many citations were not checked"
            (out.Contains "1 citation(s) were not checked")

        check "CLI: a green run reports the report as valid" (out.Contains "valid actionability-bound")

        let code, out, err = runCli [ [ cite "file:src/Thing.fs" (Some staleDigest) ] ]

        check "CLI: a stale ordinary digest exits 1" (code = 1)

        check
            "CLI: the audit error reaches the operator on stderr"
            (err.Contains "digest is stale" && err.Contains "src/Thing.fs")

        check "CLI: a failing run does not claim the report is valid" (not (out.Contains "valid actionability-bound"))

        let code, _, _ = runCli [ [ cite "file:src/Thing.fs" (Some pins.source) ] ]
        check "CLI: a fresh ordinary digest exits 0" (code = 0)

        let missingCode =
            let stderr = new StringWriter()
            let previousError = Console.Error

            try
                Console.SetError stderr
                validateCommand root (Path.Combine(root, "nope.md")) (Path.Combine(root, "nope.json"))
            finally
                Console.SetError previousError

        check "CLI: a missing report exits 1" (missingCode = 1)

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

        // A FLOOR, so a sandbox that silently skipped a group cannot report a
        // smaller green suite. The symlink cases are conditional; nothing else is.
        let minimumCases = 38

        if total < minimumCases then
            failures.Add(
                sprintf
                    "only %d cases ran, expected at least %d -- cases were skipped, so this green is not the suite"
                    total
                    minimumCases
            )

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
    exit (validateCommand (Directory.GetCurrentDirectory()) path auditPath)
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
