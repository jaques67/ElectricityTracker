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
    private const string Header = "Date,Time,Reading,Notes";

    // Older files had no Notes column; accept their header and rows too.
    private const string LegacyHeader = "Date,Time,Reading";
    private const string DateFormat = "yyyy-MM-dd";
    private const string TimeFormat = "HH:mm";

    // Older files stored seconds; accept them on load and truncate to the minute.
    private static readonly string[] TimeParseFormats = [TimeFormat, "HH:mm:ss"];

    private readonly SortedDictionary<(DateOnly Date, TimeOnly Time), ElectricityReading> _readings = new();

    public IEnumerable<ElectricityReading> Readings => _readings.Values;

    public int Count => _readings.Count;

    /// <summary>
    /// Returns the readings in date-then-time order, each with its usage since the previous reading.
    /// The first reading has no usage. Negative usage is kept so bad readings stay visible.
    /// </summary>
    public IReadOnlyList<ReadingRow> GetRowsWithUsage()
    {
        var rows = new List<ReadingRow>(_readings.Count);
        int? previous = null;
        foreach (var reading in _readings.Values)
        {
            rows.Add(new ReadingRow(reading, reading.Reading - previous));
            previous = reading.Reading;
        }

        return rows;
    }

    public bool Contains(DateOnly date, TimeOnly time) =>
        _readings.ContainsKey((date, ElectricityReading.TruncateToMinutes(time)));

    /// <summary>Adds a reading. Returns false if a reading already exists for that date and time.</summary>
    public bool TryAdd(ElectricityReading reading)
    {
        var normalised = Normalise(reading);
        return _readings.TryAdd((normalised.Date, normalised.Time), normalised);
    }

    public void AddOrReplace(ElectricityReading reading)
    {
        var normalised = Normalise(reading);
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
            if (line.Length == 0 || (i == 0 && IsHeader(line)))
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
                .Append(reading.Reading.ToString(CultureInfo.InvariantCulture)).Append(',')
                .AppendLine(EscapeField(reading.Notes));
        }

        var directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
        var tempPath = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        await File.WriteAllTextAsync(tempPath, builder.ToString(), new UTF8Encoding(false));
        File.Move(tempPath, path, overwrite: true);
    }

    private static ElectricityReading Normalise(ElectricityReading reading) =>
        reading with
        {
            Time = ElectricityReading.TruncateToMinutes(reading.Time),
            Notes = ElectricityReading.NormaliseNotes(reading.Notes),
        };

    private static bool IsHeader(string line) =>
        line.Equals(Header, StringComparison.OrdinalIgnoreCase) ||
        line.Equals(LegacyHeader, StringComparison.OrdinalIgnoreCase);

    private static ElectricityReading? ParseLine(string line)
    {
        var parts = SplitLine(line);
        if (parts is null || parts.Count is < 3 or > 4)
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

        var notes = parts.Count == 4 ? parts[3] : null;
        return new ElectricityReading(date, time, value, notes);
    }

    /// <summary>Quotes a field when it contains a comma or a double quote (RFC 4180).</summary>
    private static string EscapeField(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return value.Contains(',') || value.Contains('"')
            ? $"\"{value.Replace("\"", "\"\"")}\""
            : value;
    }

    /// <summary>Splits a CSV line into fields, honouring quotes. Returns null if a quote is unterminated.</summary>
    private static List<string>? SplitLine(string line)
    {
        var fields = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (inQuotes)
            {
                if (c != '"')
                {
                    field.Append(c);
                }
                else if (i + 1 < line.Length && line[i + 1] == '"')
                {
                    field.Append('"');
                    i++;
                }
                else
                {
                    inQuotes = false;
                }
            }
            else if (c == '"')
            {
                inQuotes = true;
            }
            else if (c == ',')
            {
                fields.Add(field.ToString());
                field.Clear();
            }
            else
            {
                field.Append(c);
            }
        }

        if (inQuotes)
        {
            return null;
        }

        fields.Add(field.ToString());
        return fields;
    }
}
