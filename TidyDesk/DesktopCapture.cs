using System.Drawing;
using System.Drawing.Imaging;

namespace TidyDesk;

internal static class DesktopCapture
{
    public static IReadOnlyList<DesktopDisplayInfo> GetDisplays()
    {
        var virtualScreen = SystemInformation.VirtualScreen;
        return Screen.AllScreens
            .Select(
                (screen, index) =>
                    new DesktopDisplayInfo(
                        NormalizeDisplayBounds(virtualScreen, screen.Bounds),
                        $"Display {index + 1}",
                        screen.Primary))
            .ToList();
    }

    public static Bitmap CaptureVirtualDesktop()
    {
        var bounds = SystemInformation.VirtualScreen;
        var bitmap = new Bitmap(
            Math.Max(1, bounds.Width),
            Math.Max(1, bounds.Height),
            PixelFormat.Format32bppPArgb);

        using var graphics = Graphics.FromImage(bitmap);
        graphics.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size);
        return bitmap;
    }

    internal static Rectangle NormalizeDisplayBounds(
        Rectangle virtualScreen,
        Rectangle displayBounds) =>
        new(
            displayBounds.Left - virtualScreen.Left,
            displayBounds.Top - virtualScreen.Top,
            displayBounds.Width,
            displayBounds.Height);
}

internal sealed record DesktopDisplayInfo(
    Rectangle Bounds,
    string Label,
    bool IsPrimary);
