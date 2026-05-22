using Hardcodet.Wpf.TaskbarNotification;
using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace LowgiUI;

public partial class MainTaskBarIcon : TaskbarIcon
{
    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetCursorPos(out Point point);

    public MainTaskBarIcon() : base()
    {
        ContextMenu = (ContextMenu)Application.Current.FindResource("SysTrayMenu");
        ContextMenu.Opened += PositionContextMenu;
        BatteryIconDrawing.DrawUnknown(this);
    }

    public new void Dispose()
    {
        if (ContextMenu != null)
        {
            ContextMenu.Opened -= PositionContextMenu;
        }

        base.Dispose();
    }

    public static void PositionContextMenu(object sender, RoutedEventArgs e)
    {
        if (sender is ContextMenu menu)
        {
            _ = menu.Dispatcher.BeginInvoke(() => PositionContextMenu(menu));
        }
    }

    private static void PositionContextMenu(ContextMenu menu)
    {
        double menuWidth = menu.ActualWidth > 0 ? menu.ActualWidth : 280;
        double menuHeight = menu.ActualHeight > 0 ? menu.ActualHeight : 360;
        double cursorX = GetCursorX(menu);
        double targetX = cursorX - (menuWidth / 2);

        menu.Placement = PlacementMode.Absolute;
        menu.HorizontalOffset = Math.Clamp(targetX, SystemParameters.WorkArea.Left + 4, SystemParameters.WorkArea.Right - menuWidth - 4);
        menu.VerticalOffset = Math.Max(0, SystemParameters.WorkArea.Bottom - menuHeight - 4);
    }

    private static double GetCursorX(ContextMenu menu)
    {
        if (!GetCursorPos(out Point cursor))
        {
            return SystemParameters.WorkArea.Right;
        }

        PresentationSource? source = PresentationSource.FromVisual(menu);
        Matrix transform = source?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
        return transform.Transform(new System.Windows.Point(cursor.X, cursor.Y)).X;
    }
}

public class MainTaskbarIconWrapper : IDisposable
{
    #region IDisposable
    private bool disposedValue;

    protected virtual void Dispose(bool disposing)
    {
        if (!disposedValue)
        {
            if (disposing)
            {
                _taskbarIcon?.Dispose();
                LogiDeviceIcon.RefCountChanged -= OnRefCountChanged;
            }

            // TODO: free unmanaged resources (unmanaged objects) and override finalizer
            // TODO: set large fields to null
            disposedValue = true;
        }
    }

    // // TODO: override finalizer only if 'Dispose(bool disposing)' has code to free unmanaged resources
    // ~MainTaskbarIconWrapper()
    // {
    //     // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
    //     Dispose(disposing: false);
    // }

    public void Dispose()
    {
        // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
    #endregion

    private TaskbarIcon? _taskbarIcon = new MainTaskBarIcon();
    public MainTaskbarIconWrapper()
    {
        LogiDeviceIcon.RefCountChanged += OnRefCountChanged;
        OnRefCountChanged(LogiDeviceIcon.RefCount);
    }

    private void OnRefCountChanged(int refCount)
    {
        if (refCount == 0)
        {
            _taskbarIcon ??= new MainTaskBarIcon();
        }
        else
        {
            _taskbarIcon?.Dispose();
            _taskbarIcon = null;
        }
    }

    public void ShowWarning(string title, string message)
    {
        TaskbarIcon icon = _taskbarIcon ??= new MainTaskBarIcon();
        icon.ShowBalloonTip(title, message, BalloonIcon.Warning);
    }
}
