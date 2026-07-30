namespace TidyDesk;

internal sealed class MainForm : Form
{
    private readonly ListView _iconList = new();
    private readonly Label _selectionLabel = new();
    private readonly Label _statusLabel = new();
    private readonly Button _organizeButton = new();
    private readonly Button _undoButton = new();
    private readonly Button _editCategoriesButton = new();
    private readonly Button _settingsButton = new();
    private readonly TextBox _searchBox = new();
    private readonly CheckBox _hideCategorizedCheckBox = new();
    private readonly CheckBox _hideUncategorizedCheckBox = new();
    private readonly CheckBox _checkAllCheckBox = new();
    private readonly ComboBox _categoryFilter = new();
    private readonly ToolTip _toolTip = new();
    private readonly HashSet<string> _checkedIconNames =
        new(StringComparer.CurrentCultureIgnoreCase);

    private IReadOnlyList<DesktopIconInfo> _desktopIcons = [];
    private OrganizerLayout _layout = new();
    private OrganizerSettings _settings;
    private bool _rebuildingList;
    private bool _updatingCheckAll;
    private bool _updatingFilters;
    private bool _startupSelectionApplied;

    public MainForm()
    {
        _settings = OrganizerSettingsStore.Load();
        Text = "TidyDesk";
        Icon = AppIcon.Create();
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(820, 600);
        Size = new Size(980, 730);
        BackColor = Color.FromArgb(246, 248, 252);
        ForeColor = Color.FromArgb(24, 31, 44);
        Font = new Font("Segoe UI", 9F);

        BuildInterface();
        ThemeManager.Apply(this, _settings.DarkMode);
        Shown += (_, _) => LoadDesktopIcons();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _toolTip.Dispose();
            Icon?.Dispose();
        }

        base.Dispose(disposing);
    }

    private void BuildInterface()
    {
        var header = new Panel
        {
            Dock = DockStyle.Top,
            Height = 126,
            Padding = new Padding(32, 24, 32, 14),
            BackColor = Color.FromArgb(20, 27, 40),
        };

        var title = new Label
        {
            AutoSize = true,
            Text = "TidyDesk",
            Font = new Font("Segoe UI", 20, FontStyle.Bold),
            ForeColor = Color.White,
            Location = new Point(30, 21),
        };
        var subtitle = new Label
        {
            AutoSize = true,
            Text = "Build smart desktop regions without moving or renaming files.",
            Font = new Font("Segoe UI", 10),
            ForeColor = Color.FromArgb(187, 198, 216),
            Location = new Point(33, 64),
        };
        var safetyPill = new Label
        {
            AutoSize = true,
            Text = "  POSITION ONLY  ",
            Font = new Font("Segoe UI", 8, FontStyle.Bold),
            ForeColor = Color.FromArgb(164, 245, 205),
            BackColor = Color.FromArgb(30, 70, 62),
            Padding = new Padding(5, 4, 5, 4),
            Location = new Point(33, 91),
        };

        ConfigureHeaderButton(_editCategoriesButton, "Edit categories", 132);
        _editCategoriesButton.Enabled = false;
        _editCategoriesButton.Click += EditCategories;
        ConfigureHeaderButton(_settingsButton, "Settings", 96);
        _settingsButton.Click += OpenSettings;
        header.Resize += (_, _) =>
        {
            _settingsButton.Left =
                header.ClientSize.Width - 32 - _settingsButton.Width;
            _editCategoriesButton.Left =
                _settingsButton.Left - 10 - _editCategoriesButton.Width;
        };
        header.Controls.AddRange(
            [
                title,
                subtitle,
                safetyPill,
                _editCategoriesButton,
                _settingsButton,
            ]);
        _toolTip.SetToolTip(
            _editCategoriesButton,
            "Open the desktop preview to edit categories without adding selected items.");
        _toolTip.SetToolTip(
            _settingsButton,
            "Change appearance and preview behavior.");

        var content = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(28, 18, 28, 20),
        };
        var toolbar = BuildToolbar();

        _iconList.Dock = DockStyle.Fill;
        _iconList.BorderStyle = BorderStyle.FixedSingle;
        _iconList.BackColor = Color.White;
        _iconList.CheckBoxes = true;
        _iconList.FullRowSelect = true;
        _iconList.HideSelection = false;
        _iconList.View = View.Details;
        _iconList.HeaderStyle = ColumnHeaderStyle.Nonclickable;
        _iconList.Columns.Add("Desktop icon", 560);
        _iconList.Columns.Add("Current category", 220);
        _iconList.ItemChecked += IconChecked;
        _iconList.ItemActivate += (_, _) =>
        {
            if (_iconList.SelectedItems.Count > 0)
            {
                var item = _iconList.SelectedItems[0];
                item.Checked = !item.Checked;
            }
        };
        _iconList.Resize += (_, _) =>
        {
            if (_iconList.Columns.Count >= 2)
            {
                _iconList.Columns[0].Width = Math.Max(
                    250,
                    _iconList.ClientSize.Width - 235);
                _iconList.Columns[1].Width = 210;
            }
        };

        var footer = BuildFooter();
        content.Controls.Add(_iconList);
        content.Controls.Add(footer);
        content.Controls.Add(toolbar);
        Controls.Add(content);
        Controls.Add(header);
    }

    private Panel BuildToolbar()
    {
        var toolbar = new Panel
        {
            Dock = DockStyle.Top,
            Height = 126,
        };

        _selectionLabel.AutoSize = true;
        _selectionLabel.Font = new Font("Segoe UI", 11, FontStyle.Bold);
        _selectionLabel.Location = new Point(0, 4);
        _selectionLabel.Text = "Desktop icons";

        var clear = CreateSecondaryButton("Clear", 78);
        var refresh = CreateSecondaryButton("Refresh", 78);
        _checkAllCheckBox.AutoSize = true;
        _checkAllCheckBox.Text = "Check all";
        _checkAllCheckBox.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _checkAllCheckBox.CheckedChanged += (_, _) =>
        {
            if (_updatingCheckAll)
            {
                return;
            }

            if (_checkAllCheckBox.Checked)
            {
                SetVisibleChecked(true);
            }
            else
            {
                ClearSelection();
            }
        };
        clear.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        refresh.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        clear.Click += (_, _) => ClearSelection();
        refresh.Click += (_, _) => LoadDesktopIcons();

        _searchBox.PlaceholderText = "Search desktop icons";
        _searchBox.Location = new Point(0, 48);
        _searchBox.Size = new Size(245, 25);
        _searchBox.TextChanged += (_, _) => RebuildIconList();

        var selectUncategorized = CreateSecondaryButton("Select uncategorized", 148);
        selectUncategorized.Location = new Point(257, 45);
        selectUncategorized.Click += (_, _) => SelectUncategorized();

        _hideCategorizedCheckBox.AutoSize = true;
        _hideCategorizedCheckBox.Text = "Hide categorized icons";
        _hideCategorizedCheckBox.Location = new Point(307, 90);
        _hideCategorizedCheckBox.CheckedChanged += FilterChanged;

        _hideUncategorizedCheckBox.AutoSize = true;
        _hideUncategorizedCheckBox.Text = "Hide uncategorized icons";
        _hideUncategorizedCheckBox.Location = new Point(465, 90);
        _hideUncategorizedCheckBox.CheckedChanged += FilterChanged;

        var categoryLabel = new Label
        {
            AutoSize = true,
            Text = "Category:",
            ForeColor = Color.FromArgb(78, 88, 106),
            Location = new Point(0, 91),
        };
        _categoryFilter.DropDownStyle = ComboBoxStyle.DropDownList;
        _categoryFilter.Location = new Point(67, 86);
        _categoryFilter.Size = new Size(220, 25);
        _categoryFilter.SelectedIndexChanged += CategoryFilterChanged;
        _toolTip.SetToolTip(
            _searchBox,
            "Filter the cached icon list without refreshing Windows Explorer (Ctrl+F).");
        _toolTip.SetToolTip(
            selectUncategorized,
            "Select every desktop icon that does not have a valid category.");
        _toolTip.SetToolTip(
            _hideCategorizedCheckBox,
            "Show only icons that still need to be organized.");
        _toolTip.SetToolTip(
            _hideUncategorizedCheckBox,
            "Show only icons that already belong to a category.");
        _toolTip.SetToolTip(
            _categoryFilter,
            "Show all icons, uncategorized icons, or one named category.");
        _toolTip.SetToolTip(
            _checkAllCheckBox,
            "Check every visible item. Unchecking this clears every selection.");

        toolbar.Resize += (_, _) =>
        {
            _checkAllCheckBox.Left = toolbar.ClientSize.Width - 270;
            _checkAllCheckBox.Top = 9;
            clear.Left = toolbar.ClientSize.Width - 176;
            refresh.Left = toolbar.ClientSize.Width - 88;
        };
        toolbar.Controls.AddRange(
        [
            _selectionLabel,
            _checkAllCheckBox,
            clear,
            refresh,
            _searchBox,
            selectUncategorized,
            categoryLabel,
            _categoryFilter,
            _hideCategorizedCheckBox,
            _hideUncategorizedCheckBox,
        ]);
        return toolbar;
    }

    protected override bool ProcessCmdKey(ref Message message, Keys keyData)
    {
        if (keyData == (Keys.Control | Keys.F))
        {
            _searchBox.Focus();
            _searchBox.SelectAll();
            return true;
        }

        if (keyData == (Keys.Control | Keys.A))
        {
            SetVisibleChecked(true);
            return true;
        }

        if (keyData == (Keys.Control | Keys.Z) && _undoButton.Enabled)
        {
            _undoButton.PerformClick();
            return true;
        }

        if (keyData == (Keys.Control | Keys.E) && _editCategoriesButton.Enabled)
        {
            _editCategoriesButton.PerformClick();
            return true;
        }

        if (keyData == (Keys.Control | Keys.Oemcomma))
        {
            _settingsButton.PerformClick();
            return true;
        }

        if (keyData == Keys.F5)
        {
            LoadDesktopIcons();
            return true;
        }

        return base.ProcessCmdKey(ref message, keyData);
    }

    private Panel BuildFooter()
    {
        var footer = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 74,
            Padding = new Padding(0, 15, 0, 0),
        };
        _statusLabel.AutoEllipsis = true;
        _statusLabel.ForeColor = Color.FromArgb(91, 101, 119);
        _statusLabel.Location = new Point(0, 23);
        _statusLabel.Size = new Size(480, 26);
        _statusLabel.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top;

        _undoButton.Text = "↶  Undo last";
        _undoButton.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
        _undoButton.ForeColor = Color.FromArgb(48, 58, 76);
        _undoButton.BackColor = Color.White;
        _undoButton.FlatStyle = FlatStyle.Flat;
        _undoButton.Size = new Size(126, 44);
        _undoButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _undoButton.Enabled = false;
        _undoButton.Click += UndoLast;

        _organizeButton.Text = "Organize selected  →";
        _organizeButton.Font = new Font("Segoe UI", 10, FontStyle.Bold);
        _organizeButton.ForeColor = Color.White;
        _organizeButton.BackColor = Color.FromArgb(78, 89, 232);
        _organizeButton.FlatStyle = FlatStyle.Flat;
        _organizeButton.FlatAppearance.BorderSize = 0;
        _organizeButton.Size = new Size(190, 44);
        _organizeButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _organizeButton.Enabled = false;
        _organizeButton.Click += OrganizeSelected;

        footer.Resize += (_, _) =>
        {
            _organizeButton.Left = footer.ClientSize.Width - _organizeButton.Width;
            _undoButton.Left = _organizeButton.Left - _undoButton.Width - 10;
            _statusLabel.Width = Math.Max(100, _undoButton.Left - 18);
        };
        footer.Controls.AddRange([_statusLabel, _undoButton, _organizeButton]);
        return footer;
    }

    private void LoadDesktopIcons()
    {
        Cursor = Cursors.WaitCursor;
        _statusLabel.Text = "Reading desktop icons…";
        _organizeButton.Enabled = false;
        try
        {
            _layout = LayoutStore.Load();
            _desktopIcons = DesktopShell.GetIcons()
                .OrderBy(icon => icon.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
            _checkedIconNames.IntersectWith(
                _desktopIcons.Select(icon => icon.DisplayName));

            var autoCategorized = CategoryClassifier.Apply(_layout, _desktopIcons);
            if (autoCategorized > 0)
            {
                LayoutStore.Save(_layout);
            }

            RefreshCategoryFilter();
            RebuildIconList();
            if (!_startupSelectionApplied)
            {
                _startupSelectionApplied = true;
                if (_settings.SelectUncategorizedOnStartup)
                {
                    SelectUncategorized();
                }
            }

            _undoButton.Enabled = UndoStore.Exists;
            _editCategoriesButton.Enabled = true;
            _statusLabel.Text = autoCategorized > 0
                ? $"{autoCategorized} icon{(autoCategorized == 1 ? string.Empty : "s")} matched category rules."
                : $"{_desktopIcons.Count} visible desktop icons found.";
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or
            System.ComponentModel.Win32Exception)
        {
            _desktopIcons = [];
            _iconList.Items.Clear();
            _editCategoriesButton.Enabled = false;
            _statusLabel.Text = "Desktop icons could not be read.";
            MessageBox.Show(
                this,
                exception.Message,
                "Desktop unavailable",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
        finally
        {
            Cursor = Cursors.Default;
        }
    }

    private void RebuildIconList()
    {
        var search = _searchBox.Text.Trim();
        var selectedFilter = _categoryFilter.SelectedItem as CategoryFilterOption;
        var visibleIcons = _desktopIcons.Where(
            icon =>
            {
                var categoryId = GetCategoryId(icon.DisplayName);
                var isCategorized = categoryId is not null;
                if ((_hideCategorizedCheckBox.Checked && isCategorized) ||
                    (_hideUncategorizedCheckBox.Checked && !isCategorized))
                {
                    return false;
                }

                if (selectedFilter is { IsUncategorized: true } && isCategorized)
                {
                    return false;
                }

                if (selectedFilter?.CategoryId is { } filterCategoryId &&
                    categoryId != filterCategoryId)
                {
                    return false;
                }

                return search.Length == 0 ||
                       icon.DisplayName.Contains(
                           search,
                           StringComparison.CurrentCultureIgnoreCase);
            });

        _rebuildingList = true;
        _iconList.BeginUpdate();
        _iconList.Items.Clear();
        foreach (var icon in visibleIcons)
        {
            var item = new ListViewItem(icon.DisplayName)
            {
                Tag = icon,
                Checked = _checkedIconNames.Contains(icon.DisplayName),
            };
            item.SubItems.Add(GetCategoryName(icon.DisplayName));
            _iconList.Items.Add(item);
        }

        _iconList.EndUpdate();
        _rebuildingList = false;
        UpdateSelectionState();
    }

    private void IconChecked(object? sender, ItemCheckedEventArgs e)
    {
        if (_rebuildingList || e.Item.Tag is not DesktopIconInfo icon)
        {
            return;
        }

        if (e.Item.Checked)
        {
            _checkedIconNames.Add(icon.DisplayName);
        }
        else
        {
            _checkedIconNames.Remove(icon.DisplayName);
        }

        UpdateSelectionState();
    }

    private void OrganizeSelected(object? sender, EventArgs e)
    {
        var selectedIcons = _desktopIcons
            .Where(icon => _checkedIconNames.Contains(icon.DisplayName))
            .ToList();
        if (selectedIcons.Count == 0)
        {
            return;
        }

        OpenRegionDesigner(selectedIcons, clearSelectionAfterApply: true);
    }

    private void EditCategories(object? sender, EventArgs e)
    {
        OpenRegionDesigner([], clearSelectionAfterApply: false);
    }

    private void OpenRegionDesigner(
        IReadOnlyList<DesktopIconInfo> selectedIcons,
        bool clearSelectionAfterApply)
    {
        DialogResult dialogResult = DialogResult.Cancel;
        OrganizerLayout? resultLayout = null;
        ApplyResult result = new(0, Array.Empty<string>());
        var undoAvailable = false;
        Hide();
        try
        {
            using var previewWindows = _settings.MinimizeOtherApplications
                ? PreviewWindowSession.MinimizeOtherApplications()
                : null;
            using var designer = new RegionDesignerForm(
                selectedIcons,
                _desktopIcons,
                _layout,
                _settings.DarkMode,
                _settings.ShowDisplayBoundaries);
            dialogResult = designer.ShowDialog();
            if (dialogResult == DialogResult.OK)
            {
                resultLayout = designer.ResultLayout;
                result = designer.ApplyResult;
                undoAvailable = designer.UndoAvailable;
            }
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or
            System.ComponentModel.Win32Exception or
            System.Runtime.InteropServices.ExternalException)
        {
            MessageBox.Show(
                this,
                exception.Message,
                "Desktop preview unavailable",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
        finally
        {
            Show();
            Activate();
        }

        if (dialogResult != DialogResult.OK || resultLayout is null)
        {
            return;
        }

        _layout = resultLayout;
        if (clearSelectionAfterApply)
        {
            _checkedIconNames.Clear();
        }

        _undoButton.Enabled = undoAvailable;
        RefreshCategoryFilter();
        RebuildIconList();
        if (!clearSelectionAfterApply && result.Missing.Count == 0)
        {
            _statusLabel.Text = undoAvailable
                ? $"Categories updated; {result.Positioned} icons placed. Undo is available."
                : $"Categories updated; {result.Positioned} icons placed.";
        }
        else
        {
            _statusLabel.Text = result.Missing.Count == 0
                ? undoAvailable
                    ? $"{result.Positioned} categorized icons placed. Undo is available."
                    : $"{result.Positioned} categorized icons placed."
                : $"{result.Positioned} icons placed; {result.Missing.Count} could not be positioned.";
        }
    }

    private void OpenSettings(object? sender, EventArgs e)
    {
        using var settingsForm = new SettingsForm(_settings);
        if (settingsForm.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        try
        {
            OrganizerSettingsStore.Save(settingsForm.ResultSettings);
            _settings = settingsForm.ResultSettings.Clone();
            ThemeManager.Apply(this, _settings.DarkMode);
            _statusLabel.Text = "Settings saved.";
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(
                this,
                exception.Message,
                "Settings could not be saved",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
    }

    private void UndoLast(object? sender, EventArgs e)
    {
        var undo = UndoStore.Load();
        if (undo is null || undo.Positions.Count == 0)
        {
            _undoButton.Enabled = false;
            _statusLabel.Text = "There is no saved layout to undo.";
            return;
        }

        try
        {
            Cursor = Cursors.WaitCursor;
            var result = DesktopShell.ApplyPositions(undo.Positions);
            LayoutStore.Save(undo.Layout);
            _layout = undo.Layout.Clone();
            UndoStore.Clear();
            _undoButton.Enabled = false;
            _checkedIconNames.Clear();
            RefreshCategoryFilter();
            RebuildIconList();
            _statusLabel.Text =
                $"Restored {result.Positioned} icon positions and the previous categories.";
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
                "Undo could not be completed",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
        finally
        {
            Cursor = Cursors.Default;
        }
    }

    private void SetVisibleChecked(bool isChecked)
    {
        _rebuildingList = true;
        _iconList.BeginUpdate();
        foreach (ListViewItem item in _iconList.Items)
        {
            item.Checked = isChecked;
            var icon = (DesktopIconInfo)item.Tag!;
            if (isChecked)
            {
                _checkedIconNames.Add(icon.DisplayName);
            }
            else
            {
                _checkedIconNames.Remove(icon.DisplayName);
            }
        }

        _iconList.EndUpdate();
        _rebuildingList = false;
        UpdateSelectionState();
    }

    private void ClearSelection()
    {
        _checkedIconNames.Clear();
        RebuildIconList();
    }

    private void SelectUncategorized()
    {
        _checkedIconNames.Clear();
        _checkedIconNames.UnionWith(
            _desktopIcons
                .Where(icon => !IsCategorized(icon.DisplayName))
                .Select(icon => icon.DisplayName));
        RebuildIconList();
    }

    private void UpdateSelectionState()
    {
        if (IsDisposed)
        {
            return;
        }

        var selected = _checkedIconNames.Count;
        _selectionLabel.Text = selected == 0
            ? $"{_iconList.Items.Count} desktop icons shown"
            : $"{selected} icon{(selected == 1 ? string.Empty : "s")} selected";
        _organizeButton.Enabled = selected > 0;

        var allVisibleChecked =
            _iconList.Items.Count > 0 &&
            _iconList.Items.Cast<ListViewItem>().All(item => item.Checked);
        _updatingCheckAll = true;
        _checkAllCheckBox.Checked = allVisibleChecked;
        _updatingCheckAll = false;
    }

    private void FilterChanged(object? sender, EventArgs e)
    {
        if (!_updatingFilters)
        {
            RebuildIconList();
        }
    }

    private void CategoryFilterChanged(object? sender, EventArgs e)
    {
        if (_updatingFilters)
        {
            return;
        }

        if (_categoryFilter.SelectedItem is CategoryFilterOption option &&
            (option.CategoryId is not null || option.IsUncategorized))
        {
            _updatingFilters = true;
            _hideCategorizedCheckBox.Checked = false;
            _hideUncategorizedCheckBox.Checked = false;
            _updatingFilters = false;
        }

        RebuildIconList();
    }

    private void RefreshCategoryFilter()
    {
        var previous = _categoryFilter.SelectedItem as CategoryFilterOption;
        _updatingFilters = true;
        _categoryFilter.BeginUpdate();
        _categoryFilter.Items.Clear();
        _categoryFilter.Items.Add(new CategoryFilterOption("All icons"));
        _categoryFilter.Items.Add(
            new CategoryFilterOption("Uncategorized", isUncategorized: true));
        foreach (var region in _layout.Regions.OrderBy(
                     region => region.Name,
                     StringComparer.CurrentCultureIgnoreCase))
        {
            _categoryFilter.Items.Add(
                new CategoryFilterOption(region.Name, categoryId: region.Id));
        }

        var replacement = _categoryFilter.Items
            .Cast<CategoryFilterOption>()
            .FirstOrDefault(option => option.Matches(previous));
        _categoryFilter.SelectedItem = replacement ?? _categoryFilter.Items[0];
        _categoryFilter.EndUpdate();
        _updatingFilters = false;
    }

    private bool IsCategorized(string iconName) => GetCategoryId(iconName) is not null;

    private Guid? GetCategoryId(string iconName)
    {
        if (!_layout.Assignments.TryGetValue(iconName, out var categoryId) ||
            !_layout.Regions.Any(region => region.Id == categoryId))
        {
            return null;
        }

        return categoryId;
    }

    private string GetCategoryName(string iconName)
    {
        if (!_layout.Assignments.TryGetValue(iconName, out var categoryId))
        {
            return "—";
        }

        return _layout.Regions.FirstOrDefault(region => region.Id == categoryId)?.Name ?? "—";
    }

    private static Button CreateSecondaryButton(string text, int width) =>
        new()
        {
            Text = text,
            Size = new Size(width, 34),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.White,
            ForeColor = Color.FromArgb(48, 58, 76),
            Cursor = Cursors.Hand,
        };

    private void InitializeComponent()
    {

    }

    private static void ConfigureHeaderButton(
        Button button,
        string text,
        int width)
    {
        button.Text = text;
        button.Size = new Size(width, 36);
        button.Top = 27;
        button.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        button.FlatStyle = FlatStyle.Flat;
        button.BackColor = Color.FromArgb(30, 38, 52);
        button.ForeColor = Color.White;
        button.Cursor = Cursors.Hand;
    }

    private sealed class CategoryFilterOption(
        string name,
        Guid? categoryId = null,
        bool isUncategorized = false)
    {
        public Guid? CategoryId { get; } = categoryId;

        public bool IsUncategorized { get; } = isUncategorized;

        public bool Matches(CategoryFilterOption? other) =>
            other is not null &&
            CategoryId == other.CategoryId &&
            IsUncategorized == other.IsUncategorized;

        public override string ToString() => name;
    }
}
