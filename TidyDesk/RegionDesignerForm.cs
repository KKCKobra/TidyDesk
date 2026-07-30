using IconLayout = TidyDesk.LayoutEngine;

namespace TidyDesk;

internal sealed class RegionDesignerForm : Form
{
    private static readonly Color[] CategoryColors =
    [
        Color.FromArgb(91, 104, 241),
        Color.FromArgb(20, 168, 145),
        Color.FromArgb(235, 126, 54),
        Color.FromArgb(211, 78, 132),
        Color.FromArgb(119, 90, 205),
        Color.FromArgb(42, 144, 212),
    ];

    private readonly IReadOnlyList<DesktopIconInfo> _icons;
    private readonly IReadOnlyList<DesktopIconInfo> _allIcons;
    private readonly Size _desktopSize;
    private readonly Size _iconSpacing;
    private readonly Bitmap _desktopImage;
    private readonly IReadOnlyList<DesktopDisplayInfo> _displays;
    private readonly OrganizerLayout _layout;
    private readonly OrganizerLayout _originalLayout;
    private readonly DesktopPreviewControl _preview = new();
    private readonly ListBox _categoryList = new();
    private readonly TextBox _nameTextBox = new();
    private readonly ComboBox _flowComboBox = new();
    private readonly TextBox _autoMatchTextBox = new();
    private readonly Label _capacityLabel = new();
    private readonly CheckedListBox _assignmentList = new();
    private readonly Button _assignAllButton = new();
    private readonly Button _clearCategoryButton = new();
    private readonly Button _useAutomaticButton = new();
    private readonly Button _deleteButton = new();
    private readonly Button _applyButton = new();
    private readonly Label _footerStatus = new();
    private readonly Dictionary<string, IconPlacement> _originalPositions =
        new(StringComparer.CurrentCultureIgnoreCase);
    private readonly HashSet<string> _undoSnapshotAttempts =
        new(StringComparer.CurrentCultureIgnoreCase);
    private bool _updatingEditor;
    private bool _updatingAssignments;
    private bool _syncingSelection;

    public RegionDesignerForm(
        IReadOnlyList<DesktopIconInfo> icons,
        IReadOnlyList<DesktopIconInfo> allIcons,
        OrganizerLayout sourceLayout)
        : this(
            icons,
            allIcons,
            sourceLayout,
            darkMode: false,
            showDisplayBoundaries: true)
    {
    }

    public RegionDesignerForm(
        IReadOnlyList<DesktopIconInfo> icons,
        IReadOnlyList<DesktopIconInfo> allIcons,
        OrganizerLayout sourceLayout,
        bool darkMode,
        bool showDisplayBoundaries)
    {
        _icons = icons;
        _allIcons = allIcons;
        _layout = sourceLayout.Clone();
        _originalLayout = sourceLayout.Clone();
        _desktopSize = SystemInformation.VirtualScreen.Size;
        _iconSpacing = DesktopShell.GetIconSpacing();
        _desktopImage = DesktopCapture.CaptureVirtualDesktop();
        _displays = DesktopCapture.GetDisplays();

        Text = "Choose desktop regions";
        Icon = AppIcon.Create();
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(1040, 680);
        Size = new Size(1360, 860);
        BackColor = Color.FromArgb(13, 18, 27);
        ForeColor = Color.White;
        Font = new Font("Segoe UI", 9F);

        BuildInterface();
        ThemeManager.Apply(this, darkMode);
        CategoryClassifier.Apply(_layout, _allIcons);
        PopulateCategories();
        _preview.SetContent(
            _desktopImage,
            _desktopSize,
            _iconSpacing,
            _displays,
            showDisplayBoundaries,
            _layout,
            _allIcons);
        UpdateApplyState();
    }

    public OrganizerLayout ResultLayout { get; private set; } = new();

    public ApplyResult ApplyResult { get; private set; } =
        new(0, Array.Empty<string>());

    public bool UndoAvailable { get; private set; }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _desktopImage.Dispose();
            Icon?.Dispose();
        }

        base.Dispose(disposing);
    }

    private RegionDefinition? SelectedRegion =>
        _categoryList.SelectedItem as RegionDefinition;

    private void BuildInterface()
    {
        var header = new Panel
        {
            Dock = DockStyle.Top,
            Height = 82,
            BackColor = Color.FromArgb(18, 24, 35),
            Padding = new Padding(28, 16, 28, 10),
        };
        var title = new Label
        {
            AutoSize = true,
            Text = "Design your desktop regions",
            Font = new Font("Segoe UI", 18, FontStyle.Bold),
            ForeColor = Color.White,
            Location = new Point(27, 15),
        };
        var subtitle = new Label
        {
            AutoSize = true,
            Text =
                "Bright borders mark each display. Drag a category and resize its white corner.",
            Font = new Font("Segoe UI", 9.5f),
            ForeColor = Color.FromArgb(173, 185, 204),
            Location = new Point(30, 50),
        };
        header.Controls.AddRange([title, subtitle]);

        var footer = BuildFooter();

        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            FixedPanel = FixedPanel.Panel2,
            SplitterWidth = 1,
            BackColor = Color.FromArgb(43, 51, 65),
        };
        split.SizeChanged += (_, _) =>
        {
            var available = split.ClientSize.Width - split.SplitterWidth;
            if (available < 60)
            {
                return;
            }

            var desired = Math.Max(360, Math.Min(410, split.ClientSize.Width / 3));
            var minimum = available >= 910 ? 550 : 25;
            var maximum = available - 25;
            split.SplitterDistance = Math.Clamp(
                available - desired,
                Math.Min(minimum, maximum),
                maximum);
        };

        _preview.Dock = DockStyle.Fill;
        _preview.SelectedRegionChanged += PreviewSelectionChanged;
        _preview.RegionBoundsChanged += (_, _) =>
        {
            UpdateCapacityLabel();
            UpdateApplyState();
        };
        split.Panel1.Controls.Add(_preview);
        split.Panel2.BackColor = Color.FromArgb(246, 248, 252);
        split.Panel2.Controls.Add(BuildEditorPanel());

        Controls.Add(split);
        Controls.Add(footer);
        Controls.Add(header);
    }

    private Control BuildEditorPanel()
    {
        var editor = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(246, 248, 252),
            ForeColor = Color.FromArgb(26, 34, 48),
            Padding = new Padding(20, 18, 20, 16),
            ColumnCount = 1,
            RowCount = 7,
        };
        editor.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        editor.RowStyles.Add(new RowStyle(SizeType.Absolute, 112));
        editor.RowStyles.Add(new RowStyle(SizeType.Absolute, 43));
        editor.RowStyles.Add(new RowStyle(SizeType.Absolute, 212));
        editor.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        editor.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        editor.RowStyles.Add(new RowStyle(SizeType.Absolute, 74));

        var categoriesTitle = new Label
        {
            Text = "Categories",
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 11, FontStyle.Bold),
            ForeColor = Color.FromArgb(24, 32, 47),
            TextAlign = ContentAlignment.MiddleLeft,
        };
        editor.Controls.Add(categoriesTitle, 0, 0);

        _categoryList.Dock = DockStyle.Fill;
        _categoryList.BorderStyle = BorderStyle.FixedSingle;
        _categoryList.Font = new Font("Segoe UI", 10);
        _categoryList.IntegralHeight = false;
        _categoryList.SelectedIndexChanged += CategorySelectionChanged;
        editor.Controls.Add(_categoryList, 0, 1);

        var categoryButtons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(0, 6, 0, 0),
        };
        var addButton = CreateEditorButton("+  New category", primary: true);
        addButton.Width = 142;
        addButton.Click += (_, _) => AddCategory();
        _deleteButton.Text = "Delete";
        _deleteButton.Size = new Size(82, 32);
        _deleteButton.FlatStyle = FlatStyle.Flat;
        _deleteButton.BackColor = Color.White;
        _deleteButton.ForeColor = Color.FromArgb(177, 57, 70);
        _deleteButton.Click += (_, _) => DeleteSelectedCategory();
        categoryButtons.Controls.AddRange([addButton, _deleteButton]);
        editor.Controls.Add(categoryButtons, 0, 2);

        editor.Controls.Add(BuildCategoryEditor(), 0, 3);

        var assignmentTitle = new Label
        {
            Text = "Icons in this category",
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 10.5f, FontStyle.Bold),
            ForeColor = Color.FromArgb(24, 32, 47),
            TextAlign = ContentAlignment.BottomLeft,
            Padding = new Padding(0, 0, 0, 8),
        };
        editor.Controls.Add(assignmentTitle, 0, 4);

        _assignmentList.Dock = DockStyle.Fill;
        _assignmentList.BorderStyle = BorderStyle.FixedSingle;
        _assignmentList.CheckOnClick = true;
        _assignmentList.IntegralHeight = false;
        _assignmentList.ItemCheck += AssignmentChecked;
        _assignmentList.SelectedIndexChanged += (_, _) =>
        {
            _useAutomaticButton.Enabled =
                _assignmentList.SelectedItem is AssignmentEntry { IsManual: true };
        };
        editor.Controls.Add(_assignmentList, 0, 5);

        var assignmentActions = new Panel
        {
            Dock = DockStyle.Fill,
        };
        _assignAllButton.Text = "Assign all";
        _assignAllButton.Size = new Size(94, 30);
        _assignAllButton.Location = new Point(0, 7);
        _assignAllButton.FlatStyle = FlatStyle.Flat;
        _assignAllButton.BackColor = Color.FromArgb(91, 104, 241);
        _assignAllButton.ForeColor = Color.White;
        _assignAllButton.Click += AssignAllToSelectedCategory;

        _clearCategoryButton.Text = "Clear";
        _clearCategoryButton.Size = new Size(76, 30);
        _clearCategoryButton.Location = new Point(100, 7);
        _clearCategoryButton.FlatStyle = FlatStyle.Flat;
        _clearCategoryButton.BackColor = Color.White;
        _clearCategoryButton.ForeColor = Color.FromArgb(68, 78, 98);
        _clearCategoryButton.Click += ClearSelectedCategory;

        _useAutomaticButton.Text = "Use automatic";
        _useAutomaticButton.Size = new Size(112, 30);
        _useAutomaticButton.Location = new Point(182, 7);
        _useAutomaticButton.FlatStyle = FlatStyle.Flat;
        _useAutomaticButton.BackColor = Color.White;
        _useAutomaticButton.ForeColor = Color.FromArgb(68, 78, 98);
        _useAutomaticButton.Enabled = false;
        _useAutomaticButton.Click += UseAutomaticRules;

        var assignmentHint = new Label
        {
            Text = "Assign all moves every selected icon into this category.",
            ForeColor = Color.FromArgb(93, 104, 123),
            Font = new Font("Segoe UI", 8.2f),
            Location = new Point(0, 44),
            AutoSize = true,
        };
        assignmentActions.Controls.AddRange(
            [_assignAllButton, _clearCategoryButton, _useAutomaticButton, assignmentHint]);
        editor.Controls.Add(assignmentActions, 0, 6);

        return editor;
    }

    private Control BuildCategoryEditor()
    {
        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.White,
            Padding = new Padding(14, 10, 14, 10),
        };

        var nameLabel = new Label
        {
            AutoSize = true,
            Text = "Category name",
            ForeColor = Color.FromArgb(85, 95, 113),
            Location = new Point(13, 9),
        };
        _nameTextBox.Location = new Point(14, 29);
        _nameTextBox.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        _nameTextBox.Width = panel.Width - 28;
        _nameTextBox.TextChanged += CategoryNameChanged;
        _nameTextBox.Leave += (_, _) => NormalizeCategoryName();

        var flowLabel = new Label
        {
            AutoSize = true,
            Text = "Icon orientation",
            ForeColor = Color.FromArgb(85, 95, 113),
            Location = new Point(13, 67),
        };
        _flowComboBox.Location = new Point(14, 87);
        _flowComboBox.Width = 168;
        _flowComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
        _flowComboBox.Items.AddRange(["Across rows →", "Down columns ↓"]);
        _flowComboBox.SelectedIndexChanged += FlowChanged;

        _capacityLabel.AutoSize = true;
        _capacityLabel.Font = new Font("Segoe UI", 9, FontStyle.Bold);
        _capacityLabel.ForeColor = Color.FromArgb(78, 89, 232);
        _capacityLabel.Location = new Point(197, 91);

        var autoMatchLabel = new Label
        {
            AutoSize = true,
            Text = "Auto-add matches",
            ForeColor = Color.FromArgb(85, 95, 113),
            Location = new Point(13, 124),
        };
        _autoMatchTextBox.Location = new Point(14, 145);
        _autoMatchTextBox.Anchor =
            AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        _autoMatchTextBox.Width = panel.Width - 28;
        _autoMatchTextBox.PlaceholderText = ".pdf, .docx, invoice";
        _autoMatchTextBox.TextChanged += AutoMatchChanged;

        var autoMatchHint = new Label
        {
            AutoSize = true,
            Text = "Extensions or words, separated by commas",
            ForeColor = Color.FromArgb(119, 129, 145),
            Font = new Font("Segoe UI", 8),
            Location = new Point(14, 174),
        };

        panel.Resize += (_, _) => _nameTextBox.Width = panel.ClientSize.Width - 28;
        panel.Controls.AddRange(
        [
            nameLabel,
            _nameTextBox,
            flowLabel,
            _flowComboBox,
            _capacityLabel,
            autoMatchLabel,
            _autoMatchTextBox,
            autoMatchHint,
        ]);
        panel.Resize += (_, _) => _autoMatchTextBox.Width = panel.ClientSize.Width - 28;
        return panel;
    }

    private Panel BuildFooter()
    {
        var footer = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 76,
            BackColor = Color.FromArgb(18, 24, 35),
            Padding = new Padding(28, 16, 28, 12),
        };

        _footerStatus.ForeColor = Color.FromArgb(171, 184, 204);
        _footerStatus.AutoEllipsis = true;
        _footerStatus.Location = new Point(29, 27);
        _footerStatus.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top;
        _footerStatus.Size = new Size(690, 22);

        var cancel = new Button
        {
            Text = "Cancel",
            Size = new Size(92, 42),
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(30, 38, 52),
            ForeColor = Color.White,
            DialogResult = DialogResult.Cancel,
        };
        cancel.Location = new Point(footer.Width - 240, 16);

        _applyButton.Text = "Apply layout";
        _applyButton.Size = new Size(132, 42);
        _applyButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _applyButton.FlatStyle = FlatStyle.Flat;
        _applyButton.FlatAppearance.BorderSize = 0;
        _applyButton.BackColor = Color.FromArgb(78, 89, 232);
        _applyButton.ForeColor = Color.White;
        _applyButton.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
        _applyButton.Location = new Point(footer.Width - 132, 16);
        _applyButton.Click += ApplyLayout;

        footer.Resize += (_, _) =>
        {
            _applyButton.Left = footer.ClientSize.Width - 28 - _applyButton.Width;
            cancel.Left = _applyButton.Left - 12 - cancel.Width;
            _footerStatus.Width = Math.Max(120, cancel.Left - 50);
        };
        footer.Controls.AddRange([_footerStatus, cancel, _applyButton]);
        AcceptButton = _applyButton;
        CancelButton = cancel;
        return footer;
    }

    private void PopulateCategories()
    {
        _categoryList.BeginUpdate();
        _categoryList.Items.Clear();
        foreach (var region in _layout.Regions)
        {
            EnsureRegionIsVisible(region);
            _categoryList.Items.Add(region);
        }

        _categoryList.EndUpdate();
        if (_categoryList.Items.Count > 0)
        {
            _categoryList.SelectedIndex = 0;
        }
        else
        {
            PopulateCategoryEditor(null);
        }
    }

    private void AddCategory()
    {
        var index = _layout.Regions.Count;
        var minimumRegionSize = IconLayout.GetMinimumRegionSize(_iconSpacing);
        var width = Math.Min(
            _desktopSize.Width,
            Math.Max(minimumRegionSize.Width, _desktopSize.Width / 3));
        var height = Math.Min(
            _desktopSize.Height,
            Math.Max(minimumRegionSize.Height, _desktopSize.Height / 3));
        var left = Math.Min(
            Math.Max(0, 48 + (index * 34)),
            Math.Max(0, _desktopSize.Width - width));
        var top = Math.Min(
            Math.Max(0, 48 + (index * 34)),
            Math.Max(0, _desktopSize.Height - height));
        var region = new RegionDefinition
        {
            Name = GetUniqueCategoryName(),
            Bounds = new Rectangle(left, top, width, height),
            Flow = IconFlow.AcrossRows,
            ColorArgb = CategoryColors[index % CategoryColors.Length].ToArgb(),
        };

        _layout.Regions.Add(region);
        _categoryList.Items.Add(region);
        _categoryList.SelectedItem = region;

        if (_layout.Regions.Count == 1)
        {
            foreach (var icon in _icons)
            {
                _layout.Assignments[icon.DisplayName] = region.Id;
                _layout.ManualOverrides.Add(icon.DisplayName);
            }
        }

        ReclassifyAutomaticIcons();
        RebuildAssignmentList();
        _preview.SelectRegion(region.Id);
        _preview.Invalidate();
        UpdateApplyState();
        _nameTextBox.Focus();
        _nameTextBox.SelectAll();
    }

    private void DeleteSelectedCategory()
    {
        var region = SelectedRegion;
        if (region is null)
        {
            return;
        }

        var assignedNames = _layout.Assignments
            .Where(pair => pair.Value == region.Id)
            .Select(pair => pair.Key)
            .ToList();
        foreach (var name in assignedNames)
        {
            _layout.Assignments.Remove(name);
        }

        var oldIndex = _categoryList.SelectedIndex;
        _layout.Regions.Remove(region);
        _categoryList.Items.Remove(region);
        if (_categoryList.Items.Count > 0)
        {
            _categoryList.SelectedIndex = Math.Min(oldIndex, _categoryList.Items.Count - 1);
        }
        else
        {
            PopulateCategoryEditor(null);
            _preview.SelectRegion(null);
        }

        ReclassifyAutomaticIcons();
        RebuildAssignmentList();
        _preview.Invalidate();
        UpdateApplyState();
    }

    private void CategorySelectionChanged(object? sender, EventArgs e)
    {
        var region = SelectedRegion;
        PopulateCategoryEditor(region);
        RebuildAssignmentList();
        _deleteButton.Enabled = region is not null;

        if (!_syncingSelection)
        {
            _syncingSelection = true;
            _preview.SelectRegion(region?.Id);
            _syncingSelection = false;
        }
    }

    private void PreviewSelectionChanged(object? sender, EventArgs e)
    {
        if (_syncingSelection)
        {
            return;
        }

        _syncingSelection = true;
        _categoryList.SelectedItem = _preview.SelectedRegionId is null
            ? null
            : _layout.Regions.FirstOrDefault(
                region => region.Id == _preview.SelectedRegionId.Value);
        _syncingSelection = false;
    }

    private void PopulateCategoryEditor(RegionDefinition? region)
    {
        _updatingEditor = true;
        var enabled = region is not null;
        _nameTextBox.Enabled = enabled;
        _flowComboBox.Enabled = enabled;
        _autoMatchTextBox.Enabled = enabled;
        _deleteButton.Enabled = enabled;
        _assignmentList.Enabled = enabled;
        _assignAllButton.Enabled = enabled && _icons.Count > 0;
        _clearCategoryButton.Enabled = enabled && _icons.Count > 0;
        _nameTextBox.Text = region?.Name ?? string.Empty;
        _flowComboBox.SelectedIndex = region?.Flow == IconFlow.DownColumns ? 1 : 0;
        _autoMatchTextBox.Text = region?.AutoMatch ?? string.Empty;
        _updatingEditor = false;
        UpdateCapacityLabel();
    }

    private void CategoryNameChanged(object? sender, EventArgs e)
    {
        if (_updatingEditor || SelectedRegion is not { } region)
        {
            return;
        }

        region.Name = _nameTextBox.Text;
        _categoryList.Refresh();
        ReclassifyAutomaticIcons();
        RebuildAssignmentList();
        _preview.Invalidate();
    }

    private void AutoMatchChanged(object? sender, EventArgs e)
    {
        if (_updatingEditor || SelectedRegion is not { } region)
        {
            return;
        }

        region.AutoMatch = _autoMatchTextBox.Text;
        ReclassifyAutomaticIcons();
        RebuildAssignmentList();
        _preview.Invalidate();
        UpdateApplyState();
    }

    private void NormalizeCategoryName()
    {
        if (SelectedRegion is not { } region)
        {
            return;
        }

        var name = region.Name.Trim();
        if (name.Length == 0)
        {
            name = GetUniqueCategoryName();
        }

        region.Name = name;
        _nameTextBox.Text = name;
        _categoryList.Refresh();
        _preview.Invalidate();
    }

    private void FlowChanged(object? sender, EventArgs e)
    {
        if (_updatingEditor || SelectedRegion is not { } region)
        {
            return;
        }

        region.Flow = _flowComboBox.SelectedIndex == 1
            ? IconFlow.DownColumns
            : IconFlow.AcrossRows;
        _preview.Invalidate();
    }

    private void RebuildAssignmentList()
    {
        var previousTopIndex =
            _assignmentList.Items.Count == 0 ? 0 : _assignmentList.TopIndex;
        var selectedIconName =
            (_assignmentList.SelectedItem as AssignmentEntry)?.Icon.DisplayName;
        var selectedRegion = SelectedRegion;
        _updatingAssignments = true;
        _assignmentList.BeginUpdate();
        _assignmentList.Items.Clear();
        foreach (var icon in _icons.OrderBy(
                     icon => icon.DisplayName,
                     StringComparer.CurrentCultureIgnoreCase))
        {
            var assignedRegion = GetAssignedRegion(icon.DisplayName);
            var entry = new AssignmentEntry(
                icon,
                assignedRegion?.Name,
                _layout.ManualOverrides.Contains(icon.DisplayName));
            var isChecked =
                selectedRegion is not null && assignedRegion?.Id == selectedRegion.Id;
            _assignmentList.Items.Add(entry, isChecked);
        }

        _assignmentList.EndUpdate();
        if (_assignmentList.Items.Count > 0)
        {
            var restoredSelection = -1;
            if (selectedIconName is not null)
            {
                for (var index = 0; index < _assignmentList.Items.Count; index++)
                {
                    if (_assignmentList.Items[index] is AssignmentEntry entry &&
                        string.Equals(
                            entry.Icon.DisplayName,
                            selectedIconName,
                            StringComparison.CurrentCultureIgnoreCase))
                    {
                        restoredSelection = index;
                        break;
                    }
                }
            }

            if (restoredSelection >= 0)
            {
                _assignmentList.SelectedIndex = restoredSelection;
            }

            _assignmentList.TopIndex = Math.Min(
                previousTopIndex,
                _assignmentList.Items.Count - 1);
        }

        _updatingAssignments = false;
    }

    private void AssignmentChecked(object? sender, ItemCheckEventArgs e)
    {
        if (_updatingAssignments ||
            SelectedRegion is not { } selectedRegion ||
            _assignmentList.Items[e.Index] is not AssignmentEntry entry)
        {
            return;
        }

        if (e.NewValue == CheckState.Checked)
        {
            _layout.Assignments[entry.Icon.DisplayName] = selectedRegion.Id;
            _layout.ManualOverrides.Add(entry.Icon.DisplayName);
        }
        else if (_layout.Assignments.TryGetValue(
                     entry.Icon.DisplayName,
                     out var assignedRegionId) &&
                 assignedRegionId == selectedRegion.Id)
        {
            _layout.Assignments.Remove(entry.Icon.DisplayName);
            _layout.ManualOverrides.Add(entry.Icon.DisplayName);
        }

        BeginInvoke(
            () =>
            {
                if (!IsDisposed)
                {
                    RebuildAssignmentList();
                    _preview.Invalidate();
                    UpdateApplyState();
                }
            });
    }

    private void UseAutomaticRules(object? sender, EventArgs e)
    {
        if (_assignmentList.SelectedItem is not AssignmentEntry entry)
        {
            return;
        }

        _layout.ManualOverrides.Remove(entry.Icon.DisplayName);
        CategoryClassifier.Apply(_layout, _allIcons);
        RebuildAssignmentList();
        _preview.Invalidate();
        UpdateApplyState();
    }

    private void AssignAllToSelectedCategory(object? sender, EventArgs e)
    {
        if (SelectedRegion is not { } selectedRegion)
        {
            return;
        }

        foreach (var icon in _icons)
        {
            _layout.Assignments[icon.DisplayName] = selectedRegion.Id;
            _layout.ManualOverrides.Add(icon.DisplayName);
        }

        RebuildAssignmentList();
        _preview.Invalidate();
        UpdateApplyState();
    }

    private void ClearSelectedCategory(object? sender, EventArgs e)
    {
        if (SelectedRegion is not { } selectedRegion)
        {
            return;
        }

        foreach (var icon in _icons)
        {
            if (_layout.Assignments.TryGetValue(
                    icon.DisplayName,
                    out var assignedRegionId) &&
                assignedRegionId == selectedRegion.Id)
            {
                _layout.Assignments.Remove(icon.DisplayName);
                _layout.ManualOverrides.Add(icon.DisplayName);
            }
        }

        RebuildAssignmentList();
        _preview.Invalidate();
        UpdateApplyState();
    }

    private void UpdateCapacityLabel()
    {
        if (SelectedRegion is not { } region)
        {
            _capacityLabel.Text = string.Empty;
            return;
        }

        var capacity = IconLayout.GetCapacity(region.Bounds, _iconSpacing);
        _capacityLabel.Text =
            $"{capacity.Columns} cols × {capacity.Rows} rows\n{capacity.Total} icon spaces";
    }

    private void UpdateApplyState()
    {
        var validCategoryIds = _layout.Regions.Select(region => region.Id).ToHashSet();
        var unassignedCount = _icons.Count(
            icon => !_layout.Assignments.TryGetValue(icon.DisplayName, out var categoryId) ||
                    !validCategoryIds.Contains(categoryId));
        var overflow = FindOverflow();
        var overlap = FindOccupiedOverlap();

        if (_layout.Regions.Count == 0)
        {
            _footerStatus.Text = "Create a category to continue.";
            _applyButton.Enabled = false;
        }
        else if (unassignedCount > 0)
        {
            _footerStatus.Text =
                $"Assign {unassignedCount} remaining icon{(unassignedCount == 1 ? string.Empty : "s")} to a category.";
            _applyButton.Enabled = false;
        }
        else if (overflow is not null)
        {
            _footerStatus.Text =
                $"Resize “{overflow.Value.Region.Name}” — it needs {overflow.Value.Count} spaces.";
            _applyButton.Enabled = false;
        }
        else if (overlap is not null)
        {
            _footerStatus.Text =
                $"Move \"{overlap.Value.First.Name}\" or \"{overlap.Value.Second.Name}\" so their regions do not overlap.";
            _applyButton.Enabled = false;
        }
        else
        {
            var categorizedCount = _allIcons.Count(
                icon => _layout.Assignments.ContainsKey(icon.DisplayName));
            _footerStatus.Text =
                $"{categorizedCount} categorized icon{(categorizedCount == 1 ? string.Empty : "s")} ready. Files stay in place.";
            _applyButton.Enabled = true;
        }
    }

    private void ApplyLayout(object? sender, EventArgs e)
    {
        NormalizeCategoryName();
        UpdateApplyState();
        if (!_applyButton.Enabled)
        {
            return;
        }

        try
        {
            Cursor = Cursors.WaitCursor;
            CategoryClassifier.Apply(_layout, _allIcons);
            var placements = IconLayout.CreatePlacements(
                _allIcons,
                _layout,
                _iconSpacing);
            var placementNames = placements
                .Select(placement => placement.DisplayName)
                .ToHashSet(StringComparer.CurrentCultureIgnoreCase);
            var iconsWithoutSnapshots = _allIcons
                .Where(
                    icon =>
                        placementNames.Contains(icon.DisplayName) &&
                        !_undoSnapshotAttempts.Contains(icon.DisplayName))
                .ToList();
            if (iconsWithoutSnapshots.Count > 0)
            {
                _undoSnapshotAttempts.UnionWith(
                    iconsWithoutSnapshots.Select(icon => icon.DisplayName));
                try
                {
                    foreach (var originalPosition in DesktopShell.GetPositions(
                                 iconsWithoutSnapshots))
                    {
                        _originalPositions.TryAdd(
                            originalPosition.DisplayName,
                            originalPosition);
                    }
                }
                catch (Exception exception) when (
                    exception is InvalidOperationException or
                    System.ComponentModel.Win32Exception)
                {
                    // Applying the requested layout is still useful when Explorer
                    // cannot provide an undo snapshot.
                }
            }

            var applyResult = DesktopShell.ApplyPositions(placements);
            if (applyResult.Missing.Count > 0)
            {
                var examples = string.Join(", ", applyResult.Missing.Take(4));
                var remaining =
                    applyResult.Missing.Count - Math.Min(4, applyResult.Missing.Count);
                var suffix = remaining > 0 ? $" and {remaining} more" : string.Empty;
                _footerStatus.Text =
                    $"{applyResult.Missing.Count} icons were not placed. Apply again to retry.";
                MessageBox.Show(
                    this,
                    "Windows Explorer did not keep every requested position for: " +
                    $"{examples}{suffix}.\n\n" +
                    "The preview will stay open so you can retry.",
                    "Some icons still need placement",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            ApplyResult = applyResult;
            LayoutStore.Save(_layout);
            UndoAvailable =
                _originalPositions.Count > 0 &&
                UndoStore.TrySave(
                    new UndoState
                    {
                        Layout = _originalLayout.Clone(),
                        Positions = _originalPositions.Values.ToList(),
                    });
            ResultLayout = _layout.Clone();
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or
            System.ComponentModel.Win32Exception or
            IOException or
            UnauthorizedAccessException)
        {
            MessageBox.Show(
                this,
                exception.Message,
                "Layout could not be applied",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
        finally
        {
            Cursor = Cursors.Default;
        }
    }

    private (RegionDefinition Region, int Count)? FindOverflow()
    {
        foreach (var region in _layout.Regions)
        {
            var count = _allIcons.Count(
                icon => _layout.Assignments.TryGetValue(icon.DisplayName, out var categoryId) &&
                        categoryId == region.Id);
            if (count > IconLayout.GetCapacity(region.Bounds, _iconSpacing).Total)
            {
                return (region, count);
            }
        }

        return null;
    }

    private (RegionDefinition First, RegionDefinition Second)? FindOccupiedOverlap()
    {
        var occupiedRegionIds = _layout.Assignments.Values.ToHashSet();
        var occupiedRegions = _layout.Regions
            .Where(region => occupiedRegionIds.Contains(region.Id))
            .ToList();
        for (var firstIndex = 0; firstIndex < occupiedRegions.Count; firstIndex++)
        {
            for (var secondIndex = firstIndex + 1;
                 secondIndex < occupiedRegions.Count;
                 secondIndex++)
            {
                var first = occupiedRegions[firstIndex];
                var second = occupiedRegions[secondIndex];
                if (first.Bounds.IntersectsWith(second.Bounds))
                {
                    return (first, second);
                }
            }
        }

        return null;
    }

    private RegionDefinition? GetAssignedRegion(string iconName)
    {
        if (!_layout.Assignments.TryGetValue(iconName, out var regionId))
        {
            return null;
        }

        return _layout.Regions.FirstOrDefault(region => region.Id == regionId);
    }

    private void ReclassifyAutomaticIcons()
    {
        CategoryClassifier.Apply(_layout, _allIcons);
        _preview.Invalidate();
    }

    private string GetUniqueCategoryName()
    {
        var suffix = _layout.Regions.Count + 1;
        while (_layout.Regions.Any(
                   region => string.Equals(
                       region.Name,
                       $"Category {suffix}",
                       StringComparison.CurrentCultureIgnoreCase)))
        {
            suffix++;
        }

        return $"Category {suffix}";
    }

    private void EnsureRegionIsVisible(RegionDefinition region)
    {
        var minimumRegionSize = IconLayout.GetMinimumRegionSize(_iconSpacing);
        var width = Math.Clamp(
            region.Bounds.Width,
            Math.Min(minimumRegionSize.Width, _desktopSize.Width),
            _desktopSize.Width);
        var height = Math.Clamp(
            region.Bounds.Height,
            Math.Min(minimumRegionSize.Height, _desktopSize.Height),
            _desktopSize.Height);
        var left = Math.Clamp(region.Bounds.Left, 0, Math.Max(0, _desktopSize.Width - width));
        var top = Math.Clamp(region.Bounds.Top, 0, Math.Max(0, _desktopSize.Height - height));
        region.Bounds = new Rectangle(left, top, width, height);
    }

    private static Button CreateEditorButton(string text, bool primary)
    {
        var button = new Button
        {
            Text = text,
            Height = 32,
            FlatStyle = FlatStyle.Flat,
            Cursor = Cursors.Hand,
            BackColor = primary ? Color.FromArgb(78, 89, 232) : Color.White,
            ForeColor = primary ? Color.White : Color.FromArgb(48, 58, 76),
        };
        button.FlatAppearance.BorderSize = primary ? 0 : 1;
        return button;
    }

    private sealed class AssignmentEntry(
        DesktopIconInfo icon,
        string? categoryName,
        bool isManual)
    {
        public DesktopIconInfo Icon { get; } = icon;

        public bool IsManual { get; } = isManual;

        public override string ToString() =>
            categoryName is null
                ? $"{Icon.DisplayName}   · unassigned"
                : $"{Icon.DisplayName}   · {categoryName}{(IsManual ? string.Empty : " (auto)")}";
    }
}
