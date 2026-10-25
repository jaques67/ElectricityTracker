using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ElectricityTracker.Models;

namespace ElectricityTracker.Services;

public class ReadingRepository
{
    private const string Header = "Date,Time,Reading";
    private const string DateFormat = "yyyy-MM-dd";
    private const string TimeFormat = "HH:mm";

    // Older files stored seconds; accept them on load and truncate to the minute.
    private static readonly string[] TimeParseFormats = [TimeFormat, "HH:mm:ss"];

    private readonly SortedDictionary<(DateOnly Date, TimeOnly Time), ElectricityReading> _readings = new();

    public IEnumerable<ElectricityReading> Readings => _readings.Values;

    public int Count => _readings.Count;

    public bool Contains(DateOnly date, TimeOnly time) =>
        _readings.ContainsKey((date, ElectricityReading.TruncateToMinutes(time)));

    /// <summary>Adds a reading. Returns false if a reading already exists for that date and time.</summary>
    public bool TryAdd(ElectricityReading reading)
    {
        var normalised = reading with { Time = ElectricityReading.TruncateToMinutes(reading.Time) };
        return _readings.TryAdd((normalised.Date, normalised.Time), normalised);
    }

    public void AddOrReplace(ElectricityReading reading)
    {
        var normalised = reading with { Time = ElectricityReading.TruncateToMinutes(reading.Time) };
        _readings[(normalised.Date, normalised.Time)] = normalised;
    }

    /// <summary>Loads readings from a CSV file. Returns a description of each malformed row.</summary>
    public async Task<IReadOnlyList<string>> LoadAsync(string path)
    {
        var errors = new List<string>();
        var lines = await File.ReadAllLinesAsync(path, Encoding.UTF8);

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (line.Length == 0 || (i == 0 && line.Equals(Header, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var reading = ParseLine(line);
            if (reading is null)
            {
                errors.Add($"Line {i + 1}: '{line}'");
                continue;
            }

            if (!TryAdd(reading))
            {
                Debug.WriteLine($"Duplicate reading on line {i + 1} for {line}; keeping the last one.");
                AddOrReplace(reading);
            }
        }

        foreach (var error in errors)
        {
            Debug.WriteLine($"Malformed CSV row in {path}: {error}");
        }

        return errors;
    }

    public async Task SaveAsync(string path)
    {
        var builder = new StringBuilder();
        builder.AppendLine(Header);
        foreach (var reading in _readings.Values)
        {
            builder.Append(reading.Date.ToString(DateFormat, CultureInfo.InvariantCulture)).Append(',')
                .Append(reading.Time.ToString(TimeFormat, CultureInfo.InvariantCulture)).Append(',')
                .AppendLine(reading.Reading.ToString(CultureInfo.InvariantCulture));
        }

        var directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
        var tempPath = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        await File.WriteAllTextAsync(tempPath, builder.ToString(), new UTF8Encoding(false));
        File.Move(tempPath, path, overwrite: true);
    }

    private static ElectricityReading? ParseLine(string line)
    {
        var parts = line.Split(',');
        if (parts.Length != 3)
        {
            return null;
        }

        if (!DateOnly.TryParseExact(parts[0].Trim(), DateFormat, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var date) ||
            !TimeOnly.TryParseExact(parts[1].Trim(), TimeParseFormats, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var time) ||
            !int.TryParse(parts[2].Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var value))
        {
            return null;
        }

        return new ElectricityReading(date, time, value);
    }
}
