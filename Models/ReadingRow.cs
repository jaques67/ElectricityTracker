using System.Globalization;

namespace ElectricityTracker.Models;

/// <summary>
/// A reading as shown in the list. Usage is the change since the previous reading and Days is the number of
/// calendar days since that reading's date (both null for the first one).
/// They are worked out from the readings and never saved to the CSV.
/// </summary>
public record ReadingRow(ElectricityReading Reading, int? Usage, int? Days)
{
    /// <summary>The usage, followed by the daily average when the previous reading was more than a day earlier.</summary>
    public string UsageText => Usage switch
    {
        null => "—",
        { } usage when Days > 1 =>
            $"{usage.ToString(CultureInfo.InvariantCulture)} / {((double)usage / Days.Value).ToString("0.0", CultureInfo.InvariantCulture)}",
        { } usage => usage.ToString(CultureInfo.InvariantCulture),
    };
}
