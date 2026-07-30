namespace TidyDesk;

internal static class AppPaths
{
    public const string ProductName = "TidyDesk";

    // This identifier is retained only to import data from pre-TidyDesk builds.
    private const string LegacyDataDirectoryName = "Desktop Region Organizer";

    private static string LocalApplicationData =>
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

    private static string DataDirectory =>
        Path.Combine(LocalApplicationData, ProductName);

    private static string LegacyDataDirectory =>
        Path.Combine(LocalApplicationData, LegacyDataDirectoryName);

    public static string GetDataFilePath(string fileName)
    {
        var currentPath = Path.Combine(DataDirectory, fileName);
        if (File.Exists(currentPath))
        {
            return currentPath;
        }

        var legacyPath = Path.Combine(LegacyDataDirectory, fileName);
        if (!File.Exists(legacyPath))
        {
            return currentPath;
        }

        try
        {
            Directory.CreateDirectory(DataDirectory);
            File.Copy(legacyPath, currentPath, overwrite: false);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            // A failed compatibility copy should not prevent TidyDesk from starting.
        }

        return currentPath;
    }
}
