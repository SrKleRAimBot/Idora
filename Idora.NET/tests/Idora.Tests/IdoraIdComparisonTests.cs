using Shouldly;
using Xunit;

namespace Idora.Tests;

/// <summary>
/// Covers equality, the natural order, and the timestamp and prefix predicates.
/// </summary>
public sealed class IdoraIdComparisonTests
{
    /// <summary>A ULID minted at 1754568000000.</summary>
    private static readonly IdoraId User = IdoraId.Parse("usr_01k226e0g0008j4ct4ank7f24s");

    /// <summary>A ULID minted one millisecond after <see cref="User"/>.</summary>
    private static readonly IdoraId LaterOrder = IdoraId.Parse("ord_01k226e0g1zzqdvk5vnacrgxv6");

    /// <summary>A UUIDv7 minted in the same millisecond as <see cref="User"/>.</summary>
    private static readonly IdoraId SameMillisecondInvoice = IdoraId.Parse("inv_0198846702007011a233445566778899");

    [Fact]
    public void Equals_ComparesTheTextOrdinally()
    {
        // Arrange: two separate string instances with the same text.
        IdoraId first = IdoraId.Parse(new string("usr_01k226e0g0008j4ct4ank7f24s".AsSpan()));
        IdoraId second = IdoraId.Parse(new string("usr_01k226e0g0008j4ct4ank7f24s".AsSpan()));

        // Act & Assert
        first.Equals(second).ShouldBeTrue();
        first.Equals((object)second).ShouldBeTrue();
        (first == second).ShouldBeTrue();
        (first != second).ShouldBeFalse();
        first.GetHashCode().ShouldBe(second.GetHashCode());
    }

    [Fact]
    public void Equals_DistinguishesPrefixesByCase()
    {
        // Arrange
        IdoraId lower = IdoraId.Parse("ord_01k226e0g1zzqdvk5vnacrgxv6");
        IdoraId upper = IdoraId.Parse("Ord_01k226e0g1zzqdvk5vnacrgxv6");

        // Act & Assert
        lower.Equals(upper).ShouldBeFalse();
        (lower != upper).ShouldBeTrue();
    }

    [Fact]
    public void Equals_AnythingButAnIdoraId_IsFalse()
    {
        // Act & Assert
        User.Equals(null).ShouldBeFalse();
        User.Equals(User.ToString()).ShouldBeFalse();
    }

    [Fact]
    public void HashSet_TreatsEqualIdentifiersAsOne()
    {
        // Act
        HashSet<IdoraId> set =
        [
            User,
            IdoraId.Parse(new string(User.ToString().AsSpan())),
            LaterOrder,
        ];

        // Assert
        set.Count.ShouldBe(2);
    }

    [Fact]
    public void CompareTo_OrdersByTimestampBeforeText()
    {
        // Arrange: "ord" sorts before "usr" as text, but the order was minted a millisecond later.
        string.CompareOrdinal(LaterOrder.ToString(), User.ToString()).ShouldBeLessThan(0);

        // Act & Assert
        User.CompareTo(LaterOrder).ShouldBeLessThan(0);
        LaterOrder.CompareTo(User).ShouldBeGreaterThan(0);
    }

    [Fact]
    public void CompareTo_WithinOneMillisecond_OrdersByValueThenPrefix()
    {
        // Arrange
        IdoraId first = IdoraId.Parse("bbb_00000000010000000000000001");
        IdoraId second = IdoraId.Parse("aaa_00000000010000000000000002");
        IdoraId samePrefixFirst = IdoraId.Parse("aaa_00000000010000000000000001");

        // Act & Assert
        first.CompareTo(second).ShouldBeLessThan(0);         // value decides, despite the prefix
        samePrefixFirst.CompareTo(first).ShouldBeLessThan(0); // same value, so prefix decides
        first.CompareTo(first).ShouldBe(0);
    }

    [Fact]
    public void CompareTo_ConsecutiveGeneratedUlids_IsGenerationOrder()
    {
        // Arrange
        IdoraGenerator generator = new();
        IdoraId[] generated = Enumerable.Range(0, 1_000).Select(_ => IdoraId.Parse(generator.Generate("evt"))).ToArray();
        IdoraId[] sorted = [.. generated];

        // Act
        Array.Sort(sorted);

        // Assert
        sorted.ShouldBe(generated);
    }

    [Fact]
    public void CompareTo_UlidsAcrossPrefixes_InterleaveInGenerationOrder()
    {
        // Arrange: one thread, one monotonic stream, three entity types.
        IdoraGenerator generator = new();
        string[] prefixes = ["usr", "ord", "inv"];
        IdoraId[] generated = Enumerable.Range(0, 300)
            .Select(i => IdoraId.Parse(generator.Generate(prefixes[i % 3])))
            .ToArray();
        IdoraId[] sorted = [.. generated.Reverse()];

        // Act
        Array.Sort(sorted);

        // Assert
        sorted.ShouldBe(generated);
    }

    [Fact]
    public void CompareTo_NonGeneric_AcceptsAnIdoraIdOrNull()
    {
        // Act & Assert
        User.CompareTo((object)LaterOrder).ShouldBeLessThan(0);
        User.CompareTo((object?)null).ShouldBeGreaterThan(0);
        Should.Throw<ArgumentException>(() => User.CompareTo((object)"usr_01k226e0g0008j4ct4ank7f24s"))
            .ParamName.ShouldBe("obj");
    }

    [Fact]
    public void ComparisonOperators_AgreeWithCompareTo()
    {
        // Act & Assert
        (User < LaterOrder).ShouldBeTrue();
        (User <= LaterOrder).ShouldBeTrue();
        (User > LaterOrder).ShouldBeFalse();
        (User >= LaterOrder).ShouldBeFalse();
        (LaterOrder > User).ShouldBeTrue();
        (LaterOrder >= User).ShouldBeTrue();
#pragma warning disable CS1718 // Comparison made to same variable: the point of the assertion.
        (User <= User).ShouldBeTrue();
        (User >= User).ShouldBeTrue();
        (User < User).ShouldBeFalse();
#pragma warning restore CS1718
    }

    [Fact]
    public void IsBeforeAndIsAfter_AreStrictInTheNaturalOrder()
    {
        // Act & Assert
        User.IsBefore(LaterOrder).ShouldBeTrue();
        User.IsAfter(LaterOrder).ShouldBeFalse();
        LaterOrder.IsAfter(User).ShouldBeTrue();
        User.IsBefore(User).ShouldBeFalse();
        User.IsAfter(User).ShouldBeFalse();

        // Same millisecond, but still strictly ordered by value.
        (SameMillisecondInvoice.IsBefore(User) || SameMillisecondInvoice.IsAfter(User)).ShouldBeTrue();
    }

    [Fact]
    public void CompareTimestamp_LooksOnlyAtTheMillisecond()
    {
        // Act & Assert
        User.CompareTimestamp(SameMillisecondInvoice).ShouldBe(0);
        User.CompareTimestamp(LaterOrder).ShouldBeLessThan(0);
        LaterOrder.CompareTimestamp(User).ShouldBeGreaterThan(0);
    }

    [Fact]
    public void IsSameMillisecond_IgnoresFormatPrefixAndRandomness()
    {
        // Act & Assert
        User.IsSameMillisecond(SameMillisecondInvoice).ShouldBeTrue();
        User.IsSameMillisecond(LaterOrder).ShouldBeFalse();
    }

    [Fact]
    public void IsCreatedBeforeAndAfter_AreStrictAtMillisecondPrecision()
    {
        // Arrange
        DateTimeOffset minted = DateTimeOffset.FromUnixTimeMilliseconds(User.TimestampMilliseconds);

        // Act & Assert
        User.IsCreatedBefore(minted).ShouldBeFalse();
        User.IsCreatedAfter(minted).ShouldBeFalse();
        User.IsCreatedBefore(minted.AddMilliseconds(1)).ShouldBeTrue();
        User.IsCreatedAfter(minted.AddMilliseconds(-1)).ShouldBeTrue();
    }

    [Fact]
    public void IsCreatedBeforeAndAfter_TruncateTheInstantsSubMillisecondPart()
    {
        // Arrange: 0.9ms into the identifier's own millisecond, which still is that millisecond.
        DateTimeOffset withinMinted =
            DateTimeOffset.FromUnixTimeMilliseconds(User.TimestampMilliseconds).AddTicks(9_999);

        // Act & Assert
        User.IsCreatedBefore(withinMinted).ShouldBeFalse();
        User.IsCreatedAfter(withinMinted).ShouldBeFalse();
    }

    [Fact]
    public void IsCreatedBeforeAndAfter_IgnoreTheInstantsOffset()
    {
        // Arrange: the same instant written in Sydney time.
        DateTimeOffset sydney = DateTimeOffset.FromUnixTimeMilliseconds(User.TimestampMilliseconds)
            .ToOffset(TimeSpan.FromHours(10))
            .AddMilliseconds(1);

        // Act & Assert
        User.IsCreatedBefore(sydney).ShouldBeTrue();
    }

    [Fact]
    public void HasPrefix_IsExactAndCaseSensitive()
    {
        // Act & Assert
        User.HasPrefix("usr").ShouldBeTrue();
        User.HasPrefix("USR").ShouldBeFalse();
        User.HasPrefix("us").ShouldBeFalse();
        User.HasPrefix("usr_").ShouldBeFalse();
        User.HasPrefix(string.Empty).ShouldBeFalse();
    }

    [Fact]
    public void HasPrefix_WithNull_ThrowsArgumentNullException()
    {
        // Act & Assert
        Should.Throw<ArgumentNullException>(() => User.HasPrefix(null!)).ParamName.ShouldBe("prefix");
    }

    [Fact]
    public void Sort_WithTheDefaultComparer_UsesTheNaturalOrder()
    {
        // Arrange
        List<IdoraId> ids = [LaterOrder, default, User];

        // Act
        ids.Sort();

        // Assert
        ids[0].IsEmpty.ShouldBeTrue();
        ids[1].ShouldBe(User);
        ids[2].ShouldBe(LaterOrder);
    }
}
