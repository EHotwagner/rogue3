module FsGgFeedbackReportTool

open System
open System.IO
open System.Security.Cryptography
open System.Text
open System.Text.Json
open System.Text.RegularExpressions

let surfaces =
    [ "scaffolding"
      "onboarding-guidance"
      "skills"
      "sdd-authoring"
      "implementation-apis"
      "dependencies-build"
      "testing"
      "evidence"
      "runtime-playtest"
      "performance"
      "documentation"
      "packaging-upgrade"
      "worker-git-pr" ]

let kinds =
    [ "positive-pattern"
      "defect"
      "friction"
      "capability-gap"
      "quality-gap"
      "documentation"
      "orchestration" ]

let criticStatuses =
    [ "actionable"; "incomplete"; "unsupported"; "duplicate"; "positive-pattern" ]

let evidenceResults =
    [ "verified"; "missing"; "stale"; "non-reproducing"; "contradictory"; "claim-only" ]

let private requiredFrontmatter =
    [ "feedbackSchema"; "date"; "workspace"; "cycle"; "lane"; "toolVersion"; "commit" ]

let private requiredSections =
    [ for number, title in
          [ 1, "Provenance and confidence"
            2, "What worked"
            3, "What did not"
            4, "Findings"
            5, "Did not exercise"
            6, "Doc-versus-behavior contradictions"
            7, "Workarounds still in the tree"
            8, "Friction and avoidable cost"
            9, "Skill value and gaps"
            10, "Outcome markers"
            11, "Falsifiable improvements"
            12, "Development-surface coverage" ] ->
        sprintf "## §%d %s" number title ]

let private requiredFindingFields =
    [ "Kind"
      "Impact"
      "Expected"
      "Observed"
      "Evidence"
      "Version"
      "Owner"
      "Recurrence"
      "Avoidable cost"
      "Disposition" ]

let private normalizeNewlines (text: string) =
    text.Replace("\r\n", "\n").Replace("\r", "\n")

let sha256Text (text: string) =
    use sha = SHA256.Create()

    text
    |> normalizeNewlines
    |> Encoding.UTF8.GetBytes
    |> sha.ComputeHash
    |> Convert.ToHexString
    |> fun value -> value.ToLowerInvariant()

let private frontmatter (text: string) =
    let lines = normalizeNewlines text |> fun value -> value.Split '\n'

    if lines.Length < 3 || lines.[0].Trim() <> "---" then
        None
    else
        lines
        |> Array.skip 1
        |> Array.tryFindIndex (fun line -> line.Trim() = "---")
        |> Option.map (fun closing ->
            lines.[1..closing]
            |> Array.choose (fun line ->
                let separator = line.IndexOf ':'

                if separator <= 0 then
                    None
                else
                    Some(line.Substring(0, separator).Trim(), line.Substring(separator + 1).Trim()))
            |> Map.ofArray)

let private sectionText (text: string) (startHeading: string) (endHeading: string) =
    let startIndex = text.IndexOf(startHeading, StringComparison.Ordinal)

    if startIndex < 0 then
        ""
    else
        let contentStart = startIndex + startHeading.Length
        let endIndex = text.IndexOf(endHeading, contentStart, StringComparison.Ordinal)

        if endIndex < 0 then
            text.Substring contentStart
        else
            text.Substring(contentStart, endIndex - contentStart)

let validateReportText (rawText: string) =
    let text = normalizeNewlines rawText
    let errors = ResizeArray<string>()

    match frontmatter text with
    | None -> errors.Add "frontmatter: expected an opening and closing --- block"
    | Some fields ->
        for field in requiredFrontmatter do
            match Map.tryFind field fields with
            | None -> errors.Add(sprintf "frontmatter: missing %s" field)
            | Some value when String.IsNullOrWhiteSpace value ->
                errors.Add(sprintf "frontmatter: %s must not be empty" field)
            | _ -> ()

        match Map.tryFind "feedbackSchema" fields with
        | Some "2" -> ()
        | Some value -> errors.Add(sprintf "frontmatter: feedbackSchema must be 2, got %s" value)
        | None -> ()

    let mutable previousIndex = -1

    for heading in requiredSections do
        let headingPattern = "(?m)^" + Regex.Escape heading + @"\s*$"
        let matches = Regex.Matches(text, headingPattern)

        if matches.Count = 0 then
            errors.Add(sprintf "sections: missing '%s'" heading)
        elif matches.Count > 1 then
            errors.Add(sprintf "sections: duplicate '%s'" heading)
        else
            let currentIndex = matches.[0].Index

            if currentIndex < previousIndex then
                errors.Add(sprintf "sections: '%s' is out of order" heading)

            previousIndex <- currentIndex

    let findings = sectionText text requiredSections.[3] requiredSections.[4]
    let findingMatches = Regex.Matches(findings, @"(?m)^#### §4\.(\d+) .+$")

    if findingMatches.Count = 0 then
        if not (findings.Contains("None observed.", StringComparison.OrdinalIgnoreCase)) then
            errors.Add "findings: use structured §4.n records or write 'None observed.'"
    else
        for index in 0 .. findingMatches.Count - 1 do
            let findingNumber = findingMatches.[index].Groups.[1].Value
            let expectedNumber = string (index + 1)

            if findingNumber <> expectedNumber then
                errors.Add(
                    sprintf "findings: expected §4.%s, got §4.%s" expectedNumber findingNumber
                )

            let chunkStart = findingMatches.[index].Index

            let chunkEnd =
                if index + 1 < findingMatches.Count then
                    findingMatches.[index + 1].Index
                else
                    findings.Length

            let chunk = findings.Substring(chunkStart, chunkEnd - chunkStart)

            for field in requiredFindingFields do
                let fieldPattern =
                    @"(?m)^- \*\*" + Regex.Escape field + @":\*\*\s+\S.*$"

                if not (Regex.IsMatch(chunk, fieldPattern)) then
                    errors.Add(sprintf "findings: §4.%s is missing '%s'" findingNumber field)

            let fieldValue name =
                let matched =
                    Regex.Match(
                        chunk,
                        @"(?m)^- \*\*" + Regex.Escape name + @":\*\*\s+(.+?)\s*$"
                    )

                if matched.Success then matched.Groups.[1].Value.Trim() else ""

            let expected = fieldValue "Expected"
            let observed = fieldValue "Observed"

            if
                not (String.IsNullOrWhiteSpace expected)
                && expected.Equals(observed, StringComparison.OrdinalIgnoreCase)
            then
                errors.Add(
                    sprintf "findings: §4.%s Expected and Observed must describe a delta" findingNumber
                )

            let kindPattern =
                @"(?m)^- \*\*Kind:\*\*\s+(" + String.concat "|" (List.map Regex.Escape kinds) + @")\s*$"

            if not (Regex.IsMatch(chunk, kindPattern)) then
                errors.Add(
                    sprintf
                        "findings: §4.%s Kind must be one of %s"
                        findingNumber
                        (String.concat ", " kinds)
                )

    let coverage = sectionText text requiredSections.[11] "\u0000"
    let rowPattern = Regex(@"(?m)^\|\s*([^|]+?)\s*\|\s*(exercised|partial|not-exercised)\s*\|")
    let rows = rowPattern.Matches coverage

    let observed =
        [ for row in rows do
              yield row.Groups.[1].Value.Trim() ]

    for surface in surfaces do
        let count = observed |> List.filter ((=) surface) |> List.length

        if count = 0 then
            errors.Add(sprintf "coverage: missing surface '%s'" surface)
        elif count > 1 then
            errors.Add(sprintf "coverage: duplicate surface '%s'" surface)

    for surface in observed do
        if not (List.contains surface surfaces) then
            errors.Add(sprintf "coverage: unknown surface '%s'" surface)

    List.ofSeq errors

type EvidenceCheck =
    { locator: string
      result: string
      sha256: string option }

type FindingAudit =
    { id: string
      status: string
      missingFacts: string list
      checkedEvidence: EvidenceCheck list
      confidenceLimits: string list }

type ActionabilityAudit =
    { auditSchema: int
      report: string
      reportSha256: string
      criticMode: string
      criticPromptVersion: string
      findings: FindingAudit list }

let private findingContracts (reportText: string) =
    let findings = sectionText reportText requiredSections.[3] requiredSections.[4]
    let matches = Regex.Matches(findings, @"(?m)^#### (§4\.\d+) .+$")

    [ for index in 0 .. matches.Count - 1 do
          let chunkStart = matches.[index].Index

          let chunkEnd =
              if index + 1 < matches.Count then matches.[index + 1].Index else findings.Length

          let chunk = findings.Substring(chunkStart, chunkEnd - chunkStart)
          let kindMatch = Regex.Match(chunk, @"(?m)^- \*\*Kind:\*\*\s+(\S+)\s*$")
          let evidenceMatch = Regex.Match(chunk, @"(?m)^- \*\*Evidence:\*\*\s+(.+?)\s*$")

          let evidence =
              if evidenceMatch.Success then
                  evidenceMatch.Groups.[1].Value.Split(';')
                  |> Array.map _.Trim()
                  |> Array.filter (String.IsNullOrWhiteSpace >> not)
                  |> Set.ofArray
              else
                  Set.empty

          yield
              matches.[index].Groups.[1].Value,
              (if kindMatch.Success then kindMatch.Groups.[1].Value else ""),
              evidence ]

let private pathComparison =
    if OperatingSystem.IsWindows() then
        StringComparison.OrdinalIgnoreCase
    else
        StringComparison.Ordinal

let private isInside root candidate =
    let normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar)
    let normalizedCandidate = Path.GetFullPath candidate
    let prefix = normalizedRoot + string Path.DirectorySeparatorChar

    normalizedCandidate.Equals(normalizedRoot, pathComparison)
    || normalizedCandidate.StartsWith(prefix, pathComparison)

let private canonicalizeExistingSegments path =
    let fullPath = Path.GetFullPath path

    match Path.GetPathRoot fullPath |> Option.ofObj with
    | None ->
        fullPath
    | Some root ->
        let separators = [| Path.DirectorySeparatorChar; Path.AltDirectorySeparatorChar |]
        let relative = Path.GetRelativePath(root, fullPath)

        relative.Split(separators, StringSplitOptions.RemoveEmptyEntries)
        |> Array.fold
            (fun current segment ->
                let next = Path.Combine(current, segment)

                let entry: FileSystemInfo =
                    if Directory.Exists next then
                        DirectoryInfo next
                    else
                        FileInfo next

                if entry.Exists && not (String.IsNullOrWhiteSpace entry.LinkTarget) then
                    match entry.ResolveLinkTarget(true) |> Option.ofObj with
                    | None -> next
                    | Some target -> target.FullName
                else
                    next)
            root
        |> Path.GetFullPath

let private containsPrivateLocatorMaterial (locator: string) =
    let absolutePath =
        Regex.IsMatch(locator, @"(^|[\s=:(""'])/(?!/)")
        || Regex.IsMatch(locator, @"(^|[\s=:(""'])([A-Za-z]:[\\/]|\\\\)")

    let secret =
        Regex.IsMatch(
            locator,
            @"(?i)(--?(token|password|secret|api[-_]?key)\b|(?:token|password|secret|api[-_]?key)\s*=)"
        )

    absolutePath || secret

let private normalizedJsonString value =
    match box value with
    | null -> ""
    | text -> (unbox<string> text).Trim()

/// The audit-binding gate's excuse ledger (scripts/check-audit-bindings.py).
///
/// This is the ONE surface whose digest cannot be pinned, because it is the only
/// place an excuse can live: excusing any stale binding REWRITES it, so a
/// citation onto it is invalidated by the documented remedy for an unrelated
/// violation. Binding it is a fixed-point equation with no fixed point.
///
/// The frozen archive the ledger was until rogue3#53. Still exempt: merged
/// audits cite this path, and a prune can still rewrite it.
let ledgerRelativePath = "scripts/audit-binding-exceptions.json"

/// The ledger as it is written today: one `<cycle-id>.json` per cycle under this
/// directory, so two concurrent cycles never share the path their excuse lands
/// in (rogue3#53). Exempting only `ledgerRelativePath` would reproduce the
/// rogue3#38 dead end at the new path the first time a cycle's finding is about
/// its own excuse.
let ledgerDirectoryPrefix = "scripts/audit-binding-exceptions/"

let private ledgerSuffix = ".json"

let private ledgerExemption =
    "this is the audit-binding excuse ledger itself: the only place an excuse can live, so "
    + "excusing any binding rewrites it and invalidates the digest just pinned"

let private derivedExemption =
    "this is a readiness ROLL-UP the repository deliberately does not track (rogue3#56): the "
    + "bytes are a run output, so no checkout can be asked to hold the digest a merged audit "
    + "pinned over them"

/// rogue3#56. The top-level readiness roll-ups the merge gate writes over inputs
/// no checkout can reproduce. A digest binding onto one of them can never go
/// fresh again, and does not even give the SAME answer twice: in a checkout that
/// has run the gate the file is present with churning bytes, and in one that has
/// not it is ABSENT entirely -- which is why rogue3#77 had to waive existence
/// before this exemption could mean anything here.
///
/// The list must stay identical to `DERIVED_ROLLUP_RELPATHS` in
/// scripts/check-audit-bindings.py: a path one tool calls unbindable and the
/// other still binds is exactly the disagreement rogue3#77 exists to close.
let derivedRollupRelativePaths =
    [ "readiness/evidence-graph.md", derivedExemption
      "readiness/performance-evidence.json", derivedExemption
      "readiness/m7-ui-performance.json", derivedExemption ]

let private gitignoreRelativePath = ".gitignore"

/// The paths the workspace `.gitignore` DECLARES ignored, as written.
///
/// A declaration reader, NOT a gitignore engine, and the difference matters:
/// real precedence is order-dependent and last-match-wins, this is
/// order-independent. `!readiness/x` followed by `readiness/x` is ignored by git
/// and NOT declared here, so the exemption is withdrawn and the citation is
/// checked as an ordinary binding. That direction is harmless. The dangerous
/// direction is the other one, so a negation is matched LOOSELY: `!readiness/x`,
/// `!/readiness/x` and a bare `!x` all stop git ignoring `readiness/x`, and every
/// one of them withdraws the path here.
///
/// Ported from `gitignore_declarations` in scripts/check-audit-bindings.py. The
/// two must answer the same question the same way, because they derive the same
/// exempt set from it.
///
/// ONE measured divergence, stated rather than claimed away. A critic fed both
/// implementations 46 adversarial fixtures -- comments in four positions, all
/// four negation forms, leading and trailing whitespace, tabs, CRLF, CR-only, a
/// BOM, no trailing newline, globs, directory rules, `!!`, a bare `!`, and
/// several exotic space characters -- and they agreed on every one except a line
/// wrapped in the C0 file/group separators U+001C-U+001F, which Python's
/// `str.strip()` treats as whitespace and .NET's `String.Trim()` does not. There
/// the F# side declares LESS, so it BINDS a path the checker exempts: the safe
/// direction, and git does not treat those bytes as whitespace either. Left
/// as-is deliberately -- matching .NET's Trim to Python's would mean hand-listing
/// Python's whitespace set, which is a larger and more fragile thing to keep in
/// agreement than the divergence it removes.
let gitignoreDeclarations (workspaceRoot: string) =
    let path = Path.Combine(workspaceRoot, gitignoreRelativePath)

    if not (File.Exists path) then
        Set.empty
    else
        let lines =
            (File.ReadAllText path).Replace("\r\n", "\n").Replace("\r", "\n").Split('\n')
            |> Array.map (fun line -> line.Trim())
            |> Array.filter (fun line ->
                not (String.IsNullOrEmpty line) && not (line.StartsWith("#", StringComparison.Ordinal)))

        let positive =
            lines
            |> Array.filter (fun line -> not (line.StartsWith("!", StringComparison.Ordinal)))
            |> Set.ofArray

        let negated =
            lines
            |> Array.filter (fun line -> line.StartsWith("!", StringComparison.Ordinal))
            |> Array.map (fun line -> line.Substring(1).Trim())
            |> Set.ofArray

        let negates (rule: string) (declared: string) =
            let rule = rule.TrimEnd('/')
            let leaf = declared.Split('/') |> Array.last

            [ declared; "/" + declared; leaf; "/" + leaf ]
            |> List.exists (fun candidate -> String.Equals(rule, candidate, StringComparison.Ordinal))

        positive
        |> Set.filter (fun declared -> not (negated |> Set.exists (fun rule -> negates rule declared)))

/// The derived roll-ups THIS workspace actually declares ignored.
///
/// Keyed on `.gitignore` rather than on `derivedRollupRelativePaths` alone, for
/// the reason `derived_exemptions` gives in scripts/check-audit-bindings.py: a
/// bare constant is a hole that widens silently, while requiring the repository
/// to say the same thing in the file that DECLARES the path a run output means
/// the two statements have to agree.
///
/// Where the checker RAISES a structural violation for a cited path the constant
/// names and `.gitignore` does not, this returns a smaller map and stops there,
/// because withdrawal is already loud on its own: the citation falls through to
/// the ordinary path and fails as `evidence file is missing` when the artifact is
/// absent and `evidence digest is stale` when its bytes have moved. There is one
/// residual gap, stated rather than hidden -- a withdrawn path that happens to be
/// present at exactly the pinned digest passes here while the checker reports the
/// disagreement. That is this tool being stricter about the citation and quieter
/// about the repository's own configuration, which it does not own.
///
/// A workspace with no `.gitignore` -- every selftest fixture -- declares
/// nothing, so nothing is derived-exempt there and a roll-up citation is checked
/// like any other file. Under-exempting is a loud failure and never a silent
/// pass, which is the direction this is allowed to be wrong in.
let derivedExemptions (workspaceRoot: string) =
    let declared = gitignoreDeclarations workspaceRoot

    derivedRollupRelativePaths
    |> List.filter (fun (rel, _) -> Set.contains rel declared)
    |> Map.ofList

/// Why the file at workspace-relative `rel` cannot have its digest checked, or
/// None when it is an ordinary file.
///
/// Takes the workspace-relative path derived from the RESOLVED location, not the
/// locator text, so `file:feedback/../scripts/audit-binding-exceptions.json`
/// is recognised as the ledger too. A textual match on the locator would let a
/// one-token rewrite reintroduce the unsatisfiable binding this exempts.
///
/// TWO exemptions, both ENUMERATED paths rather than a shape, matching
/// `exemption` in scripts/check-audit-bindings.py one for one.
///
/// First the excuse ledger. The directory prefix is not a second exemption: it
/// is the same one, following the ledger from one shared file to one file per
/// cycle. It is a PATH prefix ending in `/` plus a `.json`
/// suffix, so `scripts/audit-binding-exceptions-other/x.json` and
/// `scripts/audit-binding-exceptions/notes.md` stay bound.
///
/// Second the derived readiness roll-ups `derived` names (rogue3#56; see
/// `derivedExemptions`, which decides that set from `.gitignore` and not from a
/// bare constant). `derived` is a separate PARAMETER rather than a lookup inside
/// this function so it stays a pure decision over a path, and so a caller that
/// forgets it UNDER-exempts -- a loud stale or missing binding, never a silent
/// pass. `digestExemption` below is exactly that forgetful caller, retained so a
/// consumer of an older kit keeps compiling.
///
/// In particular a
/// citation onto another `*.audit.json` is NOT exempt under either, for one reason only: it
/// would cost the only check that notices an edit to merged evidence, and an
/// audit's digest CAN be held stable -- nothing this validator does rewrites an
/// audit.
///
/// Do NOT justify that with the gate's convergence argument. `check-audit-bindings.py`
/// can say "a stale binding onto another audit settles in a single `--grandfather`
/// pass" because it HAS an excuse ledger. This validator has none: `--grandfather`
/// changes no verdict here, and a stale citation onto another audit has no remedy
/// short of rebinding merged evidence. That is a real gap in this tool, not an
/// argument for widening the exemption.
///
/// Compared with StringComparison.Ordinal on EVERY platform, deliberately -- not
/// the `pathComparison` used elsewhere in this file, which is OrdinalIgnoreCase on
/// Windows. `exemption` in scripts/check-audit-bindings.py compares with Python
/// `==`, which is case-sensitive everywhere; matching case-insensitively on Windows
/// would exempt a path that checker still binds. The two must stay in agreement: a
/// file the checker calls unbindable is a file this validator must not call stale,
/// and vice versa.
///
/// EXISTENCE IS NOT PART OF THIS DECISION, and both tools must say so (rogue3#77).
/// A caller answered `Some` here must waive the `File.Exists` test as well as the
/// digest comparison. The checker already behaves that way -- an exempt citation
/// is diverted at `collect_bindings` and never becomes a `Binding`, so the
/// "bound file does not exist" violation cannot reach it -- and this file used to
/// behave the opposite way, testing existence UPSTREAM of this function. That
/// asymmetry forbade the one change that cannot mislead (deleting an untracked
/// file) while permitting the one that can (editing it), and it made every
/// exemption acquire a permanent path fixture.
let digestExemptionWith (derived: Map<string, string>) (rel: string) =
    let underLedgerDirectory =
        rel.StartsWith(ledgerDirectoryPrefix, StringComparison.Ordinal)
        && rel.EndsWith(ledgerSuffix, StringComparison.Ordinal)

    if String.Equals(rel, ledgerRelativePath, StringComparison.Ordinal)
       || underLedgerDirectory then
        Some ledgerExemption
    else
        Map.tryFind rel derived

/// The ledger exemption alone, for a caller that has no derived set. Retained so
/// a consumer of an older kit keeps compiling; new callers should pass the
/// workspace's `derivedExemptions` to `digestExemptionWith`. Forgetting it
/// UNDER-exempts, which is loud.
let digestExemption (rel: string) = digestExemptionWith Map.empty rel

/// A citation whose digest was deliberately not checked. Reported, never
/// silently dropped, so a reader can always tell "this citation is exempt"
/// apart from "the validator missed it".
type NotBoundCitation =
    { findingId: string
      locator: string
      path: string
      reason: string }

/// Errors, plus the citations the validator deliberately did not check.
type AuditValidation =
    { errors: string list
      notBound: NotBoundCitation list }

let private workspaceRelative (workspaceRoot: string) (resolved: string) =
    // The resolved path has had its existing segments canonicalized (symlinks
    // followed). Relativising it against a root that still contains a symlinked
    // component yields `../real/...`, which matches no exemption -- so the whole
    // exemption would silently switch off. Both sides must be canonicalized.
    let canonicalRoot = canonicalizeExistingSegments workspaceRoot

    Path
        .GetRelativePath(canonicalRoot, resolved)
        .Replace(Path.DirectorySeparatorChar, '/')

let private resolveEvidencePath (workspaceRoot: string) (locator: string) =
    if
        String.IsNullOrWhiteSpace locator
        || not (locator.StartsWith("file:", StringComparison.Ordinal))
    then
        None
    else
        let relative = locator.Substring("file:".Length).Trim()

        if String.IsNullOrWhiteSpace relative || Path.IsPathRooted relative then
            None
        else
            let root = Path.GetFullPath workspaceRoot
            let candidate = Path.GetFullPath(Path.Combine(root, relative))

            if not (isInside root candidate) then
                None
            else
                let canonicalRoot = canonicalizeExistingSegments root
                let canonicalCandidate = canonicalizeExistingSegments candidate

                if isInside canonicalRoot canonicalCandidate then
                    Some canonicalCandidate
                else
                    None

let validateActionabilityAuditDetailed
    (workspaceRoot: string)
    (reportPath: string)
    (reportText: string)
    (auditText: string)
    =
    let errors = ResizeArray<string>()
    let notBound = ResizeArray<NotBoundCitation>()

    // Read `.gitignore` ONCE per validation, not once per citation: the derived
    // exempt set is a property of the workspace, and re-reading it mid-run would
    // let two citations in one audit be judged against different rules.
    let derived = derivedExemptions workspaceRoot

    let audit =
        try
            let parsed = JsonSerializer.Deserialize<ActionabilityAudit>(auditText)

            if obj.ReferenceEquals(parsed, null) then
                invalidArg "auditText" "expected a JSON object"

            Some(unbox<ActionabilityAudit> (box parsed))
        with ex ->
            errors.Add(sprintf "audit: invalid JSON: %s" ex.Message)
            None

    match audit with
    | None -> ()
    | Some audit ->
        if audit.auditSchema <> 1 then
            errors.Add(sprintf "audit: auditSchema must be 1, got %d" audit.auditSchema)

        let expectedReport =
            Path.GetRelativePath(workspaceRoot, Path.GetFullPath reportPath)
                .Replace(Path.DirectorySeparatorChar, '/')

        if expectedReport.StartsWith("../", StringComparison.Ordinal) then
            errors.Add "audit: report must be inside the workspace"

        if audit.report <> expectedReport then
            errors.Add(sprintf "audit: report binding must be '%s'" expectedReport)

        let expectedDigest = sha256Text reportText

        if audit.reportSha256 <> expectedDigest then
            errors.Add "audit: reportSha256 does not bind the current report bytes"

        if
            audit.criticMode <> "fresh-context-subagent"
            && audit.criticMode <> "separated-critic-pass"
        then
            errors.Add
                "audit: criticMode must be fresh-context-subagent or separated-critic-pass"

        if String.IsNullOrWhiteSpace audit.criticPromptVersion then
            errors.Add "audit: criticPromptVersion must not be empty"

        let expectedFindings = findingContracts reportText

        let audits =
            (if isNull (box audit.findings) then [] else audit.findings)
            |> List.choose (fun finding ->
                if obj.ReferenceEquals(box finding, null) then
                    errors.Add "audit: findings must not contain null entries"
                    None
                else
                    Some finding)

        for id, kind, declaredEvidence in expectedFindings do
            let matches =
                audits
                |> List.filter (fun finding -> normalizedJsonString finding.id = id)

            if List.isEmpty matches then
                errors.Add(sprintf "audit: missing finding '%s'" id)
            elif matches.Length > 1 then
                errors.Add(sprintf "audit: duplicate finding '%s'" id)
            else
                let finding = matches.Head
                let status = normalizedJsonString finding.status

                if not (List.contains status criticStatuses) then
                    errors.Add(sprintf "audit: %s has unknown status '%s'" id status)

                if kind = "positive-pattern" && status <> "positive-pattern" then
                    errors.Add(sprintf "audit: %s positive-pattern must keep that disposition" id)

                if kind <> "positive-pattern" && status = "positive-pattern" then
                    errors.Add(sprintf "audit: %s is not a positive-pattern finding" id)

                if status = "incomplete" || status = "unsupported" then
                    errors.Add(
                        sprintf
                            "actionability: %s remains %s and cannot be handed off as actionable"
                            id
                            status
                    )

                let missingFacts =
                    if isNull (box finding.missingFacts) then [] else finding.missingFacts

                if
                    (status = "actionable" || status = "positive-pattern")
                    && not (List.isEmpty missingFacts)
                then
                    errors.Add(
                        sprintf
                            "audit: %s cannot be %s while missing facts are recorded"
                            id
                            status
                    )

                let checks =
                    (if isNull (box finding.checkedEvidence) then
                         []
                     else
                         finding.checkedEvidence)
                    |> List.choose (fun check ->
                        if obj.ReferenceEquals(box check, null) then
                            errors.Add(sprintf "audit: %s checkedEvidence must not contain null entries" id)
                            None
                        else
                            Some check)

                if List.isEmpty checks then
                    errors.Add(sprintf "audit: %s has no checked evidence" id)

                let checkedLocators =
                    checks |> List.map (fun check -> normalizedJsonString check.locator) |> Set.ofList

                for locator in Set.difference declaredEvidence checkedLocators do
                    errors.Add(
                        sprintf
                            "audit: %s report evidence has no matching check: %s"
                            id
                            locator
                    )

                for locator in Set.difference checkedLocators declaredEvidence do
                    errors.Add(
                        sprintf
                            "audit: %s checked evidence is not declared by the report: %s"
                            id
                            locator
                    )

                for check in checks do
                    let locator = normalizedJsonString check.locator
                    let result = normalizedJsonString check.result

                    let digest =
                        check.sha256
                        |> Option.map normalizedJsonString

                    if String.IsNullOrWhiteSpace locator then
                        errors.Add(sprintf "audit: %s evidence locator must not be empty" id)
                    elif containsPrivateLocatorMaterial locator then
                        errors.Add(
                            sprintf
                                "audit: %s evidence locator exposes an absolute path or secret material"
                                id
                        )

                    match digest with
                    | Some value when not (Regex.IsMatch(value, "^[0-9a-f]{64}$")) ->
                        errors.Add(sprintf "audit: %s evidence sha256 must be 64 lowercase hex characters" id)
                    | _ -> ()

                    if not (List.contains result evidenceResults) then
                        errors.Add(
                            sprintf "audit: %s evidence has unknown result '%s'" id result
                        )

                    if
                        (status = "actionable" || status = "positive-pattern")
                        && result <> "verified"
                    then
                        errors.Add(
                            sprintf
                                "audit: %s cannot be %s with evidence result '%s'"
                                id
                                status
                                result
                        )

                    if locator.StartsWith("file:", StringComparison.Ordinal) then
                        match resolveEvidencePath workspaceRoot locator with
                        | None ->
                            errors.Add(
                                sprintf
                                    "audit: %s evidence locator must be a workspace-relative file: path"
                                    id
                            )
                        | Some path ->
                            // The exemption is decided BEFORE existence is tested
                            // (rogue3#77). `resolveEvidencePath` above has already
                            // rejected anything that escapes the workspace, exempt
                            // or not, so waiving existence here waives exactly one
                            // check and widens nothing about WHICH paths qualify.
                            //
                            // Deliberately in this order and not the reverse. An
                            // exemption says this validator has given up tracking
                            // the file; requiring it to exist kept tracking the
                            // LEAST useful bit, since a deleted exempt file cannot
                            // mislead a reader about evidence nobody is checking,
                            // while an edited one plausibly could and is
                            // deliberately allowed. `exemption` in
                            // scripts/check-audit-bindings.py has always answered
                            // this way -- an exempt citation never becomes a
                            // `Binding` there, so its "bound file does not exist"
                            // violation cannot reach one -- and the two are
                            // required to agree.
                            let relative = workspaceRelative workspaceRoot path

                            match digestExemptionWith derived relative with
                            | Some reason ->
                                // Reported, never checked, present or absent. A
                                // pinned sha256 is not required, because requiring
                                // one would make an author paste a digest this
                                // validator promises never to compare.
                                notBound.Add
                                    { findingId = id
                                      locator = locator
                                      path = relative
                                      reason = reason }
                            | None when not (File.Exists path) ->
                                errors.Add(sprintf "audit: %s evidence file is missing: %s" id locator)
                            | None ->
                                match digest with
                                | None -> errors.Add(sprintf "audit: %s file evidence needs sha256" id)
                                | Some digest ->
                                    let actual = File.ReadAllText path |> sha256Text

                                    if digest <> actual then
                                        errors.Add(
                                            sprintf "audit: %s evidence digest is stale: %s" id locator
                                        )
                    elif not (String.IsNullOrWhiteSpace locator) && Path.IsPathRooted locator then
                        errors.Add(sprintf "audit: %s evidence locator exposes an absolute path" id)

        let expectedIds = expectedFindings |> List.map (fun (id, _, _) -> id) |> Set.ofList

        for finding in audits do
            let findingId = normalizedJsonString finding.id

            if String.IsNullOrWhiteSpace findingId then
                errors.Add "audit: finding id must not be empty"
            elif not (Set.contains findingId expectedIds) then
                errors.Add(sprintf "audit: unknown finding '%s'" findingId)

    // One audit routinely cites the same unbindable file from several findings.
    // Report each distinct citation once.
    let distinctNotBound =
        notBound
        |> Seq.distinctBy (fun citation -> citation.findingId, citation.locator)
        |> Seq.sortBy (fun citation -> citation.findingId, citation.locator)
        |> List.ofSeq

    { errors = List.ofSeq errors
      notBound = distinctNotBound }

/// Errors only. Retained so a consumer of an older kit keeps compiling; new
/// callers should use `validateActionabilityAuditDetailed`, which also reports
/// the citations that were deliberately not checked.
let validateActionabilityAudit
    (workspaceRoot: string)
    (reportPath: string)
    (reportText: string)
    (auditText: string)
    =
    (validateActionabilityAuditDetailed workspaceRoot reportPath reportText auditText).errors

type Checkpoint =
    { timestampUtc: string
      cycle: string
      phase: string
      surface: string
      kind: string
      summary: string
      evidence: string
      cost: string
      owner: string }

type ZeroEventActivationReceipt =
    { activationSchema: int
      receiptKind: string
      timestampUtc: string
      cycle: string
      exercisedPhases: string list
      evidence: string list
      reasonNoEventQualified: string }

let private requireValue name value =
    if String.IsNullOrWhiteSpace value then
        invalidArg name (sprintf "%s must not be empty" name)

let private requireCycle cycle =
    requireValue "cycle" cycle

    if not (Regex.IsMatch(cycle, "^[a-z0-9][a-z0-9-]*$")) then
        invalidArg "cycle" "cycle must be lowercase letters, digits, and hyphens"

let private checkpointDirectory root =
    Path.Combine(root, "feedback", "checkpoints")

let private checkpointEventPath root cycle =
    Path.Combine(checkpointDirectory root, cycle + ".jsonl")

let activationReceiptPath root cycle =
    Path.Combine(checkpointDirectory root, cycle + ".activation.json")

let appendCheckpoint root cycle phase surface kind summary evidence cost owner =
    for name, value in
        [ "cycle", cycle
          "phase", phase
          "surface", surface
          "kind", kind
          "summary", summary
          "evidence", evidence
          "cost", cost
          "owner", owner ] do
        requireValue name value

    requireCycle cycle

    if not (List.contains surface surfaces) then
        invalidArg "surface" (sprintf "unknown surface '%s'" surface)

    if not (List.contains kind kinds) then
        invalidArg "kind" (sprintf "unknown kind '%s'" kind)

    let receiptPath = activationReceiptPath root cycle

    if File.Exists receiptPath || Directory.Exists receiptPath then
        invalidArg
            "cycle"
            (sprintf
                "cycle '%s' already has a zero-event activation receipt; remove the contradiction before recording an event"
                cycle)

    let checkpoint =
        { timestampUtc = DateTimeOffset.UtcNow.ToString "O"
          cycle = cycle
          phase = phase
          surface = surface
          kind = kind
          summary = summary
          evidence = evidence
          cost = cost
          owner = owner }

    let directory = checkpointDirectory root
    Directory.CreateDirectory directory |> ignore
    let path = checkpointEventPath root cycle
    let line = JsonSerializer.Serialize checkpoint + Environment.NewLine
    File.AppendAllText(path, line, UTF8Encoding(false))
    path

let appendZeroEventActivation root cycle exercisedPhases evidence reasonNoEventQualified =
    requireCycle cycle
    requireValue "reason" reasonNoEventQualified

    let requireNonEmptyValues name values =
        if List.isEmpty values then
            invalidArg name (sprintf "%s must contain at least one value" name)

        for value in values do
            requireValue name value

    requireNonEmptyValues "phases" exercisedPhases
    requireNonEmptyValues "evidence" evidence

    if exercisedPhases |> List.distinct |> List.length <> List.length exercisedPhases then
        invalidArg "phases" "phases must not contain duplicates"

    if evidence |> List.exists containsPrivateLocatorMaterial then
        invalidArg "evidence" "evidence must not expose an absolute path or secret material"

    let eventPath = checkpointEventPath root cycle

    if File.Exists eventPath || Directory.Exists eventPath then
        invalidArg
            "cycle"
            (sprintf
                "cycle '%s' already has checkpoint event state; a zero-event receipt would contradict it"
                cycle)

    let directory = checkpointDirectory root
    Directory.CreateDirectory directory |> ignore
    let path = activationReceiptPath root cycle

    let receipt =
        { activationSchema = 1
          receiptKind = "zero-event-activation"
          timestampUtc = DateTimeOffset.UtcNow.ToString "O"
          cycle = cycle
          exercisedPhases = exercisedPhases
          evidence = evidence
          reasonNoEventQualified = reasonNoEventQualified }

    use stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None)
    use writer = new StreamWriter(stream, UTF8Encoding(false))
    writer.Write(JsonSerializer.Serialize receipt)
    writer.Write Environment.NewLine
    path

let private validateCheckpointFileForCycle (expectedCycle: string) (path: string) =
    let errors = ResizeArray<string>()

    if not (File.Exists path) then
        [ sprintf "checkpoints: file not found: %s" path ]
    else
        try
            let mutable lineCount = 0

            for index, line in File.ReadLines path |> Seq.indexed do
                lineCount <- lineCount + 1

                if String.IsNullOrWhiteSpace line then
                    errors.Add(sprintf "checkpoints: line %d is empty" (index + 1))
                else
                    try
                        use document = JsonDocument.Parse line
                        let root = document.RootElement

                        let readProperty (name: string) =
                            match root.TryGetProperty name with
                            | true, value when value.ValueKind = JsonValueKind.String ->
                                value.GetString() |> Option.ofObj |> Option.defaultValue ""
                            | _ ->
                                errors.Add(
                                    sprintf "checkpoints: line %d is missing %s" (index + 1) name
                                )

                                ""

                        let values =
                            [ for name in
                                  [ "timestampUtc"
                                    "cycle"
                                    "phase"
                                    "surface"
                                    "kind"
                                    "summary"
                                    "evidence"
                                    "cost"
                                    "owner" ] do
                                  yield name, readProperty name ]

                        for name, value in values do
                            if String.IsNullOrWhiteSpace value then
                                errors.Add(
                                    sprintf "checkpoints: line %d has empty %s" (index + 1) name
                                )

                        let valueOf name = values |> List.find (fst >> (=) name) |> snd
                        let cycle = valueOf "cycle"
                        let surface = valueOf "surface"
                        let kind = valueOf "kind"

                        if cycle <> expectedCycle then
                            errors.Add(
                                sprintf
                                    "checkpoints: line %d cycle must be '%s', got '%s'"
                                    (index + 1)
                                    expectedCycle
                                    cycle
                            )

                        if not (List.contains surface surfaces) then
                            errors.Add(
                                sprintf
                                    "checkpoints: line %d has unknown surface '%s'"
                                    (index + 1)
                                    surface
                            )

                        if not (List.contains kind kinds) then
                            errors.Add(
                                sprintf
                                    "checkpoints: line %d has unknown kind '%s'"
                                    (index + 1)
                                    kind
                            )
                    with ex ->
                        errors.Add(
                            sprintf
                                "checkpoints: line %d is invalid JSON: %s"
                                (index + 1)
                                ex.Message
                        )

            if lineCount = 0 then
                errors.Add(
                    "checkpoints: event file contains no events; record a zero-event activation receipt"
                )

            List.ofSeq errors
        with ex ->
            [ sprintf "checkpoints: state is unreadable: %s" ex.Message ]

let validateCheckpointFile (path: string) =
    let expectedCycle =
        Path.GetFileNameWithoutExtension path |> Option.ofObj |> Option.defaultValue ""

    validateCheckpointFileForCycle expectedCycle path

let validateZeroEventActivationFile (workspaceRoot: string) (expectedCycle: string) (path: string) =
    if Directory.Exists path then
        [ sprintf "checkpoints: zero-event activation receipt is unreadable: %s" path ]
    elif not (File.Exists path) then
        [ sprintf "checkpoints: zero-event activation receipt not found: %s" path ]
    else
        let errors = ResizeArray<string>()

        try
            let canonicalRoot = canonicalizeExistingSegments workspaceRoot
            let canonicalPath = canonicalizeExistingSegments path

            if not (isInside canonicalRoot canonicalPath) then
                raise (
                    InvalidDataException(
                        "zero-event activation receipt resolves outside the workspace"
                    )
                )

            use document = JsonDocument.Parse(File.ReadAllText canonicalPath)
            let root = document.RootElement

            if root.ValueKind <> JsonValueKind.Object then
                raise (JsonException "activation receipt root must be a JSON object")

            let allowedProperties =
                set
                    [ "activationSchema"
                      "receiptKind"
                      "timestampUtc"
                      "cycle"
                      "exercisedPhases"
                      "evidence"
                      "reasonNoEventQualified" ]

            let properties = root.EnumerateObject() |> Seq.toList

            for property in properties do
                if not (Set.contains property.Name allowedProperties) then
                    errors.Add(
                        sprintf
                            "checkpoints: activation receipt contains unknown property '%s'"
                            property.Name
                    )

            for name, count in
                properties
                |> List.countBy (fun property -> property.Name)
                |> List.filter (fun (_, count) -> count > 1) do
                errors.Add(
                    sprintf "checkpoints: activation receipt contains duplicate property '%s'" name
                )

            let readString (name: string) =
                match root.TryGetProperty name with
                | true, value when value.ValueKind = JsonValueKind.String ->
                    value.GetString() |> Option.ofObj |> Option.defaultValue ""
                | _ ->
                    errors.Add(sprintf "checkpoints: activation receipt is missing %s" name)
                    ""

            let readStringArray (name: string) =
                match root.TryGetProperty name with
                | true, value when value.ValueKind = JsonValueKind.Array ->
                    [ for item in value.EnumerateArray() do
                          if item.ValueKind = JsonValueKind.String then
                              yield item.GetString() |> Option.ofObj |> Option.defaultValue ""
                          else
                              errors.Add(
                                  sprintf
                                      "checkpoints: activation receipt %s must contain only strings"
                                      name
                              ) ]
                | _ ->
                    errors.Add(sprintf "checkpoints: activation receipt is missing %s" name)
                    []

            match root.TryGetProperty "activationSchema" with
            | true, value when value.ValueKind = JsonValueKind.Number ->
                match value.TryGetInt32() with
                | true, 1 -> ()
                | _ -> errors.Add "checkpoints: activationSchema must be 1"
            | _ -> errors.Add "checkpoints: activation receipt is missing activationSchema"

            let receiptKind = readString "receiptKind"
            let timestampUtc = readString "timestampUtc"
            let cycle = readString "cycle"
            let phases = readStringArray "exercisedPhases"
            let evidence = readStringArray "evidence"
            let reason = readString "reasonNoEventQualified"

            if receiptKind <> "zero-event-activation" then
                errors.Add "checkpoints: receiptKind must be zero-event-activation"

            match DateTimeOffset.TryParse timestampUtc with
            | true, timestamp when timestamp.Offset = TimeSpan.Zero -> ()
            | _ -> errors.Add "checkpoints: timestampUtc must be a UTC timestamp"

            if cycle <> expectedCycle then
                errors.Add(
                    sprintf
                        "checkpoints: activation receipt cycle must be '%s', got '%s'"
                        expectedCycle
                        cycle
                )

            for name, values in [ "exercisedPhases", phases; "evidence", evidence ] do
                if List.isEmpty values then
                    errors.Add(sprintf "checkpoints: activation receipt %s must not be empty" name)

                if values |> List.exists String.IsNullOrWhiteSpace then
                    errors.Add(
                        sprintf
                            "checkpoints: activation receipt %s must not contain empty values"
                            name
                    )

            if phases |> List.distinct |> List.length <> List.length phases then
                errors.Add "checkpoints: activation receipt exercisedPhases must not contain duplicates"

            if evidence |> List.exists containsPrivateLocatorMaterial then
                errors.Add(
                    "checkpoints: activation receipt evidence exposes an absolute path or secret material"
                )

            if String.IsNullOrWhiteSpace reason then
                errors.Add "checkpoints: activation receipt reasonNoEventQualified must not be empty"

            List.ofSeq errors
        with
        | :? JsonException as ex ->
            [ sprintf "checkpoints: activation receipt is malformed JSON: %s" ex.Message ]
        | :? InvalidDataException as ex ->
            [ sprintf "checkpoints: zero-event activation receipt is unreadable: %s" ex.Message ]
        | ex ->
            [ sprintf "checkpoints: zero-event activation receipt is unreadable: %s" ex.Message ]

let validateCheckpointState (root: string) (cycle: string) =
    let errors = ResizeArray<string>()

    try
        requireCycle cycle
    with :? ArgumentException as ex ->
        errors.Add(sprintf "checkpoints: %s" ex.Message)

    if errors.Count > 0 then
        List.ofSeq errors
    else
        let eventPath = checkpointEventPath root cycle
        let receiptPath = activationReceiptPath root cycle
        let hasEvents = File.Exists eventPath || Directory.Exists eventPath
        let hasReceipt = File.Exists receiptPath || Directory.Exists receiptPath

        match hasEvents, hasReceipt with
        | true, true ->
            [ sprintf
                  "checkpoints: cycle '%s' has both checkpoint events and a zero-event activation receipt"
                  cycle ]
        | true, false when Directory.Exists eventPath ->
            [ sprintf "checkpoints: checkpoint event state is unreadable: %s" eventPath ]
        | true, false ->
            try
                let canonicalRoot = canonicalizeExistingSegments root
                let canonicalEventPath = canonicalizeExistingSegments eventPath

                if not (isInside canonicalRoot canonicalEventPath) then
                    [ sprintf
                          "checkpoints: checkpoint event state is unreadable: event file resolves outside the workspace" ]
                else
                    validateCheckpointFileForCycle cycle canonicalEventPath
            with ex ->
                [ sprintf "checkpoints: checkpoint event state is unreadable: %s" ex.Message ]
        | false, true -> validateZeroEventActivationFile root cycle receiptPath
        | false, false ->
            [ sprintf
                  "checkpoints: cycle '%s' is missing both checkpoint events and a zero-event activation receipt"
                  cycle ]
