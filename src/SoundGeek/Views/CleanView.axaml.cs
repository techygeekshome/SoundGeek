using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;

namespace SoundGeek.Views;

public partial class CleanView : UserControl
{
    public CleanView() => InitializeComponent();

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    /// <summary>
    /// The "Choose files…" button on the empty state. It hands straight back to the window so
    /// there is one file picker in the app rather than two that could drift apart.
    /// </summary>
    private async void OnAddFiles(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (this.GetVisualRoot() is MainWindow window)
            await window.PickFilesAsync();
    }
}
