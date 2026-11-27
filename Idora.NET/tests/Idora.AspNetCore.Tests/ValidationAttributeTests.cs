using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Net.Http.Json;
using Idora.AspNetCore.Tests.Support;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace Idora.AspNetCore.Tests;

/// <summary>
/// Covers <see cref="IdoraIdAttribute"/> and both forms of <see cref="IdoraIdForAttribute{TEntity}"/>,
/// directly through <see cref="Validator"/> and end to end through MVC model validation.
/// </summary>
public sealed class ValidationAttributeTests
{
    private const string UserId = "usr_01k226e0g0008j4ct4ank7f24s";
    private const string OrderId = "ord_0198846702007011a233445566778899";

    [Theory]
    [InlineData(null, true)]
    [InlineData(UserId, true)]
    [InlineData(OrderId, true)]
    [InlineData("usr_nope", false)]
    [InlineData("", false)]
    public void IdoraId_WithoutAPrefix_AcceptsAnyWellFormedIdentifier(string? value, bool expected)
    {
        // Act & Assert
        new IdoraIdAttribute().IsValid(value).ShouldBe(expected);
    }

    [Theory]
    [InlineData(UserId, true)]
    [InlineData(OrderId, false)]
    [InlineData("USR_01k226e0g0008j4ct4ank7f24s", false)]
    public void IdoraId_WithAPrefix_RequiresItExactly(string value, bool expected)
    {
        // Act & Assert
        new IdoraIdAttribute("usr").IsValid(value).ShouldBe(expected);
    }

    [Fact]
    public void IdoraId_OnAnIdoraIdMember_ChecksThePrefixAndRejectsTheEmptyIdentifier()
    {
        // Arrange
        IdoraIdAttribute attribute = new("usr");

        // Act & Assert
        attribute.IsValid(IdoraId.Parse(UserId)).ShouldBeTrue();
        attribute.IsValid(IdoraId.Parse(OrderId)).ShouldBeFalse();
        attribute.IsValid(default(IdoraId)).ShouldBeFalse();
        new IdoraIdAttribute().IsValid(default(IdoraId)).ShouldBeFalse();
        new IdoraIdAttribute().IsValid((IdoraId?)null).ShouldBeTrue();
    }

    [Fact]
    public void IdoraId_OnAMemberOfAnotherType_ThrowsRatherThanQuietlyPassing()
    {
        // Act
        InvalidOperationException exception = Should.Throw<InvalidOperationException>(() => new IdoraIdAttribute().IsValid(42));

        // Assert
        exception.Message.ShouldContain("[IdoraId] can only be applied to a string or IdoraId member");
    }

    [Fact]
    public void IdoraId_ErrorMessage_NamesTheFieldAndThePrefix()
    {
        // Act & Assert
        new IdoraIdAttribute().FormatErrorMessage("CustomerId")
            .ShouldBe("The CustomerId field must be an identifier such as usr_01k2f9v8ta7m3x0qbrn4dzeh5c.");
        new IdoraIdAttribute("cus").FormatErrorMessage("CustomerId")
            .ShouldBe("The CustomerId field must be an identifier with the prefix 'cus', such as cus_01k2f9v8ta7m3x0qbrn4dzeh5c.");
        new IdoraIdAttribute("cus") { ErrorMessage = "{0} is not a {1} identifier." }.FormatErrorMessage("CustomerId")
            .ShouldBe("CustomerId is not a cus identifier.");
    }

    [Fact]
    public void IdoraId_ThroughTheValidator_ReportsTheMember()
    {
        // Arrange
        Transfer transfer = new() { FromAccountId = OrderId, ToAccountId = UserId };
        List<ValidationResult> results = [];

        // Act
        bool valid = Validator.TryValidateObject(transfer, new ValidationContext(transfer), results, validateAllProperties: true);

        // Assert
        valid.ShouldBeFalse();
        results.ShouldHaveSingleItem().MemberNames.ShouldBe([nameof(Transfer.FromAccountId)]);
    }

    [Theory]
    [InlineData(OrderId, true)]
    [InlineData(UserId, false)]
    [InlineData("ord_nope", false)]
    [InlineData(null, true)]
    public void IdoraIdFor_ChecksAgainstTheRegisteredPrefix(string? value, bool expected)
    {
        // Arrange
        Checkout checkout = new() { OrderId = value, LegacyOrderId = value };
        List<ValidationResult> results = [];

        // Act
        bool valid = Validator.TryValidateObject(checkout, ContextFor(checkout), results, validateAllProperties: true);

        // Assert: the generic and non-generic forms agree.
        valid.ShouldBe(expected);
        results.Count.ShouldBe(expected ? 0 : 2);
    }

    [Fact]
    public void IdoraIdFor_ErrorMessage_NamesTheFieldTheEntityAndThePrefix()
    {
        // Arrange
        Checkout checkout = new() { OrderId = UserId, OrderRef = IdoraId.Parse(UserId) };
        List<ValidationResult> results = [];

        // Act
        Validator.TryValidateObject(checkout, ContextFor(checkout), results, validateAllProperties: true);

        // Assert
        results.Select(result => result.ErrorMessage).ShouldBe(
        [
            "The OrderId field must be an identifier for a Order.",
            "OrderRef must start with ord_.",
        ], ignoreOrder: true);
    }

    [Fact]
    public void IdoraIdFor_WithoutServices_ExplainsWhatIsMissing()
    {
        // Arrange
        Checkout checkout = new() { OrderId = OrderId };

        // Act
        InvalidOperationException exception = Should.Throw<InvalidOperationException>(
            () => Validator.ValidateObject(checkout, new ValidationContext(checkout), validateAllProperties: true));

        // Assert
        exception.Message.ShouldContain("needs an IEntityPrefixRegistry");
        exception.Message.ShouldContain("services.AddIdora(...)");
    }

    [Fact]
    public void IdoraIdFor_AnUnregisteredEntity_ThrowsNotRegistered()
    {
        // Arrange
        Misconfigured model = new() { Id = OrderId };

        // Act & Assert
        Should.Throw<EntityPrefixNotRegisteredException>(
            () => Validator.ValidateObject(model, ContextFor(model), validateAllProperties: true))
            .EntityType.ShouldBe(typeof(Unregistered));
    }

    [Fact]
    public void IdoraIdFor_ExposesItsEntityTypeAndNeedsAContext()
    {
        // Act & Assert
        new IdoraIdForAttribute(typeof(Order)).EntityType.ShouldBe(typeof(Order));
        new IdoraIdForAttribute(typeof(Order)).RequiresValidationContext.ShouldBeTrue();
        new IdoraIdForAttribute<Order>().RequiresValidationContext.ShouldBeTrue();
        new IdoraIdForAttribute<Order>().FormatErrorMessage("Id").ShouldBe("The Id field must be an identifier for a Order.");
    }

    [Fact]
    public async Task Mvc_RejectsAnIdentifierForTheWrongEntityWithAValidationProblem()
    {
        // Arrange
        await using TestApp app = await TestApp.StartAsync(
            services => services.AddControllers().AddApplicationPart(typeof(ValidationAttributeTests).Assembly),
            web => web.MapControllers());

        // Act
        HttpResponseMessage valid = await app.Client.PostAsJsonAsync(
            "/api/checkout",
            new { orderId = OrderId, customerId = UserId },
            TestContext.Current.CancellationToken);
        HttpResponseMessage invalid = await app.Client.PostAsJsonAsync(
            "/api/checkout",
            new { orderId = UserId, customerId = OrderId },
            TestContext.Current.CancellationToken);
        ValidationProblemDetails? problem =
            await invalid.Content.ReadFromJsonAsync<ValidationProblemDetails>(TestContext.Current.CancellationToken);

        // Assert
        valid.StatusCode.ShouldBe(HttpStatusCode.OK);
        invalid.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        problem.ShouldNotBeNull();
        problem.Errors["OrderId"].ShouldBe(["The OrderId field must be an identifier for a Order."]);
        problem.Errors["CustomerId"].ShouldBe(
            ["The CustomerId field must be an identifier with the prefix 'usr', such as usr_01k2f9v8ta7m3x0qbrn4dzeh5c."]);
    }

    private static ValidationContext ContextFor(object instance) =>
        new(
            instance,
            new ServiceCollection()
                .AddIdora(options => options.RegisterEntity<Order>("ord"))
                .BuildServiceProvider(),
            items: null);

    public sealed class Transfer
    {
        [IdoraId("usr")]
        public string? FromAccountId { get; set; }

        [IdoraId("usr")]
        public string? ToAccountId { get; set; }
    }

    public sealed class Checkout
    {
        [IdoraIdFor<Order>]
        public string? OrderId { get; set; }

        [IdoraIdFor(typeof(Order))]
        public string? LegacyOrderId { get; set; }

        [IdoraIdFor<Order>(ErrorMessage = "{0} must start with {2}_.")]
        public IdoraId? OrderRef { get; set; }
    }

    public sealed class Misconfigured
    {
        [IdoraIdFor<Unregistered>]
        public string? Id { get; set; }
    }

    /// <summary>
    /// A request body validated by MVC. On a positional record MVC reads validation attributes from
    /// the constructor parameters, which is where these land without a <c>property:</c> target.
    /// </summary>
    public sealed record CheckoutRequest(
        [Required, IdoraIdFor<Order>] string OrderId,
        [Required, IdoraId("usr")] string CustomerId);
}

/// <summary>An MVC controller whose body carries Idora validation attributes.</summary>
[ApiController]
[Route("api/checkout")]
public sealed class CheckoutController : ControllerBase
{
    [HttpPost]
    public IActionResult Post(ValidationAttributeTests.CheckoutRequest request) => Ok(request);
}
