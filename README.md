# STAR Manager

STAR Manager is a Windows desktop manager for Speech To Audio Relay (STAR) which aims to make STAR setup and day-to-day management easier by providing a graphical interface for provider discovery, configuration, and process control.

## Current status

Early implementation is in progress.

Implemented so far:
- Browse to a STAR root folder
- Scan and detect STAR components (user app, coagulator, providers)
- List discovered providers in the UI
- Launch provider configuration using the existing provider configure flow
- Start and stop providers from the UI
- Launch STAR user app from the detected entrypoint
- Open coagulator website URL when available in coagulator.ini
- Theme selection (System, Dark, Light)

## Target platform

- Windows (v1)
- .NET 10 desktop

## Why this project exists

STAR providers and components are powerful, but setup can be difficult for users who are not comfortable with command-line workflows.

This utility is designed to provide:
- Faster setup discovery
- Safer provider management
- Easier launch and configuration actions
- A modern desktop UI with dark mode support

## Project structure

- src/StarManager.App: WPF desktop application
- StarManager.slnx: Solution file

## Build and run

From the repository root:

1. Restore dependencies
   dotnet restore StarManager.slnx

2. Build
   dotnet build StarManager.slnx

3. Run
   dotnet run --project src/StarManager.App/StarManager.App.csproj

## Planned next steps

- Add coagulator start and stop controls
- Add persisted settings (recent STAR paths, theme preference)
- Add fallback INI editor for common provider settings
- Add provider diagnostics and logs in the UI
- Add installer packaging workflow for GitHub releases

## Relationship to STAR

This project is a companion utility and is not a replacement for STAR itself.

Upstream STAR repository:
https://github.com/samtupy/star

## Contributing

Contributions are welcome. For now, open an issue with:
- What you were trying to do
- What happened
- Steps to reproduce
- Suggested behavior

## License

License will be added before first public release.
