# Name Plate Generator (C# / WPF)

Generates a name plate and its text as printable meshes, modelled on the reference
"[Name plate with a holder](https://www.printables.com/model/1017709-name-plate-with-a-holder)" on Printables (blankplate.step, 175 x 40 x 2.975 mm, 0.5 mm chamfer on the long edges,
text pocket 1.5 mm, letters inset by 0.08 mm and pressed into the plate).

**Just want to use it?** Download the zip from the [latest release](https://github.com/AivarasNarbutas/NamePlateGenerator/releases/latest),
right-click it > **Extract All...**, then double-click `NamePlateGenerator.exe`. Windows 10/11, nothing to install.
Step-by-step instructions: [setup and user guide](docs/USER_GUIDE.md).

The 3D preview uses HelixToolkit.Wpf (right-drag rotate, middle-drag pan, wheel zoom, view cube).

Build / run (needs the .NET 8 SDK or newer, Windows):

    dotnet run --project src/NamePlate.App
    dotnet run --project tests/NamePlate.Tests      # all checks; exit code 0 = pass

Like it? [Buy me a coffee](https://buymeacoffee.com/AivarasNarbutas) - the same link is in the program's bottom bar.

## Structure (MVVM)
- `src/NamePlate.Core` - model: settings, text outlines, mesh building, STL/3MF/JSON files. No UI.
- `src/NamePlate.App`
  - `ViewModels/MainViewModel` - every setting as a bindable property, commands (Import, Export, Reset, font file),
    status and warnings. Rebuilds on a worker thread, debounced; an older build is cancelled by a newer edit.
  - `Services/IDialogService` - file pickers and message boxes behind an interface (tests use a fake).
  - `Controls/FormattedTextEditor` - the rich-text editor; its `Runs` property binds to the view-model.
  - `Converters` - tolerant number input (`2,5` and `2.5`), 3D scene, font resolution.
  - `MainWindow` - XAML bindings only; its code-behind just moves the Helix camera (pure view state).
- `tests/NamePlate.Tests` - geometry, edge cases, file round trips, view-model and editor tests.
  Set `NAMEPLATE_BINDING_LOG=<file>` before starting the app to record WPF binding errors.

## What you can set
- Plate: length, height, thickness, chamfer (optionally also on the short ends).
- Text: front and back text (or "same as front") in a small rich-text editor. Select characters and set their font, size (mm), bold and italic individually, like in Word; "Clear" returns them to the defaults.
- Default font: any installed font or a .ttf/.otf file, bold, italic, size (mm, em height), auto-fit, line spacing.
  The font lists use Word's names, weights included ("Arial Black", "Segoe UI Light", "Arial Rounded MT Bold");
  bold/italic are simulated when a font has no such face. A font is applied when picked from the open list or
  confirmed with Enter, not while typing or scrolling through the list.
  "Save as my default font" stores it in `%APPDATA%\NamePlateGenerator\defaults.json`; new plates and Reset start with it.
- Back text: set "Text on" to Back or Both, untick "Same text as the front", then type into the Back editor.
- Placement: horizontal/vertical alignment on the plate, line alignment, edge margin, X/Y offset.
- Faces: Front, Back or Both. The back text is mirrored for you so it reads correctly from the back.
- Mode: Inlay (pocket + separate letters, the reference method), Engrave (pocket only), Emboss (raised letters, one body).
- Pocket depth, text height, fit clearance (letters made smaller by this much per side), print gap.

## Holder (stand)
The holder is a 2D cross-section (a list of points) extruded over the plate length, like the part outlines in the
RoboPack parts configurator. Each point is stored relative to the plate: X from the Front or Back face, Y from the
Bottom or Top edge (measured from the slot wall = plate surface + clearance). Changing the plate thickness, height
or length therefore resizes the holder; "Extra length" makes it longer or shorter than the plate.

- Templates: ChannelStand (the reference stand.stl: the plate slides in from the side, lips hold its edges, a leg
  leans it back) and SlotBase (the plate stands upright in a slot).
- Edit points in the list or in the 2D view: click a point, drag the red (X) / green (Y) arrow. Add inserts a point
  halfway to the next one; Delete keeps at least 3.
- Warnings: self-crossing outline, profile overlapping the plate, lips hiding the text.

## Files
Export writes next to the chosen base name `<name>`:
- `<name>.3mf` - plate, letters and holder laid out for printing, settings embedded (re-importable)
- `<name>_plate.stl`, `<name>_letters_front.stl`, `<name>_letters_back.stl` - separate objects, flat, visible side up
- `<name>_holder.stl` - the holder, standing on its end (prints without supports)
- `<name>.nameplate.json` - the settings

Import accepts the `.nameplate.json` or the `.3mf` exported by this program. An STL has no parameters, so it cannot be re-imported.

## Notes
- Meshes are built directly from the text outlines (no CAD kernel); the checks confirm they are closed and consistently oriented.
- Text always keeps at least chamfer + 0.4 mm from the plate edge; pockets always keep a 0.4 mm floor.
- Values out of range are corrected and listed as warnings under the preview, never silently.
- Pasting into the text editor inserts plain text (formatting from Word or web pages is not taken over).
- Back letters are exported rotated so their visible side faces up on the bed.
- Not yet tried in a slicer: check the 3MF opens in your slicer; the STLs are the fallback.
