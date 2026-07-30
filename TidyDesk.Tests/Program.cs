using System.Drawing;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using TidyDesk;

var tests = new (string Name, Action Run)[]
{
    ("product identity is TidyDesk", ProductIdentityIsTidyDesk),
    ("capacity follows region size", CapacityFollowsRegionSize),
    ("row flow fills across before down", RowFlowFillsAcrossBeforeDown),
    ("column flow fills down before across", ColumnFlowFillsDownBeforeAcross),
    ("overflow is rejected", OverflowIsRejected),
    ("placements are stable and alphabetical", PlacementsAreStableAndAlphabetical),
    ("desktop placement verification allows Explorer snapping", PlacementVerificationAllowsSnapping),
    ("Explorer grid spacing keeps columns adjacent", ExplorerGridSpacingKeepsColumnsAdjacent),
    ("monitor bounds normalize to the virtual desktop", MonitorBoundsNormalizeToVirtualDesktop),
    ("category rules match extensions and words", CategoryRulesMatchExtensionsAndWords),
    ("manual category changes override automation", ManualCategoryChangesOverrideAutomation),
    ("legacy assignments migrate to manual overrides", LegacyAssignmentsMigrateToManualOverrides),
    ("layouts round trip through JSON", LayoutsRoundTripThroughJson),
    ("undo snapshots round trip through JSON", UndoSnapshotsRoundTripThroughJson),
    ("organizer settings round trip through JSON", OrganizerSettingsRoundTripThroughJson),
    ("main filters and check all stay consistent", MainFiltersAndCheckAllStayConsistent),
    ("preview bulk assignment preserves scroll", PreviewBulkAssignmentPreservesScroll),
    ("category editor opens without new items", CategoryEditorOpensWithoutNewItems),
    ("windows render without layout errors", WindowsRenderWithoutLayoutErrors),
};

var failures = new List<string>();
foreach (var test in tests)
{
    try
    {
        test.Run();
        Console.WriteLine($"PASS  {test.Name}");
    }
    catch (Exception exception)
    {
        failures.Add($"{test.Name}: {exception.Message}");
        Console.WriteLine($"FAIL  {test.Name}: {exception.Message}");
    }
}

if (failures.Count == 0)
{
    Console.WriteLine($"{tests.Length} tests passed.");
    return 0;
}

Console.Error.WriteLine($"{failures.Count} layout test(s) failed.");
return 1;

static void ProductIdentityIsTidyDesk()
{
    AssertEqual("TidyDesk", AppPaths.ProductName);
    AssertEqual("TidyDesk", typeof(LayoutEngine).Assembly.GetName().Name!);
    AssertEqual("TidyDesk", typeof(LayoutEngine).Namespace!);
    RunInSta(
        () =>
        {
            var mainType = typeof(LayoutEngine).Assembly.GetType(
                "TidyDesk.MainForm",
                throwOnError: true)!;
            using var mainForm = (Form)Activator.CreateInstance(
                mainType,
                nonPublic: true)!;
            AssertEqual("TidyDesk", mainForm.Text);
            using var settings = new SettingsForm(new OrganizerSettings());
            AssertEqual("TidyDesk settings", settings.Text);
            using var expectedIcon = AppIcon.Create();
            AssertEqual(
                GetIconFingerprint(expectedIcon),
                GetIconFingerprint(
                    mainForm.Icon ??
                    throw new InvalidOperationException("The main form icon was missing.")));
            AssertEqual(
                GetIconFingerprint(expectedIcon),
                GetIconFingerprint(
                    settings.Icon ??
                    throw new InvalidOperationException("The settings icon was missing.")));
        });
}

static void CapacityFollowsRegionSize()
{
    var oneByOne = LayoutEngine.GetCapacity(
        new Rectangle(
            0,
            0,
            LayoutEngine.MinimumRegionWidth,
            LayoutEngine.MinimumRegionHeight));
    AssertEqual(new LayoutCapacity(1, 1), oneByOne);

    var threeByTwo = LayoutEngine.GetCapacity(
        new Rectangle(
            0,
            0,
            (LayoutEngine.CellWidth * 3) + (LayoutEngine.Padding * 2),
            (LayoutEngine.CellHeight * 2) +
            LayoutEngine.HeaderHeight +
            LayoutEngine.Padding));
    AssertEqual(new LayoutCapacity(3, 2), threeByTwo);
}

static void RowFlowFillsAcrossBeforeDown()
{
    var region = CreateRegion(IconFlow.AcrossRows, columns: 2, rows: 2);
    var positions = LayoutEngine.GetPositions(region, 3);
    AssertEqual(
        new Point(
            region.Bounds.Left + LayoutEngine.Padding,
            region.Bounds.Top + LayoutEngine.HeaderHeight),
        positions[0]);
    AssertEqual(
        new Point(
            positions[0].X + LayoutEngine.CellWidth,
            positions[0].Y),
        positions[1]);
    AssertEqual(
        new Point(
            positions[0].X,
            positions[0].Y + LayoutEngine.CellHeight),
        positions[2]);
}

static void ColumnFlowFillsDownBeforeAcross()
{
    var region = CreateRegion(IconFlow.DownColumns, columns: 2, rows: 2);
    var positions = LayoutEngine.GetPositions(region, 3);
    AssertEqual(
        new Point(
            positions[0].X,
            positions[0].Y + LayoutEngine.CellHeight),
        positions[1]);
    AssertEqual(
        new Point(
            positions[0].X + LayoutEngine.CellWidth,
            positions[0].Y),
        positions[2]);
}

static void OverflowIsRejected()
{
    var region = CreateRegion(IconFlow.AcrossRows, columns: 1, rows: 1);
    try
    {
        LayoutEngine.GetPositions(region, 2);
    }
    catch (InvalidOperationException)
    {
        return;
    }

    throw new InvalidOperationException("Expected an overflow exception.");
}

static void PlacementsAreStableAndAlphabetical()
{
    var region = CreateRegion(IconFlow.AcrossRows, columns: 2, rows: 1);
    var icons = new[]
    {
        new DesktopIconInfo { DisplayName = "Zulu", ShellIndex = 2 },
        new DesktopIconInfo { DisplayName = "Alpha", ShellIndex = 1 },
    };
    var layout = new OrganizerLayout
    {
        Regions = [region],
        Assignments = new Dictionary<string, Guid>
        {
            ["Zulu"] = region.Id,
            ["Alpha"] = region.Id,
        },
    };

    var placements = LayoutEngine.CreatePlacements(icons, layout);
    AssertEqual("Alpha", placements[0].DisplayName);
    AssertEqual("Zulu", placements[1].DisplayName);
}

static void PlacementVerificationAllowsSnapping()
{
    AssertEqual(
        true,
        DesktopShell.IsNearTarget(new Point(680, 524), new Point(640, 480)));
    AssertEqual(
        false,
        DesktopShell.IsNearTarget(new Point(730, 572), new Point(640, 480)));
}

static void ExplorerGridSpacingKeepsColumnsAdjacent()
{
    var packedSpacing = (nint)((96 << 16) | 112);
    var spacing = DesktopShell.DecodeIconSpacing(packedSpacing);
    AssertEqual(new Size(112, 96), spacing);

    var region = new RegionDefinition
    {
        Name = "Wide grid",
        Flow = IconFlow.AcrossRows,
        Bounds = new Rectangle(
            40,
            60,
            (spacing.Width * 3) + (LayoutEngine.Padding * 2),
            spacing.Height + LayoutEngine.HeaderHeight + LayoutEngine.Padding),
    };
    var positions = LayoutEngine.GetPositions(region, 3, spacing);
    AssertEqual(spacing.Width, positions[1].X - positions[0].X);
    AssertEqual(spacing.Width, positions[2].X - positions[1].X);
    AssertEqual(
        true,
        DesktopShell.IsNearTarget(
            new Point(positions[0].X + 54, positions[0].Y + 46),
            positions[0],
            spacing));
    AssertEqual(
        false,
        DesktopShell.IsNearTarget(
            new Point(positions[0].X + 58, positions[0].Y),
            positions[0],
            spacing));
}

static void MonitorBoundsNormalizeToVirtualDesktop()
{
    var virtualScreen = new Rectangle(-1920, -200, 4480, 1640);
    AssertEqual(
        new Rectangle(0, 200, 1920, 1080),
        DesktopCapture.NormalizeDisplayBounds(
            virtualScreen,
            new Rectangle(-1920, 0, 1920, 1080)));
    AssertEqual(
        new Rectangle(1920, 0, 2560, 1440),
        DesktopCapture.NormalizeDisplayBounds(
            virtualScreen,
            new Rectangle(0, -200, 2560, 1440)));
}

static void CategoryRulesMatchExtensionsAndWords()
{
    var documents = CreateRegion(IconFlow.AcrossRows, columns: 2, rows: 2);
    documents.Name = "Documents";
    var finance = CreateRegion(IconFlow.AcrossRows, columns: 2, rows: 2);
    finance.Name = "Finance";
    finance.AutoMatch = "invoice, *.qfx";
    var layout = new OrganizerLayout
    {
        Regions = [documents, finance],
    };
    var icons = new[]
    {
        new DesktopIconInfo
        {
            DisplayName = "Quarterly report",
            ShellIndex = 0,
            SourcePath = @"C:\Desktop\Quarterly report.pdf",
        },
        new DesktopIconInfo
        {
            DisplayName = "July invoice",
            ShellIndex = 1,
            SourcePath = @"C:\Desktop\July invoice.data",
        },
        new DesktopIconInfo
        {
            DisplayName = "Bank export",
            ShellIndex = 2,
            SourcePath = @"C:\Desktop\Bank export.qfx",
        },
    };

    AssertEqual(3, CategoryClassifier.Apply(layout, icons));
    AssertEqual(documents.Id, layout.Assignments["Quarterly report"]);
    AssertEqual(finance.Id, layout.Assignments["July invoice"]);
    AssertEqual(finance.Id, layout.Assignments["Bank export"]);
}

static void ManualCategoryChangesOverrideAutomation()
{
    var documents = CreateRegion(IconFlow.AcrossRows, columns: 2, rows: 2);
    documents.Name = "Documents";
    var projects = CreateRegion(IconFlow.AcrossRows, columns: 2, rows: 2);
    projects.Name = "Projects";
    var icon = new DesktopIconInfo
    {
        DisplayName = "Roadmap",
        ShellIndex = 0,
        SourcePath = @"C:\Desktop\Roadmap.pdf",
    };
    var layout = new OrganizerLayout
    {
        Regions = [documents, projects],
        Assignments = new Dictionary<string, Guid> { [icon.DisplayName] = projects.Id },
        ManualOverrides = new HashSet<string> { icon.DisplayName },
    };

    AssertEqual(0, CategoryClassifier.Apply(layout, [icon]));
    AssertEqual(projects.Id, layout.Assignments[icon.DisplayName]);

    layout.Assignments.Remove(icon.DisplayName);
    AssertEqual(0, CategoryClassifier.Apply(layout, [icon]));
    if (layout.Assignments.ContainsKey(icon.DisplayName))
    {
        throw new InvalidOperationException("A manual unassignment was overwritten.");
    }

    layout.ManualOverrides.Remove(icon.DisplayName);
    AssertEqual(1, CategoryClassifier.Apply(layout, [icon]));
    AssertEqual(documents.Id, layout.Assignments[icon.DisplayName]);
}

static void LegacyAssignmentsMigrateToManualOverrides()
{
    var categoryId = Guid.NewGuid();
    var legacy = new OrganizerLayout
    {
        SchemaVersion = 0,
        Assignments = new Dictionary<string, Guid> { ["Keep me"] = categoryId },
    };

    var migrated = LayoutStore.Normalize(legacy);
    AssertEqual(OrganizerLayout.CurrentSchemaVersion, migrated.SchemaVersion);
    if (!migrated.ManualOverrides.Contains("Keep me"))
    {
        throw new InvalidOperationException(
            "An existing user assignment was not protected during migration.");
    }
}

static void LayoutsRoundTripThroughJson()
{
    var region = CreateRegion(IconFlow.DownColumns, columns: 2, rows: 3);
    region.Name = "Projects";
    region.AutoMatch = ".sln, project";
    var layout = new OrganizerLayout
    {
        Regions = [region],
        Assignments = new Dictionary<string, Guid> { ["Roadmap"] = region.Id },
        ManualOverrides = new HashSet<string> { "Roadmap" },
    };
    var options = new JsonSerializerOptions
    {
        Converters = { new JsonStringEnumConverter() },
    };

    var json = JsonSerializer.Serialize(layout, options);
    var restored = JsonSerializer.Deserialize<OrganizerLayout>(json, options) ??
                   throw new InvalidOperationException("The layout was not restored.");
    AssertEqual("Projects", restored.Regions[0].Name);
    AssertEqual(region.Bounds, restored.Regions[0].Bounds);
    AssertEqual(IconFlow.DownColumns, restored.Regions[0].Flow);
    AssertEqual(".sln, project", restored.Regions[0].AutoMatch);
    AssertEqual(region.Id, restored.Assignments["Roadmap"]);
    if (!restored.ManualOverrides.Contains("Roadmap"))
    {
        throw new InvalidOperationException("The manual override was not restored.");
    }
}

static void UndoSnapshotsRoundTripThroughJson()
{
    var region = CreateRegion(IconFlow.AcrossRows, columns: 2, rows: 2);
    var state = new UndoState
    {
        Layout = new OrganizerLayout { Regions = [region] },
        Positions =
        [
            new IconPlacement("Roadmap", 4, new Point(320, 240)),
        ],
    };
    var options = new JsonSerializerOptions
    {
        Converters = { new JsonStringEnumConverter() },
    };

    var json = JsonSerializer.Serialize(state, options);
    var restored = JsonSerializer.Deserialize<UndoState>(json, options) ??
                   throw new InvalidOperationException("The undo snapshot was not restored.");
    AssertEqual(1, restored.Positions.Count);
    AssertEqual("Roadmap", restored.Positions[0].DisplayName);
    AssertEqual(new Point(320, 240), restored.Positions[0].Position);
    AssertEqual(region.Id, restored.Layout.Regions[0].Id);
}

static void OrganizerSettingsRoundTripThroughJson()
{
    AssertEqual(true, new OrganizerSettings().DarkMode);
    var settings = new OrganizerSettings
    {
        DarkMode = true,
        MinimizeOtherApplications = false,
        ShowDisplayBoundaries = false,
        SelectUncategorizedOnStartup = true,
    };
    var restored = OrganizerSettingsStore.Deserialize(
        OrganizerSettingsStore.Serialize(settings));
    AssertEqual(true, restored.DarkMode);
    AssertEqual(false, restored.MinimizeOtherApplications);
    AssertEqual(false, restored.ShowDisplayBoundaries);
    AssertEqual(true, restored.SelectUncategorizedOnStartup);
}

static void MainFiltersAndCheckAllStayConsistent()
{
    RunInSta(
        () =>
        {
            var assembly = typeof(LayoutEngine).Assembly;
            var mainType = assembly.GetType("TidyDesk.MainForm", throwOnError: true)!;
            using var mainForm = (Form)Activator.CreateInstance(
                mainType,
                nonPublic: true)!;
            var games = CreateRegion(IconFlow.AcrossRows, columns: 2, rows: 2);
            games.Name = "Games";
            var documents = CreateRegion(IconFlow.AcrossRows, columns: 2, rows: 2);
            documents.Name = "Documents";
            var icons = new[]
            {
                new DesktopIconInfo { DisplayName = "Game one", ShellIndex = 0 },
                new DesktopIconInfo { DisplayName = "Notes.txt", ShellIndex = 1 },
                new DesktopIconInfo { DisplayName = "Loose item", ShellIndex = 2 },
            };
            var layout = new OrganizerLayout
            {
                Regions = [games, documents],
                Assignments = new Dictionary<string, Guid>
                {
                    [icons[0].DisplayName] = games.Id,
                    [icons[1].DisplayName] = documents.Id,
                },
            };

            SetPrivateField(mainType, mainForm, "_desktopIcons", icons);
            SetPrivateField(mainType, mainForm, "_layout", layout);
            InvokePrivate(mainType, mainForm, "RefreshCategoryFilter");
            InvokePrivate(mainType, mainForm, "RebuildIconList");

            var categoryFilter =
                GetPrivateField<ComboBox>(mainType, mainForm, "_categoryFilter");
            if (!categoryFilter.Items.Cast<object>().Any(item => item.ToString() == "Games") ||
                !categoryFilter.Items.Cast<object>().Any(
                    item => item.ToString() == "Documents"))
            {
                throw new InvalidOperationException("Named categories were not filter options.");
            }

            var hideUncategorized =
                GetPrivateField<CheckBox>(mainType, mainForm, "_hideUncategorizedCheckBox");
            hideUncategorized.Checked = true;
            var iconList = GetPrivateField<ListView>(mainType, mainForm, "_iconList");
            AssertEqual(2, iconList.Items.Count);

            var checkAll = GetPrivateField<CheckBox>(mainType, mainForm, "_checkAllCheckBox");
            checkAll.Checked = true;
            AssertEqual(
                true,
                iconList.Items.Cast<ListViewItem>().All(item => item.Checked));
            checkAll.Checked = false;
            AssertEqual(
                false,
                iconList.Items.Cast<ListViewItem>().Any(item => item.Checked));
            var checkedNames = GetPrivateField<HashSet<string>>(
                mainType,
                mainForm,
                "_checkedIconNames");
            AssertEqual(0, checkedNames.Count);
        });
}

static void PreviewBulkAssignmentPreservesScroll()
{
    RunInSta(
        () =>
        {
            var icons = Enumerable.Range(0, 30)
                .Select(
                    index => new DesktopIconInfo
                    {
                        DisplayName = $"Item {index:00}.txt",
                        ShellIndex = index,
                    })
                .ToArray();
            var work = CreateRegion(IconFlow.AcrossRows, columns: 5, rows: 6);
            work.Name = "Work";
            var other = CreateRegion(IconFlow.AcrossRows, columns: 5, rows: 6);
            other.Name = "Other";
            other.Bounds = new Rectangle(
                work.Bounds.Right + 40,
                work.Bounds.Top,
                other.Bounds.Width,
                other.Bounds.Height);
            var layout = new OrganizerLayout
            {
                Regions = [work, other],
                Assignments = icons.ToDictionary(
                    icon => icon.DisplayName,
                    icon => icon.ShellIndex % 2 == 0 ? work.Id : other.Id),
                ManualOverrides = icons
                    .Select(icon => icon.DisplayName)
                    .ToHashSet(StringComparer.CurrentCultureIgnoreCase),
            };

            var designerType = typeof(LayoutEngine).Assembly.GetType(
                "TidyDesk.RegionDesignerForm",
                throwOnError: true)!;
            using var designer = (Form)Activator.CreateInstance(
                designerType,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                binder: null,
                args: [icons, icons, layout],
                culture: null)!;
            designer.StartPosition = FormStartPosition.Manual;
            designer.Location = new Point(-32_000, -32_000);
            designer.Show();
            Application.DoEvents();

            var assignmentList =
                GetPrivateField<CheckedListBox>(
                    designerType,
                    designer,
                    "_assignmentList");
            assignmentList.TopIndex = 10;
            assignmentList.SelectedIndex = 12;
            var expectedTopIndex = assignmentList.TopIndex;
            InvokePrivate(designerType, designer, "RebuildAssignmentList");
            AssertEqual(expectedTopIndex, assignmentList.TopIndex);

            var assignAll =
                GetPrivateField<Button>(designerType, designer, "_assignAllButton");
            assignAll.PerformClick();
            Application.DoEvents();
            var currentLayout =
                GetPrivateField<OrganizerLayout>(designerType, designer, "_layout");
            AssertEqual(
                true,
                icons.All(icon => currentLayout.Assignments[icon.DisplayName] == work.Id));
            designer.Hide();
        });
}

static void CategoryEditorOpensWithoutNewItems()
{
    RunInSta(
        () =>
        {
            var icons = new[]
            {
                new DesktopIconInfo { DisplayName = "Alpha", ShellIndex = 0 },
                new DesktopIconInfo { DisplayName = "Bravo", ShellIndex = 1 },
            };
            var region = CreateRegion(IconFlow.AcrossRows, columns: 2, rows: 2);
            var layout = new OrganizerLayout
            {
                Regions = [region],
                Assignments = icons.ToDictionary(icon => icon.DisplayName, _ => region.Id),
                ManualOverrides = icons
                    .Select(icon => icon.DisplayName)
                    .ToHashSet(StringComparer.CurrentCultureIgnoreCase),
            };
            using var designer = new RegionDesignerForm(
                [],
                icons,
                layout,
                darkMode: true,
                showDisplayBoundaries: false);
            designer.StartPosition = FormStartPosition.Manual;
            designer.Location = new Point(-32_000, -32_000);
            designer.Show();
            Application.DoEvents();

            var designerType = designer.GetType();
            var assignmentList = GetPrivateField<CheckedListBox>(
                designerType,
                designer,
                "_assignmentList");
            var assignAll = GetPrivateField<Button>(
                designerType,
                designer,
                "_assignAllButton");
            AssertEqual(0, assignmentList.Items.Count);
            AssertEqual(false, assignAll.Enabled);
            AssertEqual(
                false,
                GetPrivateField<ListBox>(
                        designerType,
                        designer,
                        "_categoryList")
                    .BackColor == Color.White);
            designer.Hide();
        });
}

static void WindowsRenderWithoutLayoutErrors()
{
    RunInSta(
        () =>
        {
            var outputDirectory = Path.Combine(
                Environment.CurrentDirectory,
                "TestResults");
            Directory.CreateDirectory(outputDirectory);

            var assembly = typeof(LayoutEngine).Assembly;
            var mainType = assembly.GetType("TidyDesk.MainForm", throwOnError: true)!;
            using var mainForm = (Form)Activator.CreateInstance(
                mainType,
                nonPublic: true)!;
            ThemeManager.Apply(mainForm, darkMode: false);
            RenderForm(
                mainForm,
                Path.Combine(outputDirectory, "main-window.png"));
            ThemeManager.Apply(mainForm, darkMode: true);
            RenderForm(
                mainForm,
                Path.Combine(outputDirectory, "main-window-dark.png"));

            var liveIcons = DesktopShell.GetIcons();
            if (liveIcons.Count > 0)
            {
                var livePositions = DesktopShell.GetPositions([liveIcons[0]]);
                AssertEqual(1, livePositions.Count);
            }

            var icons = new[]
            {
                new DesktopIconInfo { DisplayName = "Budget.xlsx", ShellIndex = 0 },
                new DesktopIconInfo { DisplayName = "Notes.txt", ShellIndex = 1 },
                new DesktopIconInfo { DisplayName = "Project", ShellIndex = 2 },
            };
            var region = CreateRegion(IconFlow.AcrossRows, columns: 3, rows: 2);
            region.Name = "Work";
            region.ColorArgb = Color.FromArgb(91, 104, 241).ToArgb();
            var layout = new OrganizerLayout
            {
                Regions = [region],
                Assignments = icons.ToDictionary(icon => icon.DisplayName, _ => region.Id),
                ManualOverrides = icons
                    .Select(icon => icon.DisplayName)
                    .ToHashSet(StringComparer.CurrentCultureIgnoreCase),
            };
            var designerType = assembly.GetType(
                "TidyDesk.RegionDesignerForm",
                throwOnError: true)!;
            using var designer = (Form)Activator.CreateInstance(
                designerType,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                binder: null,
                args: [icons, icons, layout],
                culture: null)!;
            RenderForm(
                designer,
                Path.Combine(outputDirectory, "region-designer.png"));
            ThemeManager.Apply(designer, darkMode: true);
            RenderForm(
                designer,
                Path.Combine(outputDirectory, "region-designer-dark.png"));

            using var settingsForm = new SettingsForm(
                new OrganizerSettings { DarkMode = true });
            RenderForm(
                settingsForm,
                Path.Combine(outputDirectory, "settings-dark.png"));

            using var multiDisplayImage = new Bitmap(3200, 1200);
            using (var graphics = Graphics.FromImage(multiDisplayImage))
            {
                graphics.Clear(Color.FromArgb(34, 43, 58));
                using var secondaryFill = new SolidBrush(Color.FromArgb(47, 55, 69));
                graphics.FillRectangle(secondaryFill, new Rectangle(0, 176, 1280, 1024));
                using var primaryFill = new SolidBrush(Color.FromArgb(28, 35, 48));
                graphics.FillRectangle(primaryFill, new Rectangle(1280, 0, 1920, 1080));
            }

            using var monitorForm = new Form
            {
                Text = "Multi-display preview rendering",
                Size = new Size(1120, 620),
            };
            var monitorPreview = new DesktopPreviewControl
            {
                Dock = DockStyle.Fill,
            };
            var monitorRegion = CreateRegion(
                IconFlow.AcrossRows,
                columns: 3,
                rows: 2);
            monitorRegion.Name = "Projects";
            monitorRegion.Bounds = new Rectangle(
                1420,
                100,
                monitorRegion.Bounds.Width,
                monitorRegion.Bounds.Height);
            monitorPreview.SetContent(
                multiDisplayImage,
                multiDisplayImage.Size,
                LayoutEngine.DefaultIconSpacing,
                [
                    new DesktopDisplayInfo(
                        new Rectangle(0, 176, 1280, 1024),
                        "Display 2",
                        false),
                    new DesktopDisplayInfo(
                        new Rectangle(1280, 0, 1920, 1080),
                        "Display 1",
                        true),
                ],
                true,
                new OrganizerLayout { Regions = [monitorRegion] },
                []);
            monitorForm.Controls.Add(monitorPreview);
            RenderForm(
                monitorForm,
                Path.Combine(outputDirectory, "multi-display-preview.png"));
        });
}

static void RunInSta(Action action)
{
    Exception? failure = null;
    var thread = new Thread(
        () =>
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
    thread.SetApartmentState(ApartmentState.STA);
    thread.Start();
    thread.Join();

    if (failure is not null)
    {
        throw new InvalidOperationException($"STA test failed: {failure}", failure);
    }
}

static T GetPrivateField<T>(Type type, object instance, string name)
{
    var field = type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic) ??
                throw new InvalidOperationException($"Field {name} was not found.");
    return (T)(field.GetValue(instance) ??
               throw new InvalidOperationException($"Field {name} was null."));
}

static void SetPrivateField(Type type, object instance, string name, object value)
{
    var field = type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic) ??
                throw new InvalidOperationException($"Field {name} was not found.");
    field.SetValue(instance, value);
}

static void InvokePrivate(Type type, object instance, string name)
{
    var method = type.GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic) ??
                 throw new InvalidOperationException($"Method {name} was not found.");
    method.Invoke(instance, null);
}

static void RenderForm(Form form, string path)
{
    form.StartPosition = FormStartPosition.Manual;
    form.Location = new Point(-32_000, -32_000);
    form.Show();
    Application.DoEvents();
    form.PerformLayout();
    using var bitmap = new Bitmap(form.ClientSize.Width, form.ClientSize.Height);
    form.DrawToBitmap(bitmap, form.ClientRectangle);
    bitmap.Save(path);
    form.Hide();

    if (new FileInfo(path).Length < 1_000)
    {
        throw new InvalidOperationException($"{Path.GetFileName(path)} was unexpectedly empty.");
    }
}

static RegionDefinition CreateRegion(IconFlow flow, int columns, int rows) =>
    new()
    {
        Name = "Test",
        Flow = flow,
        Bounds = new Rectangle(
            40,
            60,
            (LayoutEngine.CellWidth * columns) + (LayoutEngine.Padding * 2),
            (LayoutEngine.CellHeight * rows) +
            LayoutEngine.HeaderHeight +
            LayoutEngine.Padding),
    };

static void AssertEqual<T>(T expected, T actual)
    where T : notnull
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new InvalidOperationException(
            $"Expected {expected}, received {actual}.");
    }
}

static int GetIconFingerprint(Icon icon)
{
    using var bitmap = icon.ToBitmap();
    var hash = new HashCode();
    hash.Add(bitmap.Width);
    hash.Add(bitmap.Height);
    for (var y = 0; y < bitmap.Height; y++)
    {
        for (var x = 0; x < bitmap.Width; x++)
        {
            hash.Add(bitmap.GetPixel(x, y).ToArgb());
        }
    }

    return hash.ToHashCode();
}
