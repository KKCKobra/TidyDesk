using System.Text.Json;
using System.Text.Json.Serialization;

namespace TidyDesk;

internal static class UndoStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private static string UndoPath =>
        AppPaths.GetDataFilePath("undo.json");

    public static bool Exists => File.Exists(UndoPath);

    public static bool TrySave(UndoState state)
    {
        try
        {
            var directory = Path.GetDirectoryName(UndoPath)!;
            Directory.CreateDirectory(directory);
            File.WriteAllText(
                UndoPath,
                JsonSerializer.Serialize(state, SerializerOptions));
            return true;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public static UndoState? Load()
    {
        try
        {
            if (!File.Exists(UndoPath))
            {
                return null;
            }

            return JsonSerializer.Deserialize<UndoState>(
                File.ReadAllText(UndoPath),
                SerializerOptions);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    public static void Clear()
    {
        try
        {
            File.Delete(UndoPath);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            // A stale undo file is harmless. The next successful layout replaces it.
        }
    }
}
