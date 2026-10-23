# Xbox 360 Guide port — implementation notes

## Scope

This work targets the compact, left-side Xbox 360 Guide overlay shown in the repository reference screenshot, not the full Blades dashboard. Changes belong on `redesign/classic-blades-ltsc`; do not modify `main`.

## Current application

- WPF / .NET 8 Windows desktop app (`BladesOverlay.csproj`).
- Main UI and controller polling currently live in `Program.cs` (`Overlay` class).
- XInput Guide-button handling and an integrated file browser already exist in this branch.
- The project currently uses code-built WPF controls; adding DashX360's XAML directly would require its window code-behind, custom controls, view models, assets, and services to be ported or replaced.

## Visual reference observations

The screenshot committed at the repository root shows:
- A compact gold/yellow Guide panel attached to the left edge, with a curved outer edge and thin layered gold/cream outlines.
- Repeating low-contrast Xbox-style symbol texture in the panel background.
- Profile card at the top with gamer picture, Gamerscore and online status.
- Community counters, Private Chat and Personal Settings rows.
- A compact media transport row and Xbox 360 logo panel.
- A dark translucent scrim over the underlying dashboard.
- A footer/status band with controller glyphs and contextual actions (Y, X, B, A).

## DashX360 components identified

The DashX360 source has a dedicated `views/BladesGuideWindow.xaml`, plus `BladesGuideChrome.cs` for the curved gold silhouette and `BladePadGlyph.cs` for controller glyphs. These are useful references, but are not drop-in files: namespaces, app models, event handlers, theme/font assets, and view models belong to DashX360.

Reference repository: https://github.com/ZivvoZ/dashx360

## Porting strategy

1. Keep this app's existing overlay/controller/browser implementation and introduce a dedicated Guide presentation layer rather than replacing the entire app.
2. Port/adapt the gold chrome drawing into this app's namespace and size it relative to the actual Guide panel height instead of assuming a fixed 720 px design coordinate.
3. Rebuild the visible Guide layout using local WPF controls: profile/status card, community counters, chat/settings rows, media controls, logo plate, scrim, and contextual controller footer.
4. Keep input routing in one place. The same selected row must respond consistently to keyboard and XInput; B/Escape backs out of a subview, A/Enter activates, and the Guide button toggles the overlay.
5. Preserve the integrated file browser as a Games/utility view and ensure its selection/scroll state remains intact when switching views.
6. Inspect the full DashX360 source and the Xbox 360 style guide before copying any binary assets. Port only the necessary code/resources, retain upstream attribution and follow the source repository's license. Do not copy Microsoft/Xbox branding assets without checking their provenance and redistribution terms.
7. Build and test on Windows (the available repository connector cannot execute the WPF build). Specifically test launch, keyboard navigation, XInput navigation, Guide-button toggle, hotkey fallback on LTSC, and file-browser back/launch behavior.

## Current limitations / verification status

- This document records the initial audit; it does not claim the port has been implemented or compiled.
- The official style-guide PDF is in the repository root, but its pages still need visual/text inspection before making exact typography, spacing, or asset decisions.
- Avoid global replacement of Windows Game Bar settings until there is a tested, reversible opt-in path. The default should not disable the user's existing Game Bar.
