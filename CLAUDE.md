# Pizza ordering proof of concept

React order builder + .NET order service for a pizza ordering proof of concept.

## Where things are

- `docs/spec.md`: source of truth for requirements, decisions, API, golden cases and invariants. Read the relevant sections before planning. If code, tests and spec disagree, stop and ask.
- `src/Pizza.OrderService/`, `src/Pizza.OrderBuilder/`, `tests/`: each has its own `CLAUDE.md`.
- `build/`: containers and the guardrail script.

## Commands

- `dotnet build` / `dotnet test` (from root)
- `dotnet run --project src/Pizza.OrderService` (http://localhost:5080)
- `npm install`, then `npm run dev -w src/Pizza.OrderBuilder`
- `npm test` (from root: frontend tests)
- `docker compose up` (builder on http://localhost:8080)

## Rules everywhere

- Work through `/slice`: plan first, wait for my OK.
- Prefer deep modules: a small public interface hiding substantial logic. Test through that interface. No thin classes, interfaces or files that only forward calls.
- Proportionate design: no MediatR, CQRS, repository interfaces, generic pipelines, auth or EF Core.
- Use only the packages listed in each `CLAUDE.md`. Ask before adding any other.
- Run tests after a change and report the real result. Never claim something works without running it.
- Keep diffs small and focused. Don't reformat or rename unrelated code.
