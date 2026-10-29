# Tests

Spec: `docs/spec.md` §9 (golden cases), §10 (invariants).

- Golden cases were calculated by hand. Never change an expected value. If one looks wrong, stop and show the arithmetic.
- Test only through public interfaces: `PricingEngine.Price`, `OrderBook`, the HTTP endpoints, `orderServiceClient`. No `InternalsVisibleTo`.
- Each test targets an invariant from §10. Name it after the invariant.
- Money in tests is `decimal` (`29.28m`). Never `double`.
- Concurrency tests use real parallelism (`Task.WhenAll`), repeat in a loop, and assert on outcomes, not timing.
- Every test is isolated: the test assembly sets `[assembly: FixtureLifeCycle(LifeCycle.InstancePerTestCase)]`, so each test gets a fresh fixture instance. No shared mutable state in static fields or `[OneTimeSetUp]`.
- Use NUnit's constraint model (`Assert.That(actual, Is.EqualTo(expected))`). Golden cases use `TestCaseSource`, since `decimal` can't appear in attributes.

## Packages

Backend: NUnit, NUnit3TestAdapter, `Microsoft.NET.Test.Sdk`, `Microsoft.AspNetCore.Mvc.Testing`. Frontend: Vitest.
