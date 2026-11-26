namespace Idora.EntityFrameworkCore.Tests.Support;

/// <summary>The prefixes the main model's tests run with.</summary>
/// <remarks>
/// EF Core builds a context type's model once per process and caches it, and these prefixes shape
/// that model (the exact key lengths), so every test of <see cref="TestDbContext"/> must register
/// the same ones. That mirrors an application, where the registry is a singleton.
/// </remarks>
public static class TestRegistrations
{
    public static void Configure(IdoraOptions options) =>
        options
            .RegisterEntity<User>("usr")
            .RegisterEntity<Order>("ord")
            .RegisterEntity<Invoice>("inv")
            .RegisterEntity<Animal>("ani")
            .RegisterEntity<Cat>("cat");
}
