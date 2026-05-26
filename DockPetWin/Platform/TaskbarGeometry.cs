using System.Windows;
using Forms = System.Windows.Forms;
using WpfPoint = System.Windows.Point;
using WpfSize = System.Windows.Size;

namespace DockPetWin.Platform;

public enum TaskbarEdge
{
    Bottom,
    Top,
    Left,
    Right,
    Unknown
}

public sealed record DisplayOption(string Id, string Title);

internal readonly record struct TaskbarActivityArea(
    Rect Screen,
    Rect WorkingArea,
    TaskbarEdge Edge,
    double MinX,
    double MaxX,
    double MinY,
    double MaxY)
{
    public bool UsesHorizontalMovement => Edge is TaskbarEdge.Bottom or TaskbarEdge.Top or TaskbarEdge.Unknown;

    public WpfPoint AnchorForPercent(double percent, WpfSize catSize)
    {
        var normalized = Math.Clamp(percent, 0, 100) / 100;

        return Edge switch
        {
            TaskbarEdge.Top => new WpfPoint(
                MinX + Math.Max(0, MaxX - MinX - catSize.Width) * normalized,
                WorkingArea.Top + catSize.Height - 6),
            TaskbarEdge.Left => new WpfPoint(
                WorkingArea.Left + catSize.Width - 6,
                MinY + Math.Max(0, MaxY - MinY - catSize.Height) * normalized + catSize.Height),
            TaskbarEdge.Right => new WpfPoint(
                WorkingArea.Right - 6,
                MinY + Math.Max(0, MaxY - MinY - catSize.Height) * normalized + catSize.Height),
            _ => new WpfPoint(
                MinX + Math.Max(0, MaxX - MinX - catSize.Width) * normalized,
                BottomEdgeAnchorY)
        };
    }

    public WpfPoint ClampAnchor(WpfPoint anchor, WpfSize catSize)
    {
        return Edge switch
        {
            TaskbarEdge.Left or TaskbarEdge.Right => new WpfPoint(
                anchor.X,
                Math.Clamp(anchor.Y, MinY + catSize.Height, MaxY)),
            TaskbarEdge.Top => new WpfPoint(
                Math.Clamp(anchor.X, MinX, MaxX - catSize.Width),
                Math.Max(anchor.Y, WorkingArea.Top + catSize.Height)),
            TaskbarEdge.Bottom => new WpfPoint(
                Math.Clamp(anchor.X, MinX, MaxX - catSize.Width),
                Math.Min(anchor.Y, BottomEdgeAnchorY)),
            _ => new WpfPoint(
                Math.Clamp(anchor.X, MinX, MaxX - catSize.Width),
                Math.Min(anchor.Y, BottomEdgeAnchorY))
        };
    }

    public WpfPoint MoveAnchor(WpfPoint anchor, double delta, WpfSize catSize)
    {
        return UsesHorizontalMovement
            ? ClampAnchor(new WpfPoint(anchor.X + delta, anchor.Y), catSize)
            : ClampAnchor(new WpfPoint(anchor.X, anchor.Y + delta), catSize);
    }

    public bool IsAtStart(WpfPoint anchor, WpfSize catSize)
    {
        return UsesHorizontalMovement
            ? anchor.X <= MinX + 0.1
            : anchor.Y <= MinY + catSize.Height + 0.1;
    }

    public bool IsAtEnd(WpfPoint anchor, WpfSize catSize)
    {
        return UsesHorizontalMovement
            ? anchor.X + catSize.Width >= MaxX - 0.1
            : anchor.Y >= MaxY - 0.1;
    }

    private double BottomEdgeAnchorY => WorkingArea.Bottom;
}

internal static class TaskbarGeometry
{
    public static IReadOnlyList<DisplayOption> DisplayOptions()
    {
        return Forms.Screen.AllScreens
            .Select(screen => new DisplayOption(
                screen.DeviceName,
                $"{screen.DeviceName} {screen.Bounds.Width}x{screen.Bounds.Height}" + (screen.Primary ? " 主显示器" : "")))
            .ToList();
    }

    public static TaskbarActivityArea Current(double dpiScaleX, double dpiScaleY, string? displayID)
    {
        var screen = SelectedScreen(displayID);
        var bounds = ToDip(screen.Bounds, dpiScaleX, dpiScaleY);
        var working = ToDip(screen.WorkingArea, dpiScaleX, dpiScaleY);
        var edge = InferEdge(bounds, working);

        return new TaskbarActivityArea(
            Screen: bounds,
            WorkingArea: working,
            Edge: edge,
            MinX: working.Left,
            MaxX: working.Right,
            MinY: working.Top,
            MaxY: working.Bottom);
    }

    private static Forms.Screen SelectedScreen(string? displayID)
    {
        if (!string.IsNullOrWhiteSpace(displayID))
        {
            var selected = Forms.Screen.AllScreens.FirstOrDefault(screen => screen.DeviceName == displayID);
            if (selected is not null)
            {
                return selected;
            }
        }

        return Forms.Screen.PrimaryScreen ?? Forms.Screen.AllScreens.First();
    }

    private static Rect ToDip(System.Drawing.Rectangle rectangle, double dpiScaleX, double dpiScaleY)
    {
        return new Rect(
            rectangle.Left / dpiScaleX,
            rectangle.Top / dpiScaleY,
            rectangle.Width / dpiScaleX,
            rectangle.Height / dpiScaleY);
    }

    private static TaskbarEdge InferEdge(Rect screen, Rect working)
    {
        const double threshold = 8;

        if (working.Bottom < screen.Bottom - threshold)
        {
            return TaskbarEdge.Bottom;
        }

        if (working.Top > screen.Top + threshold)
        {
            return TaskbarEdge.Top;
        }

        if (working.Left > screen.Left + threshold)
        {
            return TaskbarEdge.Left;
        }

        if (working.Right < screen.Right - threshold)
        {
            return TaskbarEdge.Right;
        }

        return TaskbarEdge.Unknown;
    }
}
