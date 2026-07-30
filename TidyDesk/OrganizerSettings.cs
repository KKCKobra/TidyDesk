using System.Text.Json;

namespace TidyDesk;

internal sealed class OrganizerSettings
{
    public bool DarkMode { get; set; } = true;

    public bool MinimizeOtherApplications { get; set; } = true;

    public bool ShowDisplayBoundaries { get; set; } = true;

    public bool SelectUncategorizedOnStartup { get; set; }

    public OrganizerSettings Clone() =>
        new()
        {
            DarkMode = DarkMode,
            MinimizeOtherApplications = MinimizeOtherApplications,
            ShowDisplayBoundaries = ShowDisplayBoundaries,
            SelectUncategorizedOnStartup = SelectUncategorizedOnStartup,
        };
}

internal static class OrganizerSettingsStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
    };

    private static string SettingsPath =>
        AppPaths.GetDataFilePath("settings.json");

    public static OrganizerSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath))
            {
                return new OrganizerSettings();
            }

            return Deserialize(File.ReadAllText(SettingsPath));
        }
        catch (Exception exception) when (
            exception is JsonException or IOException or UnauthorizedAccessException)
        {
            return new OrganizerSettings();
        }
    }

    public static void Save(OrganizerSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        File.WriteAllText(SettingsPath, Serialize(settings));
    }

    internal static string Serialize(OrganizerSettings settings) =>
        JsonSerializer.Serialize(settings, SerializerOptions);

    internal static OrganizerSettings Deserialize(string json) =>
        JsonSerializer.Deserialize<OrganizerSettings>(json, SerializerOptions) ??
        new OrganizerSettings();
}
