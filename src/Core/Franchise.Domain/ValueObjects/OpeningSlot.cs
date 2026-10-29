using Franchise.Domain.Errors;

namespace Franchise.Domain.ValueObjects;

/// <summary>One opening period of a week day, in the restaurant's local time.
/// A period ending at or before it starts runs past midnight (e.g. 19:00 →
/// 01:00 on Saturday ends on Sunday at 01:00).</summary>
public sealed record OpeningSlot(DayOfWeek Day, TimeOnly Opens, TimeOnly Closes)
{
    public void Validate()
    {
        if (!Enum.IsDefined(Day))
        {
            throw DomainException.Validation("Unknown day of week.");
        }

        if (Opens == Closes)
        {
            throw DomainException.Validation("An opening period cannot start and end at the same time.");
        }
    }

    public bool RunsPastMidnight => Closes <= Opens;

    /// <summary>Whether the local instant falls in this period (start
    /// inclusive, end exclusive).</summary>
    public bool Contains(DayOfWeek day, TimeOnly time)
    {
        if (!RunsPastMidnight)
        {
            return day == Day && time >= Opens && time < Closes;
        }

        var nextDay = (DayOfWeek)(((int)Day + 1) % 7);
        return (day == Day && time >= Opens) || (day == nextDay && time < Closes);
    }
}
