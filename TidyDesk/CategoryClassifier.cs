namespace TidyDesk;

public static class CategoryClassifier
{
    private static readonly Dictionary<string, HashSet<string>> PresetExtensions =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["documents"] =
            [
                ".doc", ".docx", ".pdf", ".txt", ".rtf", ".odt", ".md",
            ],
            ["images"] =
            [
                ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp", ".svg", ".heic",
            ],
            ["videos"] =
            [
                ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".webm", ".m4v",
            ],
            ["audio"] =
            [
                ".mp3", ".wav", ".flac", ".m4a", ".aac", ".ogg",
            ],
            ["archives"] =
            [
                ".zip", ".rar", ".7z", ".tar", ".gz",
            ],
            ["spreadsheets"] =
            [
                ".xls", ".xlsx", ".csv", ".ods",
            ],
            ["presentations"] =
            [
                ".ppt", ".pptx", ".odp",
            ],
            ["code"] =
            [
                ".cs", ".py", ".js", ".ts", ".html", ".css", ".json", ".xml",
                ".sql", ".cpp", ".h", ".java", ".go", ".rs",
            ],
            ["installers"] =
            [
                ".exe", ".msi", ".msix", ".iso",
            ],
            ["shortcuts"] =
            [
                ".lnk", ".url",
            ],
        };

    private static readonly Dictionary<string, string[]> PresetNames =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["documents"] = ["document", "documents", "docs"],
            ["images"] = ["image", "images", "picture", "pictures", "photo", "photos"],
            ["videos"] = ["video", "videos", "movie", "movies"],
            ["audio"] = ["audio", "music", "songs"],
            ["archives"] = ["archive", "archives", "compressed", "zips"],
            ["spreadsheets"] = ["spreadsheet", "spreadsheets", "sheets"],
            ["presentations"] = ["presentation", "presentations", "slides"],
            ["code"] = ["code", "coding", "development", "dev"],
            ["installers"] = ["installer", "installers", "downloads", "setup"],
            ["shortcuts"] = ["shortcut", "shortcuts", "apps", "applications"],
        };

    public static int Apply(
        OrganizerLayout layout,
        IEnumerable<DesktopIconInfo> icons)
    {
        var changed = 0;

        foreach (var icon in icons)
        {
            if (layout.ManualOverrides.Contains(icon.DisplayName))
            {
                continue;
            }

            var match = layout.Regions
                .Select(
                    (region, index) =>
                        (Region: region, Score: GetScore(region, icon), Index: index))
                .Where(candidate => candidate.Score > 0)
                .OrderByDescending(candidate => candidate.Score)
                .ThenBy(candidate => candidate.Index)
                .FirstOrDefault();

            if (match.Region is not null)
            {
                if (!layout.Assignments.TryGetValue(icon.DisplayName, out var assignedId) ||
                    assignedId != match.Region.Id)
                {
                    layout.Assignments[icon.DisplayName] = match.Region.Id;
                    changed++;
                }

                continue;
            }

            if (layout.Assignments.ContainsKey(icon.DisplayName))
            {
                layout.Assignments.Remove(icon.DisplayName);
                changed++;
            }
        }

        return changed;
    }

    public static int GetScore(RegionDefinition region, DesktopIconInfo icon)
    {
        var extension = NormalizeExtension(icon.Extension);
        var names = new[]
        {
            icon.DisplayName,
            icon.SourcePath is null ? string.Empty : Path.GetFileName(icon.SourcePath),
        };
        var bestScore = 0;

        foreach (var pattern in ParsePatterns(region.AutoMatch))
        {
            if (IsExtensionPattern(pattern, extension))
            {
                bestScore = Math.Max(bestScore, 100);
            }
            else if (names.Any(name => WildcardContains(name, pattern)))
            {
                bestScore = Math.Max(bestScore, 80);
            }
        }

        var categoryName = region.Name.Trim();
        if (categoryName.Length >= 3 &&
            names.Any(name => name.Contains(
                categoryName,
                StringComparison.CurrentCultureIgnoreCase)))
        {
            bestScore = Math.Max(bestScore, 50);
        }

        foreach (var preset in PresetNames)
        {
            if (!preset.Value.Any(
                    name => categoryName.Contains(
                        name,
                        StringComparison.CurrentCultureIgnoreCase)))
            {
                continue;
            }

            if (PresetExtensions[preset.Key].Contains(extension))
            {
                bestScore = Math.Max(bestScore, 60);
            }
        }

        return bestScore;
    }

    private static IEnumerable<string> ParsePatterns(string value) =>
        value.Split(
                [',', ';'],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(pattern => pattern.Length > 0);

    private static bool IsExtensionPattern(string pattern, string extension)
    {
        var normalized = pattern.Trim();
        if (normalized.StartsWith("*.", StringComparison.Ordinal))
        {
            normalized = normalized[1..];
        }
        else if (!normalized.StartsWith('.') &&
                 !normalized.Contains('*') &&
                 !normalized.Contains('?') &&
                 normalized.Length <= 5 &&
                 normalized.All(character => char.IsLetterOrDigit(character)))
        {
            normalized = $".{normalized}";
        }

        return normalized.StartsWith('.') &&
               string.Equals(
                   NormalizeExtension(normalized),
                   extension,
                   StringComparison.OrdinalIgnoreCase);
    }

    private static bool WildcardContains(string value, string pattern)
    {
        if (value.Length == 0)
        {
            return false;
        }

        var normalized = pattern.Trim('*', ' ');
        return normalized.Length > 0 &&
               value.Contains(normalized, StringComparison.CurrentCultureIgnoreCase);
    }

    private static string NormalizeExtension(string extension) =>
        extension.Length == 0
            ? string.Empty
            : extension.StartsWith('.') ? extension.ToLowerInvariant() : $".{extension.ToLowerInvariant()}";
}
