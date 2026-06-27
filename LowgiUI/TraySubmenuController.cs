using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace LowgiUI;

public static partial class TraySubmenuController
{
    private const double MenuSpacing = 4;
    private const double BoundsInflate = 6;
    private const double HoverEdgeDeadZone = 5;
    private const int OpenDelayMs = 120;
    private const int AimDelayMs = 300;
    private const int CloseDelayMs = 300;
    private const int PointerHistoryMs = 350;
    private const int MaxPointerHistory = 6;

    private static readonly DependencyProperty ControllerProperty = DependencyProperty.RegisterAttached(
        "Controller",
        typeof(Controller),
        typeof(TraySubmenuController),
        new PropertyMetadata(null));

    public static readonly DependencyProperty EnableProperty = DependencyProperty.RegisterAttached(
        "Enable",
        typeof(bool),
        typeof(TraySubmenuController),
        new PropertyMetadata(false, OnEnableChanged));

    public static readonly DependencyProperty SubmenuArrowTextProperty = DependencyProperty.RegisterAttached(
        "SubmenuArrowText",
        typeof(string),
        typeof(TraySubmenuController),
        new FrameworkPropertyMetadata("\u203A", FrameworkPropertyMetadataOptions.Inherits));

    public static readonly DependencyProperty SubmenuArrowColumnProperty = DependencyProperty.RegisterAttached(
        "SubmenuArrowColumn",
        typeof(int),
        typeof(TraySubmenuController),
        new FrameworkPropertyMetadata(2, FrameworkPropertyMetadataOptions.Inherits));

    public static readonly DependencyProperty SubmenuBorderMarginProperty = DependencyProperty.RegisterAttached(
        "SubmenuBorderMargin",
        typeof(Thickness),
        typeof(TraySubmenuController),
        new FrameworkPropertyMetadata(new Thickness(MenuSpacing, 0, 0, 0), FrameworkPropertyMetadataOptions.Inherits));

    [StructLayout(LayoutKind.Sequential)]
    private struct WinPoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WinRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public WinRect Monitor;
        public WinRect Work;
        public uint Flags;
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetCursorPos(out WinPoint point);

    [LibraryImport("user32.dll")]
    private static partial IntPtr MonitorFromPoint(WinPoint point, uint flags);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetMonitorInfoW(IntPtr monitor, ref MonitorInfo monitorInfo);

    public static bool GetEnable(DependencyObject element) => (bool)element.GetValue(EnableProperty);

    public static void SetEnable(DependencyObject element, bool value) => element.SetValue(EnableProperty, value);

    public static string GetSubmenuArrowText(DependencyObject element) => (string)element.GetValue(SubmenuArrowTextProperty);

    public static void SetSubmenuArrowText(DependencyObject element, string value) => element.SetValue(SubmenuArrowTextProperty, value);

    public static int GetSubmenuArrowColumn(DependencyObject element) => (int)element.GetValue(SubmenuArrowColumnProperty);

    public static void SetSubmenuArrowColumn(DependencyObject element, int value) => element.SetValue(SubmenuArrowColumnProperty, value);

    public static Thickness GetSubmenuBorderMargin(DependencyObject element) => (Thickness)element.GetValue(SubmenuBorderMarginProperty);

    public static void SetSubmenuBorderMargin(DependencyObject element, Thickness value) => element.SetValue(SubmenuBorderMarginProperty, value);

    private static void OnEnableChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        if (dependencyObject is not ContextMenu menu)
        {
            return;
        }

        if ((bool)e.NewValue)
        {
            Controller controller = new(menu);
            menu.SetValue(ControllerProperty, controller);
            controller.Attach();
        }
        else if (menu.GetValue(ControllerProperty) is Controller controller)
        {
            controller.Detach();
            menu.ClearValue(ControllerProperty);
        }
    }

    private sealed class Controller
    {
        private readonly ContextMenu _menu;
        private readonly DispatcherTimer _openTimer;
        private readonly DispatcherTimer _closeTimer;
        private readonly List<MenuItem> _openItems = [];
        private readonly List<PointerSample> _pointerHistory = [];

        private MenuItem? _pendingItem;
        private MenuItem? _activeItem;
        private Popup? _activePopup;
        private Rect _activeItemBounds;
        private Rect _activePopupBounds;
        private bool _activeOpensLeft;
        private bool? _submenusOpenLeft;
        private bool _isClosingSubmenus;

        private readonly record struct PointerSample(Point Position, DateTimeOffset Timestamp);

        public Controller(ContextMenu menu)
        {
            _menu = menu;
            _openTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(OpenDelayMs) };
            _closeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(CloseDelayMs) };
            _openTimer.Tick += OpenTimerTick;
            _closeTimer.Tick += CloseTimerTick;
        }

        public void Attach()
        {
            _menu.Opened += MenuOpened;
            _menu.Closed += MenuClosed;
            _menu.AddHandler(MenuItem.MouseEnterEvent, new MouseEventHandler(MenuItemMouseEnter), true);
            _menu.AddHandler(MenuItem.SubmenuOpenedEvent, new RoutedEventHandler(SubmenuOpened), true);
            _menu.AddHandler(MenuItem.SubmenuClosedEvent, new RoutedEventHandler(SubmenuClosed), true);
            _menu.AddHandler(UIElement.MouseMoveEvent, new MouseEventHandler(MenuMouseMove), true);
            _menu.AddHandler(UIElement.MouseLeaveEvent, new MouseEventHandler(MenuMouseLeave), true);
        }

        public void Detach()
        {
            _openTimer.Stop();
            _closeTimer.Stop();
            _menu.Opened -= MenuOpened;
            _menu.Closed -= MenuClosed;
            _menu.RemoveHandler(MenuItem.MouseEnterEvent, new MouseEventHandler(MenuItemMouseEnter));
            _menu.RemoveHandler(MenuItem.SubmenuOpenedEvent, new RoutedEventHandler(SubmenuOpened));
            _menu.RemoveHandler(MenuItem.SubmenuClosedEvent, new RoutedEventHandler(SubmenuClosed));
            _menu.RemoveHandler(UIElement.MouseMoveEvent, new MouseEventHandler(MenuMouseMove));
            _menu.RemoveHandler(UIElement.MouseLeaveEvent, new MouseEventHandler(MenuMouseLeave));
        }

        private void MenuOpened(object sender, RoutedEventArgs e)
        {
            _menu.Dispatcher.BeginInvoke(ConfigureAllOpenItems, DispatcherPriority.Loaded);
        }

        private void MenuClosed(object sender, RoutedEventArgs e)
        {
            _openTimer.Stop();
            _closeTimer.Stop();
            _pendingItem = null;
            _activeItem = null;
            _activePopup = null;
            _submenusOpenLeft = null;
            _pointerHistory.Clear();
            _openItems.Clear();
        }

        private void MenuItemMouseEnter(object sender, MouseEventArgs e)
        {
            TrackPointer(e);

            if (FindParentMenuItem(e.OriginalSource as DependencyObject) is not { IsEnabled: true } menuItem)
            {
                return;
            }

            if (!menuItem.HasItems)
            {
                CloseSubmenusForRootLeaf(menuItem);
                return;
            }

            if (IsNearMenuItemEdge(menuItem, e))
            {
                return;
            }

            ConfigureMenuItem(menuItem);
            QueueSubmenuOpen(menuItem, GetOpenDelay(menuItem));
            _closeTimer.Stop();
        }

        private bool IsNearMenuItemEdge(MenuItem menuItem, MouseEventArgs e)
        {
            if (_activeItem is null || menuItem == _activeItem || menuItem.ActualHeight <= HoverEdgeDeadZone * 2)
            {
                return false;
            }

            double y = e.GetPosition(menuItem).Y;
            return y <= HoverEdgeDeadZone || y >= menuItem.ActualHeight - HoverEdgeDeadZone;
        }

        private void SubmenuOpened(object sender, RoutedEventArgs e)
        {
            if (e.OriginalSource is not MenuItem menuItem)
            {
                return;
            }

            if (menuItem != _activeItem && IsPointerAimingAtActiveSubmenu(menuItem))
            {
                QueueSubmenuOpen(menuItem, TimeSpan.FromMilliseconds(AimDelayMs));
                menuItem.IsSubmenuOpen = false;
                if (_activeItem is { HasItems: true, IsEnabled: true })
                {
                    _activeItem.IsSubmenuOpen = true;
                }

                return;
            }

            ConfigureMenuItem(menuItem);
            _activeItem = menuItem;
            _activePopup = FindPopup(menuItem);
            AddOpenItem(menuItem);
            UpdateActiveBounds();
            ConfigureAllOpenItems();
        }

        private void SubmenuClosed(object sender, RoutedEventArgs e)
        {
            if (e.OriginalSource is not MenuItem menuItem)
            {
                return;
            }

            if (_isClosingSubmenus)
            {
                return;
            }

            if (_menu.IsOpen && ShouldKeepSubmenuOpen(menuItem))
            {
                _menu.Dispatcher.BeginInvoke(() => menuItem.IsSubmenuOpen = true, DispatcherPriority.Input);
                return;
            }

            _openItems.Remove(menuItem);
            if (menuItem == _activeItem)
            {
                _activeItem = null;
                _activePopup = null;
            }
        }

        private void MenuMouseMove(object sender, MouseEventArgs e)
        {
            TrackPointer(e);

            if (FindParentMenuItem(e.OriginalSource as DependencyObject) is not { IsEnabled: true } menuItem)
            {
                return;
            }

            if (!menuItem.HasItems)
            {
                CloseSubmenusForRootLeaf(menuItem);
            }
            else if (menuItem != _activeItem && !IsNearMenuItemEdge(menuItem, e))
            {
                ConfigureMenuItem(menuItem);
                QueueSubmenuOpen(menuItem, GetOpenDelay(menuItem));
            }

            if (IsCursorInsideMenuSystem())
            {
                _closeTimer.Stop();
            }
        }

        private void CloseSubmenusForRootLeaf(MenuItem menuItem)
        {
            if (ItemsControl.ItemsControlFromItemContainer(menuItem) != _menu)
            {
                return;
            }

            _openTimer.Stop();
            _pendingItem = null;
            CloseOpenSubmenus();
            _activeItem = null;
            _activePopup = null;
        }

        private void MenuMouseLeave(object sender, MouseEventArgs e)
        {
            _closeTimer.Stop();
            _closeTimer.Start();
        }

        private void OpenTimerTick(object? sender, EventArgs e)
        {
            _openTimer.Stop();
            if (_pendingItem is not { HasItems: true, IsEnabled: true } menuItem)
            {
                return;
            }

            ConfigureMenuItem(menuItem);
            _activeItem = menuItem;
            menuItem.IsSubmenuOpen = true;
            _menu.Dispatcher.BeginInvoke(UpdateActiveBounds, DispatcherPriority.Loaded);
        }

        private void QueueSubmenuOpen(MenuItem menuItem, TimeSpan delay)
        {
            _pendingItem = menuItem;
            _openTimer.Stop();
            _openTimer.Interval = delay;
            _openTimer.Start();
        }

        private TimeSpan GetOpenDelay(MenuItem menuItem)
        {
            return IsPointerAimingAtActiveSubmenu(menuItem)
                ? TimeSpan.FromMilliseconds(AimDelayMs)
                : TimeSpan.FromMilliseconds(OpenDelayMs);
        }

        private void CloseTimerTick(object? sender, EventArgs e)
        {
            _closeTimer.Stop();
            if (IsCursorInsideMenuSystem())
            {
                return;
            }

            if (_activeItem is not null || _openItems.Count > 0)
            {
                CloseOpenSubmenus();
            }

            _activeItem = null;
            _activePopup = null;
        }

        private void ConfigureAllOpenItems()
        {
            ConfigureItems(_menu);
        }

        private void ConfigureItems(ItemsControl parent)
        {
            parent.UpdateLayout();

            for (int i = 0; i < parent.Items.Count; i++)
            {
                MenuItem? menuItem = parent.Items[i] as MenuItem
                    ?? parent.ItemContainerGenerator.ContainerFromIndex(i) as MenuItem;

                if (menuItem is null)
                {
                    continue;
                }

                ConfigureMenuItem(menuItem);
                ConfigureItems(menuItem);
            }
        }

        private void ConfigureMenuItem(MenuItem menuItem)
        {
            if (!menuItem.HasItems)
            {
                return;
            }

            menuItem.ApplyTemplate();
            Popup? popup = FindPopup(menuItem);
            if (popup is null)
            {
                return;
            }

            popup.PlacementTarget = menuItem;
            popup.Placement = PlacementMode.Custom;
            popup.HorizontalOffset = 0;
            popup.VerticalOffset = 0;
            popup.CustomPopupPlacementCallback = (popupSize, targetSize, offset) => GetPlacement(menuItem, popupSize, targetSize);
        }

        private CustomPopupPlacement[] GetPlacement(MenuItem menuItem, Size popupSize, Size targetSize)
        {
            Rect itemBounds = GetElementBounds(menuItem);
            Rect parentBounds = GetParentMenuBounds(menuItem);
            Rect workArea = GetWorkArea(menuItem);

            bool openLeft = _submenusOpenLeft ?? ShouldOpenLeft(parentBounds, popupSize, workArea);
            _submenusOpenLeft = openLeft;

            double targetX = openLeft ? parentBounds.Left - popupSize.Width : parentBounds.Right;
            double targetY = Math.Clamp(itemBounds.Top, workArea.Top + MenuSpacing, workArea.Bottom - popupSize.Height - MenuSpacing);

            _activeOpensLeft = openLeft;
            SetDirection(menuItem, openLeft);

            return
            [
                new CustomPopupPlacement(
                    new Point(targetX - itemBounds.Left, targetY - itemBounds.Top),
                    PopupPrimaryAxis.Horizontal)
            ];
        }

        private static bool ShouldOpenLeft(Rect parentBounds, Size popupSize, Rect workArea)
        {
            double rightX = parentBounds.Right + popupSize.Width + MenuSpacing;
            if (rightX <= workArea.Right)
            {
                return false;
            }

            return parentBounds.Left - popupSize.Width - MenuSpacing >= workArea.Left;
        }

        private Rect GetParentMenuBounds(MenuItem menuItem)
        {
            ItemsControl? parent = ItemsControl.ItemsControlFromItemContainer(menuItem);
            if (parent is MenuItem parentItem && FindPopup(parentItem)?.Child is FrameworkElement popupChild && popupChild.IsVisible)
            {
                return GetElementBounds(popupChild);
            }

            if (parent is FrameworkElement parentElement)
            {
                return GetElementBounds(parentElement);
            }

            return GetElementBounds(menuItem);
        }

        private void UpdateActiveBounds()
        {
            if (_activeItem is null)
            {
                return;
            }

            _activeItemBounds = GetElementBounds(_activeItem);
            _activePopup = FindPopup(_activeItem);
            if (_activePopup?.Child is FrameworkElement child && child.IsVisible)
            {
                _activePopupBounds = GetElementBounds(child);
            }
            else
            {
                _activePopupBounds = Rect.Empty;
            }
        }

        private bool IsCursorInsideMenuSystem()
        {
            if (!GetCursorPos(out WinPoint cursorPixel))
            {
                return false;
            }

            Point cursor = ToDip(_menu, new Point(cursorPixel.X, cursorPixel.Y));
            UpdateActiveBounds();

            if (ContainsInflated(GetElementBounds(_menu), cursor))
            {
                return true;
            }

            if (_activeItem is not null && ContainsInflated(_activeItemBounds, cursor))
            {
                return true;
            }

            if (IsInsideOpenSubmenus(cursor))
            {
                return true;
            }

            if (!_activePopupBounds.IsEmpty && ContainsInflated(_activePopupBounds, cursor))
            {
                return true;
            }

            return IsInsideSafeCorridor(cursor) || IsInsideSafeTriangle(cursor);
        }

        private bool IsInsideSafeCorridor(Point cursor)
        {
            if (_activeItem is null || _activePopupBounds.IsEmpty)
            {
                return false;
            }

            double left = _activeOpensLeft ? _activePopupBounds.Right : _activeItemBounds.Right;
            double right = _activeOpensLeft ? _activeItemBounds.Left : _activePopupBounds.Left;
            if (right < left)
            {
                (left, right) = (right, left);
            }

            Rect corridor = new(
                left,
                Math.Min(_activeItemBounds.Top, _activePopupBounds.Top),
                Math.Max(1, right - left),
                Math.Max(_activeItemBounds.Bottom, _activePopupBounds.Bottom) - Math.Min(_activeItemBounds.Top, _activePopupBounds.Top));
            corridor.Inflate(BoundsInflate, BoundsInflate);

            return corridor.Contains(cursor);
        }

        private bool IsInsideSafeTriangle(Point cursor)
        {
            if (_activeItem is null || _activePopupBounds.IsEmpty || _pointerHistory.Count < 2)
            {
                return false;
            }

            return IsInsideSafeTriangle(cursor, _activePopupBounds, GetOpensLeft(_activeItemBounds, _activePopupBounds));
        }

        private bool IsPointerAimingAtActiveSubmenu(MenuItem candidateItem)
        {
            if (_activeItem is null
                || candidateItem == _activeItem
                || _activePopupBounds.IsEmpty
                || ItemsControl.ItemsControlFromItemContainer(candidateItem) != ItemsControl.ItemsControlFromItemContainer(_activeItem)
                || _pointerHistory.Count < 2)
            {
                return false;
            }

            UpdateActiveBounds();
            if (_activePopupBounds.IsEmpty)
            {
                return false;
            }

            Point current = _pointerHistory[^1].Position;
            Point anchor = GetAimAnchorPoint(candidateItem);
            (Point edgeA, Point edgeB) = GetSubmenuAimEdge(_activePopupBounds, GetOpensLeft(_activeItemBounds, _activePopupBounds));

            // Safe-triangle/menu-aim logic: while the pointer is travelling from the
            // current menu toward the exposed edge of the already-open submenu, do not
            // immediately switch to a neighbouring item that the pointer crosses over.
            // Once the pointer exits this virtual triangle, normal hover timing resumes.
            return IsPointInTriangle(current, anchor, edgeA, edgeB);
        }

        private bool IsInsideSafeTriangle(Point cursor, Rect popupBounds, bool opensLeft)
        {
            if (popupBounds.IsEmpty || _pointerHistory.Count < 2)
            {
                return false;
            }

            Point anchor = GetOldestRecentPointer();
            (Point edgeA, Point edgeB) = GetSubmenuAimEdge(popupBounds, opensLeft);
            return IsPointInTriangle(cursor, anchor, edgeA, edgeB);
        }

        private Point GetOldestRecentPointer()
        {
            DateTimeOffset cutoff = DateTimeOffset.Now.AddMilliseconds(-PointerHistoryMs);
            foreach (PointerSample sample in _pointerHistory)
            {
                if (sample.Timestamp >= cutoff)
                {
                    return sample.Position;
                }
            }

            return _pointerHistory[0].Position;
        }

        private Point GetAimAnchorPoint(MenuItem candidateItem)
        {
            DateTimeOffset cutoff = DateTimeOffset.Now.AddMilliseconds(-PointerHistoryMs);
            Rect candidateBounds = GetElementBounds(candidateItem);
            Point anchor = _pointerHistory[0].Position;

            for (int i = 0; i < _pointerHistory.Count; i++)
            {
                PointerSample sample = _pointerHistory[i];
                if (sample.Timestamp < cutoff)
                {
                    continue;
                }

                if (candidateBounds.Contains(sample.Position))
                {
                    return anchor;
                }

                anchor = sample.Position;
            }

            return anchor;
        }

        private static (Point A, Point B) GetSubmenuAimEdge(Rect popupBounds, bool opensLeft)
        {
            return opensLeft
                ? (new Point(popupBounds.Right, popupBounds.Top), new Point(popupBounds.Right, popupBounds.Bottom))
                : (new Point(popupBounds.Left, popupBounds.Top), new Point(popupBounds.Left, popupBounds.Bottom));
        }

        private static bool IsPointInTriangle(Point point, Point a, Point b, Point c)
        {
            static double Area(Point p1, Point p2, Point p3)
            {
                return Math.Abs(
                    (p1.X * (p2.Y - p3.Y)
                     + p2.X * (p3.Y - p1.Y)
                     + p3.X * (p1.Y - p2.Y)) / 2);
            }

            double total = Area(a, b, c);
            double a1 = Area(point, b, c);
            double a2 = Area(a, point, c);
            double a3 = Area(a, b, point);

            return Math.Abs(total - (a1 + a2 + a3)) < 0.5;
        }

        private void TrackPointer(MouseEventArgs e)
        {
            Point position = ToDip(_menu, _menu.PointToScreen(e.GetPosition(_menu)));
            DateTimeOffset now = DateTimeOffset.Now;
            _pointerHistory.Add(new PointerSample(position, now));

            DateTimeOffset cutoff = now.AddMilliseconds(-PointerHistoryMs);
            _pointerHistory.RemoveAll(sample => sample.Timestamp < cutoff);
            if (_pointerHistory.Count > MaxPointerHistory)
            {
                _pointerHistory.RemoveRange(0, _pointerHistory.Count - MaxPointerHistory);
            }
        }

        private bool ShouldKeepSubmenuOpen(MenuItem menuItem)
        {
            if (!GetCursorPos(out WinPoint cursorPixel))
            {
                return false;
            }

            Point cursor = ToDip(_menu, new Point(cursorPixel.X, cursorPixel.Y));
            Rect itemBounds = menuItem == _activeItem && !_activeItemBounds.IsEmpty
                ? _activeItemBounds
                : GetElementBounds(menuItem);
            if (itemBounds.Contains(cursor))
            {
                return true;
            }

            Popup? popup = FindPopup(menuItem);
            Rect popupBounds = popup?.Child is FrameworkElement child && child.IsVisible
                ? GetElementBounds(child)
                : menuItem == _activeItem ? _activePopupBounds : Rect.Empty;

            if (!popupBounds.IsEmpty)
            {
                if (ContainsInflated(popupBounds, cursor))
                {
                    return true;
                }

                bool opensLeft = GetOpensLeft(itemBounds, popupBounds);
                return IsInsideSafeCorridor(cursor, itemBounds, popupBounds, opensLeft)
                    || IsInsideSafeTriangle(cursor, popupBounds, opensLeft);
            }

            return false;
        }

        private static bool IsInsideSafeCorridor(Point cursor, Rect itemBounds, Rect popupBounds, bool opensLeft)
        {
            if (popupBounds.IsEmpty)
            {
                return false;
            }

            double left = opensLeft ? popupBounds.Right : itemBounds.Right;
            double right = opensLeft ? itemBounds.Left : popupBounds.Left;
            if (right < left)
            {
                (left, right) = (right, left);
            }

            Rect corridor = new(
                left,
                Math.Min(itemBounds.Top, popupBounds.Top),
                Math.Max(1, right - left),
                Math.Max(itemBounds.Bottom, popupBounds.Bottom) - Math.Min(itemBounds.Top, popupBounds.Top));
            corridor.Inflate(BoundsInflate, BoundsInflate);

            return corridor.Contains(cursor);
        }

        private static bool GetOpensLeft(Rect itemBounds, Rect popupBounds)
        {
            return popupBounds.Right <= itemBounds.Left || popupBounds.Left < itemBounds.Left;
        }

        private void AddOpenItem(MenuItem menuItem)
        {
            if (!_openItems.Contains(menuItem))
            {
                _openItems.Add(menuItem);
            }
        }

        private void CloseOpenSubmenus()
        {
            _isClosingSubmenus = true;
            try
            {
                foreach (MenuItem item in _openItems.ToArray())
                {
                    item.IsSubmenuOpen = false;
                }

                _openItems.Clear();
            }
            finally
            {
                _isClosingSubmenus = false;
            }
        }

        private bool IsInsideOpenSubmenus(Point cursor)
        {
            foreach (MenuItem item in _openItems.ToArray())
            {
                if (!item.IsSubmenuOpen)
                {
                    _openItems.Remove(item);
                    continue;
                }

                if (ContainsInflated(GetElementBounds(item), cursor))
                {
                    return true;
                }

                Popup? popup = FindPopup(item);
                if (popup?.Child is FrameworkElement child && child.IsVisible && ContainsInflated(GetElementBounds(child), cursor))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ContainsInflated(Rect rect, Point point)
        {
            if (rect.IsEmpty)
            {
                return false;
            }

            rect.Inflate(BoundsInflate, BoundsInflate);
            return rect.Contains(point);
        }

        private static Popup? FindPopup(MenuItem menuItem)
        {
            return menuItem.Template.FindName("PART_Popup", menuItem) as Popup;
        }

        private static MenuItem? FindParentMenuItem(DependencyObject? source)
        {
            DependencyObject? current = source;

            while (current is not null)
            {
                if (current is MenuItem menuItem)
                {
                    return menuItem;
                }

                current = VisualTreeHelper.GetParent(current);
            }

            return null;
        }
    }

    private static void SetDirection(DependencyObject element, bool openLeft)
    {
        SetSubmenuArrowText(element, openLeft ? "\u2039" : "\u203A");
        SetSubmenuArrowColumn(element, openLeft ? 0 : 2);
        SetSubmenuBorderMargin(element, openLeft ? new Thickness(0, 0, MenuSpacing, 0) : new Thickness(MenuSpacing, 0, 0, 0));
    }

    private static Rect GetElementBounds(FrameworkElement element)
    {
        Point topLeft = ToDip(element, element.PointToScreen(new Point(0, 0)));
        Point bottomRight = ToDip(element, element.PointToScreen(new Point(element.ActualWidth, element.ActualHeight)));

        return new Rect(topLeft, bottomRight);
    }

    private static Rect GetWorkArea(Visual visual)
    {
        Point screenPoint = visual is FrameworkElement element
            ? element.PointToScreen(new Point(0, 0))
            : new Point(0, 0);
        IntPtr monitor = MonitorFromPoint(new WinPoint { X = (int)screenPoint.X, Y = (int)screenPoint.Y }, 2);
        MonitorInfo info = new() { Size = Marshal.SizeOf<MonitorInfo>() };

        if (monitor == IntPtr.Zero || !GetMonitorInfoW(monitor, ref info))
        {
            return SystemParameters.WorkArea;
        }

        return new Rect(
            ToDip(visual, new Point(info.Work.Left, info.Work.Top)),
            ToDip(visual, new Point(info.Work.Right, info.Work.Bottom)));
    }

    private static Point ToDip(Visual visual, Point point)
    {
        Matrix transform = PresentationSource.FromVisual(visual)?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
        return transform.Transform(point);
    }
}
