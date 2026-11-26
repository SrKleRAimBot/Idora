using System.Net;
using Idora.AspNetCore.Tests.Support;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace Idora.AspNetCore.Tests;

/// <summary>
/// Covers the <c>idora</c> route constraint, both through real routing and directly.
/// </summary>
public sealed class RouteConstraintTests
{
    private const string UserId = "usr_01k226e0g0008j4ct4ank7f24s";
    private const string OrderId = "ord_0198846702007011a233445566778899";

    [Theory]
    [InlineData("/things/" + UserId, HttpStatusCode.OK)]
    [InlineData("/things/" + OrderId, HttpStatusCode.OK)]
    [InlineData("/things/usr_nope", HttpStatusCode.NotFound)]
    [InlineData("/things/01k226e0g0008j4ct4ank7f24s", HttpStatusCode.NotFound)]
    [InlineData("/things/USR_01K226E0G0008J4CT4ANK7F24S", HttpStatusCode.NotFound)]
    public async Task UnprefixedConstraint_MatchesAnyWellFormedIdentifier(string path, HttpStatusCode expected)
    {
        // Arrange
        await using TestApp app = await TestApp.StartAsync(null, web =>
            web.MapGet("/things/{id:idora}", (string id) => id));

        // Act
        HttpResponseMessage response = await app.Client.GetAsync(path, TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(expected);
    }

    [Theory]
    [InlineData(UserId, HttpStatusCode.OK)]
    [InlineData("ord_01k226e0g0008j4ct4ank7f24s", HttpStatusCode.NotFound)]
    [InlineData("USR_01k226e0g0008j4ct4ank7f24s", HttpStatusCode.NotFound)]
    public async Task PrefixedConstraint_MatchesOnlyThatPrefixExactly(string id, HttpStatusCode expected)
    {
        // Arrange
        await using TestApp app = await TestApp.StartAsync(null, web =>
            web.MapGet("/users/{id:idora(usr)}", (IdoraId id) => id.Prefix));

        // Act
        HttpResponseMessage response = await app.Client.GetAsync($"/users/{id}", TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(expected);
    }

    [Fact]
    public async Task PrefixedConstraints_LetRoutesThatDifferOnlyByEntityLiveSideBySide()
    {
        // Arrange
        await using TestApp app = await TestApp.StartAsync(null, web =>
        {
            web.MapGet("/lookup/{id:idora(usr)}", () => "user");
            web.MapGet("/lookup/{id:idora(ord)}", () => "order");
        });

        // Act
        string user = await app.Client.GetStringAsync($"/lookup/{UserId}", TestContext.Current.CancellationToken);
        string order = await app.Client.GetStringAsync($"/lookup/{OrderId}", TestContext.Current.CancellationToken);

        // Assert
        user.ShouldBe("user");
        order.ShouldBe("order");
    }

    [Fact]
    public async Task Constraint_GeneratesLinksFromAnIdoraIdValue()
    {
        // Arrange
        await using TestApp app = await TestApp.StartAsync(null, web =>
            web.MapGet("/users/{id:idora(usr)}", (IdoraId id) => id.Prefix).WithName("GetUser"));
        LinkGenerator links = app.Services.GetRequiredService<LinkGenerator>();

        // Act
        string? matching = links.GetPathByName("GetUser", new { id = IdoraId.Parse(UserId) });
        string? mismatched = links.GetPathByName("GetUser", new { id = IdoraId.Parse(OrderId) });

        // Assert
        matching.ShouldBe($"/users/{UserId}");
        mismatched.ShouldBeNull();
    }

    [Fact]
    public async Task Template_WithAnInvalidPrefix_FailsWhenTheRoutesAreBuilt()
    {
        // Arrange
        await using TestApp app = await TestApp.StartAsync(null, web =>
            web.MapGet("/users/{id:idora(us_r)}", (IdoraId id) => id.Prefix));

        // Act
        Exception exception = await Should.ThrowAsync<Exception>(
            () => app.Client.GetAsync("/anything", TestContext.Current.CancellationToken));

        // Assert: whatever routing wraps it in, the cause is the prefix.
        Exception? cause = exception;
        while (cause is not null and not InvalidEntityPrefixException)
        {
            cause = cause.InnerException;
        }

        cause.ShouldBeOfType<InvalidEntityPrefixException>().Prefix.ShouldBe("us_r");
    }

    [Fact]
    public void Constructor_WithAnInvalidPrefix_Throws()
    {
        // Act & Assert
        Should.Throw<InvalidEntityPrefixException>(() => new IdoraRouteConstraint("us_r")).Prefix.ShouldBe("us_r");
        Should.Throw<ArgumentNullException>(() => new IdoraRouteConstraint(null!)).ParamName.ShouldBe("prefix");
    }

    [Fact]
    public void Prefix_ReflectsTheConstructorUsed()
    {
        // Act & Assert
        new IdoraRouteConstraint().Prefix.ShouldBeNull();
        new IdoraRouteConstraint("usr").Prefix.ShouldBe("usr");
    }

    [Fact]
    public void Match_WithAMissingOrNullValue_IsFalse()
    {
        // Arrange
        IdoraRouteConstraint constraint = new();

        // Act & Assert
        constraint.Match(null, null, "id", [], RouteDirection.IncomingRequest).ShouldBeFalse();
        constraint.Match(null, null, "id", new RouteValueDictionary { ["id"] = null }, RouteDirection.IncomingRequest)
            .ShouldBeFalse();
    }

    [Fact]
    public void Match_WithANonStringValue_ChecksItsInvariantText()
    {
        // Arrange
        IdoraRouteConstraint constraint = new("usr");

        // Act & Assert
        constraint.Match(null, null, "id", new RouteValueDictionary { ["id"] = IdoraId.Parse(UserId) }, RouteDirection.UrlGeneration)
            .ShouldBeTrue();
        constraint.Match(null, null, "id", new RouteValueDictionary { ["id"] = 42 }, RouteDirection.UrlGeneration)
            .ShouldBeFalse();
    }

    [Fact]
    public void Match_WithNullArguments_Throws()
    {
        // Arrange
        IdoraRouteConstraint constraint = new();

        // Act & Assert
        Should.Throw<ArgumentNullException>(() => constraint.Match(null, null, null!, [], RouteDirection.IncomingRequest));
        Should.Throw<ArgumentNullException>(() => constraint.Match(null, null, "id", null!, RouteDirection.IncomingRequest));
    }

    [Fact]
    public void AddIdoraRouteConstraint_RegistersTheConstraintUnderItsName()
    {
        // Arrange
        ServiceCollection services = new();

        // Act
        services.AddIdoraRouteConstraint().AddIdoraRouteConstraint();
        RouteOptions options = services.BuildServiceProvider()
            .GetRequiredService<Microsoft.Extensions.Options.IOptions<RouteOptions>>().Value;

        // Assert
        options.ConstraintMap[IdoraRouteConstraint.Name].ShouldBe(typeof(IdoraRouteConstraint));
    }

    [Fact]
    public void AddIdoraRouteConstraint_WithNullServices_Throws()
    {
        // Act & Assert
        Should.Throw<ArgumentNullException>(() => ((IServiceCollection)null!).AddIdoraRouteConstraint())
            .ParamName.ShouldBe("services");
    }
}
