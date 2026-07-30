# TidyDesk

TidyDesk is a Windows desktop app for arranging icons into visual, named
regions without moving the underlying files into folders.

## How it works

1. Start the app and select the desktop icons you want to arrange.
   Use search to narrow the list, **Select uncategorized** for a quick cleanup,
   **Hide categorized icons** or **Hide uncategorized icons** to focus the list,
   or use the **Category** menu to show one named category such as Games,
   Launchers, Documents, or Videos. **Check all** applies to the visible list;
   unchecking it clears the entire selection.
2. Choose **Organize selected**.
   Other open applications are minimized while the preview is active so the
   desktop is unobstructed. They are restored when the preview closes.
   Use **Edit categories** when you only want to create, rename, move, resize,
   or delete category regions without bringing a new selection into the editor.
3. Create categories in the desktop preview.
   On multi-display desktops, a high-contrast labeled border marks the exact
   bounds of every screen, including screens positioned left of or above the
   primary display.
4. Drag each category to the desired area and resize its white corner handle.
   The region size determines how many icon columns and rows it contains.
5. Choose whether icons flow across rows or down columns, and check which icons
   belong to the selected category.
   **Assign all** adds every icon selected on the main window to the current
   category in one click, while **Clear** removes those icons from it.
   The **Auto-add matches** field accepts extensions and words such as
   `.pdf, .docx, invoice`. Common category names such as Documents, Images,
   Videos, Music, Archives, Spreadsheets, Presentations, Code, Installers, and
   Apps also provide built-in extension matching.
6. Choose **Apply layout**.

The app verifies the final position of every icon and retries any that Explorer
does not place on the first attempt. Occupied regions cannot overlap, and the
preview remains open if Explorer still leaves an icon behind so Apply can be
retried safely.

Column and row spacing comes from the live Windows Explorer desktop grid rather
than a hard-coded size. Custom icon spacing, display scaling, and DPI settings
therefore keep adjacent preview cells adjacent on the real desktop.

Layouts and category assignments are saved under the current user's local app
data. Applying a layout changes only the icon positions exposed by Windows
Explorer; it does not rename or move any desktop files.

TidyDesk runs locally and does not include network or telemetry code.

Automatically matched icons are marked with **(auto)** in the category editor.
Manually moving an icon to another category—or manually unassigning it—creates
an override, so future automatic matching will not change that choice. Select an
overridden icon and choose **Use automatic rules** to opt it back into matching.

After applying a layout, **Undo last** restores both the previous desktop icon
coordinates and the previous category assignments. The undo snapshot survives
an app restart and is replaced by the next successful Apply.

Useful shortcuts:

- `Ctrl+F` focuses icon search.
- `Ctrl+A` selects all currently visible icons.
- `Ctrl+Z` runs **Undo last** when an undo snapshot is available.
- `Ctrl+E` opens **Edit categories**.
- `Ctrl+,` opens **Settings**.
- `F5` refreshes the desktop inventory.
- Double-clicking an icon row toggles its selection.

The **Settings** window includes dark mode, optional preview minimization,
display-boundary visibility, and automatic startup selection of uncategorized
icons. Settings are saved alongside the layout under local application data.

## Requirements

- Windows
- .NET 10 SDK when building from source

## Build and verify

```powershell
dotnet build '.\TidyDesk.slnx' --configuration Release
dotnet run --project '.\TidyDesk.Tests\TidyDesk.Tests.csproj' --configuration Release
```

The app targets .NET 10 for Windows and uses Windows Forms.

## Author

KKCKobra
