using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace LowgiUI;

public static class TrayThemeManager
{
    private const string BackgroundBrush = "TrayMenuBackgroundBrush";
    private const string HoverBrush = "TrayMenuHoverBrush";
    private const string PressedBrush = "TrayMenuPressedBrush";
    private const string DangerHoverBrush = "TrayMenuDangerHoverBrush";
    private const string DangerPressedBrush = "TrayMenuDangerPressedBrush";
    private const string BorderBrush = "TrayMenuBorderBrush";
    private const string TextBrush = "TrayMenuTextBrush";
    private const string DisabledTextBrush = "TrayMenuDisabledTextBrush";
    private const string TitleBrush = "TrayMenuTitleBrush";
    private const string ButtonBrush = "TrayMenuButtonBrush";
    private const string LinkBrush = "TrayMenuLinkBrush";
    private const string ScrollThumbBrush = "TrayMenuScrollThumbBrush";

    public static void Apply(bool lightTheme)
    {
        ThemePalette palette = lightTheme ? ThemePalette.Light : ThemePalette.Dark;
        ApplyPalette(palette);
        CheckTheme.SetLightTheme(lightTheme);
        RefreshOpenUi();

        _ = Application.Current.Dispatcher.BeginInvoke(
            RefreshOpenUi,
            DispatcherPriority.Render);
    }

    private static void ApplyPalette(ThemePalette palette)
    {
        SetBrush(BackgroundBrush, palette.Background);
        SetBrush(HoverBrush, palette.Hover);
        SetBrush(PressedBrush, palette.Pressed);
        SetBrush(DangerHoverBrush, palette.DangerHover);
        SetBrush(DangerPressedBrush, palette.DangerPressed);
        SetBrush(BorderBrush, palette.Border);
        SetBrush(TextBrush, palette.Text);
        SetBrush(DisabledTextBrush, palette.DisabledText);
        SetBrush(TitleBrush, palette.Title);
        SetBrush(ButtonBrush, palette.Button);
        SetBrush(LinkBrush, palette.Link);
        SetBrush(ScrollThumbBrush, palette.ScrollThumb);
    }

    private static void SetBrush(string key, Color color)
    {
        ResourceDictionary dictionary = FindResourceDictionary(Application.Current.Resources, key)
            ?? Application.Current.Resources;

        if (dictionary.Contains(key) && dictionary[key] is SolidColorBrush brush && !brush.IsFrozen)
        {
            brush.Color = color;
            return;
        }

        dictionary[key] = new SolidColorBrush(color);
    }

    private static ResourceDictionary? FindResourceDictionary(ResourceDictionary dictionary, string key)
    {
        if (dictionary.Contains(key))
        {
            return dictionary;
        }

        foreach (ResourceDictionary mergedDictionary in dictionary.MergedDictionaries)
        {
            ResourceDictionary? match = FindResourceDictionary(mergedDictionary, key);
            if (match != null)
            {
                return match;
            }
        }

        return null;
    }

    private static void RefreshOpenUi()
    {
        HashSet<DependencyObject> visited = new(ReferenceEqualityComparer.Instance);

        foreach (Window window in Application.Current.Windows)
        {
            RefreshElementTree(window, visited);
        }

        if (Application.Current.TryFindResource("SysTrayMenu") is ContextMenu menu)
        {
            RefreshElementTree(menu, visited);
        }
    }

    private static void RefreshElementTree(DependencyObject root, HashSet<DependencyObject> visited)
    {
        if (!visited.Add(root))
        {
            return;
        }

        ApplyThemeReferences(root);

        if (root is FrameworkElement element)
        {
            element.ApplyTemplate();
            element.UpdateLayout();
        }

        if (root is ItemsControl itemsControl)
        {
            RefreshItemContainers(itemsControl, visited);
        }

        if (root is MenuItem menuItem
            && menuItem.Template.FindName("PART_Popup", menuItem) is Popup submenuPopup)
        {
            RefreshElementTree(submenuPopup, visited);
        }

        if (root is Popup { Child: DependencyObject popupChild })
        {
            RefreshElementTree(popupChild, visited);
        }

        if (root is Visual or Visual3D)
        {
            int childCount = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < childCount; i++)
            {
                RefreshElementTree(VisualTreeHelper.GetChild(root, i), visited);
            }
        }

        foreach (object child in LogicalTreeHelper.GetChildren(root))
        {
            if (child is DependencyObject dependencyChild)
            {
                RefreshElementTree(dependencyChild, visited);
            }
        }

        if (root is FrameworkElement updatedElement)
        {
            updatedElement.InvalidateVisual();
        }
    }

    private static void RefreshItemContainers(ItemsControl itemsControl, HashSet<DependencyObject> visited)
    {
        itemsControl.UpdateLayout();

        for (int i = 0; i < itemsControl.Items.Count; i++)
        {
            DependencyObject? container = itemsControl.ItemContainerGenerator.ContainerFromIndex(i);
            if (container != null)
            {
                RefreshElementTree(container, visited);
            }
        }
    }

    private static void ApplyThemeReferences(DependencyObject element)
    {
        switch (element)
        {
            case Window window:
                SetResource(window, Window.BackgroundProperty, BackgroundBrush);
                SetResource(window, Control.ForegroundProperty, TextBrush);
                break;
            case ContextMenu menu:
                SetResource(menu, Control.BackgroundProperty, BackgroundBrush);
                SetResource(menu, Control.ForegroundProperty, TextBrush);
                SetResource(menu, Control.BorderBrushProperty, BorderBrush);
                break;
            case MenuItem menuItem:
                menuItem.SetCurrentValue(Control.BackgroundProperty, Brushes.Transparent);
                SetResource(menuItem, Control.ForegroundProperty, menuItem.IsEnabled ? TextBrush : DisabledTextBrush);
                menuItem.SetCurrentValue(Control.BorderBrushProperty, Brushes.Transparent);
                break;
            case Button button:
                SetResource(button, Control.ForegroundProperty, TextBrush);
                button.ClearValue(Control.BackgroundProperty);
                button.ClearValue(Control.BorderBrushProperty);
                break;
            case AccessText accessText:
                SetResource(accessText, TextElement.ForegroundProperty, TextBrush);
                break;
            case TextBlock textBlock:
                SetResource(textBlock, TextBlock.ForegroundProperty, textBlock.IsEnabled ? TextBrush : DisabledTextBrush);
                break;
            case Hyperlink hyperlink:
                SetContentResource(hyperlink, TextElement.ForegroundProperty, LinkBrush);
                break;
            case TextElement textElement:
                SetContentResource(textElement, TextElement.ForegroundProperty, TextBrush);
                break;
            case Border border:
                ApplyBorderTheme(border);
                break;
            case Panel panel:
                ApplyPanelTheme(panel);
                break;
            case Shape shape:
                ApplyShapeTheme(shape);
                break;
            case Thumb thumb:
                SetResource(thumb, Control.BackgroundProperty, ScrollThumbBrush);
                SetResource(thumb, Control.BorderBrushProperty, BorderBrush);
                break;
            case ScrollBar scrollBar:
                SetResource(scrollBar, Control.BackgroundProperty, BackgroundBrush);
                break;
            case ScrollViewer scrollViewer:
                SetResource(scrollViewer, Control.BackgroundProperty, BackgroundBrush);
                break;
        }
    }

    private static void ApplyBorderTheme(Border border)
    {
        if (border.Name is "PopupBorder" or "AboutWindowBorder")
        {
            SetResource(border, Border.BackgroundProperty, BackgroundBrush);
            SetResource(border, Border.BorderBrushProperty, BorderBrush);
            return;
        }

        if (border.ActualHeight is > 0 and <= 2)
        {
            SetResource(border, Border.BackgroundProperty, BorderBrush);
        }
    }

    private static void ApplyPanelTheme(Panel panel)
    {
        if (panel is FrameworkElement { Name: "TitleBar" })
        {
            SetResource(panel, Panel.BackgroundProperty, TitleBrush);
            return;
        }

        if (panel.Background != null)
        {
            SetResource(panel, Panel.BackgroundProperty, BackgroundBrush);
        }
    }

    private static void ApplyShapeTheme(Shape shape)
    {
        if (shape.Fill != null)
        {
            SetResource(shape, Shape.FillProperty, TextBrush);
        }

        if (shape.Stroke != null)
        {
            SetResource(shape, Shape.StrokeProperty, TextBrush);
        }
    }

    private static void SetResource(FrameworkElement element, DependencyProperty property, string resourceKey)
    {
        element.SetResourceReference(property, resourceKey);
    }

    private static void SetContentResource(FrameworkContentElement element, DependencyProperty property, string resourceKey)
    {
        element.SetResourceReference(property, resourceKey);
    }

    private sealed record ThemePalette(
        Color Background,
        Color Hover,
        Color Pressed,
        Color DangerHover,
        Color DangerPressed,
        Color Border,
        Color Text,
        Color DisabledText,
        Color Title,
        Color Button,
        Color Link,
        Color ScrollThumb)
    {
        public static ThemePalette Dark { get; } = new(
            Color(0x0D1117),
            Color(0x2D333B),
            Color(0x3A424D),
            Color(0x7F1D1D),
            Color(0x991B1B),
            Color(0x30363D),
            Color(0xF0F6FC),
            Color(0x8B949E),
            Color(0x161B22),
            Color(0x21262D),
            Color(0x58A6FF),
            Color(0x7D8590));

        public static ThemePalette Light { get; } = new(
            Color(0xFFFFFF),
            Color(0xE9EEF5),
            Color(0xDDE5EF),
            Color(0xF3D0D0),
            Color(0xE8B8B8),
            Color(0xB8C2CC),
            Color(0x111827),
            Color(0x6B7280),
            Color(0xF3F4F6),
            Color(0xEEF2F7),
            Color(0x0969DA),
            Color(0x9AA7B5));

        private static Color Color(int rgb)
        {
            return System.Windows.Media.Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
        }
    }
}
