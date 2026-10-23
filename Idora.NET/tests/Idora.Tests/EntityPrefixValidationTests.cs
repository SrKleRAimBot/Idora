using Shouldly;
using Idora.Tests.Support;
using Xunit;

namespace Idora.Tests;

/// <summary>
/// Covers the rules a prefix must satisfy, applied identically at registration time and at
/// <see cref="IIdoraGenerator.Generate(string)"/> time.
/// </summary>
public sealed class EntityPrefixValidationTests
{
    /// <summary>Prefixes that must be rejected, and why.</summary>
    public static TheoryData<string> InvalidPrefixes() =>
    [
        "",                    // empty
        " ",                   // whitespace is not a letter or digit
        "   ",
        "us r",                // embedded space
        "us_r",                // contains the separator
        "_usr",                // leading separator
        "usr_",                // trailing separator
        "usr-1",               // hyphen
        "usr.1",               // full stop
        "user!",               // punctuation
        "usér",                // non-ASCII letter
        "ユーザ",  // non-Latin script
        "\U0001F600",          // emoji
        "usr\n",               // control character
        "12345678901234567",   // 17 characters, one over the limit
        "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
    ];

    /// <summary>Prefixes that must be accepted.</summary>
    public static TheoryData<string> ValidPrefixes() =>
    [
        "a",                   // shortest possible
        "1",                   // a digit alone is fine
        "usr",                 // the common case
        "usr1",                // letters and digits mixed
        "1usr",                // may start with a digit
        "USR",                 // uppercase is allowed, though lowercase reads better
        "UsR",
        "1234567890123456",    // exactly 16 characters, the limit
    ];

    [Theory]
    [MemberData(nameof(InvalidPrefixes))]
    public void RegisterEntity_WithAnInvalidPrefix_ThrowsAndReportsThePrefix(string prefix)
    {
        // Arrange
        IdoraOptions options = new();

        // Act
        InvalidEntityPrefixException exception =
            Should.Throw<InvalidEntityPrefixException>(() => options.RegisterEntity<User>(prefix));

        // Assert
        exception.Prefix.ShouldBe(prefix);
        exception.ShouldBeAssignableTo<IdoraException>();
    }

    [Theory]
    [MemberData(nameof(ValidPrefixes))]
    public void RegisterEntity_WithAValidPrefix_Succeeds(string prefix)
    {
        // Arrange
        IdoraOptions options = new();

        // Act
        options.RegisterEntity<User>(prefix);

        // Assert
        options.Registrations[typeof(User)].ShouldBe(prefix);
    }

    [Theory]
    [MemberData(nameof(InvalidPrefixes))]
    public void Generate_WithAnInvalidAdHocPrefix_Throws(string prefix)
    {
        // Arrange
        IdoraGenerator generator = new();

        // Act
        InvalidEntityPrefixException exception =
            Should.Throw<InvalidEntityPrefixException>(() => generator.Generate(prefix));

        // Assert
        exception.Prefix.ShouldBe(prefix);
    }

    [Theory]
    [MemberData(nameof(ValidPrefixes))]
    public void Generate_WithAValidAdHocPrefix_ProducesAnIdentifierWithThatPrefix(string prefix)
    {
        // Arrange
        IdoraGenerator generator = new();

        // Act
        string id = generator.Generate(prefix);

        // Assert
        id.ShouldStartWith(prefix + "_");
        id.Length.ShouldBe(prefix.Length + 1 + 26);
    }

    [Fact]
    public void RegisterEntity_WithANullPrefix_ThrowsArgumentNullExceptionNamingThePrefixParameter()
    {
        // Arrange
        IdoraOptions options = new();

        // Act
        ArgumentNullException exception =
            Should.Throw<ArgumentNullException>(() => options.RegisterEntity<User>(null!));

        // Assert
        exception.ParamName.ShouldBe("prefix");
    }

    [Fact]
    public void Generate_WithANullAdHocPrefix_ThrowsArgumentNullExceptionNamingTheArgument()
    {
        // Arrange
        IdoraGenerator generator = new();

        // Act
        ArgumentNullException exception = Should.Throw<ArgumentNullException>(() => generator.Generate(null!));

        // Assert
        exception.ParamName.ShouldBe("entityPrefix");
    }

    [Fact]
    public void RegisterEntity_WithAPrefixContainingTheSeparator_ExplainsWhyItIsRejected()
    {
        // Arrange
        IdoraOptions options = new();

        // Act
        InvalidEntityPrefixException exception =
            Should.Throw<InvalidEntityPrefixException>(() => options.RegisterEntity<User>("user_account"));

        // Assert
        exception.Message.ShouldContain("separator");
    }

    [Fact]
    public void RegisterEntity_WithAnOverlongPrefix_ReportsBothLengths()
    {
        // Arrange
        IdoraOptions options = new();
        string prefix = new('a', 17);

        // Act
        InvalidEntityPrefixException exception =
            Should.Throw<InvalidEntityPrefixException>(() => options.RegisterEntity<User>(prefix));

        // Assert
        exception.Message.ShouldContain("17");
        exception.Message.ShouldContain("16");
    }
}
