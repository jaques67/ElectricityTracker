# AGENTS.md

Guidance for AI coding agents working on **ElectricityTracker**.

## Project overview

ElectricityTracker is a cross-platform Avalonia desktop app for capturing electricity meter readings.
The user enters a **date**, a **time** and a **meter reading**, plus optional **notes** (for example
"used tumble dryer"). Readings are kept in memory while the
app runs and written to a CSV file when the app closes. The CSV will be used later for analysis, so
its format must stay stable and machine-readable.

## Tech stack

- .NET 10 (`net10.0`), C# with nullable reference types enabled
- Avalonia 12.1.4 (`Avalonia`, `Avalonia.Desktop`, `Avalonia.Themes.Fluent`, `Avalonia.Fonts.Inter`)
- `AvaloniaUI.DiagnosticsSupport` (Debug builds only; `WithDeveloperTools()` in `Program.cs`)
- Single project, no solution file, no test project yet

## Current layout

| File | Purpose |
|------|---------|
| `Program.cs` | Entry point; `BuildAvaloniaApp()` (keep it, the designer uses it) |
| `App.axaml` / `App.axaml.cs` | Application, Fluent theme, creates `MainWindow` |
| `MainWindow.axaml` / `MainWindow.axaml.cs` | Main window (still the template placeholder) |
| `app.manifest` | Windows application manifest |
| `.junie/` | JetBrains Junie config; leave it alone |

Suggested layout for new code (keep it small, don't over-engineer):

```
Models/ElectricityReading.cs      // record: DateOnly Date, TimeOnly Time, int Reading, string? Notes
Services/SettingsService.cs       // load/save the ini file
Services/ReadingRepository.cs     // in-memory store + CSV load/save + usage calculation
MainWindow.axaml(.cs)             // input fields, add button, list of readings with usage
```

## Build and run

```sh
dotnet restore
dotnet build
dotnet run
```

Run `dotnet build` after changes and make sure it finishes with no errors or new warnings.

## Functional requirements

### 1. Settings (ini file)

- Store app settings in an ini file named `settings.ini` in the per-user app data folder:
  `Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ElectricityTracker", "settings.ini")`.
  Create the folder if it doesn't exist. Don't write settings next to the executable.
- Format:

  ```ini
  [Data]
  CsvDirectory=/Users/someone/Documents
  CsvFileName=readings.csv
  ```

- Read and write it with a small hand-written parser (sections, `key=value`, ignore blank lines and
  lines starting with `;` or `#`). Don't add a NuGet package just for this.
- A missing or unreadable ini file is not an error. Treat it as "no CSV configured".

### 2. Startup

1. Load `settings.ini`.
2. If no CSV path is configured, **or the configured file doesn't exist**, ask the user for a file
   name and location with `TopLevel.StorageProvider.SaveFilePickerAsync` (default name
   `readings.csv`, `.csv` file type filter).
3. Write the chosen directory and file name back to `settings.ini` straight away.
4. If the file exists, load its readings into the in-memory store. If the user picked a new file
   that doesn't exist yet, start with an empty store. The file is created on close.
5. If the user cancels the picker, carry on with an empty store and no path. Ask again on close
   (see section 4).

Use the async `StorageProvider` APIs and call them once the window is shown (for example from the
`Opened` event), not in the constructor.

### 3. Data model and in-memory store

- `ElectricityReading` is a `record` with:
  - `DateOnly Date`
  - `TimeOnly Time`: hours and minutes only, no seconds. Truncate anything finer
    (`new TimeOnly(t.Hour, t.Minute)`).
  - `int Reading`: the meter value, a whole number (no decimals)
  - `string? Notes`: optional free text about the reading (for example "used tumble dryer").
    Single line only: trim it, replace any line breaks with spaces, and store `null` when it's empty
    or whitespace.
- One date can have many readings, but **the time must be unique within a date**. The key is
  `(Date, Time)`.
- Recommended store: `SortedDictionary<(DateOnly Date, TimeOnly Time), ElectricityReading>`, or a
  `SortedDictionary<DateOnly, SortedDictionary<TimeOnly, int>>`. Either keeps entries ordered by
  date, then time, and stops duplicate times.
- If the user adds a reading for a `(Date, Time)` that already exists, don't overwrite it silently.
  Show a validation message, or ask before replacing it.
- On load, if the CSV itself contains duplicate `(Date, Time)` rows, keep the last one and log a
  warning with `Debug.WriteLine` or `Trace`.

### 4. Data capture UI

- Input fields:
  - Date: `CalendarDatePicker` (or `DatePicker`), defaulting to today
  - Time: `TimePicker` with hours and minutes only (no seconds; 24-hour clock,
    `ClockIdentifier="24HourClock"`, or an equivalent `HH:mm` text box), defaulting to the current
    time truncated to the minute
  - Reading: `NumericUpDown` (`Increment="1"`, `Minimum="0"`) or a validated `TextBox`. Must be a
    non-negative whole number; show a validation error for decimals rather than rounding them.
  - Notes: optional single-line `TextBox` (`AcceptsReturn="False"`) with a placeholder such as
    "e.g. used tumble dryer". Leaving it empty is fine.
- When loading the CSV, also accept old `HH:mm:ss` times and truncate them to the minute.
- An **Add** button validates the input and adds it to the store. Show validation errors in the
  window, not as exceptions.
- Show the captured readings (for example in a `ListBox` or `DataGrid`) ordered by date, then time,
  including the notes.
- Add a **Usage** column to the list: the current entry's reading minus the previous entry's reading,
  where "previous" is the entry just before it in date-then-time order (it may be on an earlier
  date). The first entry has no previous reading, so leave its usage blank (or show `—`).
  - Usage is **derived, never stored**. Don't add it to `ElectricityReading` or write it to the CSV
    (section 6 is unchanged). Calculate it after the CSV is loaded and again whenever the store
    changes (add or replace), because inserting a reading between two others changes the usage of
    the entry after it.
  - Put the calculation in `Services/` (for example a method on `ReadingRepository` that returns
    display rows of reading + usage) so it can be unit tested, rather than in code-behind.
  - If usage comes out negative (meter replaced or a typo), still show the value rather than hiding
    it or throwing, so the user can spot and fix the bad reading.

### 5. Saving on close

- Data is written **only when the app closes**. Handle `Window.Closing`. Because the file picker is
  async, cancel the first close (`e.Cancel = true`), do the async work, then close the window again
  with a guard flag so you don't loop.
- If there's no CSV path yet (the user cancelled at startup), ask for the save location with the save
  file picker, then update `settings.ini`.
- If the configured file's folder no longer exists, ask for a new location as well.
- Write all readings **ordered by date, then time**, replacing the file's contents. Write to a temp
  file in the same folder and then `File.Move(temp, target, overwrite: true)` so a crash can't leave
  a half-written CSV.

### 6. CSV format

```csv
Date,Time,Reading,Notes
2026-10-25,07:30,12345,
2026-10-25,19:05,12352,used tumble dryer
2026-10-26,08:00,12360,"washing machine, then dryer"
```

- Header row: `Date,Time,Reading,Notes`. Don't add a usage column; usage is worked out from the
  readings (see section 4).
- Date `yyyy-MM-dd`, time `HH:mm` (24-hour, no seconds), reading as a plain whole number (no
  decimal point)
- Notes is always the fourth column; write an empty field when there are no notes. Quote it the
  RFC 4180 way: wrap the field in double quotes if it contains a comma or a double quote, and double
  any double quotes inside (`"said ""hi"""`). The loader must handle quoted fields, so split lines
  with a small hand-written parser rather than `string.Split(',')`.
- When loading, also accept old 3-column rows (`Date,Time,Reading`, with or without that header)
  and treat them as having no notes.
- Always use `CultureInfo.InvariantCulture` for parsing and formatting (no thousands separators),
  whatever the OS locale
- UTF-8, no BOM
- When loading, skip the header and blank lines. Report malformed rows to the user (or log them)
  instead of crashing.

## Coding conventions

- Match the existing style: file-scoped namespaces (`namespace ElectricityTracker;`), 4-space
  indentation, `PascalCase` types and members, `_camelCase` private fields.
- Keep nullable warnings at zero, and don't use `!` to silence them without good reason.
- Use `async`/`await` for file and dialog I/O. Never block the UI thread with `.Result` or `.Wait()`.
- Keep file I/O and parsing out of code-behind (put it in `Services/`) so it can be unit tested later.
- Don't add NuGet packages (MVVM toolkits, CSV or ini libraries) without asking the user first. The
  requirements can be met with the BCL and Avalonia.
- Don't commit build output (`bin/`, `obj/`). It's already covered by `.gitignore`.
