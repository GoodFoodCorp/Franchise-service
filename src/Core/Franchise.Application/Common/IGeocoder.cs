using Franchise.Domain.ValueObjects;

namespace Franchise.Application.Common;

/// <summary>Turns a postal address into coordinates. Returns null when the
/// address does not exist, and throws <see cref="GeocodingUnavailableException"/>
/// when the provider cannot answer: the two must not be confused.</summary>
public interface IGeocoder
{
    Task<GeoPoint?> GeocodeAsync(string address, CancellationToken ct = default);
}

public sealed class GeocodingUnavailableException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>Clock port, so time-dependent rules (opening hours) are testable.</summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
