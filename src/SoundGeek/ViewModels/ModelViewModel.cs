using System.Windows.Input;
using Avalonia.Threading;
using SoundGeek.Core.Services;

namespace SoundGeek.ViewModels;

/// <summary>
/// The Model screen. One file, downloaded on request, checked before it is kept.
///
/// This is the only thing SoundGeek ever fetches from the internet, and it only happens when
/// somebody presses the button on this screen. Worth stating plainly, because the whole point of
/// the app is that recordings never leave the machine.
/// </summary>
public sealed class ModelViewModel : ObservableObject
{
    private readonly ShellViewModel _shell;
    private CancellationTokenSource? _cts;

    public ModelViewModel(ShellViewModel shell)
    {
        _shell = shell;
        Download = new RelayCommand(() => _ = DownloadAsync());
        Cancel = new RelayCommand(() => _cts?.Cancel());
        Remove = new RelayCommand(RemoveModel);
    }

    public string SizeText => $"{ModelCatalog.Bytes / 1_000_000d:0.0} MB";
    public string Origin => ModelCatalog.Origin;
    public string Checksum => ModelCatalog.Sha256Hex;

    public bool IsDownloaded => ModelCatalog.IsDownloaded;
    public bool IsMissing => !IsDownloaded && !IsDownloading;

    private bool _isDownloading;
    public bool IsDownloading
    {
        get => _isDownloading;
        private set
        {
            if (!SetField(ref _isDownloading, value)) return;
            OnPropertyChanged(nameof(IsMissing));
            OnPropertyChanged(nameof(CanRemove));
        }
    }

    public bool CanRemove => IsDownloaded && !IsDownloading;

    private double _progress;
    public double Progress { get => _progress; private set => SetField(ref _progress, value); }

    private string _note = "";
    public string Note { get => _note; private set => SetField(ref _note, value); }

    public ICommand Download { get; }
    public ICommand Cancel { get; }
    public ICommand Remove { get; }

    private async Task DownloadAsync()
    {
        if (IsDownloading || IsDownloaded) return;

        IsDownloading = true;
        Progress = 0;
        Note = "Starting…";
        _cts = new CancellationTokenSource();

        try
        {
            await ModelCatalog.DownloadAsync(new Progress<double>(p =>
            {
                Progress = p * 100;
                Note = $"{p:P0} of {SizeText}";
            }), _cts.Token);

            Note = "";
        }
        catch (OperationCanceledException)
        {
            Note = "Cancelled. Nothing was kept.";
        }
        catch (Exception ex)
        {
            Log.Write($"Model: {ex}");
            Note = "That download did not finish: " + ex.Message;
        }
        finally
        {
            _cts?.Dispose();
            _cts = null;
            IsDownloading = false;
            Refresh();
            _shell.OnModelChanged();
        }
    }

    private void RemoveModel()
    {
        try
        {
            ModelCatalog.Delete();
            Note = "";
        }
        catch (Exception ex)
        {
            Note = "It could not be removed: " + ex.Message;
        }

        Refresh();
        _shell.OnModelChanged();
    }

    public void Refresh()
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(Refresh);
            return;
        }

        OnPropertyChanged(nameof(IsDownloaded));
        OnPropertyChanged(nameof(IsMissing));
        OnPropertyChanged(nameof(CanRemove));
    }
}
