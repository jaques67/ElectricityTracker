using System;

namespace ElectricityTracker.Models;

/// <summary>
/// The price per kWh and the day of the month the running cost total resets.
/// A reset day past the end of a short month falls on that month's last day.
/// </summary>
public record EnergyTariff(decimal RatePerKWh, int ResetDay)
{
    public const decimal DefaultRatePerKWh = 0.1535m;
    public const int DefaultResetDay = 10;

    public static EnergyTariff Default { get; } = new(DefaultRatePerKWh, DefaultResetDay);

    /// <summary>The first day of the billing period that contains the date.</summary>
    public DateOnly PeriodStart(DateOnly date)
    {
        var start = ResetDateIn(date.Year, date.Month);
        if (date >= start)
        {
            return start;
        }

        var previousMonth = new DateOnly(date.Year, date.Month, 1).AddMonths(-1);
        return ResetDateIn(previousMonth.Year, previousMonth.Month);
    }

    /// <summary>The first day of the billing period after the one that contains the date.</summary>
    public DateOnly NextPeriodStart(DateOnly date)
    {
        var start = PeriodStart(date);
        var nextMonth = new DateOnly(start.Year, start.Month, 1).AddMonths(1);
        return ResetDateIn(nextMonth.Year, nextMonth.Month);
    }

    private DateOnly ResetDateIn(int year, int month) =>
        new(year, month, Math.Min(ResetDay, DateTime.DaysInMonth(year, month)));
}
