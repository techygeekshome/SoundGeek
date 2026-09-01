using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using TechyGeeksHome.Common;
using SoundGeek.Core.Services;
using SoundGeek.ViewModels;

namespace SoundGeek.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private ShellViewModel? Vm => DataContext as ShellViewModel;

    /// <summary>
    /// The noise model holds a native handle. Letting go of it when the window closes means the
    /// process actually exits rather than lingering.
    /// </summary>
    protected override void OnClosed(EventArgs e)
    {
        Vm?.Shutdown();
        base.OnClosed(e);
    }

    // ------------------------------------------------------------------ drag and drop

    /// <summary>
    /// Only offer a copy cursor for files we can actually read. Accepting a drop and then
    /// silently ignoring it is worse than refusing it in the first place.
    /// </summary>
    private static void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = e.Data.Contains(DataFormats.Files)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        var items = e.Data.GetFiles();
        if (items is null || Vm is null) return;

        var paths = items
            .Select(i => i.TryGetLocalPath())
            .Where(p => !string.IsNullOrEmpty(p))
            .Select(p => p!)
            .ToList();

        AddPaths(paths);
    }

    /// <summary>
    /// A dropped folder is taken to mean everything in it that we can read - one level only,
    /// because walking a whole drive because somebody dropped C:\ would be a nasty surprise.
    /// </summary>
    private void AddPaths(IEnumerable<string> paths)
    {
        if (Vm is null) return;

        var files = new List<string>();
        foreach (var p in paths)
        {
            if (Directory.Exists(p))
            {
                try
                {
                    files.AddRange(Directory.EnumerateFiles(p).Where(MediaDecoder.IsSupported));
                }
                catch (Exception ex)
                {
                    Log.Write($"Listing {p}: {ex.Message}");
                }
            }
            else
            {
                files.Add(p);
            }
        }

        Vm.AddFiles(files);
        Vm.Page = "Clean";
    }

    // ------------------------------------------------------------------ header buttons

    internal async Task PickFilesAsync()
    {
        if (Vm is null) return;

        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Choose recordings to clean",
            AllowMultiple = true,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Audio and video")
                {
                    Patterns = MediaDecoder.SupportedExtensions.Select(e => "*" + e).ToList()
                },
                FilePickerFileTypes.All
            }
        });

        AddPaths(files.Select(f => f.TryGetLocalPath()).Where(p => p is not null).Select(p => p!));
    }

    private async void OnAddFiles(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        => await PickFilesAsync();

    private async void OnStart(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (Vm is null) return;
        await Vm.RunQueueAsync();
    }

    private void OnStop(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => Vm?.Cancel();

    private void OnClearFinished(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        => Vm?.ClearFinished();

    // ------------------------------------------------------------------ sidebar foot

    private void OnAbout(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        => new AboutWindow(AppFacts.Info).ShowDialog(this);

    private async void OnCheckUpdates(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is Button b) { b.IsEnabled = false; b.Content = "Checking…"; }

        try
        {
            var result = await UpdateChecker.CheckAsync(AppFacts.Info);
            await Notice.ShowAsync(this, "Check for updates", result.Message,
                result.Status == UpdateStatus.UpdateAvailable ? result.ReleaseUrl : null);
        }
        finally
        {
            if (sender is Button b2) { b2.IsEnabled = true; b2.Content = "Check for updates"; }
        }
    }
}
