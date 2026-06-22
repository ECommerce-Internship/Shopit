---
name: bug-fixing
description: Use this skill whenever fixing a reported or discovered bug in the Shopit codebase. Enforces a disciplined fix process — reproduce with a failing test first, locate the root cause, apply the minimal fix, confirm the test passes, then scan the rest of the codebase for the same bug pattern. Trigger this skill any time the user says "fix this bug", reports unexpected behavior, or asks to investigate a defect.
---

# Bug Fixing Skill

This skill enforces a strict, ordered workflow for fixing bugs in Shopit. Do not skip steps or reorder them, even if the fix seems obvious.

## Step 1 — Reproduce with a failing test

Before touching any source file, write an automated test that demonstrates the bug. The test must fail for the same reason the bug occurs (not for an unrelated reason like a missing mock or compile error).

- Place the test in the appropriate test project (e.g. `Shopit.API.Tests`, `Shopit.Application.Tests`, or `Shopit.Infrastructure.Tests`) matching the layer where the bug lives.
- Name the test to describe the bug, e.g. `Should_Fail_When_ServiceRegisteredTwice`.
- Run the test and confirm it fails. Paste/show the failing output before proceeding.
- If the bug cannot be captured by an automated test (e.g. a pure configuration issue), write the smallest possible reproduction script or explicit manual verification steps instead, and state clearly why a unit test isn't applicable.

Do not proceed to Step 2 until the failing test (or equivalent reproduction) is confirmed.

## Step 2 — Locate the root cause

- Identify the exact file(s) and line(s) responsible.
- Explain in plain language *why* the bug happens, not just *where*. A location without a causal explanation is not sufficient.
- If the bug could plausibly stem from more than one place, rule out the alternatives explicitly before fixing.

## Step 3 — Apply the minimal fix

- Fix only what is necessary to resolve the root cause identified in Step 2.
- Do not perform unrelated refactors, renames, or style changes in the same fix.
- If the fix touches a shared/critical file (e.g. `Program.cs`, `Startup.cs`, DI configuration, migrations), provide the full updated file content, not a diff or partial snippet.

## Step 4 — Confirm the test passes

- Re-run the exact failing test from Step 1 and confirm it now passes.
- Run the full relevant test suite (not just the new test) to confirm no regressions were introduced.
- Show the passing output before proceeding.

## Step 5 — Scan for the same bug elsewhere

- Search the codebase for the same bug pattern (e.g. duplicate registrations, the same incorrect comparison operator, the same missing null check) using grep/search tools, not just visual inspection.
- For each additional occurrence found, repeat Steps 2–4 (locate, fix, confirm) for that occurrence.
- Explicitly report the scan results, even if no further occurrences are found — "scanned X, found none" is a valid and required output, not an optional courtesy.

## Completion checklist

A bug fix produced under this skill is only considered complete when:
- [ ] A failing test (or documented equivalent) was shown before any fix
- [ ] Root cause was explained, not just located
- [ ] Fix was minimal and scoped to the root cause
- [ ] The original failing test now passes
- [ ] The full test suite was run with no new failures
- [ ] A codebase-wide scan for the same pattern was performed and reported