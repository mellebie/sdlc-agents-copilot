// Infrastructure/TcpaTestCollection.cs
// Source: Agent 09b (Drew) — Functional & E2E Tests
// Defines an xUnit collection fixture so all TCPA functional test classes share a single
// TcpaTestFactory instance. This prevents Serilog's static ReloadableLogger from being
// frozen multiple times (which throws InvalidOperationException) when each class would
// otherwise create its own factory.
//
// Usage in test classes:
//   [Collection(TcpaTestCollection.Name)]
//   public class MyTests : FunctionalTestBase
//   {
//       public MyTests(TcpaTestFactory factory) : base(factory) { }
//   }

using Xunit;

namespace TCPA.Functional.Tests.Infrastructure;

/// <summary>
/// Registers the shared <see cref="TcpaTestFactory"/> collection fixture.
/// One WebApplicationFactory instance is created for the entire test run.
/// All test classes in this collection share the same InMemory database — tests
/// must use unique phone numbers / message IDs to avoid state interference.
/// </summary>
[CollectionDefinition(Name)]
public class TcpaTestCollection : ICollectionFixture<TcpaTestFactory>
{
    public const string Name = "TcpaFunctional";
}
