namespace TidyDesk;

internal static class ThemeManager
{
    private static readonly Color LightBackground = Color.FromArgb(246, 248, 252);
    private static readonly Color LightCard = Color.White;
    private static readonly Color LightText = Color.FromArgb(24, 31, 44);
    private static readonly Color LightMuted = Color.FromArgb(85, 95, 113);
    private static readonly Color DarkBackground = Color.FromArgb(14, 19, 29);
    private static readonly Color DarkCard = Color.FromArgb(24, 31, 44);
    private static readonly Color DarkInput = Color.FromArgb(17, 23, 34);
    private static readonly Color DarkText = Color.FromArgb(232, 237, 246);
    private static readonly Color DarkMuted = Color.FromArgb(166, 177, 196);
    private static readonly Color Chrome = Color.FromArgb(20, 27, 40);
    private static readonly Color DesignerChrome = Color.FromArgb(18, 24, 35);
    private static readonly Color SecondaryChrome = Color.FromArgb(30, 38, 52);
    private static readonly Color Accent = Color.FromArgb(78, 89, 232);
    private static readonly Color SecondaryAccent = Color.FromArgb(91, 104, 241);
    private static readonly Color Danger = Color.FromArgb(202, 83, 96);

    public static void Apply(Control root, bool darkMode)
    {
        ApplyControl(root, darkMode, chromeParent: false);
        root.Invalidate(invalidateChildren: true);
    }

    private static void ApplyControl(
        Control control,
        bool darkMode,
        bool chromeParent)
    {
        if (control is DesktopPreviewControl)
        {
            return;
        }

        var isChromeContainer =
            control is Panel &&
            (control.BackColor == Chrome ||
             control.BackColor == DesignerChrome);
        var inChrome = chromeParent || isChromeContainer;
        var background = darkMode ? DarkBackground : LightBackground;
        var card = darkMode ? DarkCard : LightCard;
        var input = darkMode ? DarkInput : LightCard;
        var text = darkMode ? DarkText : LightText;
        var muted = darkMode ? DarkMuted : LightMuted;

        switch (control)
        {
            case Form:
                control.BackColor = background;
                control.ForeColor = text;
                break;
            case SplitContainer split:
                split.BackColor = darkMode
                    ? Color.FromArgb(46, 56, 72)
                    : Color.FromArgb(43, 51, 65);
                break;
            case Panel or TableLayoutPanel or FlowLayoutPanel:
                if (!isChromeContainer)
                {
                    var isCard =
                        control.BackColor == LightCard ||
                        control.BackColor == DarkCard;
                    control.BackColor = isCard ? card : background;
                    control.ForeColor = text;
                }

                break;
            case ListView or ListBox or CheckedListBox:
                control.BackColor = input;
                control.ForeColor = text;
                break;
            case TextBox or ComboBox:
                control.BackColor = input;
                control.ForeColor = text;
                break;
            case Button button:
                ApplyButton(button, darkMode, inChrome);
                break;
            case CheckBox:
                control.BackColor = inChrome
                    ? control.Parent?.BackColor ?? DesignerChrome
                    : control.Parent?.BackColor ?? background;
                control.ForeColor = inChrome ? Color.White : text;
                break;
            case Label label:
                ApplyLabel(label, darkMode, inChrome, text, muted);
                break;
            default:
                if (!inChrome)
                {
                    control.ForeColor = text;
                }

                break;
        }

        foreach (Control child in control.Controls)
        {
            ApplyControl(child, darkMode, inChrome);
        }
    }

    private static void ApplyButton(Button button, bool darkMode, bool inChrome)
    {
        var isAccent =
            button.BackColor == Accent ||
            button.BackColor == SecondaryAccent ||
            button.Text.Contains("Apply", StringComparison.OrdinalIgnoreCase) ||
            button.Text.Contains("Organize", StringComparison.OrdinalIgnoreCase) ||
            button.Text.Contains("New category", StringComparison.OrdinalIgnoreCase) ||
            button.Text.Contains("Assign all", StringComparison.OrdinalIgnoreCase) ||
            button.Text.Equals("Save settings", StringComparison.OrdinalIgnoreCase);
        if (isAccent)
        {
            button.BackColor =
                button.Text.Contains("Assign all", StringComparison.OrdinalIgnoreCase)
                    ? SecondaryAccent
                    : Accent;
            button.ForeColor = Color.White;
            return;
        }

        button.BackColor = inChrome
            ? SecondaryChrome
            : darkMode
                ? DarkCard
                : LightCard;
        button.ForeColor = button.Text.Equals(
            "Delete",
            StringComparison.OrdinalIgnoreCase)
            ? Danger
            : inChrome
                ? Color.White
                : darkMode
                    ? DarkText
                    : Color.FromArgb(48, 58, 76);
    }

    private static void ApplyLabel(
        Label label,
        bool darkMode,
        bool inChrome,
        Color text,
        Color muted)
    {
        if (inChrome)
        {
            if (label.ForeColor != Color.FromArgb(164, 245, 205))
            {
                label.ForeColor = label.Font.Bold ? Color.White : DarkMuted;
            }

            return;
        }

        if (label.ForeColor == Accent ||
            label.ForeColor == SecondaryAccent)
        {
            label.ForeColor = darkMode
                ? Color.FromArgb(143, 154, 255)
                : Accent;
            return;
        }

        label.ForeColor = label.Font.Bold ? text : muted;
    }
}
