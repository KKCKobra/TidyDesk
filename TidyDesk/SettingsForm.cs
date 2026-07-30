namespace TidyDesk;

internal sealed class SettingsForm : Form
{
    private readonly CheckBox _darkMode = new();
    private readonly CheckBox _minimizeApplications = new();
    private readonly CheckBox _showDisplayBoundaries = new();
    private readonly CheckBox _selectUncategorized = new();

    public SettingsForm(OrganizerSettings source)
    {
        Text = "TidyDesk settings";
        Icon = AppIcon.Create();
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(590, 485);
        Font = new Font("Segoe UI", 9F);

        _darkMode.Checked = source.DarkMode;
        _minimizeApplications.Checked = source.MinimizeOtherApplications;
        _showDisplayBoundaries.Checked = source.ShowDisplayBoundaries;
        _selectUncategorized.Checked = source.SelectUncategorizedOnStartup;

        BuildInterface();
        ThemeManager.Apply(this, source.DarkMode);
        _darkMode.CheckedChanged += (_, _) =>
            ThemeManager.Apply(this, _darkMode.Checked);
    }

    public OrganizerSettings ResultSettings { get; private set; } = new();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Icon?.Dispose();
        }

        base.Dispose(disposing);
    }

    private void BuildInterface()
    {
        var header = new Panel
        {
            Dock = DockStyle.Top,
            Height = 82,
            BackColor = Color.FromArgb(20, 27, 40),
        };
        header.Controls.Add(
            new Label
            {
                AutoSize = true,
                Text = "Settings",
                Font = new Font("Segoe UI", 18, FontStyle.Bold),
                ForeColor = Color.White,
                Location = new Point(27, 16),
            });
        header.Controls.Add(
            new Label
            {
                AutoSize = true,
                Text = "Personalize the organizer and desktop preview.",
                ForeColor = Color.FromArgb(187, 198, 216),
                Location = new Point(30, 51),
            });

        var options = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(26, 20, 26, 12),
            ColumnCount = 1,
            RowCount = 4,
        };
        for (var index = 0; index < 4; index++)
        {
            options.RowStyles.Add(new RowStyle(SizeType.Percent, 25));
        }

        options.Controls.Add(
            CreateOption(
                _darkMode,
                "Dark mode",
                "Use a dark interface on the main window, settings, and category editor."),
            0,
            0);
        options.Controls.Add(
            CreateOption(
                _minimizeApplications,
                "Minimize other applications for preview",
                "Temporarily clear every other app from the desktop while editing regions."),
            0,
            1);
        options.Controls.Add(
            CreateOption(
                _showDisplayBoundaries,
                "Show display borders and labels",
                "Mark each physical monitor in the preview, including the primary display."),
            0,
            2);
        options.Controls.Add(
            CreateOption(
                _selectUncategorized,
                "Select uncategorized icons at startup",
                "Prepare icons that still need a category when the organizer first opens."),
            0,
            3);

        var footer = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 72,
            Padding = new Padding(26, 14, 26, 14),
        };
        var cancel = new Button
        {
            Text = "Cancel",
            DialogResult = DialogResult.Cancel,
            FlatStyle = FlatStyle.Flat,
            Size = new Size(94, 40),
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
        };
        var save = new Button
        {
            Text = "Save settings",
            FlatStyle = FlatStyle.Flat,
            Size = new Size(132, 40),
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            BackColor = Color.FromArgb(78, 89, 232),
            ForeColor = Color.White,
        };
        save.FlatAppearance.BorderSize = 0;
        save.Click += SaveSettings;
        footer.Resize += (_, _) =>
        {
            save.Left = footer.ClientSize.Width - 26 - save.Width;
            cancel.Left = save.Left - 10 - cancel.Width;
        };
        footer.Controls.AddRange([cancel, save]);

        Controls.Add(options);
        Controls.Add(footer);
        Controls.Add(header);
        AcceptButton = save;
        CancelButton = cancel;
    }

    private static Panel CreateOption(
        CheckBox checkBox,
        string title,
        string description)
    {
        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 0, 10),
            BackColor = Color.White,
            Padding = new Padding(16, 11, 16, 8),
        };
        checkBox.AutoSize = true;
        checkBox.Text = title;
        checkBox.Font = new Font("Segoe UI", 10, FontStyle.Bold);
        checkBox.Location = new Point(14, 10);
        var hint = new Label
        {
            AutoSize = false,
            Text = description,
            ForeColor = Color.FromArgb(85, 95, 113),
            Location = new Point(35, 38),
            Size = new Size(485, 34),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
        };
        panel.Resize += (_, _) => hint.Width = Math.Max(100, panel.ClientSize.Width - 48);
        panel.Controls.AddRange([checkBox, hint]);
        return panel;
    }

    private void SaveSettings(object? sender, EventArgs e)
    {
        ResultSettings = new OrganizerSettings
        {
            DarkMode = _darkMode.Checked,
            MinimizeOtherApplications = _minimizeApplications.Checked,
            ShowDisplayBoundaries = _showDisplayBoundaries.Checked,
            SelectUncategorizedOnStartup = _selectUncategorized.Checked,
        };
        DialogResult = DialogResult.OK;
        Close();
    }
}
