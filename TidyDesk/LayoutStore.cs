using System.Text.Json;
using System.Text.Json.Serialization;

namespace TidyDesk;

internal static class LayoutStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private static string SettingsPath => AppPaths.GetDataFilePath("layout.json");

    public static OrganizerLayout Load()
    {
        try
        {
            if (!File.Exists(SettingsPath))
            {
                return new OrganizerLayout();
            }

            var json = File.ReadAllText(SettingsPath);
            var layout = JsonSerializer.Deserialize<OrganizerLayout>(
                json,
                SerializerOptions);
            return Normalize(layout ?? new OrganizerLayout());
        }
        catch (JsonException)
        {
            return new OrganizerLayout();
        }
        catch (IOException)
        {
            return new OrganizerLayout();
        }
        catch (UnauthorizedAccessException)
        {
            return new OrganizerLayout();
        }
    }

    public static void Save(OrganizerLayout layout)
    {
        layout.SchemaVersion = OrganizerLayout.CurrentSchemaVersion;
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        var json = JsonSerializer.Serialize(layout, SerializerOptions);
        File.WriteAllText(SettingsPath, json);
    }

    internal static OrganizerLayout Normalize(OrganizerLayout layout)
    {
        var wasLegacyLayout =
            layout.SchemaVersion < OrganizerLayout.CurrentSchemaVersion;
        layout.Assignments = new Dictionary<string, Guid>(
            layout.Assignments,
            StringComparer.OrdinalIgnoreCase);
        layout.ManualOverrides = new HashSet<string>(
            layout.ManualOverrides,
            StringComparer.OrdinalIgnoreCase);

        if (wasLegacyLayout)
        {
            layout.ManualOverrides.UnionWith(layout.Assignments.Keys);
        }

        layout.SchemaVersion = OrganizerLayout.CurrentSchemaVersion;
        return layout;
    }
}
