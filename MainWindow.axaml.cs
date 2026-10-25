using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using ElectricityTracker.Models;
using ElectricityTracker.Services;

namespace ElectricityTracker;

public partial class MainWindow : Window
{
    private readonly SettingsService _settings = new();
    private readonly ReadingRepository _repository = new();
    private readonly ObservableCollection<ReadingRow> _displayedReadings = new();

    private ElectricityReading? _pendingReplacement;
    private bool _isClosingHandled;

    public MainWindow()
    {
        InitializeComponent();

        ReadingsList.ItemsSource = _displayedReadings;
        ResetInputs();

        Opened += OnOpened;
        Closing += OnClosing;
    }

    private async void OnOpened(object? sender, EventArgs e)
    {
        try
        {
            await InitialiseDataAsync();
        }
        catch (Exception ex)
        {
            ShowError($"Could not load readings: {ex.Message}");
        }
    }

    private async Task InitialiseDataAsync()
    {
        _settings.Load();

        var path = _settings.CsvPath;
        if (path is null || !File.Exists(path))
        {
            path = await PickCsvPathAsync("Choose where to store electricity readings");
            if (path is null)
            {
                ShowError("No CSV file selected. You will be asked for a location when the app closes.");
                return;
            }

            _settings.SetCsvPath(path);
            _settings.Save();
        }

        if (File.Exists(path))
        {
            var errors = await _repository.LoadAsync(path);
            RefreshReadings();

            if (errors.Count > 0)
            {
                ShowError($"Skipped {errors.Count} malformed row(s) in {path}: {string.Join("; ", errors)}");
                return;
            }

            ShowInfo($"Loaded {_repository.Count} reading(s) from {path}.");
        }
        else
        {
            ShowInfo($"New readings will be saved to {path} when the app closes.");
        }
    }

    private void OnAddClick(object? sender, RoutedEventArgs e)
    {
        HideReplace();

        var reading = ReadInputs();
        if (reading is null)
        {
            return;
        }

        if (!_repository.TryAdd(reading))
        {
            _pendingReplacement = reading;
            ReplaceButton.IsVisible = true;
            ShowError($"A reading already exists for {FormatKey(reading)}. Change the time, or choose 'Replace existing'.");
            return;
        }

        RefreshReadings(reading);
        ShowInfo($"Added reading for {FormatKey(reading)}.");
        ResetInputs();
    }

    private void OnReplaceClick(object? sender, RoutedEventArgs e)
    {
        if (_pendingReplacement is null)
        {
            HideReplace();
            return;
        }

        var reading = _pendingReplacement;
        _repository.AddOrReplace(reading);
        HideReplace();
        RefreshReadings(reading);
        ShowInfo($"Replaced reading for {FormatKey(reading)}.");
        ResetInputs();
    }

    private async void OnDateClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: ReadingRow row })
        {
            return;
        }

        var reading = row.Reading;
        var notes = await new NotesDialog(reading).ShowDialog<string?>(this);
        if (notes is null)
        {
            return;
        }

        if (!_repository.UpdateNotes(reading.Date, reading.Time, notes))
        {
            ShowError($"No reading found for {FormatKey(reading)}.");
            return;
        }

        RefreshReadings(reading);
        ShowInfo($"Updated notes for {FormatKey(reading)}.");
    }

    private ElectricityReading? ReadInputs()
    {
        if (DateInput.SelectedDate is not { } date)
        {
            ShowError("Please choose a date.");
            return null;
        }

        if (TimeInput.SelectedTime is not { } time)
        {
            ShowError("Please choose a time.");
            return null;
        }

        if (ReadingInput.Value is not { } value)
        {
            ShowError("Please enter a reading.");
            return null;
        }

        if (value < 0)
        {
            ShowError("The reading cannot be negative.");
            return null;
        }

        if (value != decimal.Truncate(value))
        {
            ShowError("The reading must be a whole number.");
            return null;
        }

        if (value > int.MaxValue)
        {
            ShowError("The reading is too large.");
            return null;
        }

        return new ElectricityReading(
            DateOnly.FromDateTime(date),
            ElectricityReading.TruncateToMinutes(TimeOnly.FromTimeSpan(time)),
            (int)value,
            ElectricityReading.NormaliseNotes(NotesInput.Text));
    }

    private void ResetInputs()
    {
        var now = DateTime.Now;
        DateInput.SelectedDate = now.Date;
        TimeInput.SelectedTime = new TimeSpan(now.Hour, now.Minute, 0);
        ReadingInput.Value = null;
        NotesInput.Text = null;
    }

    private void HideReplace()
    {
        _pendingReplacement = null;
        ReplaceButton.IsVisible = false;
    }

    private void RefreshReadings(ElectricityReading? selected = null)
    {
        // Usage depends on the neighbouring readings, so rebuild every row whenever the store changes.
        _displayedReadings.Clear();
        ReadingRow? selectedRow = null;
        foreach (var row in _repository.GetRowsWithUsage())
        {
            _displayedReadings.Add(row);
            if (selected is not null && row.Reading.Date == selected.Date &&
                row.Reading.Time == ElectricityReading.TruncateToMinutes(selected.Time))
            {
                selectedRow = row;
            }
        }

        ReadingHeader.Text = $"Reading ({_repository.Count} total)";

        if (selectedRow is not null)
        {
            ReadingsList.SelectedItem = selectedRow;
            ReadingsList.ScrollIntoView(selectedRow);
        }
    }

    private async void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_isClosingHandled)
        {
            return;
        }

        // The file picker and file I/O are async, so cancel this close and close again once saved.
        e.Cancel = true;

        try
        {
            if (await SaveReadingsAsync())
            {
                _isClosingHandled = true;
                Close();
            }
        }
        catch (Exception ex)
        {
            ShowError($"Could not save readings: {ex.Message}");
        }
    }

    /// <summary>Writes the readings to the CSV file. Returns false if the window should stay open.</summary>
    private async Task<bool> SaveReadingsAsync()
    {
        var path = _settings.CsvPath;
        var directory = path is null ? null : Path.GetDirectoryName(Path.GetFullPath(path));

        if (path is null || directory is null || !Directory.Exists(directory))
        {
            if (_repository.Count == 0)
            {
                return true;
            }

            path = await PickCsvPathAsync("Choose where to save electricity readings");
            if (path is null)
            {
                ShowError("Readings were not saved. Choose a save location to close the app.");
                return false;
            }

            _settings.SetCsvPath(path);
            _settings.Save();
        }

        await _repository.SaveAsync(path);
        return true;
    }

    private async Task<string?> PickCsvPathAsync(string title)
    {
        var options = new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = _settings.CsvFileName ?? "readings.csv",
            DefaultExtension = "csv",
            ShowOverwritePrompt = false,
            FileTypeChoices =
            [
                new FilePickerFileType("CSV files") { Patterns = ["*.csv"] },
            ],
        };

        if (!string.IsNullOrWhiteSpace(_settings.CsvDirectory) && Directory.Exists(_settings.CsvDirectory))
        {
            options.SuggestedStartLocation = await StorageProvider.TryGetFolderFromPathAsync(_settings.CsvDirectory);
        }

        var file = await StorageProvider.SaveFilePickerAsync(options);
        return file?.TryGetLocalPath();
    }

    private static string FormatKey(ElectricityReading reading) =>
        $"{reading.Date:yyyy-MM-dd} {reading.Time:HH:mm}";

    private void ShowInfo(string message)
    {
        StatusText.Foreground = Brushes.Gray;
        StatusText.Text = message;
    }

    private void ShowError(string message)
    {
        StatusText.Foreground = Brushes.IndianRed;
        StatusText.Text = message;
    }
}
