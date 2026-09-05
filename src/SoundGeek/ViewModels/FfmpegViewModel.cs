using System.Net.Http;
using System.Windows.Input;
using Avalonia.Threading;
using SoundGeek.Core.Services;

namespace SoundGeek.ViewModels;

/// <summary>
/// The ffmpeg row on the Model screen. It sits alongside the speech model because from the
/// user's side it is the same kind of thing: a large optional download the app needs before it
/// can do part of its job, fetched only when asked for.
///
/// Before this existed the app shipped able to read WAV and nothing else, and told the user to
/// go and find ffmpeg themselves. Almost nobody did.
/// </summary>
public sealed class FfmpegViewModel : ObservableObject
{
    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromMinutes(30)
    };

    private readonly ShellViewModel _shell;
    private CancellationTokenSource? _cts;

    public FfmpegViewModel(ShellViewModel shell)
    {
        _shell = shell;
        Download = new RelayCommand(() => _ = DownloadAsync());
        Cancel = new RelayCommand(() => _cts?.Cancel());
        Remove = new RelayCommand(RemoveTool);
    }

    public string Title => "ffmpeg";

    public string Description =>
        "Needed to read MP3, M4A, MP4, MKV, FLAC and the rest. Without it only WAV files can be " +
        "cleaned up.";

    /// <summary>
    /// Where it comes from and under what terms, shown rather than buried. ffmpeg is run as a
    /// separate process and never linked into this application.
    /// </summary>
    public string OriginText =>
        $"ffmpeg {FfmpegCatalog.Version}, GPL, from gyan.dev  ·  run as a separate program, never " +
        "built into SoundGeek";

    public string SizeText => IsDownloaded
        ? $"{FfmpegCatalog.SizeOnDisk / 1_000_000d:0} MB"
        : $"{FfmpegCatalog.ApproxBytes / 1_000_000d:0} MB download";

    /// <summary>
    /// True when the app can decode, whether or not we were the ones who provided it. Somebody
    /// who already has ffmpeg on PATH should not be offered a 110 MB download they do not need.
    /// </summary>
    public bool IsAvailableAnywhere => MediaDecoder.FfmpegAvailable;

    public bool IsDownloaded => FfmpegCatalog.IsDownloaded;

    public bool IsMissing => !IsAvailableAnywhere && !IsDownloading;

    /// <summary>Only our own copy can be removed. One found on PATH is not ours to delete.</summary>
    public bool CanRemove => IsDownloaded && !IsDownloading;

    public string LocationText => MediaDecoder.FindFfmpeg() ?? "Not found on this machine.";

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
            var progress = new Progress<double>(p =>
            {
                Progress = p * 100;
                Note = $"{p:P0} of {FfmpegCatalog.ApproxBytes / 1_000_000d:0} MB";
            });

            await FfmpegCatalog.DownloadAsync(Http, progress, _cts.Token);
            Note = "";
        }
        catch (OperationCanceledException)
        {
            Note = "Cancelled. Nothing was kept.";
        }
        catch (Exception ex)
        {
            Log.Write($"ffmpeg: {ex}");
            Note = "That download did not finish: " + ex.Message;
        }
        finally
        {
            _cts?.Dispose();
            _cts = null;
            IsDownloading = false;
            Refresh();
            _shell.RefreshReadiness();
        }
    }

    private void RemoveTool()
    {
        try
        {
            FfmpegCatalog.Delete();
            Note = "";
        }
        catch (Exception ex)
        {
            Note = "It could not be removed: " + ex.Message;
        }

        Refresh();
        _shell.RefreshReadiness();
    }

    public void Refresh()
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(Refresh);
            return;
        }

        OnPropertyChanged(nameof(IsDownloaded));
        OnPropertyChanged(nameof(IsAvailableAnywhere));
        OnPropertyChanged(nameof(IsMissing));
        OnPropertyChanged(nameof(CanRemove));
        OnPropertyChanged(nameof(SizeText));
        OnPropertyChanged(nameof(LocationText));
    }
}
