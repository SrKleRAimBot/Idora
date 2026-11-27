using System.ComponentModel.DataAnnotations;
using System.Globalization;

namespace Idora.AspNetCore;

/// <summary>
/// Validates that a property or parameter holds a well-formed identifier, optionally with a
/// particular prefix: <c>[IdoraId]</c> or <c>[IdoraId("usr")]</c>.
/// </summary>
/// <remarks>
/// <para>
/// Applies to <see cref="string"/> and <see cref="IdoraId"/> members, nullable or not. A
/// <see langword="null"/> value passes, as it does for every other validation attribute; combine
/// with <see cref="RequiredAttribute"/> to insist on a value. <c>default(IdoraId)</c> fails, because
/// on a non-nullable <see cref="IdoraId"/> it is what a missing value looks like.
/// </para>
/// <para>
/// A string is checked with <see cref="IdoraId.IsValid(string, string)"/>: strictly, and exact
/// about the prefix's case. An <see cref="IdoraId"/> is already well formed, so only its prefix is
/// checked. To check against the prefix registered for an entity type instead of repeating it here,
/// use <see cref="IdoraIdForAttribute{TEntity}"/>.
/// </para>
/// <para>
/// Works wherever DataAnnotations do: MVC and Razor Pages model validation, options validation,
/// <see cref="Validator"/>, and any other validator that runs validation attributes.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// public sealed class TransferRequest
/// {
///     [Required, IdoraId("acc")]
///     public string? FromAccountId { get; init; }
///
///     [Required, IdoraId("acc")]
///     public string? ToAccountId { get; init; }
/// }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter, AllowMultiple = false)]
public sealed class IdoraIdAttribute : ValidationAttribute
{
    /// <summary>Requires a well-formed identifier with any prefix.</summary>
    public IdoraIdAttribute()
        : base("The {0} field must be an identifier such as usr_01k2f9v8ta7m3x0qbrn4dzeh5c.")
    {
    }

    /// <summary>Requires a well-formed identifier with <paramref name="prefix"/>.</summary>
    /// <param name="prefix">The prefix the identifier must have, compared exactly, case included.</param>
    /// <remarks>
    /// The prefix is not checked here, for the same reason <see cref="EntityPrefixAttribute"/> does
    /// not check its own: an attribute constructor throws only when something reflects over it, which
    /// would surface as a confusing failure deep inside validation. A prefix that could never appear
    /// in an identifier simply means nothing validates.
    /// </remarks>
    public IdoraIdAttribute(string prefix)
        : base("The {0} field must be an identifier with the prefix '{1}', such as {1}_01k2f9v8ta7m3x0qbrn4dzeh5c.")
    {
        Prefix = prefix;
    }

    /// <summary>The prefix the identifier must have, or <see langword="null"/> if any prefix will do.</summary>
    public string? Prefix { get; }

    /// <inheritdoc />
    public override string FormatErrorMessage(string name) =>
        string.Format(CultureInfo.CurrentCulture, ErrorMessageString, name, Prefix);

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">
    /// The attribute is applied to a member that is neither a <see cref="string"/> nor an
    /// <see cref="IdoraId"/>.
    /// </exception>
    public override bool IsValid(object? value) => IdoraIdValidation.IsValid(value, Prefix, "IdoraId");
}
