using System.ComponentModel.DataAnnotations;
using System.Globalization;

namespace Idora.AspNetCore;

/// <summary>
/// Validates that a property or parameter holds an identifier for <typeparamref name="TEntity"/>,
/// carrying exactly the prefix registered for it: <c>[IdoraIdFor&lt;User&gt;]</c>.
/// </summary>
/// <typeparam name="TEntity">The entity type the identifier should belong to.</typeparam>
/// <remarks>
/// <para>
/// The registry-aware form of <see cref="IdoraIdAttribute"/>: the prefix is looked up in the
/// <see cref="IEntityPrefixRegistry"/> that <c>AddIdora</c> registers, so it still lives in one place.
/// That lookup needs the request's services, which MVC and Razor Pages supply through the
/// <see cref="ValidationContext"/>; <see cref="Validator"/> needs a context created with a service
/// provider.
/// </para>
/// <para>
/// An entity type with no registered prefix fails validation with an
/// <see cref="EntityPrefixNotRegisteredException"/> rather than marking the value invalid: the value
/// is not at fault, the configuration is.
/// </para>
/// <para>
/// Otherwise it behaves exactly as <see cref="IdoraIdAttribute"/> does: <see cref="string"/> and
/// <see cref="IdoraId"/> members, <see langword="null"/> passes, <c>default(IdoraId)</c> fails.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// public sealed class PlaceOrder
/// {
///     [Required, IdoraIdFor&lt;Customer&gt;]
///     public string? CustomerId { get; init; }
/// }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter, AllowMultiple = false)]
public sealed class IdoraIdForAttribute<TEntity> : ValidationAttribute
{
    /// <summary>Requires an identifier with the prefix registered for <typeparamref name="TEntity"/>.</summary>
    public IdoraIdForAttribute()
        : base(IdoraIdValidation.ForEntityMessage)
    {
    }

    /// <inheritdoc />
    public override bool RequiresValidationContext => true;

    /// <inheritdoc />
    public override string FormatErrorMessage(string name) =>
        IdoraIdValidation.FormatForEntity(ErrorMessageString, name, typeof(TEntity), prefix: null);

    /// <inheritdoc />
    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext) =>
        IdoraIdValidation.ValidateForEntity(value, validationContext, typeof(TEntity), ErrorMessageString);
}

/// <summary>
/// Validates that a property or parameter holds an identifier for an entity type, carrying exactly
/// the prefix registered for it: <c>[IdoraIdFor(typeof(User))]</c>.
/// </summary>
/// <remarks>
/// The non-generic form of <see cref="IdoraIdForAttribute{TEntity}"/>, for languages and code
/// generators without generic attributes. The two behave identically.
/// </remarks>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter, AllowMultiple = false)]
public sealed class IdoraIdForAttribute : ValidationAttribute
{
    /// <summary>Requires an identifier with the prefix registered for <paramref name="entityType"/>.</summary>
    /// <param name="entityType">The entity type the identifier should belong to.</param>
    public IdoraIdForAttribute(Type entityType)
        : base(IdoraIdValidation.ForEntityMessage)
    {
        EntityType = entityType;
    }

    /// <summary>The entity type the identifier should belong to.</summary>
    public Type EntityType { get; }

    /// <inheritdoc />
    public override bool RequiresValidationContext => true;

    /// <inheritdoc />
    public override string FormatErrorMessage(string name) =>
        IdoraIdValidation.FormatForEntity(ErrorMessageString, name, EntityType, prefix: null);

    /// <inheritdoc />
    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext) =>
        IdoraIdValidation.ValidateForEntity(value, validationContext, EntityType, ErrorMessageString);
}

/// <summary>The checks the Idora validation attributes share.</summary>
internal static class IdoraIdValidation
{
    /// <summary>
    /// The default message for the entity-aware attributes. <c>{0}</c> is the member, <c>{1}</c> the
    /// entity type's name and <c>{2}</c> its prefix, once known.
    /// </summary>
    internal const string ForEntityMessage = "The {0} field must be an identifier for a {1}.";

    /// <summary>
    /// Whether <paramref name="value"/> is acceptable: <see langword="null"/>, or a well-formed
    /// identifier with <paramref name="prefix"/> if one is given.
    /// </summary>
    /// <param name="value">The member's value.</param>
    /// <param name="prefix">The prefix required, or <see langword="null"/> for any.</param>
    /// <param name="attributeName">The attribute as written in source, for the misuse message.</param>
    internal static bool IsValid(object? value, string? prefix, string attributeName) => value switch
    {
        null => true,
        string text => prefix is null ? IdoraId.IsValid(text) : IdoraId.IsValid(text, prefix),
        IdoraId id => !id.IsEmpty && (prefix is null || id.HasPrefix(prefix)),
        _ => throw new InvalidOperationException(
            string.Format(
                CultureInfo.InvariantCulture,
                "[{0}] can only be applied to a string or IdoraId member, but the value is a '{1}'.",
                attributeName,
                value.GetType())),
    };

    /// <summary>
    /// Validates <paramref name="value"/> against the prefix registered for
    /// <paramref name="entityType"/>, found through the validation context's services.
    /// </summary>
    internal static ValidationResult? ValidateForEntity(
        object? value,
        ValidationContext validationContext,
        Type entityType,
        string errorMessage)
    {
        ArgumentNullException.ThrowIfNull(validationContext);

        if (value is null)
        {
            return ValidationResult.Success;
        }

        IEntityPrefixRegistry registry = validationContext.GetService(typeof(IEntityPrefixRegistry)) as IEntityPrefixRegistry
            ?? throw new InvalidOperationException(
                string.Format(
                    CultureInfo.InvariantCulture,
                    "[IdoraIdFor] on '{0}' needs an IEntityPrefixRegistry, but the validation context has no "
                    + "service provider that supplies one. Register Idora with services.AddIdora(...), and "
                    + "validate through MVC or Razor Pages, or create the "
                    + "ValidationContext with the application's service provider.",
                    validationContext.MemberName ?? validationContext.DisplayName));

        string prefix = registry.GetPrefix(entityType);
        if (IsValid(value, prefix, "IdoraIdFor"))
        {
            return ValidationResult.Success;
        }

        string[]? memberNames = validationContext.MemberName is { } memberName ? [memberName] : null;
        return new ValidationResult(
            FormatForEntity(errorMessage, validationContext.DisplayName, entityType, prefix),
            memberNames);
    }

    /// <summary>Formats an entity-aware message, with the prefix when it is known.</summary>
    internal static string FormatForEntity(string errorMessage, string name, Type entityType, string? prefix) =>
        string.Format(CultureInfo.CurrentCulture, errorMessage, name, entityType.Name, prefix);
}
