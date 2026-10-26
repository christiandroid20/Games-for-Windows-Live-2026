# Games for Windows LIVE 2026 — Classic Blades

A lightweight, local-only overlay inspired by the original 2007 Games for Windows LIVE / Xbox 360 Blades interface. This is a community recreation, not an official Microsoft product.

## Goals

- Compact Xbox 360 Guide: gold panel with curved edge and Player tab, gamer card, Community, media transport, controller-button footer, other tabs receding on the right edge, and open/close/selection transitions. See `docs/guide-fidelity.md`.
- Windows 10 22H2 and Windows 11 compatibility target.
- Win+G hotkey, with Ctrl+Alt+G fallback for a DS4Windows PS-button keyboard mapping when another app owns Win+G.
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
- Ctrl+Alt+G: fallback hotkey. In DS4Windows, assign the physical PS button to the keyboard combo Ctrl+Alt+G (for example, with a profile special action / keyboard macro).
- Arrow keys: navigate (Left/Right move inside button rows, otherwise switch tab; PageUp/PageDown always switch tab); Enter: launch; Esc / Backspace: back (closes from the top level).
- Controller: Guide toggles; D-pad navigates, LB/RB switch tab; A selects; B goes back; Y closes; X locks the PC (in the file browser: back to drives).

The Guide button is not part of the documented public XInput gamepad state: `XInputGetState` exposes the standard D-pad, shoulders, face buttons and other listed controls, but not the PS/Guide button. With a PlayStation controller emulated as an Xbox pad by DS4Windows, map the physical PS button to Ctrl+Alt+G in the DS4Windows profile. This activates the overlay through the registered global keyboard shortcut even when the virtual XInput state cannot report Guide. Win+G may still be reserved by Windows Game Bar.

## Notes

- No service is installed and no Windows component is removed or disabled.
- Store protocol handlers must already be registered by their respective clients.
- Adding/opening a game currently launches an executable for that session; it does not persist a library entry yet.
- Test on the exact LTSC image before distributing. Compatibility is a target, not a claim that every Windows release has already been tested.

## License

GPL-3.0-or-later; see LICENSE.


## Modular development (pre-DLL stage)

The current branch keeps modules in the same executable while establishing stable contracts before any DLL split:

- `Controllers/ControllerContracts.cs`: controller state, connection and battery models plus `IControllerProvider`.
- `Controllers/XInputControllerProvider.cs`: the first provider for XInput player slots 1–4 and XInput battery information.
- `Controllers/ControllerDiagnosticsWindow.cs`: a small diagnostic view for connected XInput slots and battery data.
- `Profiles/WindowsProfileService.cs`: starts with the Windows display name and best-effort account picture; users can override the Guide display name and image locally. Settings are saved under the current user's Local AppData.
- `Avatars/AvatarContracts.cs`: an interface boundary for a future offline avatar renderer/catalog.

The universal controller goal is **not complete yet**. This first provider currently reads XInput only. Physical HID/Windows battery enumeration and safe correlation between physical controllers and virtual XInput devices are future providers; the UI must not claim to detect every controller until those are implemented and tested. Battery values that cannot be verified should remain unavailable rather than guessed.

The classic Xbox 360 avatar idea is feasible as an offline feature, but it is separate from Xbox LIVE. The old XNA Gamer Services avatar APIs were designed for Xbox 360 titles; Windows implementations do not render the original avatar system automatically. A faithful implementation would need a compatible offline renderer and legally distributable assets, or a clean-room avatar system inspired by the era. This repository does not bundle Microsoft avatar meshes, clothing, textures, or other proprietary assets.
