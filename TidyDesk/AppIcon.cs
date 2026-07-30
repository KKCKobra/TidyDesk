namespace TidyDesk;

internal static class AppIcon
{
    public static Icon Create()
    {
        using var stream = typeof(AppIcon).Assembly.GetManifestResourceStream(
            "TidyDesk.AppIcon.ico");
        if (stream is null)
        {
            return (Icon)SystemIcons.Application.Clone();
        }

        using var icon = new Icon(stream);
        return (Icon)icon.Clone();
    }
}
