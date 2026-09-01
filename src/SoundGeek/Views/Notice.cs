using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using TechyGeeksHome.Common;

namespace SoundGeek.Views;

/// <summary>
/// A small message window, built in code because it is fifteen lines of layout and does not
/// warrant a XAML file of its own. Used for the update check result, which is the only thing in
/// SoundGeek that needs to interrupt somebody.
/// </summary>
internal static class Notice
{
    public static Task ShowAsync(Window owner, string title, string message, string? openUrl)
    {
        var text = new TextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brush.Parse("#BEC4D2"),
            FontSize = 13
        };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Margin = new Thickness(0, 20, 0, 0)
        };

        var window = new Window
        {
            Title = title,
            Width = 420,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Brush.Parse("#0A0D16"),
            FontFamily = new FontFamily("Segoe UI Variable Display, Segoe UI, Arial")
        };

        if (openUrl is not null)
        {
            var open = new Button { Content = "Open the releases page", Padding = new Thickness(14, 8) };
            open.Click += (_, _) => { AppInfo.OpenUrl(openUrl); window.Close(); };
            buttons.Children.Add(open);
        }

        var close = new Button { Content = "Close", Padding = new Thickness(14, 8) };
        close.Click += (_, _) => window.Close();
        buttons.Children.Add(close);

        window.Content = new StackPanel
        {
            Margin = new Thickness(22),
            Children = { text, buttons }
        };

        return window.ShowDialog(owner);
    }
}
