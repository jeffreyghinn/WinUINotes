# WinUI Notes

WinUI Notes is a lightweight note-taking app for Windows, built with WinUI 3 and the Windows App SDK. Create notes, keep them on your device, and return to them from a simple desktop interface.

## Features

- Create and edit plain-text notes.
- See a short preview of each note in the notes list.
- Keep notes and app preferences in the app's local Windows data folder.
- Restore the main window's size, position, and display mode when you reopen the app.
- Use **WinUI Notes Pro** features, including custom note titles, sorting, and a recoverable Trash. Deleted notes in Trash are kept for up to 30 days.

The app's Pro entitlement is checked through the Microsoft Store. Pro features are available when the app has a valid Pro license.

## Requirements

- Windows 10, version 1809 (build 17763) or later.
- .NET 8 SDK.
- Visual Studio 2022 with the **Windows application development** workload, or the .NET SDK with the Windows App SDK dependencies restored.

The project targets `net8.0-windows10.0.19041.0` and supports x86, x64, and ARM64 builds.

## Build and run

From the repository directory, restore and build the project:

```powershell
dotnet restore .\WinUINotes.slnx
dotnet build .\WinUINotes.slnx -c Debug -p:Platform=x64
```

To launch it from Visual Studio, open `WinUINotes.slnx`, choose the `x64` platform and a debug target, then start the project. WinUI 3 apps require Windows; they cannot run on macOS or Linux.

## Data and privacy

Notes are stored as text files in the app's Windows local data folder. The app also stores note metadata and preferences there. Windows app data management controls the lifetime and location of this data. The Trash is local to the app and automatically removes items after 30 days.

## Project layout

- `Models/` — note data and metadata caching.
- `Views/` — notes list, note editor, Trash, and About pages.
- `Services/` — Trash handling and Microsoft Store Pro license checks.
- `MainWindow.xaml` — the main window and navigation layout.

## License

No license has been specified for this repository. Contact the project owner before redistributing it.
