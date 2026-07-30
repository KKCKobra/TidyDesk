using System.Drawing;

namespace TidyDesk;

public enum IconFlow
{
    AcrossRows,
    DownColumns,
}

public sealed class DesktopIconInfo
{
    public required string DisplayName { get; init; }

    public required int ShellIndex { get; init; }

    public string? SourcePath { get; init; }

    public string Extension =>
        SourcePath is null
            ? Path.GetExtension(DisplayName)
            : Path.GetExtension(SourcePath);

    public override string ToString() => DisplayName;
}

public sealed class RegionDefinition
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public string Name { get; set; } = "New category";

    public Rectangle Bounds { get; set; }

    public IconFlow Flow { get; set; }

    public int ColorArgb { get; set; }

    public string AutoMatch { get; set; } = string.Empty;

    public RegionDefinition Clone() =>
        new()
        {
            Id = Id,
            Name = Name,
            Bounds = Bounds,
            Flow = Flow,
            ColorArgb = ColorArgb,
            AutoMatch = AutoMatch,
        };

    public override string ToString() => Name;
}

public sealed class OrganizerLayout
{
    public const int CurrentSchemaVersion = 2;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    public List<RegionDefinition> Regions { get; set; } = [];

    public Dictionary<string, Guid> Assignments { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    public HashSet<string> ManualOverrides { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    public OrganizerLayout Clone() =>
        new()
        {
            SchemaVersion = CurrentSchemaVersion,
            Regions = Regions.Select(region => region.Clone()).ToList(),
            Assignments = new Dictionary<string, Guid>(
                Assignments,
                StringComparer.OrdinalIgnoreCase),
            ManualOverrides = new HashSet<string>(
                ManualOverrides,
                StringComparer.OrdinalIgnoreCase),
        };
}

public readonly record struct IconPlacement(
    string DisplayName,
    int ShellIndex,
    Point Position);

public readonly record struct LayoutCapacity(int Columns, int Rows)
{
    public int Total => checked(Columns * Rows);
}

public readonly record struct ApplyResult(int Positioned, IReadOnlyList<string> Missing);

internal sealed class UndoState
{
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;

    public OrganizerLayout Layout { get; set; } = new();

    public List<IconPlacement> Positions { get; set; } = [];
}
