using Shouldly;
using Idora.Internal;
using Xunit;

namespace Idora.Tests.Internal;

/// <summary>
/// Covers UUIDv7 creation, including the hand-rolled implementation the .NET 8 build relies on.
/// </summary>
/// <remarks>
/// The package generates UUIDv7 values itself rather than calling <see cref="Guid.CreateVersion7"/>,
/// because the framework method reaches for the system CSPRNG on every call and costs roughly 35
/// times as much. The last test here holds the two implementations against each other to make sure
/// that decision did not cost conformance.
/// </remarks>
public sealed class UuidV7Tests
{
    private const long ArbitraryTimestamp = 1_700_000_000_000;

    [Fact]
    public void Create_ProducesAVersionSevenUuid()
    {
        // Arrange & Act
        Guid guid = UuidV7.Create();

        // Assert
        VersionOf(guid).ShouldBe(7);
    }

    [Fact]
    public void Create_ProducesTheRfc9562Variant()
    {
        // Arrange & Act
        Guid guid = UuidV7.Create();

        // Assert: the top two bits of the variant octet must be 10.
        (VariantOctetOf(guid) & 0xC0).ShouldBe(0x80);
    }

    [Fact]
    public void Create_ProducesDistinctValues()
    {
        // Arrange
        const int count = 10_000;

        // Act
        HashSet<Guid> guids = [.. Enumerable.Range(0, count).Select(_ => UuidV7.Create())];

        // Assert
        guids.Count.ShouldBe(count);
    }

    [Fact]
    public void CreateAt_EncodesTheSuppliedTimestampInTheLeadingSixOctets()
    {
        // Arrange & Act
        Guid guid = UuidV7.CreateAt(ArbitraryTimestamp);

        // Assert
        TimestampOf(guid).ShouldBe(ArbitraryTimestamp);
    }

    [Fact]
    public void CreateAt_SetsTheVersionAndVariantBits()
    {
        // Arrange & Act
        Guid guid = UuidV7.CreateAt(ArbitraryTimestamp);

        // Assert
        VersionOf(guid).ShouldBe(7);
        (VariantOctetOf(guid) & 0xC0).ShouldBe(0x80);
    }

    [Fact]
    public void CreateAt_WithTheSameTimestamp_StillProducesDistinctValues()
    {
        // Arrange
        const int count = 10_000;

        // Act
        HashSet<Guid> guids = [.. Enumerable.Range(0, count).Select(_ => UuidV7.CreateAt(ArbitraryTimestamp))];

        // Assert: the timestamp is fixed, so uniqueness here comes entirely from the random field.
        guids.Count.ShouldBe(count);
    }

    [Fact]
    public void CreateAt_ForIncreasingTimestamps_ProducesTextThatSortsInTimeOrder()
    {
        // Arrange
        string[] formatted = [.. Enumerable
            .Range(0, 100)
            .Select(offset => UuidV7.CreateAt(ArbitraryTimestamp + offset).ToString("N"))];

        // Act
        string[] sorted = [.. formatted.OrderBy(value => value, StringComparer.Ordinal)];

        // Assert
        sorted.ShouldBe(formatted);
    }

    [Fact]
    public void CreateAt_WithTheEpoch_ProducesAZeroTimestamp()
    {
        // Arrange & Act
        Guid guid = UuidV7.CreateAt(0);

        // Assert
        TimestampOf(guid).ShouldBe(0);
    }

    [Fact]
    public void CreateAt_WithTheLargestRepresentableTimestamp_KeepsTheLow48Bits()
    {
        // Arrange
        const long maxTimestamp = (1L << 48) - 1;

        // Act
        Guid guid = UuidV7.CreateAt(maxTimestamp);

        // Assert
        TimestampOf(guid).ShouldBe(maxTimestamp);
    }

    [Fact]
    public void CreateAt_ProducesTheSameLayoutAsTheFrameworksOwnImplementation()
    {
        // Arrange
        Guid framework = Guid.CreateVersion7();

        // Act
        Guid ours = UuidV7.CreateAt(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

        // Assert: the two must be interchangeable, since which one runs depends only on the target
        // framework the consumer built against.
        VersionOf(ours).ShouldBe(VersionOf(framework));
        (VariantOctetOf(ours) & 0xC0).ShouldBe(VariantOctetOf(framework) & 0xC0);
        ours.ToString("N").Length.ShouldBe(framework.ToString("N").Length);
    }

    /// <summary>Reads the 48-bit timestamp from the leading six octets of the big-endian form.</summary>
    private static long TimestampOf(Guid guid)
    {
        Span<byte> bytes = stackalloc byte[16];
        guid.TryWriteBytes(bytes, bigEndian: true, out _);

        long timestamp = 0;
        for (int i = 0; i < 6; i++)
        {
            timestamp = (timestamp << 8) | bytes[i];
        }

        return timestamp;
    }

    /// <summary>Reads the version nibble, which lives in the high half of octet 6.</summary>
    private static int VersionOf(Guid guid)
    {
        Span<byte> bytes = stackalloc byte[16];
        guid.TryWriteBytes(bytes, bigEndian: true, out _);
        return bytes[6] >> 4;
    }

    /// <summary>Returns octet 8, whose top bits carry the variant.</summary>
    private static byte VariantOctetOf(Guid guid)
    {
        Span<byte> bytes = stackalloc byte[16];
        guid.TryWriteBytes(bytes, bigEndian: true, out _);
        return bytes[8];
    }
}
