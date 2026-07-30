using System.Drawing;

namespace TidyDesk;

public static class LayoutEngine
{
    public const int CellWidth = 88;
    public const int CellHeight = 92;
    public const int Padding = 12;
    public const int HeaderHeight = 36;
    public const int MinimumRegionWidth = CellWidth + (Padding * 2);
    public const int MinimumRegionHeight = CellHeight + HeaderHeight + Padding;

    public static Size DefaultIconSpacing => new(CellWidth, CellHeight);

    public static LayoutCapacity GetCapacity(Rectangle bounds)
        => GetCapacity(bounds, DefaultIconSpacing);

    public static LayoutCapacity GetCapacity(Rectangle bounds, Size iconSpacing)
    {
        ValidateIconSpacing(iconSpacing);
        var usableWidth = Math.Max(0, bounds.Width - (Padding * 2));
        var usableHeight = Math.Max(0, bounds.Height - HeaderHeight - Padding);
        return new LayoutCapacity(
            Math.Max(1, usableWidth / iconSpacing.Width),
            Math.Max(1, usableHeight / iconSpacing.Height));
    }

    public static Size GetMinimumRegionSize(Size iconSpacing)
    {
        ValidateIconSpacing(iconSpacing);
        return new Size(
            iconSpacing.Width + (Padding * 2),
            iconSpacing.Height + HeaderHeight + Padding);
    }

    public static IReadOnlyList<Point> GetPositions(
        RegionDefinition region,
        int iconCount)
        => GetPositions(region, iconCount, DefaultIconSpacing);

    public static IReadOnlyList<Point> GetPositions(
        RegionDefinition region,
        int iconCount,
        Size iconSpacing)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(iconCount);

        var capacity = GetCapacity(region.Bounds, iconSpacing);
        if (iconCount > capacity.Total)
        {
            throw new InvalidOperationException(
                $"“{region.Name}” holds {capacity.Total} icons but has {iconCount} assigned.");
        }

        var positions = new List<Point>(iconCount);
        for (var index = 0; index < iconCount; index++)
        {
            int column;
            int row;
            if (region.Flow == IconFlow.AcrossRows)
            {
                column = index % capacity.Columns;
                row = index / capacity.Columns;
            }
            else
            {
                row = index % capacity.Rows;
                column = index / capacity.Rows;
            }

            positions.Add(
                new Point(
                    region.Bounds.Left + Padding + (column * iconSpacing.Width),
                    region.Bounds.Top + HeaderHeight + (row * iconSpacing.Height)));
        }

        return positions;
    }

    public static IReadOnlyList<IconPlacement> CreatePlacements(
        IEnumerable<DesktopIconInfo> icons,
        OrganizerLayout layout)
        => CreatePlacements(icons, layout, DefaultIconSpacing);

    public static IReadOnlyList<IconPlacement> CreatePlacements(
        IEnumerable<DesktopIconInfo> icons,
        OrganizerLayout layout,
        Size iconSpacing)
    {
        var iconsByRegion = icons
            .Where(icon => layout.Assignments.ContainsKey(icon.DisplayName))
            .GroupBy(icon => layout.Assignments[icon.DisplayName])
            .ToDictionary(group => group.Key, group => group.ToList());

        var placements = new List<IconPlacement>();
        foreach (var region in layout.Regions)
        {
            if (!iconsByRegion.TryGetValue(region.Id, out var regionIcons))
            {
                continue;
            }

            regionIcons.Sort(
                (left, right) => StringComparer.CurrentCultureIgnoreCase.Compare(
                    left.DisplayName,
                    right.DisplayName));

            var positions = GetPositions(region, regionIcons.Count, iconSpacing);
            for (var index = 0; index < regionIcons.Count; index++)
            {
                placements.Add(
                    new IconPlacement(
                        regionIcons[index].DisplayName,
                        regionIcons[index].ShellIndex,
                        positions[index]));
            }
        }

        return placements;
    }

    private static void ValidateIconSpacing(Size iconSpacing)
    {
        if (iconSpacing.Width <= 0 || iconSpacing.Height <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(iconSpacing),
                "Desktop icon spacing must have positive dimensions.");
        }
    }
}
