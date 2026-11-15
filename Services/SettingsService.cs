using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using ElectricityTracker.Models;

namespace ElectricityTracker.Services;

public class SettingsService
{
    private const string DataSection = "Data";
    private const string CsvDirectoryKey = "CsvDirectory";
    private const string CsvFileNameKey = "CsvFileName";
    private const string EnergySection = "Energy";
    private const string RateKey = "RatePerKWh";
    private const string ResetDayKey = "ResetDay";

    private readonly string _settingsPath;

    public SettingsService()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ElectricityTracker",
            "settings.ini"))
    {
    }

    public SettingsService(string settingsPath)
    {
        _settingsPath = settingsPath;
    }

    public string? CsvDirectory { get; set; }

    public string? CsvFileName { get; set; }

    /// <summary>The price per kWh and monthly reset day. Defaults to $0.1535 per kWh, resetting on the 10th.</summary>
    public EnergyTariff Tariff { get; set; } = EnergyTariff.Default;

    /// <summary>False when the ini file has no [Energy] settings yet, so the defaults should be written out.</summary>
    public bool HasEnergySettings { get; private set; }

    public string? CsvPath =>
        string.IsNullOrWhiteSpace(CsvDirectory) || string.IsNullOrWhiteSpace(CsvFileName)
            ? null
            : Path.Combine(CsvDirectory, CsvFileName);

    public void SetCsvPath(string path)
    {
        CsvDirectory = Path.GetDirectoryName(path);
        CsvFileName = Path.GetFileName(path);
    }

    public void Load()
    {
        try
        {
            if (!File.Exists(_settingsPath))
            {
                return;
            }

            var sections = Parse(File.ReadAllLines(_settingsPath));
            if (sections.TryGetValue(DataSection, out var data))
            {
                CsvDirectory = NullIfEmpty(data.GetValueOrDefault(CsvDirectoryKey));
                CsvFileName = NullIfEmpty(data.GetValueOrDefault(CsvFileNameKey));
            }

            if (sections.TryGetValue(EnergySection, out var energy))
            {
                LoadTariff(energy);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Debug.WriteLine($"Could not read settings from {_settingsPath}: {ex.Message}");
        }
    }

    public void Save()
    {
        var directory = Path.GetDirectoryName(_settingsPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var builder = new StringBuilder();
        builder.AppendLine($"[{DataSection}]");
        builder.AppendLine($"{CsvDirectoryKey}={CsvDirectory}");
        builder.AppendLine($"{CsvFileNameKey}={CsvFileName}");
        builder.AppendLine();
        builder.AppendLine($"[{EnergySection}]");
        builder.AppendLine("; Price per kWh, and the day of the month the running cost total resets (1-31)");
        builder.AppendLine($"{RateKey}={Tariff.RatePerKWh.ToString(CultureInfo.InvariantCulture)}");
        builder.AppendLine($"{ResetDayKey}={Tariff.ResetDay.ToString(CultureInfo.InvariantCulture)}");
        File.WriteAllText(_settingsPath, builder.ToString(), new UTF8Encoding(false));
        HasEnergySettings = true;
    }

    private void LoadTariff(Dictionary<string, string> energy)
    {
        var hasRate = decimal.TryParse(energy.GetValueOrDefault(RateKey), NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture, out var rate);
        var hasResetDay = int.TryParse(energy.GetValueOrDefault(ResetDayKey), NumberStyles.None,
            CultureInfo.InvariantCulture, out var resetDay) && resetDay is >= 1 and <= 31;

        if (!hasRate)
        {
            Debug.WriteLine($"Invalid or missing {RateKey} in {_settingsPath}; using {EnergyTariff.DefaultRatePerKWh}.");
        }

        if (!hasResetDay)
        {
            Debug.WriteLine($"Invalid or missing {ResetDayKey} in {_settingsPath}; using {EnergyTariff.DefaultResetDay}.");
        }

        Tariff = new EnergyTariff(
            hasRate ? rate : EnergyTariff.DefaultRatePerKWh,
            hasResetDay ? resetDay : EnergyTariff.DefaultResetDay);
        // Only missing keys are written out on startup; an invalid value is left for the user to fix.
        HasEnergySettings = energy.ContainsKey(RateKey) && energy.ContainsKey(ResetDayKey);
    }

    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private static Dictionary<string, Dictionary<string, string>> Parse(IEnumerable<string> lines)
    {
        var sections = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, string>? current = null;

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith(';') || line.StartsWith('#'))
            {
                continue;
            }

            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                var name = line[1..^1].Trim();
                if (!sections.TryGetValue(name, out current))
                {
                    current = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    sections[name] = current;
                }

                continue;
            }

            var separator = line.IndexOf('=');
            if (separator <= 0 || current is null)
            {
                continue;
            }

            current[line[..separator].Trim()] = line[(separator + 1)..].Trim();
        }

        return sections;
    }
}
