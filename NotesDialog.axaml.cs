using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using ElectricityTracker.Models;

namespace ElectricityTracker;

/// <summary>
/// Edits the notes of one reading. ShowDialog returns the new text (empty when cleared), or null when cancelled.
/// </summary>
public partial class NotesDialog : Window
{
    // Parameterless constructor for the XAML loader and designer.
    public NotesDialog()
    {
        InitializeComponent();
    }

    public NotesDialog(ElectricityReading reading) : this()
    {
        ReadingText.Text = $"{reading.Date:yyyy-MM-dd} {reading.Time:HH:mm}  ·  {reading.Reading}";
        NotesInput.Text = reading.Notes;
        Opened += OnOpened;
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        NotesInput.Focus();
        NotesInput.SelectAll();
    }

    private void OnSaveClick(object? sender, RoutedEventArgs e) => Close(NotesInput.Text ?? string.Empty);

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(null);
}
