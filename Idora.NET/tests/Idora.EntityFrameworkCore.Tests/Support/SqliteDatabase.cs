using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Idora.EntityFrameworkCore.Tests.Support;

/// <summary>
/// A private in-memory SQLite database and the container an application would build around it.
/// </summary>
/// <remarks>
/// An in-memory SQLite database lives exactly as long as its first connection, so this holds one open
/// for the life of the test and hands it to every context. Each test gets its own database, so tests
/// never see each other's rows.
/// </remarks>
public sealed class SqliteDatabase<TContext> : IDisposable
    where TContext : DbContext
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly ServiceProvider _services;

    public SqliteDatabase(Action<IdoraOptions> configureIdora)
    {
        _connection.Open();

        _services = new ServiceCollection()
            .AddIdora(configureIdora)
            .AddDbContext<TContext>(options => options.UseSqlite(_connection))
            .BuildServiceProvider();

        using TContext context = CreateContext();
        context.Database.EnsureCreated();
    }

    /// <summary>The container, for resolving Idora's own services in assertions.</summary>
    public IServiceProvider Services => _services;

    /// <summary>A fresh context over the shared connection, as a request scope would get.</summary>
    public TContext CreateContext() => _services.CreateScope().ServiceProvider.GetRequiredService<TContext>();

    public void Dispose()
    {
        _services.Dispose();
        _connection.Dispose();
    }
}
