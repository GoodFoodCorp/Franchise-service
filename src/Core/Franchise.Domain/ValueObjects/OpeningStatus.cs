namespace Franchise.Domain.ValueObjects;

/// <summary>Why a restaurant takes orders or not, right now.</summary>
public enum OpeningState
{
    /// <summary>Within its opening hours and taking orders.</summary>
    Open,

    /// <summary>Closed by the restaurant itself (incident, rush, day off…)
    /// until it reopens, whatever its hours say.</summary>
    ClosedByRestaurant,

    /// <summary>Outside its opening hours.</summary>
    OutsideHours,

    /// <summary>No opening hours given: never open.</summary>
    NoHours,

    /// <summary>Taken off the network by head office.</summary>
    Inactive,
}

/// <summary>The one opening status customers and the restaurant both see.
/// <paramref name="ClosesAt"/>: end of the current period when open;
/// <paramref name="NextDay"/> / <paramref name="NextOpens"/>: start of the
/// next period when outside hours. Times are the restaurant's local time.</summary>
public sealed record OpeningStatus(
    OpeningState State,
    TimeOnly? ClosesAt = null,
    DayOfWeek? NextDay = null,
    TimeOnly? NextOpens = null)
{
    public bool IsOpen => State == OpeningState.Open;
}
