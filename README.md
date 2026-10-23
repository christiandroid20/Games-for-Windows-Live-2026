# Games for Windows LIVE 2026 — Classic Blades

A lightweight, local-only overlay inspired by the original 2007 Games for Windows LIVE / Xbox 360 Blades interface. This is a community recreation, not an official Microsoft product.

## Goals

- Classic blade navigation, metallic panels, Xbox-green accents, and open/close/selection transitions.
- Windows 10 22H2 and Windows 11 compatibility target.
- Win+G hotkey, with Ctrl+Alt+G fallback when another app owns Win+G.
- Optional controller navigation and Guide-button detection through the commonly used undocumented XInput ordinal 100.
- Launch Steam, Epic Games Launcher, GOG GALAXY, or a manually selected local executable.
- No Xbox LIVE sign-in, networking service, Windows service, telemetry, or required third-party NuGet dependency.

## Requirements

- Windows 10/11 x64.
- .NET 8 Desktop Runtime for framework-dependent builds; a self-contained publish can bundle the runtime.
- Controller optional; keyboard navigation works without one.

## Build

Install the .NET 8 SDK, then run:

~~~powershell
dotnet restore
dotnet build -c Release
~~~

Self-contained single-file publish for Windows x64:

~~~powershell
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
~~~

Publish output: bin/Release/net8.0-windows/win-x64/publish/.

## Controls

- Win+G: open/close (unless Windows Game Bar or another app owns the hotkey).
- Ctrl+Alt+G: fallback hotkey.
- Arrow keys: navigate; Enter: launch; Esc / Backspace: close.
- Controller: Guide toggles; D-pad or LB/RB navigates; A selects; B/Back closes.

The Guide button is not part of the documented public XInput API. It may be unavailable on some Windows builds, controller models, or XInput runtimes. The overlay falls back to keyboard input if the undocumented entry point is missing.

## Notes

- No service is installed and no Windows component is removed or disabled.
- Store protocol handlers must already be registered by their respective clients.
- Adding/opening a game currently launches an executable for that session; it does not persist a library entry yet.
- Test on the exact LTSC image before distributing. Compatibility is a target, not a claim that every Windows release has already been tested.

## License

GPL-3.0-or-later; see LICENSE.
