using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace ElectricityTracker.Services;

public class SettingsService
{
    private const string DataSection = "Data";
    private const string CsvDirectoryKey = "CsvDirectory";
    private const string CsvFileNameKey = "CsvFileName";

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
                CsvDirectory = data.GetValueOrDefault(CsvDirectoryKey);
                CsvFileName = data.GetValueOrDefault(CsvFileNameKey);
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
        File.WriteAllText(_settingsPath, builder.ToString(), new UTF8Encoding(false));
    }

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
