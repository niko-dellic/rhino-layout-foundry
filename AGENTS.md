# Rhino Layout Foundry contributor guidance

## Scope and safety

- These instructions apply to the entire repository.
- Preserve user and concurrent-agent changes. Do not revert unrelated work in a dirty tree.
- Keep document mutation, layout generation, import, and persistence behavior separate from presentation-only work unless the task explicitly requests behavior changes.
- Leave operating-system context menus, file pickers, color dialogs, and message boxes native. Foundry-owned triggers and surrounding surfaces must still use the Foundry design system.

## UI design system

Foundry uses a restrained, shadcn-inspired system adapted to Eto.Forms and Rhino dark/light themes. The authoritative implementation is in `src/RhinoLayoutFoundry.UI`:

- `FoundryTheme.cs` owns all colors, spacing, and semantic surfaces. Add or reuse semantic tokens here; do not scatter literal UI colors through dialogs.
- `FoundryDialogButton.cs` is the standard text action.
- `FoundryToolbarIconButton.cs` is the standard 32 × 32 icon action.
- `FoundryToolbarButtonGroup.cs` is the shared capsule for mutually exclusive toolbar modes.
- `FoundryFormField.cs` wraps text boxes, text areas, selects, and numeric inputs.
- `FoundryToolbarField.cs` adapts fields to 32px toolbar rows.
- `FoundryCheckBox.cs`, `FoundryColorField.cs`, and `FoundrySlider.cs` provide custom controls where native styling conflicts with the system.
- `FoundryDialogActions.cs` standardizes dialog action placement.
- `FoundryViewIcons.cs` and `FoundryHierarchyIcons.cs` provide resolution-independent icon drawing.

When adding or changing UI:

1. Reuse a shared control before creating a new native `Button`, `ColorPicker`, `Slider`, or ad-hoc field shell.
2. If a genuinely new interaction is needed, create one reusable internal Foundry component with complete rest, hover, pressed, focus, disabled, and keyboard states.
3. Single-line controls and toolbar buttons are 32px high. Standard borders are 1px, radii are 6px, and spacing comes from `FoundryTheme.Space*`.
4. Buttons use the quiet outlined/ghost treatment. Routine and primary actions are not filled. Destructive actions use red text/border and a faint red hover tint, never a solid red fill.
5. Toolbar buttons need an opaque neutral resting surface because they can overlap white canvas sheets. Active controls use a neutral dark surface (`#151515` in dark mode), not a blue underline or saturated fill.
6. Mutually exclusive view toggles belong in one shared background capsule; only the active segment gets its own border.
7. Separate toolbar groups with a 1 × 20px `CanvasBorder` rule and `Space1` gaps.
8. Fields use a quiet surface, legible value text, a low-contrast border, internal padding, and a neutral focus ring. Search icons stay inside the field and never overlap the caret.
9. Interactive preview cards may keep larger geometry but must reuse the same border, hover, focus, selected, and disabled tokens.
10. Use neutral hierarchy/zebra surfaces for differentiation. Preserve native selected-row colors over zebra backgrounds.
11. Keep the existing blue selection accent only for genuine selection affordances such as canvas lasso/selection; do not use it for routine button or field focus chrome.
12. Verify every new component in both Rhino dark and light themes and at Retina/high-DPI scale. Prefer vector/custom-drawn icons over raster assets.

## Keyboard and accessibility contract

- Preserve Tab traversal and visible neutral focus rings.
- Enter/Space activate buttons and color triggers.
- Escape closes or cancels the current editor/dialog where applicable.
- Arrow keys operate selects, numeric controls, and sliders; Home/End should reach slider bounds.
- Space toggles checkboxes.
- Disabled controls remain readable but visibly lower contrast.
- Provide useful tooltips for icon-only controls.

## Validation and development install

Use the installed .NET SDK and avoid overwriting a Rhino-loaded build output when Rhino is running.

```bash
dotnet build src/RhinoLayoutFoundry.UI/RhinoLayoutFoundry.UI.csproj --no-restore -p:UseSharedCompilation=false
dotnet test tests/RhinoLayoutFoundry.Core.Tests/RhinoLayoutFoundry.Core.Tests.csproj --no-restore -p:RunAnalyzers=false --no-build
```

Build the Rhino host to an isolated `BaseOutputPath`, assemble the required `.rhp`, UI, Core, `.deps.json`, and `.runtimeconfig.json` files into `src/RhinoLayoutFoundry.Rhino/bin/<Configuration>/net8.0`, then run:

```bash
./scripts/install-dev-macos.sh Debug
```

Before handoff:

- Require zero build warnings and errors.
- Run the existing core suite.
- Run `git diff --check`.
- Compare SHA-256 hashes between the assembled and installed RHP/UI/Core binaries.
- Tell the user to fully quit and reopen Rhino after installing a changed bundle.
