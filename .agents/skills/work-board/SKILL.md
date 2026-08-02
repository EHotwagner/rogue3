---
name: work-board
description: Use when explicitly asked to burn down one coordination-wired product workspace's board. Reconcile and triage backlog first, fan out isolated item workers through disjoint lanes, verify, and re-plan.
---

# work-board

Burn down one coordination-wired workspace's board. The local board is both plan and ledger.

1. Reconcile the workspace and consume the complete four-part `check-board` result.
2. Run [backlog-triage](references/backlog-triage.md), classifying every relevant parked row without
   guessing human judgement and promoting only evidenced actionable work to `Ready`.
3. Compute local disjoint lanes through the normal scheduler and size them against the fixed
   concurrency model below.
4. Spawn one fresh disposable worker per lane; give each a stable feedback cycle id. Each owns one item
   through claim, implementation, review, green merge, obligations, verified feedback, and done. During
   worker setup, interactive/game work
   must explicitly invoke the `pnext-item` performance-first planning gate before implementation
   begins.

## Concurrency model — two waves of three, plus two reviewers

The host runs **two waves concurrently**, `A` and `B`, with **three item-worker slots each**: at most
**six item workers in flight**. Size with `batch --repo <this-repo> -n 6` and split the returned
disjoint set across the two waves; `take` remains the claiming read, so never hand a worker an item
number. Fewer than six schedulable items is normal — fill what you can and leave the rest empty.

**Two further subagents are dedicated to review and are never item workers.** They hold no claim, take
no item, and do not count against the six. They serve the host: adversarially verifying merged items
and worker feedback against ground truth, and standing in as the independent fresh-context critic a
lane needs, so lanes do not each improvise their own. Total concurrent agents: **eight**.

**Refill rule.** When the number of items actively being worked across *both* waves falls to **three or
fewer**, consolidate the survivors into a single wave and immediately start the second wave from a
freshly sized disjoint batch. Do not wait for a full drain — a wave that idles while one straggler
finishes is the throughput this model exists to recover. Reconciliation and Backlog triage still run
before that new wave, against a fresh read (step 7); consolidation changes *when* you re-plan, never
*whether* you re-plan.

Both caps are ceilings, not targets. The shared rate budget still governs: a worker returning `EX_RATE`
(exit 75) is a fleet-wide stop for all eight agents, not that worker's problem. Six concurrent workers
reach that limit sooner than a conservative cap does, so treat the back-off discipline in
[deep detail](references/deep-detail.md) as load-bearing rather than theoretical.
5. Report live item state immediately. Whenever the host changes or observes a material transition
   (`Ready`, `In progress`, review, CI, merged, release, downstream adoption, `Blocked`, or `Done`),
   emit exactly two concise user-facing lines:
   - `<item> — <new status>: <work in progress or gate being awaited>`
   - `Active: <item> — <current activity/gate>; ...` listing every currently active item and its
     current activity or gate.
   Do not defer either line to a wave summary or final response. Keep the driver turn alive while any
   item remains active, continue the host loop, and report each transition when it occurs.
6. Run the exact checkpoint, schema-v2 report, and activation-envelope validators against merged paths.
   Missing, invalid, unreadable, or wrong-cycle feedback fails closed; then discard the worker.
7. Reconcile and re-triage from a fresh read after every wave so worker-filed follow-ups enter the next
   plan while the simple-versus-complex SDD lifecycle branch remains inside each item worker.
8. Stop only when fresh reconciliation and triage leave no startable or actionable/untriaged work and
   every completed cycle is covered by a validated workspace feedback roll-up. Surface deliberately
   parked and human-blocked backlog without spinning; then update/land the workspace report.

Load [host-loop](references/host-loop.md) for the shared worker/verification/termination contract and
[workspace-scope](references/workspace-scope.md) for the single-repository ledger rules.
Load [feedback-contract](references/feedback-contract.md) for worker activation, exact validation
commands, zero-event representation, host acceptance, and board termination.
Load [deep detail](references/deep-detail.md) only for recovery paths and extended rationale.
