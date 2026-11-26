using System.ComponentModel;
using System.Globalization;

namespace Idora;

/// <summary>
/// Converts an <see cref="IdoraId"/> to and from its text, for the parts of .NET that discover
/// conversions through <see cref="TypeDescriptor"/>.
/// </summary>
/// <remarks>
/// <para>
/// Applied to <see cref="IdoraId"/> with <see cref="TypeConverterAttribute"/>. It is what lets
/// configuration binding (<c>IConfiguration.Get&lt;T&gt;()</c>, options) and MVC model binding fill
/// an <see cref="IdoraId"/> from a string with no registration. Minimal APIs need nothing at all:
/// they bind through <see cref="IParsable{TSelf}"/>.
/// </para>
/// <para>
/// Conversion from a string is as strict as <see cref="IdoraId.Parse(string)"/> and throws the same
/// <see cref="InvalidIdException"/>. Conversion to a string gives the identifier's text.
/// </para>
/// </remarks>
public sealed class IdoraIdTypeConverter : TypeConverter
{
    /// <inheritdoc />
    public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType) =>
        sourceType == typeof(string) || base.CanConvertFrom(context, sourceType);

    /// <inheritdoc />
    public override bool CanConvertTo(ITypeDescriptorContext? context, Type? destinationType) =>
        destinationType == typeof(string) || base.CanConvertTo(context, destinationType);

    /// <inheritdoc />
    /// <exception cref="InvalidIdException"><paramref name="value"/> is a string that is not a valid identifier.</exception>
    public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value) =>
        value is string text ? IdoraId.Parse(text) : base.ConvertFrom(context, culture, value);

    /// <inheritdoc />
    public override object? ConvertTo(
        ITypeDescriptorContext? context,
        CultureInfo? culture,
        object? value,
        Type destinationType) =>
        destinationType == typeof(string) && value is IdoraId id
            ? id.ToString()
            : base.ConvertTo(context, culture, value, destinationType);

    /// <inheritdoc />
    public override bool IsValid(ITypeDescriptorContext? context, object? value) =>
        value is IdoraId || (value is string text && IdoraId.IsValid(text));
}
