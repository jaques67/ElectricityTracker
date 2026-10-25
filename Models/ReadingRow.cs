namespace ElectricityTracker.Models;

/// <summary>
/// A reading as shown in the list. Usage is the change since the previous reading (null for the first one).
/// It is worked out from the readings and never saved to the CSV.
/// </summary>
public record ReadingRow(ElectricityReading Reading, int? Usage);
