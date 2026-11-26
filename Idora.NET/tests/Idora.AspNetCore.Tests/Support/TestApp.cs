using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Idora.AspNetCore.Tests.Support;

/// <summary>
/// An application hosted in memory: real routing, binding and validation, no sockets.
/// </summary>
public sealed class TestApp : IAsyncDisposable
{
    private readonly WebApplication _app;

    private TestApp(WebApplication app)
    {
        _app = app;
        Client = app.GetTestClient();
    }

    public HttpClient Client { get; }

    public IServiceProvider Services => _app.Services;

    /// <summary>Builds and starts an application.</summary>
    /// <param name="configureServices">Registers services, after Idora and the route constraint.</param>
    /// <param name="configureApp">Maps endpoints.</param>
    public static async Task<TestApp> StartAsync(
        Action<IServiceCollection>? configureServices,
        Action<WebApplication> configureApp)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();

        builder.Services
            .AddIdora(options => options
                .RegisterEntity<User>("usr")
                .RegisterEntity<Order>("ord"))
            .AddIdoraRouteConstraint();
        configureServices?.Invoke(builder.Services);

        WebApplication app = builder.Build();
        configureApp(app);
        await app.StartAsync(TestContext.Current.CancellationToken);

        return new TestApp(app);
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await _app.DisposeAsync();
    }
}

/// <summary>Registered as <c>usr</c>.</summary>
public sealed class User;

/// <summary>Registered as <c>ord</c>.</summary>
public sealed class Order;

/// <summary>Never registered.</summary>
public sealed class Unregistered;
