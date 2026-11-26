using Shouldly;
using Idora.Tests.Support;
using Xunit;

namespace Idora.Tests;

/// <summary>
/// Covers every way of turning text into an <see cref="IdoraId"/>: the throwing and non-throwing
/// parses, the validity checks, each with and without an expected prefix and for strings and spans,
/// and the exceptions they raise.
/// </summary>
/// <remarks>
/// The shared conformance vectors already pin down which inputs are accepted and why the rest are
/// rejected. These tests cover what the vectors cannot: argument handling, the overloads agreeing
/// with one another, and what the exception tells a caller.
/// </remarks>
public sealed class IdoraIdParsingTests
{
    private const string UlidId = "usr_01k226e0g0008j4ct4ank7f24s";
    private const string UuidV7Id = "inv_0198846702007011a233445566778899";

    [Fact]
    public void Parse_AString_WrapsThatSameInstance()
    {
        // Arrange
        string text = UlidId;

        // Act
        IdoraId id = IdoraId.Parse(text);

        // Assert
        ReferenceEquals(id.ToString(), text).ShouldBeTrue();
    }

    [Fact]
    public void Parse_AGeneratedUlid_ReadsTheClockTheGeneratorUsed()
    {
        // Arrange
        IdoraGenerator generator = new(options => options.RegisterEntity<User>("usr"));
        long before = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        // Act
        IdoraId id = IdoraId.Parse(generator.Generate<User>());
        long after = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        // Assert
        id.Format.ShouldBe(IdGenerationStrategy.Ulid);
        id.Prefix.ShouldBe("usr");
        id.TimestampMilliseconds.ShouldBeInRange(before, after);
    }

    [Fact]
    public void Parse_AGeneratedUuidV7_ReadsTheClockTheGeneratorUsed()
    {
        // Arrange
        IdoraGenerator generator = new(options => options.UseUuidV7());
        long before = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        // Act
        IdoraId id = IdoraId.Parse(generator.Generate("inv"));
        long after = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        // Assert
        id.Format.ShouldBe(IdGenerationStrategy.UuidV7);
        id.TimestampMilliseconds.ShouldBeInRange(before, after);
    }

    [Fact]
    public void Parse_ThousandsOfGeneratedIdentifiersInBothFormats_AcceptsEveryOne()
    {
        // Arrange
        IdoraGenerator ulids = new();
        IdoraGenerator uuids = new(options => options.UseUuidV7());

        for (int i = 0; i < 5_000; i++)
        {
            // Act & Assert: random fields cover the whole alphabet, so this exercises every character.
            IdoraId.IsValid(ulids.Generate("a1")).ShouldBeTrue();
            IdoraId.IsValid(uuids.Generate("a1")).ShouldBeTrue();
        }
    }

    [Fact]
    public void Parse_WithANullString_ThrowsArgumentNullException()
    {
        // Act
        ArgumentNullException exception = Should.Throw<ArgumentNullException>(() => IdoraId.Parse((string)null!));

        // Assert
        exception.ParamName.ShouldBe("text");
    }

    [Fact]
    public void Parse_WithANullExpectedPrefix_ThrowsArgumentNullException()
    {
        // Act
        ArgumentNullException exception =
            Should.Throw<ArgumentNullException>(() => IdoraId.Parse(UlidId, expectedPrefix: null!));

        // Assert
        exception.ParamName.ShouldBe("expectedPrefix");
    }

    [Fact]
    public void Parse_WithTheExpectedPrefix_Succeeds()
    {
        // Act
        IdoraId id = IdoraId.Parse(UlidId, expectedPrefix: "usr");

        // Assert
        id.Prefix.ShouldBe("usr");
    }

    [Theory]
    [InlineData("ord")]
    [InlineData("USR")]
    [InlineData("us")]
    [InlineData("usrx")]
    public void Parse_WithADifferentExpectedPrefix_ThrowsAPrefixMismatch(string expectedPrefix)
    {
        // Act
        InvalidIdException exception =
            Should.Throw<InvalidIdException>(() => IdoraId.Parse(UlidId, expectedPrefix));

        // Assert
        exception.Reason.ShouldBe(InvalidIdReason.PrefixMismatch);
        exception.ExpectedPrefix.ShouldBe(expectedPrefix);
        exception.Text.ShouldBe(UlidId);
        exception.Message.ShouldContain($"has the prefix 'usr', but an identifier with the prefix '{expectedPrefix}'");
    }

    [Fact]
    public void Parse_AMalformedIdentifierWithAnExpectedPrefix_ReportsTheMalformationNotTheMismatch()
    {
        // Act
        InvalidIdException exception =
            Should.Throw<InvalidIdException>(() => IdoraId.Parse("usr_nope", expectedPrefix: "ord"));

        // Assert
        exception.Reason.ShouldBe(InvalidIdReason.Value);
        exception.ExpectedPrefix.ShouldBe("ord");
    }

    [Fact]
    public void Parse_WithABadPrefixAndABadValue_ReportsThePrefixFirst()
    {
        // Act
        InvalidIdException exception = Should.Throw<InvalidIdException>(() => IdoraId.Parse("us-r_nope"));

        // Assert
        exception.Reason.ShouldBe(InvalidIdReason.Prefix);
    }

    [Fact]
    public void Parse_WithABadPrefix_CarriesThePrefixProblemAsTheInnerException()
    {
        // Act
        InvalidIdException exception =
            Should.Throw<InvalidIdException>(() => IdoraId.Parse("us-r_01k226e0g0008j4ct4ank7f24s"));

        // Assert
        InvalidEntityPrefixException inner = exception.InnerException.ShouldBeOfType<InvalidEntityPrefixException>();
        inner.Prefix.ShouldBe("us-r");
        inner.Message.ShouldContain("contains the character '-' at index 2");
        exception.Message.ShouldContain(inner.Message);
    }

    [Fact]
    public void Parse_WithAnEmptyPrefix_ExplainsThatThePrefixIsEmpty()
    {
        // Act
        InvalidIdException exception =
            Should.Throw<InvalidIdException>(() => IdoraId.Parse("_01k226e0g0008j4ct4ank7f24s"));

        // Assert
        exception.Reason.ShouldBe(InvalidIdReason.Prefix);
        exception.InnerException!.Message.ShouldContain("cannot be empty");
    }

    [Fact]
    public void Parse_WithNoSeparator_SuggestsAddingAPrefix()
    {
        // Act
        InvalidIdException exception =
            Should.Throw<InvalidIdException>(() => IdoraId.Parse("01k226e0g0008j4ct4ank7f24s"));

        // Assert
        exception.Reason.ShouldBe(InvalidIdReason.NoSeparator);
        exception.InnerException.ShouldBeNull();
        exception.Message.ShouldContain("contains no '_' separator");
        exception.Message.ShouldContain("put the entity's prefix and an underscore in front of it");
    }

    [Theory]
    [InlineData("usr_", "nothing after the separator")]
    [InlineData("usr_01k226e0g0008j4ct4ank7f24", "It is 25 characters long, but a ULID is 26 characters and a UUIDv7 32")]
    [InlineData("usr_01K226E0G0008J4CT4ANK7F24S", "uppercase character 'K' at index 2")]
    [InlineData("usr_01k226e0g0008j4ct4ank7f24u", "the character 'u' at index 25, which is not in the lowercase Crockford Base32 alphabet")]
    [InlineData("usr_81k226e0g0008j4ct4ank7f24s", "begins with '8'. A ULID's first character is never greater than '7'")]
    [InlineData("inv_0198846702007011A233445566778899", "uppercase character 'A' at index 16")]
    [InlineData("inv_0000000000017000800000000000000g", "the character 'g' at index 31, which is not a lowercase hexadecimal digit")]
    [InlineData("inv_00000000000140008000000000000001", "'4' at index 12, where a UUIDv7 carries its version, 7")]
    [InlineData("inv_0000000000017000c000000000000001", "'c' at index 16, where a UUIDv7 carries its variant")]
    public void Parse_WithABadValue_ExplainsExactlyWhatIsWrong(string text, string explanation)
    {
        // Act
        InvalidIdException exception = Should.Throw<InvalidIdException>(() => IdoraId.Parse(text));

        // Assert
        exception.Reason.ShouldBe(InvalidIdReason.Value);
        exception.Message.ShouldContain(explanation);
    }

    [Fact]
    public void Parse_WithAHyphenatedUuid_PointsAtTheHyphen()
    {
        // Act
        InvalidIdException exception =
            Should.Throw<InvalidIdException>(() => IdoraId.Parse("inv_01988467-0200-7011-a233-445566778899"));

        // Assert: 36 characters is neither length, so the length is what is reported.
        exception.Reason.ShouldBe(InvalidIdReason.Value);
        exception.Message.ShouldContain("It is 36 characters long");
    }

    [Fact]
    public void Parse_WithVeryLongInput_QuotesOnlyTheStartOfItInTheMessage()
    {
        // Arrange
        string text = "usr_" + new string('0', 10_000);

        // Act
        InvalidIdException exception = Should.Throw<InvalidIdException>(() => IdoraId.Parse(text));

        // Assert
        exception.Text.ShouldBe(text);
        exception.Message.Length.ShouldBeLessThan(400);
        exception.Message.ShouldContain("…");
    }

    [Fact]
    public void Parse_ASpan_ReadsTheSameIdentifierAsTheString()
    {
        // Arrange
        ReadOnlySpan<char> text = ("[" + UuidV7Id + "]").AsSpan(1, UuidV7Id.Length);

        // Act
        IdoraId id = IdoraId.Parse(text);

        // Assert
        id.ShouldBe(IdoraId.Parse(UuidV7Id));
        id.ToString().ShouldBe(UuidV7Id);
    }

    [Fact]
    public void Parse_ASpanWithTheExpectedPrefix_Succeeds()
    {
        // Act
        IdoraId id = IdoraId.Parse(UuidV7Id.AsSpan(), "inv");

        // Assert
        id.Prefix.ShouldBe("inv");
    }

    [Fact]
    public void Parse_AnInvalidSpan_ThrowsWithTheTextCopiedOut()
    {
        // Act
        InvalidIdException exception = Should.Throw<InvalidIdException>(() => IdoraId.Parse("nope".AsSpan()));

        // Assert
        exception.Text.ShouldBe("nope");
        exception.Reason.ShouldBe(InvalidIdReason.NoSeparator);
    }

    [Fact]
    public void Parse_ASpanWithAnotherPrefix_ThrowsAPrefixMismatch()
    {
        // Act
        InvalidIdException exception =
            Should.Throw<InvalidIdException>(() => IdoraId.Parse(UuidV7Id.AsSpan(), "ord"));

        // Assert
        exception.Reason.ShouldBe(InvalidIdReason.PrefixMismatch);
        exception.ExpectedPrefix.ShouldBe("ord");
    }

    [Fact]
    public void Parse_ASpanWithANullExpectedPrefix_ThrowsArgumentNullException()
    {
        // Act
        ArgumentNullException exception =
            Should.Throw<ArgumentNullException>(() => IdoraId.Parse(UuidV7Id.AsSpan(), null!));

        // Assert
        exception.ParamName.ShouldBe("expectedPrefix");
    }

    [Fact]
    public void ExplicitConversion_FromAValidString_Parses()
    {
        // Act
        IdoraId id = (IdoraId)UlidId;

        // Assert
        id.ShouldBe(IdoraId.Parse(UlidId));
    }

    [Fact]
    public void ExplicitConversion_FromAnInvalidString_Throws()
    {
        // Act & Assert
        Should.Throw<InvalidIdException>(() => (IdoraId)"nope");
    }

    [Fact]
    public void TryParse_AValidString_ReturnsTrueAndTheIdentifier()
    {
        // Act
        bool parsed = IdoraId.TryParse(UlidId, out IdoraId id);

        // Assert
        parsed.ShouldBeTrue();
        id.ToString().ShouldBe(UlidId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("nope")]
    [InlineData("usr_nope")]
    [InlineData("u-r_01k226e0g0008j4ct4ank7f24s")]
    public void TryParse_AnInvalidString_ReturnsFalseAndTheEmptyIdentifier(string? text)
    {
        // Act
        bool parsed = IdoraId.TryParse(text, out IdoraId id);

        // Assert
        parsed.ShouldBeFalse();
        id.IsEmpty.ShouldBeTrue();
    }

    [Fact]
    public void TryParse_WithTheExpectedPrefix_ReturnsTrue()
    {
        // Act
        bool parsed = IdoraId.TryParse(UlidId, "usr", out IdoraId id);

        // Assert
        parsed.ShouldBeTrue();
        id.Prefix.ShouldBe("usr");
    }

    [Fact]
    public void TryParse_WithAnotherPrefix_ReturnsFalse()
    {
        // Act
        bool parsed = IdoraId.TryParse(UlidId, "USR", out IdoraId id);

        // Assert
        parsed.ShouldBeFalse();
        id.IsEmpty.ShouldBeTrue();
    }

    [Fact]
    public void TryParse_WithANullTextAndAnExpectedPrefix_ReturnsFalse()
    {
        // Act
        bool parsed = IdoraId.TryParse((string?)null, "usr", out _);

        // Assert
        parsed.ShouldBeFalse();
    }

    [Fact]
    public void TryParse_WithANullExpectedPrefix_ThrowsBecauseThatIsACallerMistake()
    {
        // Act
        ArgumentNullException exception =
            Should.Throw<ArgumentNullException>(() => IdoraId.TryParse(UlidId, null!, out _));

        // Assert
        exception.ParamName.ShouldBe("expectedPrefix");
    }

    [Fact]
    public void TryParse_ASpan_ReturnsTrueAndACopyOfTheText()
    {
        // Act
        bool parsed = IdoraId.TryParse(UlidId.AsSpan(), out IdoraId id);

        // Assert
        parsed.ShouldBeTrue();
        id.ToString().ShouldBe(UlidId);
    }

    [Fact]
    public void TryParse_AnInvalidSpan_ReturnsFalse()
    {
        // Act
        bool parsed = IdoraId.TryParse("usr_".AsSpan(), out IdoraId id);

        // Assert
        parsed.ShouldBeFalse();
        id.IsEmpty.ShouldBeTrue();
    }

    [Fact]
    public void TryParse_ASpanWithAnExpectedPrefix_ChecksThePrefix()
    {
        // Act
        bool matching = IdoraId.TryParse(UlidId.AsSpan(), "usr", out IdoraId matched);
        bool mismatched = IdoraId.TryParse(UlidId.AsSpan(), "ord", out IdoraId unmatched);

        // Assert
        matching.ShouldBeTrue();
        matched.Prefix.ShouldBe("usr");
        mismatched.ShouldBeFalse();
        unmatched.IsEmpty.ShouldBeTrue();
    }

    [Fact]
    public void TryParse_ASpanWithANullExpectedPrefix_ThrowsArgumentNullException()
    {
        // Act & Assert
        Should.Throw<ArgumentNullException>(() => IdoraId.TryParse(UlidId.AsSpan(), null!, out _));
    }

    [Theory]
    [InlineData(UlidId, true)]
    [InlineData(UuidV7Id, true)]
    [InlineData("nope", false)]
    [InlineData(null, false)]
    public void IsValid_AgreesWithTryParse(string? text, bool expected)
    {
        // Act
        bool valid = IdoraId.IsValid(text);
        bool validSpan = IdoraId.IsValid(text.AsSpan());

        // Assert
        valid.ShouldBe(expected);
        validSpan.ShouldBe(expected);
        IdoraId.TryParse(text, out _).ShouldBe(expected);
    }

    [Theory]
    [InlineData(UlidId, "usr", true)]
    [InlineData(UlidId, "ord", false)]
    [InlineData(UlidId, "USR", false)]
    [InlineData("usr_nope", "usr", false)]
    [InlineData(null, "usr", false)]
    public void IsValid_WithAnExpectedPrefix_ChecksThePrefixExactly(string? text, string expectedPrefix, bool expected)
    {
        // Act
        bool valid = IdoraId.IsValid(text, expectedPrefix);
        bool validSpan = IdoraId.IsValid(text.AsSpan(), expectedPrefix);

        // Assert
        valid.ShouldBe(expected);
        validSpan.ShouldBe(expected);
    }

    [Fact]
    public void IsValid_WithANullExpectedPrefix_ThrowsArgumentNullException()
    {
        // Act & Assert
        Should.Throw<ArgumentNullException>(() => IdoraId.IsValid(UlidId, null!));
        Should.Throw<ArgumentNullException>(() => IdoraId.IsValid(UlidId.AsSpan(), null!));
    }

    [Fact]
    public void IParsable_ParsesThroughTheGenericInterface()
    {
        // Act
        IdoraId parsed = ParseGenerically<IdoraId>(UlidId);
        bool tryParsed = TryParseGenerically(UlidId, out IdoraId tried);
        bool rejected = TryParseGenerically<IdoraId>("nope", out _);

        // Assert
        parsed.ToString().ShouldBe(UlidId);
        tryParsed.ShouldBeTrue();
        tried.ShouldBe(parsed);
        rejected.ShouldBeFalse();
    }

    [Fact]
    public void ISpanParsable_ParsesThroughTheGenericInterface()
    {
        // Act
        IdoraId parsed = ParseSpanGenerically<IdoraId>(UuidV7Id.AsSpan());
        bool tryParsed = TryParseSpanGenerically(UuidV7Id.AsSpan(), out IdoraId tried);
        bool rejected = TryParseSpanGenerically<IdoraId>("nope".AsSpan(), out _);

        // Assert
        parsed.ToString().ShouldBe(UuidV7Id);
        tryParsed.ShouldBeTrue();
        tried.ShouldBe(parsed);
        rejected.ShouldBeFalse();
        Should.Throw<InvalidIdException>(() => ParseSpanGenerically<IdoraId>("nope".AsSpan()));
    }

    [Fact]
    public void InvalidIdException_DerivesFromTheCommonBase()
    {
        // Act
        InvalidIdException exception = Should.Throw<InvalidIdException>(() => IdoraId.Parse("nope"));

        // Assert
        exception.ShouldBeAssignableTo<IdoraException>();
    }

    [Fact]
    public void InvalidIdException_StandardConstructors_LeaveTheDetailsUnset()
    {
        // Act
        InvalidIdException bare = new();
        InvalidIdException withMessage = new("message");
        InvalidIdException withInner = new("message", new InvalidOperationException());

        // Assert
        bare.Reason.ShouldBeNull();
        bare.Text.ShouldBeNull();
        bare.ExpectedPrefix.ShouldBeNull();
        withMessage.Message.ShouldBe("message");
        withInner.InnerException.ShouldBeOfType<InvalidOperationException>();
    }

    [Fact]
    public void InvalidIdException_DetailedConstructor_KeepsEveryDetail()
    {
        // Act
        InvalidIdException exception = new("message", "usr_x", InvalidIdReason.Value, "usr");

        // Assert
        exception.Text.ShouldBe("usr_x");
        exception.Reason.ShouldBe(InvalidIdReason.Value);
        exception.ExpectedPrefix.ShouldBe("usr");
        exception.InnerException.ShouldBeNull();
    }

    private static T ParseGenerically<T>(string text)
        where T : IParsable<T> =>
        T.Parse(text, provider: null);

    private static bool TryParseGenerically<T>(string text, out T result)
        where T : IParsable<T> =>
        T.TryParse(text, provider: null, out result!);

    private static T ParseSpanGenerically<T>(ReadOnlySpan<char> text)
        where T : ISpanParsable<T> =>
        T.Parse(text, provider: null);

    private static bool TryParseSpanGenerically<T>(ReadOnlySpan<char> text, out T result)
        where T : ISpanParsable<T> =>
        T.TryParse(text, provider: null, out result!);

    [Theory]
    [InlineData("value")]
    [InlineData("prefix")]
    [InlineData("noSeparator")]
    public void Parse_EnormousInput_KeepsEveryMessageInTheChainShort(string enormousPart)
    {
        // Arrange: an identifier being parsed usually comes from a request, so a rejection must not
        // repeat a megabyte of it into a log line, including in the prefix exception it wraps.
        string huge = new('x', 1 << 20);
        string text = enormousPart switch
        {
            "value" => "usr_" + huge,
            "prefix" => huge + "_x",
            _ => huge,
        };

        // Act
        InvalidIdException exception = Should.Throw<InvalidIdException>(() => IdoraId.Parse(text));

        // Assert
        exception.Text.ShouldBe(text);
        for (Exception? e = exception; e is not null; e = e.InnerException)
        {
            e.Message.Length.ShouldBeLessThan(1000);
        }
    }

    [Theory]
    [InlineData("us_r")]
    [InlineData("")]
    [InlineData(null)]
    public void ParseTryParseAndIsValid_AnInvalidExpectedPrefix_ThrowWhateverTheText(string? badPrefix)
    {
        // Arrange: a mistake in the calling code, reported as such rather than as a mismatch, and for
        // a malformed text as much as a valid one. A megabyte-long prefix stands in for the rest.
        string expectedPrefix = badPrefix ?? new string('p', 1 << 20);

        foreach (string text in new[] { "usr_01k226e0g0008j4ct4ank7f24s", "nonsense" })
        {
            // Act & Assert
            InvalidEntityPrefixException[] thrown =
            [
                Should.Throw<InvalidEntityPrefixException>(() => IdoraId.Parse(text, expectedPrefix)),
                Should.Throw<InvalidEntityPrefixException>(() => IdoraId.Parse(text.AsSpan(), expectedPrefix)),
                Should.Throw<InvalidEntityPrefixException>(() => IdoraId.TryParse(text, expectedPrefix, out _)),
                Should.Throw<InvalidEntityPrefixException>(() => IdoraId.IsValid(text, expectedPrefix)),
            ];
            foreach (InvalidEntityPrefixException exception in thrown)
            {
                exception.Prefix.ShouldBe(expectedPrefix);
                exception.Message.Length.ShouldBeLessThan(1000);
            }
        }
    }

    [Fact]
    public void ParseTryParseAndIsValid_NullTextAndAnInvalidExpectedPrefix_ReportThePrefix()
    {
        // Act & Assert: the expected prefix is checked before anything about the text, even its
        // absence, in every form, as specification section 6.1 requires.
        Should.Throw<InvalidEntityPrefixException>(() => IdoraId.Parse(null!, "us_r"));
        Should.Throw<InvalidEntityPrefixException>(() => IdoraId.TryParse(null, "us_r", out _));
        Should.Throw<InvalidEntityPrefixException>(() => IdoraId.IsValid(null, "us_r"));
    }
}
