using System;

namespace ElectricityTracker.Models;

public record ElectricityReading(DateOnly Date, TimeOnly Time, int Reading)
{
    public static TimeOnly TruncateToMinutes(TimeOnly time) => new(time.Hour, time.Minute);
}
