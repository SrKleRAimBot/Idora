using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Idora.AspNetCore.Tests.Support;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace Idora.AspNetCore.Tests;

/// <summary>
/// Proves that <see cref="IdoraId"/> binds in minimal APIs and MVC with nothing registered for it:
/// from the route, the query string, a header and a JSON body, rejecting malformed input with a 400.
/// </summary>
public sealed class BindingTests
{
    private const string UserId = "usr_01k226e0g0008j4ct4ank7f24s";
    private const string OrderId = "ord_0198846702007011a233445566778899";

    [Fact]
    public async Task MinimalApi_BindsAnIdoraIdFromTheRoute()
    {
        // Arrange
        await using TestApp app = await TestApp.StartAsync(null, web =>
            web.MapGet("/users/{id}", (IdoraId id) => $"{id.Prefix} {id.TimestampMilliseconds}"));

        // Act
        string body = await app.Client.GetStringAsync($"/users/{UserId}", TestContext.Current.CancellationToken);

        // Assert
        body.ShouldBe("usr 1754568000000");
    }

    [Fact]
    public async Task MinimalApi_RejectsAMalformedIdentifierWithABadRequest()
    {
        // Arrange
        await using TestApp app = await TestApp.StartAsync(null, web =>
            web.MapGet("/users/{id}", (IdoraId id) => id.Prefix));

        // Act
        HttpResponseMessage response = await app.Client.GetAsync("/users/usr_nope", TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task MinimalApi_BindsOptionalQueryAndHeaderValues()
    {
        // Arrange
        await using TestApp app = await TestApp.StartAsync(null, web =>
            web.MapGet("/search", (IdoraId? after, [FromHeader(Name = "X-Request-Id")] IdoraId? requestId) =>
                $"{after?.ToString() ?? "-"} {requestId?.ToString() ?? "-"}"));
        using HttpRequestMessage request = new(HttpMethod.Get, $"/search?after={OrderId}");
        request.Headers.Add("X-Request-Id", UserId);

        // Act
        HttpResponseMessage withBoth = await app.Client.SendAsync(request, TestContext.Current.CancellationToken);
        string withNeither = await app.Client.GetStringAsync("/search", TestContext.Current.CancellationToken);

        // Assert
        (await withBoth.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).ShouldBe($"{OrderId} {UserId}");
        withNeither.ShouldBe("- -");
    }

    [Fact]
    public async Task MinimalApi_RoundTripsIdoraIdsInAJsonBody()
    {
        // Arrange
        await using TestApp app = await TestApp.StartAsync(null, web =>
            web.MapPost("/orders", (PlaceOrder order) => order with { OrderId = IdoraId.Parse(OrderId) }));

        // Act
        HttpResponseMessage response = await app.Client.PostAsJsonAsync(
            "/orders",
            new { customerId = UserId },
            TestContext.Current.CancellationToken);
        JsonElement body = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        body.GetProperty("customerId").GetString().ShouldBe(UserId);
        body.GetProperty("orderId").GetString().ShouldBe(OrderId);
    }

    [Fact]
    public async Task MinimalApi_RejectsAMalformedIdentifierInAJsonBody()
    {
        // Arrange
        await using TestApp app = await TestApp.StartAsync(null, web =>
            web.MapPost("/orders", (PlaceOrder order) => order));

        // Act
        HttpResponseMessage response = await app.Client.PostAsJsonAsync(
            "/orders",
            new { customerId = "customer-1" },
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Mvc_BindsAnIdoraIdFromTheRouteAndQuery()
    {
        // Arrange
        await using TestApp app = await StartMvcAsync();

        // Act
        string body = await app.Client.GetStringAsync(
            $"/api/users/{UserId}?referrer={OrderId}",
            TestContext.Current.CancellationToken);

        // Assert
        body.ShouldBe($"usr ord");
    }

    [Fact]
    public async Task Mvc_RejectsAMalformedIdentifierWithAProblemDetailsResponse()
    {
        // Arrange
        await using TestApp app = await StartMvcAsync();

        // Act
        HttpResponseMessage response = await app.Client.GetAsync("/api/users/usr_nope", TestContext.Current.CancellationToken);
        ValidationProblemDetails? problem =
            await response.Content.ReadFromJsonAsync<ValidationProblemDetails>(TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        problem.ShouldNotBeNull();
        problem.Errors.ShouldContainKey("id");
    }

    [Fact]
    public async Task Mvc_RoundTripsIdoraIdsInAJsonBody()
    {
        // Arrange
        await using TestApp app = await StartMvcAsync();

        // Act
        HttpResponseMessage response = await app.Client.PostAsJsonAsync(
            "/api/users/orders",
            new { customerId = UserId },
            TestContext.Current.CancellationToken);
        PlaceOrder? body = await response.Content.ReadFromJsonAsync<PlaceOrder>(TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        body.ShouldNotBeNull();
        body.CustomerId.ShouldBe(IdoraId.Parse(UserId));
    }

    private static Task<TestApp> StartMvcAsync() =>
        TestApp.StartAsync(
            services => services.AddControllers().AddApplicationPart(typeof(BindingTests).Assembly),
            web => web.MapControllers());

    /// <summary>A request body carrying identifiers.</summary>
    public sealed record PlaceOrder(IdoraId CustomerId, IdoraId? OrderId = null);
}

/// <summary>An MVC controller binding <see cref="IdoraId"/> parameters.</summary>
[ApiController]
[Route("api/users")]
public sealed class BindingController : ControllerBase
{
    [HttpGet("{id}")]
    public string Get(IdoraId id, [FromQuery] IdoraId? referrer) => $"{id.Prefix} {referrer?.Prefix}";

    [HttpPost("orders")]
    public BindingTests.PlaceOrder Post(BindingTests.PlaceOrder order) => order;
}
