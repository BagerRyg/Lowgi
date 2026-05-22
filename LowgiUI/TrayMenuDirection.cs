using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace LowgiUI;

public static class TrayMenuDirection
{
    private const double MinSubmenuRightSpace = 240;
    private const double SubmenuOverlap = 28;
    private static readonly RoutedEventHandler SubmenuOpenedHandler = MenuItemSubmenuOpened;
    private static readonly MouseEventHandler MenuMouseMoveHandler = MenuMouseMove;

    public static readonly DependencyProperty AutoFlipSubmenusProperty = DependencyProperty.RegisterAttached(
        "AutoFlipSubmenus",
        typeof(bool),
        typeof(TrayMenuDirection),
        new PropertyMetadata(false, OnAutoFlipSubmenusChanged));

    public static readonly DependencyProperty OpenSubmenusLeftProperty = DependencyProperty.RegisterAttached(
        "OpenSubmenusLeft",
        typeof(bool),
        typeof(TrayMenuDirection),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.Inherits));

    public static readonly DependencyProperty SubmenuArrowTextProperty = DependencyProperty.RegisterAttached(
        "SubmenuArrowText",
        typeof(string),
        typeof(TrayMenuDirection),
        new FrameworkPropertyMetadata("\u203A", FrameworkPropertyMetadataOptions.Inherits));

    public static readonly DependencyProperty SubmenuArrowColumnProperty = DependencyProperty.RegisterAttached(
        "SubmenuArrowColumn",
        typeof(int),
        typeof(TrayMenuDirection),
        new FrameworkPropertyMetadata(2, FrameworkPropertyMetadataOptions.Inherits));

    public static readonly DependencyProperty SubmenuPopupPlacementProperty = DependencyProperty.RegisterAttached(
        "SubmenuPopupPlacement",
        typeof(PlacementMode),
        typeof(TrayMenuDirection),
        new FrameworkPropertyMetadata(PlacementMode.Right, FrameworkPropertyMetadataOptions.Inherits));

    public static readonly DependencyProperty SubmenuPopupHorizontalOffsetProperty = DependencyProperty.RegisterAttached(
        "SubmenuPopupHorizontalOffset",
        typeof(double),
        typeof(TrayMenuDirection),
        new FrameworkPropertyMetadata(-8d, FrameworkPropertyMetadataOptions.Inherits));

    public static readonly DependencyProperty SubmenuPopupPaddingProperty = DependencyProperty.RegisterAttached(
        "SubmenuPopupPadding",
        typeof(Thickness),
        typeof(TrayMenuDirection),
        new FrameworkPropertyMetadata(new Thickness(12, 4, 4, 4), FrameworkPropertyMetadataOptions.Inherits));

    public static readonly DependencyProperty SubmenuPopupMarginProperty = DependencyProperty.RegisterAttached(
        "SubmenuPopupMargin",
        typeof(Thickness),
        typeof(TrayMenuDirection),
        new FrameworkPropertyMetadata(new Thickness(-10, 0, 0, 0), FrameworkPropertyMetadataOptions.Inherits));

    public static bool GetAutoFlipSubmenus(DependencyObject element) => (bool)element.GetValue(AutoFlipSubmenusProperty);

    public static void SetAutoFlipSubmenus(DependencyObject element, bool value) => element.SetValue(AutoFlipSubmenusProperty, value);

    public static bool GetOpenSubmenusLeft(DependencyObject element) => (bool)element.GetValue(OpenSubmenusLeftProperty);

    public static void SetOpenSubmenusLeft(DependencyObject element, bool value) => element.SetValue(OpenSubmenusLeftProperty, value);

    public static string GetSubmenuArrowText(DependencyObject element) => (string)element.GetValue(SubmenuArrowTextProperty);

    public static void SetSubmenuArrowText(DependencyObject element, string value) => element.SetValue(SubmenuArrowTextProperty, value);

    public static int GetSubmenuArrowColumn(DependencyObject element) => (int)element.GetValue(SubmenuArrowColumnProperty);

    public static void SetSubmenuArrowColumn(DependencyObject element, int value) => element.SetValue(SubmenuArrowColumnProperty, value);

    public static PlacementMode GetSubmenuPopupPlacement(DependencyObject element) => (PlacementMode)element.GetValue(SubmenuPopupPlacementProperty);

    public static void SetSubmenuPopupPlacement(DependencyObject element, PlacementMode value) => element.SetValue(SubmenuPopupPlacementProperty, value);

    public static double GetSubmenuPopupHorizontalOffset(DependencyObject element) => (double)element.GetValue(SubmenuPopupHorizontalOffsetProperty);

    public static void SetSubmenuPopupHorizontalOffset(DependencyObject element, double value) => element.SetValue(SubmenuPopupHorizontalOffsetProperty, value);

    public static Thickness GetSubmenuPopupPadding(DependencyObject element) => (Thickness)element.GetValue(SubmenuPopupPaddingProperty);

    public static void SetSubmenuPopupPadding(DependencyObject element, Thickness value) => element.SetValue(SubmenuPopupPaddingProperty, value);

    public static Thickness GetSubmenuPopupMargin(DependencyObject element) => (Thickness)element.GetValue(SubmenuPopupMarginProperty);

    public static void SetSubmenuPopupMargin(DependencyObject element, Thickness value) => element.SetValue(SubmenuPopupMarginProperty, value);

    private static void OnAutoFlipSubmenusChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        if (dependencyObject is not ContextMenu menu)
        {
            return;
        }

        if ((bool)e.NewValue)
        {
            menu.Opened += MenuOpened;
            menu.AddHandler(MenuItem.SubmenuOpenedEvent, SubmenuOpenedHandler, true);
            menu.AddHandler(UIElement.MouseMoveEvent, MenuMouseMoveHandler, true);
        }
        else
        {
            menu.Opened -= MenuOpened;
            menu.RemoveHandler(MenuItem.SubmenuOpenedEvent, SubmenuOpenedHandler);
            menu.RemoveHandler(UIElement.MouseMoveEvent, MenuMouseMoveHandler);
        }
    }

    private static void MenuOpened(object sender, RoutedEventArgs e)
    {
        if (sender is not ContextMenu menu)
        {
            return;
        }

        _ = menu.Dispatcher.BeginInvoke(() => UpdateDirection(menu), DispatcherPriority.Loaded);
    }

    private static void UpdateDirection(ContextMenu menu)
    {
        Rect menuBounds = GetElementBounds(menu);
        Rect workArea = SystemParameters.WorkArea;
        double rightSpace = workArea.Right - menuBounds.Right;
        double leftSpace = menuBounds.Left - workArea.Left;
        bool openLeft = rightSpace < MinSubmenuRightSpace && leftSpace > rightSpace;

        SetDirection(menu, openLeft);
        SetOpenSubmenusLeftOnItems(menu, openLeft);
    }

    private static void MenuItemSubmenuOpened(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is MenuItem menuItem)
        {
            UpdateMenuItemDirection(menuItem);
        }
    }

    private static void MenuMouseMove(object sender, MouseEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source && FindParentMenuItem(source) is { } menuItem)
        {
            UpdateMenuItemDirection(menuItem);
        }
    }

    private static void UpdateMenuItemDirection(MenuItem menuItem)
    {
        if (!menuItem.HasItems)
        {
            return;
        }

        bool openLeft = ShouldOpenLeft(menuItem);
        SetDirection(menuItem, openLeft);
        SetOpenSubmenusLeftOnItems(menuItem, openLeft);
    }

    private static bool ShouldOpenLeft(FrameworkElement element)
    {
        Rect itemBounds = GetElementBounds(element);
        Rect workArea = SystemParameters.WorkArea;
        double rightSpace = workArea.Right - itemBounds.Right;
        double leftSpace = itemBounds.Left - workArea.Left;

        return rightSpace < MinSubmenuRightSpace && leftSpace > rightSpace;
    }

    private static void SetOpenSubmenusLeftOnItems(ItemsControl parent, bool openLeft)
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

            SetDirection(menuItem, openLeft);
            SetOpenSubmenusLeftOnItems(menuItem, openLeft);
        }
    }

    private static void SetDirection(DependencyObject element, bool openLeft)
    {
        SetOpenSubmenusLeft(element, openLeft);
        SetSubmenuArrowText(element, openLeft ? "\u2039" : "\u203A");
        SetSubmenuArrowColumn(element, openLeft ? 0 : 2);
        SetSubmenuPopupPlacement(element, openLeft ? PlacementMode.Left : PlacementMode.Right);
        SetSubmenuPopupHorizontalOffset(element, openLeft ? SubmenuOverlap : -SubmenuOverlap);
        SetSubmenuPopupPadding(element, openLeft ? new Thickness(4, 4, 12, 4) : new Thickness(12, 4, 4, 4));
        SetSubmenuPopupMargin(element, new Thickness(0));
    }

    private static Rect GetElementBounds(FrameworkElement element)
    {
        Point topLeft = ToDip(element, element.PointToScreen(new Point(0, 0)));
        Point bottomRight = ToDip(element, element.PointToScreen(new Point(element.ActualWidth, element.ActualHeight)));

        return new Rect(topLeft, bottomRight);
    }

    private static MenuItem? FindParentMenuItem(DependencyObject source)
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

    private static Point ToDip(Visual visual, Point point)
    {
        Matrix transform = PresentationSource.FromVisual(visual)?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
        return transform.Transform(point);
    }
}
