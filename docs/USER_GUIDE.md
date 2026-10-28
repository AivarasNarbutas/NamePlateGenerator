# Name Plate Generator - setup and user guide

Name Plate Generator makes a 3D-printable **name plate**, its **text** and a matching **holder (stand)**.
You set sizes, text, fonts and the holder shape; it exports files for your slicer.
It is modelled on the "Name plate with a holder" design: a plate with a text pocket, loose letters that are
pressed into the pocket (so a single-colour printer can make a two-colour plate), and a stand.

---

## 1. Setup

**Requirements:** Windows 10 or 11. Nothing else is needed to *use* a published copy; to build from source you
need the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) or newer.

### Option A - run from source (developers)

```
git clone https://github.com/AivarasNarbutas/NamePlateGenerator.git
cd NamePlateGenerator
dotnet run --project src/NamePlate.App
```

The first run downloads two small packages (HelixToolkit, Clipper2, LibTess) from nuget.org.

### Option B - make a copy that runs without the .NET SDK

```
dotnet publish src/NamePlate.App -c Release -r win-x64 --self-contained -o publish
```

Copy the whole `publish` folder (about 160 MB) to any Windows PC and start `NamePlateGenerator.exe`.
Do not copy the `.exe` alone; it needs the files beside it.

### Check that everything works (optional)

```
dotnet run --project tests/NamePlate.Tests
```

It prints `ALL ... PASSED` when the geometry, file export/import and the editors behave as expected.

---

## 2. The window at a glance

| Area | What it is |
|---|---|
| **Left, tab "Plate and text"** | Plate size, the text editors, default font, placement, text depth. |
| **Left, tab "Holder"** | The stand: template, clearance, a 2D profile editor and the list of profile points. |
| **Left, tab "Files"** | Import, Reset and Export. |
| **Right** | Live 3D preview. It updates about a quarter of a second after you stop typing. |
| **Under the preview** | A status line (sizes, triangle count) and any **warnings** in red. |

**Preview controls:** right-drag rotates, middle-drag pans, mouse wheel zooms, the cube in the corner snaps to
standard views. Buttons above the preview: **Front**, **Back**, **Iso**, **Fit**. **Show holder** hides the
stand in the preview only. **Print layout** shows the parts laid out as they are exported.

**Entering numbers:** type a value and press **Enter** or click elsewhere. Both `2.5` and `2,5` work. A red
border means the text is not a number and the old value is kept.

---

## 3. Making a plate - step by step

1. **Plate size** (tab "Plate and text" -> Plate). Defaults match the original design: 175 x 40 mm,
   2.975 mm thick, 0.5 mm chamfer on the long edges. "Chamfer the short ends too" is optional.
2. **Type the text** in the **Front** editor. Press Enter for a new line.
3. **Choose where the text goes** with **Text on** (Front, Back or Both). See section 4 for the back side.
4. **Check the preview.** Fix anything red under it (section 8).
5. **Export** (tab "Files", section 6) and print.

### Fonts

- **Default font** (box below the editors): the font used wherever you have not formatted the text yourself.
  Pick from the list or type a name and press Enter. The list uses the same names as Word, including weights
  such as "Arial Black" or "Segoe UI Light". Bold and Italic are simulated when the font has no such face.
- **Font file:** the **...** button picks a `.ttf` or `.otf` file instead; **x** goes back to the installed font.
- **Size (mm)** is the font's em height; capital letters come out about 70 % of it.
  **Auto-fit** scales all text to fill the plate inside the margins.
- **Save as my default font:** remembers family, file, size, bold, italic and line spacing. New plates and
  **Reset** start with them. (Stored in `%APPDATA%\NamePlateGenerator\defaults.json`.)

### Formatting single characters (like in Word)

Select characters in a text editor, then use the bar above it:

| Control | Effect |
|---|---|
| Font box | Font of the selection. Applies when you pick from the open list or press Enter. |
| Size box + Enter | Size in mm of the selection. |
| **B** / **I** (or Ctrl+B / Ctrl+I) | Bold / italic. |
| **Clear** | Removes all your formatting from the selection (it follows the default font again). |

Underline, alignment, lists and similar shortcuts are switched off because the plate cannot show them.
Pasting inserts plain text only.

### Placement

Horizontal / Vertical put the text block on the plate. **Lines** aligns the lines among themselves.
**Edge margin** is the minimum distance to the plate edge (at least chamfer + 0.4 mm is always kept).
**Offset X / Y** nudge the text; **Line spacing** is in the default-font box.

### Text depth and mode

| Mode | Result |
|---|---|
| **Inlay** | A pocket in the plate **and** separate letters to press into it (the original method). |
| **Engrave** | A pocket only. |
| **Emboss** | Raised letters, one body. |

- **Pocket depth:** how deep the pocket is (default 1.5 mm; a 0.4 mm floor is always kept).
- **Text height:** height of the loose letters (Inlay) or of raised letters (Emboss).
- **Fit clearance:** the letters are made this much smaller on every side so they press in (default 0.08 mm,
  the value found to fit tightly in the original design). Adjust it if your printer fits tighter or looser.
- **Print gap:** distance between the parts in the 3MF.

---

## 4. Text on the back side

1. Set **Text on** to **Back** or **Both**.
2. Under "Back", **untick "Same text as the front"**. The back editor becomes active, starting with a copy of
   the front text.
3. Type the back text. You read it as you look at the back; the program mirrors it correctly for the plate.

A grey hint under "Back" always says which step is missing. Letters for the back are exported flipped so their
visible side faces up on the print bed. Raised (Emboss) text on the back cannot lie flat; you get a warning.

---

## 5. The holder (stand)

The holder is a **2D cross-section** (a polygon of points) that the program extrudes over the plate length.
Every point is stored relative to the plate - "X from Front/Back face, Y from Bottom/Top edge" - so **when you
change the plate's thickness, height or length, the holder follows**.

Tab **Holder**:

- **Make a holder:** tick to export it with the plate.
- **Clearance:** gap between plate and slot (default 0.025 mm). **Extra length:** holder length = plate length
  + this (negative makes it shorter).
- **Template + Apply:** *ChannelStand* is the original stand (the plate slides in from the side, lips hold its
  edges, a leg leans it back). *SlotBase* is a low block with an upright slot. Applying replaces your points.
- **2D profile view:** the yellow bar is the plate seen from the end (front face on the left). Click a point
  to select it; drag the **red** arrow (X) or **green** arrow (Y) to move it. Right-drag pans, the wheel zooms,
  **Fit** frames everything.
- **Profile points list:** edit X/Y numbers directly. "X from"/"Y from" choose which face or edge the number is
  measured from; switching keeps the point where it is.
- **Add point** inserts a point halfway after the selected one; **Delete point** removes it (at least 3 remain).

Warnings appear if the outline crosses itself, overlaps the plate, or the holder's lips hide part of the text.

---

## 6. Exporting for printing

Tab **Files**:

1. Tick the formats: **3MF** (all parts in one file), **STL** (one file per part), **Project (.json)** (the
   settings, so you can edit later).
2. Click **Export...** and choose a base name, e.g. `JonasJonaitis`.

| File | Contents |
|---|---|
| `Name.3mf` | Plate, loose letters and holder laid out for printing, plus your settings (re-importable). |
| `Name_plate.stl` | The plate with the pocket(s), lying flat, front face up. |
| `Name_letters_front.stl`, `Name_letters_back.stl` | Loose letters, flat, visible side up. |
| `Name_holder.stl` | The holder **standing on its end** so it prints without supports. |
| `Name.nameplate.json` | The settings only. |

If a file already exists you are asked before it is replaced.

### Printing tips

- Print the **plate and the letters as separate objects**, then press the letters into the pocket (a little
  glue helps). Use another colour for the letters.
- No supports are needed for the plate or the letters; the holder is oriented for support-free printing.
- If letters are too tight or too loose, change **Fit clearance** and export again.
- The 3MF has not yet been tried in every slicer. If yours does not open it correctly, use the STL files.

---

## 7. Editing later: Import, Reset

- **Import...** opens a `.nameplate.json` or a `.3mf` that this program exported - everything (sizes, formatted
  text, holder points) comes back, so you can change one thing and export again.
  An STL cannot be imported (it carries no settings).
- **Reset** returns every setting and the text to the defaults (your saved default font, if you made one).
  It asks first.

---

## 8. Messages and what to do

| Message (red, under the preview) | What to do |
|---|---|
| "... text does not fit inside the plate and was clipped" | Lower the size, tick **Auto-fit**, or enlarge the plate. |
| "Font ... is not installed" / "Font file not found" | Pick another font, or restore the file. A substitute font is used meanwhile. |
| "Pocket depth limited ... to keep a 0.4 mm floor" | The plate is too thin for that depth (less than half the thickness with text on both sides). Use a thicker plate or a smaller depth. |
| "Letters are ... taller than the pocket and will stand proud" | Text height is larger than the pocket depth; lower it unless you want that. |
| "The holder hides part of the front/back text" | Move the text with Edge margin or Offset Y, or change the holder profile. |
| "The holder profile overlaps the plate" | A point lies inside the plate. Move it out or raise the clearance. |
| "The holder outline crosses itself" | Points are in the wrong order. Reapply the template, or fix the point order. |
| "The fit clearance removes ... of the letter area" | Clearance is too big for thin strokes; lower it or use a bolder font. |

Other problems:

- **A number does not change:** press Enter or click out of the box; a red border means it is not a number.
- **Preview looks tiny or off-centre:** click **Fit**.
- **The back text editor is grey:** see section 4.
- **Something else:** start the program with the environment variable `NAMEPLATE_BINDING_LOG` set to a file path
  to record interface errors, and send that file along with a description of what you did.
