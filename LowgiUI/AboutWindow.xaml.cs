using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Navigation;

namespace LowgiUI;

public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();
        DataContext = this;
    }

    public string VersionLabel => $"Version 1.1 ({GetBuildLabel()})";

    private static string GetBuildLabel()
    {
        string baseDirectory = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string? folderName = Path.GetFileName(baseDirectory);

        return !string.IsNullOrWhiteSpace(folderName)
            && folderName.StartsWith("Build ", StringComparison.OrdinalIgnoreCase)
                ? folderName
                : "Development";
    }

    private void HyperlinkRequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri)
        {
            UseShellExecute = true
        });
        e.Handled = true;
    }

    private void TitleBarMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void CloseButtonClick(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
