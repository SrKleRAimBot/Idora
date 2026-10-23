using Shouldly;
using Idora.Internal;
using Idora.Tests.Support;
using Xunit;

namespace Idora.Tests;

/// <summary>
/// Pins the clock these identifiers are stamped from to UTC.
/// </summary>
/// <remarks>
/// <para>
/// Both ULID and UUIDv7 define their timestamp as milliseconds since the Unix epoch, which is an
/// absolute instant with no time zone attached. Servers in Sydney, London and Los Angeles that agree
/// on the instant therefore produce identifiers that interleave in true creation order, and that
/// order survives being sorted in a database that knows nothing about where the rows came from.
/// </para>
/// <para>
/// Swapping the single <see cref="DateTime.UtcNow"/> read for <see cref="DateTime.Now"/> would break
/// that silently: every server would shift its identifiers by its own UTC offset, so a row created
/// in Auckland would sort twelve hours ahead of one created at the same instant in London. Nothing
/// would throw, and the identifiers would still look perfectly well formed.
/// </para>
/// <para>
/// That mistake is invisible on a machine already running in UTC, because there local time and UTC
/// are the same. The assertions below therefore only bite when the host has a non-zero offset, and
/// CI deliberately runs the suite in a non-UTC time zone so that they do.
/// </para>
/// </remarks>
public sealed class TimestampSourceTests
{
    /// <summary>The host's offset from UTC, which is zero on a machine running in UTC.</summary>
    private static readonly TimeSpan LocalOffset = TimeZoneInfo.Local.GetUtcOffset(DateTime.UtcNow);

    /// <summary>Generous enough to absorb a slow test host, far tighter than any real UTC offset.</summary>
    private static readonly long ToleranceMs = 30_000;

    [Fact]
    public void CurrentTimestampMs_ReadsUtc_NotTheHostsLocalTime()
    {
        // Arrange
        long utcNow = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        // Act
        long actual = UlidGenerator.CurrentTimestampMs();

        // Assert
        actual.ShouldBeInRange(utcNow - ToleranceMs, utcNow + ToleranceMs);
        ShouldNotBeLocalTime(actual, utcNow);
    }

    [Fact]
    public void Generate_WithUlidStrategy_EncodesUtcRegardlessOfTheHostsTimeZone()
    {
        // Arrange
        IdoraGenerator generator = new(options => options.RegisterEntity<User>("usr"));
        long utcNow = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        // Act
        string id = generator.Generate<User>();

        // Assert
        long encoded = UlidDecoder.DecodeTimestamp(UlidDecoder.Split(id).Value);
        encoded.ShouldBeInRange(utcNow - ToleranceMs, utcNow + ToleranceMs);
        ShouldNotBeLocalTime(encoded, utcNow);
    }

    [Fact]
    public void Generate_WithUuidV7Strategy_EncodesUtcRegardlessOfTheHostsTimeZone()
    {
        // Arrange
        IdoraGenerator generator = new(options =>
            options.UseUuidV7().RegisterEntity<User>("usr"));
        long utcNow = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        // Act
        string id = generator.Generate<User>();

        // Assert: the first 12 hex characters are the 48-bit millisecond timestamp.
        long encoded = Convert.ToInt64(UlidDecoder.Split(id).Value[..12], fromBase: 16);
        encoded.ShouldBeInRange(utcNow - ToleranceMs, utcNow + ToleranceMs);
        ShouldNotBeLocalTime(encoded, utcNow);
    }

    [Fact]
    public void CreateAt_TreatsItsArgumentAsAnAbsoluteInstant()
    {
        // Arrange: the same instant expressed in three different time zones.
        DateTimeOffset instant = new(2026, 8, 7, 12, 0, 0, TimeSpan.Zero);
        DateTimeOffset inSydney = instant.ToOffset(TimeSpan.FromHours(10));
        DateTimeOffset inLosAngeles = instant.ToOffset(TimeSpan.FromHours(-7));

        // Act
        long fromUtc = UuidTimestampOf(instant);
        long fromSydney = UuidTimestampOf(inSydney);
        long fromLosAngeles = UuidTimestampOf(inLosAngeles);

        // Assert: one instant, one timestamp, whatever offset it was written down with.
        fromSydney.ShouldBe(fromUtc);
        fromLosAngeles.ShouldBe(fromUtc);
    }

    [Fact]
    public void Generate_ForIdentifiersMadeAtTheSameInstantInDifferentZones_OrdersThemByInstant()
    {
        // Arrange: three servers, three time zones, creating entities a millisecond apart.
        DateTimeOffset firstInstant = new(2026, 8, 7, 12, 0, 0, TimeSpan.Zero);
        DateTimeOffset secondInstant = firstInstant.AddMilliseconds(1).ToOffset(TimeSpan.FromHours(10));
        DateTimeOffset thirdInstant = firstInstant.AddMilliseconds(2).ToOffset(TimeSpan.FromHours(-7));

        // Act
        string[] ids =
        [
            UuidV7.CreateAt(firstInstant.ToUnixTimeMilliseconds()).ToString("N"),
            UuidV7.CreateAt(secondInstant.ToUnixTimeMilliseconds()).ToString("N"),
            UuidV7.CreateAt(thirdInstant.ToUnixTimeMilliseconds()).ToString("N"),
        ];

        // Assert: sorting the text puts them back in true creation order, not zone order.
        ids.ShouldBe([.. ids.OrderBy(id => id, StringComparer.Ordinal)]);
    }

    /// <summary>
    /// Asserts the value is not what a local-time implementation would have produced.
    /// </summary>
    /// <remarks>
    /// A no-op on a host running in UTC, where the two are indistinguishable by construction.
    /// </remarks>
    private static void ShouldNotBeLocalTime(long actual, long utcNow)
    {
        if (LocalOffset == TimeSpan.Zero)
        {
            return;
        }

        long asLocalTime = utcNow + (long)LocalOffset.TotalMilliseconds;
        actual.ShouldNotBeInRange(
            asLocalTime - ToleranceMs,
            asLocalTime + ToleranceMs,
            $"the timestamp looks like local time in {TimeZoneInfo.Local.Id} (offset {LocalOffset}) rather than UTC");
    }

    /// <summary>Reads the 48-bit millisecond timestamp back out of a generated UUIDv7.</summary>
    private static long UuidTimestampOf(DateTimeOffset instant) =>
        Convert.ToInt64(
            UuidV7.CreateAt(instant.ToUnixTimeMilliseconds()).ToString("N")[..12],
            fromBase: 16);
}
