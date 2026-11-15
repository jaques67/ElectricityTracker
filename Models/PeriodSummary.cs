using System;

namespace ElectricityTracker.Models;

/// <summary>Usage and cost of the readings in one billing period, from Start up to (not including) End.</summary>
public record PeriodSummary(DateOnly Start, DateOnly End, int Usage, decimal Cost);
