---
name: slice
description: Build the next vertical slice of the pizza proof of concept: plan, wait for approval, test first, verify, independent review, propose a commit.
disable-model-invocation: true
---

Slice to build: $ARGUMENTS (if empty, the next unfinished slice below).

## Slice order

0. Scaffold: `PizzaOrdering.slnx`, the `Pizza.OrderService` web API, the `Pizza.OrderService.Tests` NUnit project (with the assembly-level `FixtureLifeCycle` attribute from `tests/CLAUDE.md`), the Vite React-TS app in `src/Pizza.OrderBuilder`, `tests/Pizza.OrderBuilder.Tests` with Vitest, and a root `package.json` with both as npm workspaces. Then confirm `dotnet test` and `npm test` run.
1. Pricing engine and golden cases.
2. Menu and quote endpoints.
3. Builder UI with live total.
4. Submit and status (including polling and advance).
5. Update and cancel, with conflicts.
6. Idempotent retries and `expectedTotal`.
7. Containers (`build/`).
8. Frontend client tests.
9. README.

## Steps

1. Read the spec sections and `CLAUDE.md` files this slice touches.
2. **Plan:** files to touch, any change to a public interface, and tests to add by invariant number (spec §10). Stop and wait for my OK.
3. Write the tests first for pricing and lifecycle behavior, then implement.
4. Run `dotnet test` and/or `npm test`. Report the actual result.
5. Run the `spec-reviewer` subagent. Fix blocking findings, or tell me why not.
6. Propose a one-line commit message scoped to this slice. Don't commit unless I ask.
7. If I rejected or corrected anything you produced during this slice, remind me to make a note of it.
