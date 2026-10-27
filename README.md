# Games for Windows LIVE 2026 — Classic Blades

A lightweight, local-only overlay inspired by the original 2007 Games for Windows LIVE / Xbox 360 Blades interface. This is a community recreation, not an official Microsoft product.

## Goals

- Compact Xbox 360 Guide: gold panel with curved edge and Player tab, gamer card, Community, media transport, controller-button footer, other tabs receding on the right edge, and open/close/selection transitions. See `docs/guide-fidelity.md`.
- Windows 10 22H2 and Windows 11 compatibility target.
- Win+G hotkey, with Ctrl+Alt+G fallback for a DS4Windows PS-button keyboard mapping when another app owns Win+G.
- Controller navigation and Guide-button detection through XInputGetStateEx (ordinal 100), with documented XInput fallback.
- Launch Steam, Epic Games Launcher, GOG GALAXY, or select local executables through the built-in Guide file browser.
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
- Controller: Guide toggles when exposed by XInputGetStateEx; D-pad navigates, LB/RB switch tab; A selects; B goes back; Y closes; X locks the PC (in the file browser: back to drives).

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


## Controller and game selection

The controller provider first tries the commonly used XInputGetStateEx extension at ordinal 100, which can expose the Guide bit, then falls back to documented XInput APIs if that export is unavailable. Support depends on the XInput implementation and DS4Windows virtual controller and has not been verified on every setup. Ctrl+Alt+G remains available as a keyboard fallback. The built-in game browser lets you navigate drives and folders inside the Guide and launch .exe files without opening the Windows file picker. If HidHide hides the physical controller, raw HID detection would require allowlisting the app in HidHide; the current Guide-button path uses the virtual XInput device.


## Achievement notification bridge (new)

BladesOverlay now includes a **local-only named-pipe event bridge** and a classic-inspired achievement toast. It does not open a network port and does not pretend a test event was awarded by Steam, Epic, Xbox, or RetroAchievements.

### Test the toast

1. Start `BladesOverlay.exe` and leave it running.
2. Run `AchievementTest.exe`. It sends one clearly labeled local test event through the Windows named pipe `BladesOverlay.AchievementEvents`.
3. The overlay displays a toast with source, game, title, description, and points, then dismisses it automatically.

The GitHub Actions artifact `BladesOverlay-Windows-x64` contains both published apps. Download and extract the artifact, then launch the overlay first.

### Integration status

- **Test bridge and toast:** implemented as the first integration boundary.
- **Steam / Epic / Xbox:** not yet wired to real provider callbacks. Each platform needs a supported event source; an ordinary desktop app cannot safely assume it can intercept every other app's private notification window.
- **RetroAchievements:** per-user API credentials and real unlock polling are not yet implemented. API keys must remain private and should never be committed to the repository. See the [official API guide](https://api-docs.retroachievements.org/getting-started.html).


## Logros locales del Guide

El overlay incluye un primer catálogo de hitos locales inspirado en la era Xbox 360. Se desbloquean automáticamente al realizar acciones detectables dentro del propio overlay y se guardan en `%LOCALAPPDATA%\GamesForWindowsLIVE2026\Achievements.json`.

| Logro | G | Condición |
|---|---:|---|
| ¡Bienvenida al Guide! | 5 | Primera ejecución del overlay |
| De vuelta al 2007 | 10 | Abrir el Guide por primera vez |
| ¡A jugar! | 10 | Detectar una ventana de juego por primera vez |
| Mi identidad, mi perfil | 5 | Guardar el perfil local |
| A mi manera | 5 | Guardar un tema del Guide |

Estos son logros locales de la aplicación, no logros oficiales de Xbox LIVE ni sincronizados con Steam o Epic. El catálogo es una primera versión; los hitos se ampliarán junto con la detección de eventos y la pantalla de colección de logros.
