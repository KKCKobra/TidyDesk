using System.Drawing.Drawing2D;
using IconLayout = TidyDesk.LayoutEngine;

namespace TidyDesk;

internal sealed class DesktopPreviewControl : Control
{
    private const int HandleSize = 14;

    private Bitmap? _desktopImage;
    private Size _desktopSize = new(1920, 1080);
    private Size _iconSpacing = IconLayout.DefaultIconSpacing;
    private IReadOnlyList<DesktopDisplayInfo> _displays = [];
    private bool _showDisplayBoundaries = true;
    private OrganizerLayout _layout = new();
    private IReadOnlyList<DesktopIconInfo> _icons = [];
    private Guid? _selectedRegionId;
    private DragMode _dragMode;
    private Point _lastDesktopPoint;

    public DesktopPreviewControl()
    {
        DoubleBuffered = true;
        ResizeRedraw = true;
        BackColor = Color.FromArgb(12, 16, 24);
        Cursor = Cursors.Default;
        TabStop = true;
    }

    public event EventHandler? SelectedRegionChanged;

    public event EventHandler? RegionBoundsChanged;

    public Guid? SelectedRegionId => _selectedRegionId;

    public void SetContent(
        Bitmap desktopImage,
        Size desktopSize,
        Size iconSpacing,
        IReadOnlyList<DesktopDisplayInfo> displays,
        bool showDisplayBoundaries,
        OrganizerLayout layout,
        IReadOnlyList<DesktopIconInfo> icons)
    {
        _desktopImage = desktopImage;
        _desktopSize = desktopSize;
        _iconSpacing = iconSpacing;
        _displays = displays;
        _showDisplayBoundaries = showDisplayBoundaries;
        _layout = layout;
        _icons = icons;
        Invalidate();
    }

    public void SelectRegion(Guid? regionId)
    {
        if (_selectedRegionId == regionId)
        {
            return;
        }

        _selectedRegionId = regionId;
        Invalidate();
        SelectedRegionChanged?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        e.Graphics.TextRenderingHint =
            System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        var previewBounds = GetPreviewBounds();
        using var background = new SolidBrush(Color.FromArgb(12, 16, 24));
        e.Graphics.FillRectangle(background, ClientRectangle);

        if (_desktopImage is not null)
        {
            e.Graphics.DrawImage(_desktopImage, previewBounds);
        }

        using var veil = new SolidBrush(Color.FromArgb(82, 5, 9, 16));
        e.Graphics.FillRectangle(veil, previewBounds);

        using var outline = new Pen(Color.FromArgb(65, 255, 255, 255), 1);
        e.Graphics.DrawRectangle(outline, previewBounds);
        DrawDisplayBoundaries(e.Graphics);

        if (_layout.Regions.Count == 0)
        {
            DrawEmptyState(e.Graphics, previewBounds);
            return;
        }

        foreach (var region in _layout.Regions)
        {
            DrawRegion(e.Graphics, region);
        }
    }

    private void DrawDisplayBoundaries(Graphics graphics)
    {
        if (!_showDisplayBoundaries || _displays.Count <= 1)
        {
            return;
        }

        using var shadowPen = new Pen(Color.FromArgb(190, 4, 8, 15), 5);
        using var displayFont = new Font(Font.FontFamily, 8.2f, FontStyle.Bold);
        using var labelText = new SolidBrush(Color.White);
        using var labelFill = new SolidBrush(Color.FromArgb(220, 13, 18, 27));

        foreach (var display in _displays)
        {
            var bounds = DesktopToCanvas(display.Bounds);
            bounds.Width = Math.Max(1, bounds.Width - 1);
            bounds.Height = Math.Max(1, bounds.Height - 1);

            graphics.DrawRectangle(shadowPen, bounds);
            using var borderPen = new Pen(
                display.IsPrimary
                    ? Color.FromArgb(245, 104, 208, 255)
                    : Color.FromArgb(235, 255, 255, 255),
                display.IsPrimary ? 3f : 2.5f);
            graphics.DrawRectangle(borderPen, bounds);

            var label = display.IsPrimary
                ? $"{display.Label.ToUpperInvariant()}  •  PRIMARY"
                : display.Label.ToUpperInvariant();
            var labelSize = graphics.MeasureString(label, displayFont);
            var labelBounds = new RectangleF(
                bounds.Left + 8,
                bounds.Top + 8,
                labelSize.Width + 16,
                labelSize.Height + 8);
            graphics.FillRoundedRectangle(
                labelFill,
                Rectangle.Round(labelBounds),
                new Size(7, 7));
            graphics.DrawString(
                label,
                displayFont,
                labelText,
                labelBounds.Left + 8,
                labelBounds.Top + 4);
        }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left)
        {
            return;
        }

        Focus();
        var hit = HitTest(e.Location);
        if (hit.Region is null)
        {
            SelectRegion(null);
            return;
        }

        SelectRegion(hit.Region.Id);
        _dragMode = hit.IsResizeHandle ? DragMode.Resize : DragMode.Move;
        _lastDesktopPoint = CanvasToDesktop(e.Location);
        Capture = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        if (_dragMode == DragMode.None)
        {
            var hit = HitTest(e.Location);
            Cursor = hit.IsResizeHandle
                ? Cursors.SizeNWSE
                : hit.Region is null
                    ? Cursors.Default
                    : Cursors.SizeAll;
            return;
        }

        var region = GetSelectedRegion();
        if (region is null)
        {
            return;
        }

        var desktopPoint = CanvasToDesktop(e.Location);
        var delta = new Size(
            desktopPoint.X - _lastDesktopPoint.X,
            desktopPoint.Y - _lastDesktopPoint.Y);
        if (delta.Width == 0 && delta.Height == 0)
        {
            return;
        }

        var bounds = region.Bounds;
        if (_dragMode == DragMode.Move)
        {
            var left = Math.Clamp(
                bounds.Left + delta.Width,
                0,
                Math.Max(0, _desktopSize.Width - bounds.Width));
            var top = Math.Clamp(
                bounds.Top + delta.Height,
                0,
                Math.Max(0, _desktopSize.Height - bounds.Height));
            region.Bounds = new Rectangle(left, top, bounds.Width, bounds.Height);
        }
        else
        {
            var minimumRegionSize = IconLayout.GetMinimumRegionSize(_iconSpacing);
            var width = Math.Clamp(
                bounds.Width + delta.Width,
                minimumRegionSize.Width,
                Math.Max(minimumRegionSize.Width, _desktopSize.Width - bounds.Left));
            var height = Math.Clamp(
                bounds.Height + delta.Height,
                minimumRegionSize.Height,
                Math.Max(minimumRegionSize.Height, _desktopSize.Height - bounds.Top));
            region.Bounds = new Rectangle(bounds.Left, bounds.Top, width, height);
        }

        _lastDesktopPoint = desktopPoint;
        Invalidate();
        RegionBoundsChanged?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button != MouseButtons.Left)
        {
            return;
        }

        _dragMode = DragMode.None;
        Capture = false;
    }

    protected override void OnMouseCaptureChanged(EventArgs e)
    {
        base.OnMouseCaptureChanged(e);
        if (!Capture)
        {
            _dragMode = DragMode.None;
        }
    }

    private void DrawEmptyState(Graphics graphics, Rectangle previewBounds)
    {
        var box = new Rectangle(
            previewBounds.Left + (previewBounds.Width / 2) - 190,
            previewBounds.Top + (previewBounds.Height / 2) - 58,
            380,
            116);
        using var fill = new SolidBrush(Color.FromArgb(210, 20, 26, 38));
        using var border = new Pen(Color.FromArgb(90, 255, 255, 255), 1);
        using var titleFont = new Font(Font.FontFamily, 13, FontStyle.Bold);
        using var bodyFont = new Font(Font.FontFamily, 9);
        using var titleBrush = new SolidBrush(Color.White);
        using var bodyBrush = new SolidBrush(Color.FromArgb(190, 225, 231, 240));

        graphics.FillRoundedRectangle(fill, box, new Size(12, 12));
        graphics.DrawRoundedRectangle(border, box, new Size(12, 12));
        graphics.DrawString(
            "Create your first category",
            titleFont,
            titleBrush,
            box.Left + 22,
            box.Top + 22);
        graphics.DrawString(
            "Then drag its area and resize the corner handle.",
            bodyFont,
            bodyBrush,
            box.Left + 22,
            box.Top + 57);
    }

    private void DrawRegion(Graphics graphics, RegionDefinition region)
    {
        var bounds = DesktopToCanvas(region.Bounds);
        if (bounds.Width < 2 || bounds.Height < 2)
        {
            return;
        }

        var color = Color.FromArgb(region.ColorArgb);
        var selected = region.Id == _selectedRegionId;
        using var fill = new SolidBrush(Color.FromArgb(selected ? 78 : 55, color));
        using var headerFill = new SolidBrush(Color.FromArgb(selected ? 205 : 170, color));
        using var border = new Pen(
            selected ? Color.White : Color.FromArgb(215, color),
            selected ? 2.5f : 1.5f);
        using var gridPen = new Pen(Color.FromArgb(50, 255, 255, 255), 1)
        {
            DashStyle = DashStyle.Dot,
        };

        graphics.FillRoundedRectangle(fill, bounds, new Size(10, 10));
        var headerHeight = Math.Max(
            22,
            DesktopLengthToCanvas(IconLayout.HeaderHeight, vertical: true));
        var header = new Rectangle(
            bounds.Left,
            bounds.Top,
            bounds.Width,
            Math.Min(bounds.Height, headerHeight));
        graphics.FillRoundedRectangle(headerFill, header, new Size(10, 10));

        DrawGrid(graphics, region, bounds, gridPen);
        DrawAssignedIcons(graphics, region);

        graphics.DrawRoundedRectangle(border, bounds, new Size(10, 10));

        var capacity = IconLayout.GetCapacity(region.Bounds, _iconSpacing);
        using var nameFont = new Font(Font.FontFamily, 9.5f, FontStyle.Bold);
        using var labelBrush = new SolidBrush(Color.White);
        var title = $"{region.Name}   {capacity.Columns} × {capacity.Rows}";
        graphics.DrawString(
            title,
            nameFont,
            labelBrush,
            new RectangleF(
                bounds.Left + 10,
                bounds.Top + 5,
                Math.Max(0, bounds.Width - 20),
                header.Height - 5));

        if (selected)
        {
            var handle = GetResizeHandle(bounds);
            using var handleFill = new SolidBrush(Color.White);
            using var handleBorder = new Pen(Color.FromArgb(160, color), 2);
            graphics.FillEllipse(handleFill, handle);
            graphics.DrawEllipse(handleBorder, handle);
        }
    }

    private void DrawGrid(
        Graphics graphics,
        RegionDefinition region,
        Rectangle canvasBounds,
        Pen gridPen)
    {
        var capacity = IconLayout.GetCapacity(region.Bounds, _iconSpacing);
        var contentTop = DesktopToCanvas(
            new Point(
                region.Bounds.Left,
                region.Bounds.Top + IconLayout.HeaderHeight)).Y;

        for (var column = 1; column < capacity.Columns; column++)
        {
            var desktopX = region.Bounds.Left +
                           IconLayout.Padding +
                           (column * _iconSpacing.Width);
            var x = DesktopToCanvas(new Point(desktopX, 0)).X;
            graphics.DrawLine(
                gridPen,
                x,
                contentTop,
                x,
                canvasBounds.Bottom - 1);
        }

        for (var row = 1; row < capacity.Rows; row++)
        {
            var desktopY = region.Bounds.Top +
                           IconLayout.HeaderHeight +
                           (row * _iconSpacing.Height);
            var y = DesktopToCanvas(new Point(0, desktopY)).Y;
            graphics.DrawLine(
                gridPen,
                canvasBounds.Left + 1,
                y,
                canvasBounds.Right - 1,
                y);
        }
    }

    private void DrawAssignedIcons(Graphics graphics, RegionDefinition region)
    {
        var assigned = _icons
            .Where(
                icon => _layout.Assignments.TryGetValue(icon.DisplayName, out var regionId) &&
                        regionId == region.Id)
            .OrderBy(icon => icon.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        var capacity = IconLayout.GetCapacity(region.Bounds, _iconSpacing);
        var visibleCount = Math.Min(assigned.Count, capacity.Total);
        IReadOnlyList<Point> positions;
        try
        {
            positions = IconLayout.GetPositions(region, visibleCount, _iconSpacing);
        }
        catch (InvalidOperationException)
        {
            return;
        }

        using var iconFill = new SolidBrush(Color.FromArgb(235, 242, 246, 252));
        using var foldFill = new SolidBrush(Color.FromArgb(190, 197, 213, 235));
        using var textBrush = new SolidBrush(Color.White);
        using var textFont = new Font(Font.FontFamily, 7.2f, FontStyle.Regular);

        for (var index = 0; index < visibleCount; index++)
        {
            var cell = DesktopToCanvas(
                new Rectangle(
                    positions[index].X,
                    positions[index].Y,
                    _iconSpacing.Width,
                    _iconSpacing.Height));

            var iconSize = Math.Clamp(Math.Min(cell.Width, cell.Height) / 3, 8, 22);
            var iconBounds = new Rectangle(
                cell.Left + Math.Max(2, (cell.Width - iconSize) / 2),
                cell.Top + 5,
                iconSize,
                iconSize);
            graphics.FillRoundedRectangle(iconFill, iconBounds, new Size(3, 3));
            graphics.FillRectangle(
                foldFill,
                iconBounds.Right - Math.Max(3, iconSize / 4),
                iconBounds.Top,
                Math.Max(3, iconSize / 4),
                Math.Max(3, iconSize / 4));

            var textBounds = new RectangleF(
                cell.Left + 2,
                iconBounds.Bottom + 2,
                Math.Max(0, cell.Width - 4),
                Math.Max(0, cell.Bottom - iconBounds.Bottom - 3));
            using var format = new StringFormat
            {
                Alignment = StringAlignment.Center,
                Trimming = StringTrimming.EllipsisCharacter,
                FormatFlags = StringFormatFlags.NoWrap,
            };
            graphics.DrawString(
                assigned[index].DisplayName,
                textFont,
                textBrush,
                textBounds,
                format);
        }
    }

    private HitResult HitTest(Point canvasPoint)
    {
        for (var index = _layout.Regions.Count - 1; index >= 0; index--)
        {
            var region = _layout.Regions[index];
            var bounds = DesktopToCanvas(region.Bounds);
            if (!bounds.Contains(canvasPoint))
            {
                continue;
            }

            return new HitResult(
                region,
                GetResizeHandle(bounds).Contains(canvasPoint));
        }

        return new HitResult(null, false);
    }

    private Rectangle GetPreviewBounds()
    {
        const int margin = 20;
        var availableWidth = Math.Max(1, ClientSize.Width - (margin * 2));
        var availableHeight = Math.Max(1, ClientSize.Height - (margin * 2));
        var scale = Math.Min(
            (float)availableWidth / Math.Max(1, _desktopSize.Width),
            (float)availableHeight / Math.Max(1, _desktopSize.Height));
        var width = Math.Max(1, (int)Math.Round(_desktopSize.Width * scale));
        var height = Math.Max(1, (int)Math.Round(_desktopSize.Height * scale));
        return new Rectangle(
            (ClientSize.Width - width) / 2,
            (ClientSize.Height - height) / 2,
            width,
            height);
    }

    private Rectangle DesktopToCanvas(Rectangle desktopRectangle)
    {
        var topLeft = DesktopToCanvas(desktopRectangle.Location);
        var bottomRight = DesktopToCanvas(
            new Point(desktopRectangle.Right, desktopRectangle.Bottom));
        return Rectangle.FromLTRB(
            topLeft.X,
            topLeft.Y,
            bottomRight.X,
            bottomRight.Y);
    }

    private Point DesktopToCanvas(Point desktopPoint)
    {
        var preview = GetPreviewBounds();
        return new Point(
            preview.Left +
            (int)Math.Round(desktopPoint.X * (double)preview.Width / _desktopSize.Width),
            preview.Top +
            (int)Math.Round(desktopPoint.Y * (double)preview.Height / _desktopSize.Height));
    }

    private Point CanvasToDesktop(Point canvasPoint)
    {
        var preview = GetPreviewBounds();
        return new Point(
            Math.Clamp(
                (int)Math.Round(
                    (canvasPoint.X - preview.Left) *
                    (double)_desktopSize.Width /
                    preview.Width),
                0,
                _desktopSize.Width),
            Math.Clamp(
                (int)Math.Round(
                    (canvasPoint.Y - preview.Top) *
                    (double)_desktopSize.Height /
                    preview.Height),
                0,
                _desktopSize.Height));
    }

    private int DesktopLengthToCanvas(int length, bool vertical)
    {
        var preview = GetPreviewBounds();
        var desktopLength = vertical ? _desktopSize.Height : _desktopSize.Width;
        var canvasLength = vertical ? preview.Height : preview.Width;
        return (int)Math.Round(length * (double)canvasLength / desktopLength);
    }

    private static Rectangle GetResizeHandle(Rectangle regionBounds) =>
        new(
            regionBounds.Right - HandleSize,
            regionBounds.Bottom - HandleSize,
            HandleSize,
            HandleSize);

    private RegionDefinition? GetSelectedRegion() =>
        _selectedRegionId is null
            ? null
            : _layout.Regions.FirstOrDefault(
                region => region.Id == _selectedRegionId.Value);

    private enum DragMode
    {
        None,
        Move,
        Resize,
    }

    private readonly record struct HitResult(
        RegionDefinition? Region,
        bool IsResizeHandle);
}
