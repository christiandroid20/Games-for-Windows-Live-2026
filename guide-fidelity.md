# Guide fidelity notes (reference: `{4938…}` target screenshot, 1920×1080)

The Guide is drawn in an 830×1080 design space (`GuideChrome`, `GuideUi`, `Overlay`) inside a `Viewbox`,
so it scales uniformly with screen height like the original.

## Measured from the reference (not eyeballed)

| Element | Value |
|---|---|
| Body gold | `#F5C41F` → `#FDD53A` (centre) → `#E3B725` (right), horizontal |
| Header band (0–141 px) | `#4B431A` → `#756516` → `#A78C0D` → `#C8A40E` → `#E9B818`, cream outline `#E8E9CA`, rounded lower-right corner |
| Footer band (858–1080 px) | `#3F3B1E` → `#655819` → `#867213` → `#A78C0D` → `#BC9C0E` → `#D1AB0E` → `#DDB510` |
| Edge | dark rule `#5B552E` (4 px), cream rule `#FAF2B9` (4 px, +8 px), outer outline `#46432F`; point tables in `GuideChrome` |
| Player tab shoulder | starts y≈130, outer x≈823, label runs top-to-bottom at x≈770 |
| Content column | x 154–709: gamer card 165–286, Community 303–419, rows 62 px, media box 572–709, logo plate 727–840 |
| Other tabs | dark `#3A3A3A–#454545` strips on the right edge, ring body `#343536` |

A Python re-render of the same point tables matched the reference outer edge within 1–3 px for the full height.

## Known differences

- Not yet compared against a real Windows screenshot (the build was authored without a Windows runtime).
- Typeface is Segoe UI; the console used a different face.
- No avatar render, no Xbox/Windows logo artwork: the plate shows an original orb + wordmark, or `brand.png` placed next to the executable.
- Community counters, Gamerscore and online status are intentionally not simulated (offline profile).
- Shuffle button is shown disabled (Windows has no global shuffle key).
- Receding tabs are simplified rectangles with a rounded top-left corner.
