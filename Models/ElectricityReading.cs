using System;

namespace ElectricityTracker.Models;

public record ElectricityReading(DateOnly Date, TimeOnly Time, int Reading, string? Notes = null)
{
    public static TimeOnly TruncateToMinutes(TimeOnly time) => new(time.Hour, time.Minute);

    /// <summary>Makes notes a single trimmed line, or null when there is nothing to keep.</summary>
    public static string? NormaliseNotes(string? notes)
    {
        if (string.IsNullOrWhiteSpace(notes))
        {
            return null;
        }

        return notes.ReplaceLineEndings(" ").Trim();
    }
}
