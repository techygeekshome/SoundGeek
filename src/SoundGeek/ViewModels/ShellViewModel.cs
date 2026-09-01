using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using SoundGeek.Core.Models;
using SoundGeek.Core.Services;

namespace SoundGeek.ViewModels;

/// <summary>
/// The whole application's state. SoundGeek is small enough that one view model is honest;
/// splitting it would be ceremony rather than structure.
/// </summary>
public sealed class ShellViewModel : ObservableObject
{
    private readonly CleanupEngine _engine = new();
    private CancellationTokenSource? _cts;

    public ShellViewModel()
    {
        ShowClean = new RelayCommand(() => Page = "Clean");
        ShowModel = new RelayCommand(() => Page = "Model");
        ShowSettings = new RelayCommand(() => Page = "Settings");

        Model = new ModelViewModel(this);
        SelectedMode = Modes.First(m => m.Mode == CleanupMode.Gentle);

        RefreshReadiness();
    }

    // ---------------------------------------------------------------- navigation

    private string _page = "Clean";
    public string Page
    {
        get => _page;
        set
        {
            if (!SetField(ref _page, value)) return;
            OnPropertyChanged(nameof(IsClean));
            OnPropertyChanged(nameof(IsModel));
            OnPropertyChanged(nameof(IsSettings));
            OnPropertyChanged(nameof(PageTitle));
            OnPropertyChanged(nameof(StatusLine));
        }
    }

    public bool IsClean => Page == "Clean";
    public bool IsModel => Page == "Model";
    public bool IsSettings => Page == "Settings";

    public ICommand ShowClean { get; }
    public ICommand ShowModel { get; }
    public ICommand ShowSettings { get; }

    // ---------------------------------------------------------------- chrome

    public string BrandName => "SoundGeek";
    public string BrandBy => "by TechyGeeksHome";
    public string VersionText => TechyGeeksHome.Common.AppInfo.CurrentVersionText;

    public string ModelFolder => ModelCatalog.Directory;
    public string FfmpegLocation => MediaDecoder.FindFfmpeg() ?? "Not found on this machine.";

    public string PageTitle => Page switch
    {
        "Model" => "Model",
        "Settings" => "Settings",
        _ => "Clean"
    };

    /// <summary>
    /// The one line under the title. Every app in the range says what was found and what was
    /// changed here, never a bare "Ready".
    /// </summary>
    public string StatusLine => Page switch
    {
        "Model" => Model.IsDownloaded
            ? $"The speech model is here, {ModelCatalog.Bytes / 1_000_000d:0.0} MB, kept in {ModelCatalog.Directory}"
            : $"The speech model has not been downloaded. It is {ModelCatalog.Bytes / 1_000_000d:0.0} MB and only the strongest mode needs it.",
        "Settings" => "What SoundGeek will and will not do, in plain words.",
        _ => Jobs.Count == 0
            ? "Nothing queued. Drop recordings here - they are read on this machine and nothing is uploaded."
            : $"{Jobs.Count} file{(Jobs.Count == 1 ? "" : "s")} · {Jobs.Count(j => j.Job.State == JobState.Done)} done"
              + (Jobs.Any(j => j.Job.State == JobState.Failed)
                  ? $" · {Jobs.Count(j => j.Job.State == JobState.Failed)} failed" : "")
    };

    // ---------------------------------------------------------------- readiness

    private string _readiness = "";
    public string Readiness { get => _readiness; private set => SetField(ref _readiness, value); }

    private bool _hasReadinessProblem;
    public bool HasReadinessProblem { get => _hasReadinessProblem; private set => SetField(ref _hasReadinessProblem, value); }

    public void RefreshReadiness()
    {
        Model.Refresh();

        if (SelectedMode.Mode == CleanupMode.Voice && !ModelCatalog.IsDownloaded)
        {
            Readiness = $"The strongest cleanup needs a {ModelCatalog.Bytes / 1_000_000d:0.0} MB speech model, " +
                        "and it has not been downloaded yet. Open Model and fetch it, or pick one of the other " +
                        "two, which need nothing.";
            HasReadinessProblem = true;
        }
        else if (!MediaDecoder.FfmpegAvailable)
        {
            Readiness = "ffmpeg was not found, so only WAV files can be read. Put ffmpeg.exe next to " +
                        "SoundGeek to handle MP3, M4A, MP4 and the rest.";
            HasReadinessProblem = true;
        }
        else
        {
            Readiness = "";
            HasReadinessProblem = false;
        }

        OnPropertyChanged(nameof(CanStart));
        OnPropertyChanged(nameof(StatusLine));
    }

    public ModelViewModel Model { get; }

    /// <summary>
    /// Called after the model is downloaded or removed. Somebody who has just fetched it almost
    /// certainly wants the mode it unlocks; somebody who has just removed it cannot have it.
    /// </summary>
    public void OnModelChanged()
    {
        if (Model.IsDownloaded) SelectedMode = Modes.First(m => m.Mode == CleanupMode.Voice);
        else if (SelectedMode.Mode == CleanupMode.Voice) SelectedMode = Modes.First(m => m.Mode == CleanupMode.Gentle);

        RefreshReadiness();
    }

    // ---------------------------------------------------------------- the queue

    public ObservableCollection<JobViewModel> Jobs { get; } = new();

    public void AddFiles(IEnumerable<string> paths)
    {
        foreach (var p in paths)
        {
            if (!File.Exists(p)) continue;
            if (!MediaDecoder.IsSupported(p)) continue;
            if (Jobs.Any(j => string.Equals(j.Job.SourcePath, p, StringComparison.OrdinalIgnoreCase))) continue;
            Jobs.Add(new JobViewModel(new CleanupJob { SourcePath = p }));
        }

        OnPropertyChanged(nameof(StatusLine));
        OnPropertyChanged(nameof(HasJobs));
        OnPropertyChanged(nameof(CanStart));
    }

    public void ClearFinished()
    {
        foreach (var j in Jobs.Where(j => j.Job.State is JobState.Done or JobState.Failed or JobState.Cancelled).ToList())
            Jobs.Remove(j);

        OnPropertyChanged(nameof(StatusLine));
        OnPropertyChanged(nameof(HasJobs));
    }

    public bool HasJobs => Jobs.Count > 0;

    // ---------------------------------------------------------------- options

    public ObservableCollection<ModeOption> Modes { get; } = new(ModeOption.All);

    private ModeOption _selectedMode = ModeOption.All[1];
    public ModeOption SelectedMode
    {
        get => _selectedMode;
        set
        {
            if (!SetField(ref _selectedMode, value)) return;
            OnPropertyChanged(nameof(ModeExplanation));
            OnPropertyChanged(nameof(IsGentle));
            RefreshReadiness();
        }
    }

    public bool IsGentle => SelectedMode.Mode == CleanupMode.Gentle;

    public string ModeExplanation => SelectedMode.Explanation;

    public ObservableCollection<StrengthOption> Strengths { get; } = new(StrengthOption.All);

    private StrengthOption _selectedStrength = StrengthOption.All[1];
    public StrengthOption SelectedStrength { get => _selectedStrength; set => SetField(ref _selectedStrength, value); }

    private bool _removeHum = true;
    public bool RemoveHum { get => _removeHum; set => SetField(ref _removeHum, value); }

    private bool _removeRumble = true;
    public bool RemoveRumble { get => _removeRumble; set => SetField(ref _removeRumble, value); }

    private bool _evenOutLevels = true;
    public bool EvenOutLevels { get => _evenOutLevels; set => SetField(ref _evenOutLevels, value); }

    public ObservableCollection<TargetOption> Targets { get; } = new(TargetOption.All);

    private TargetOption _selectedTarget = TargetOption.All[0];
    public TargetOption SelectedTarget { get => _selectedTarget; set => SetField(ref _selectedTarget, value); }

    // ---------------------------------------------------------------- running

    private bool _isRunning;
    public bool IsRunning
    {
        get => _isRunning;
        private set
        {
            if (!SetField(ref _isRunning, value)) return;
            OnPropertyChanged(nameof(CanStart));
            OnPropertyChanged(nameof(NotRunning));
        }
    }

    public bool NotRunning => !IsRunning;

    public bool CanStart => !IsRunning
                            && Jobs.Any(j => j.Job.State == JobState.Waiting)
                            && (SelectedMode.Mode != CleanupMode.Voice || ModelCatalog.IsDownloaded);

    /// <summary>
    /// Works through the queue one file at a time. Sequential on purpose: the model and the
    /// filters already use several cores, so running two files at once makes both slower and
    /// makes the progress meaningless.
    /// </summary>
    public async Task RunQueueAsync()
    {
        if (!CanStart) return;

        IsRunning = true;
        _cts = new CancellationTokenSource();

        var options = new CleanupEngine.Options
        {
            Mode = SelectedMode.Mode,
            RemoveHum = RemoveHum,
            RemoveRumble = RemoveRumble,
            EvenOutLevels = EvenOutLevels,
            TargetLufs = SelectedTarget.Lufs,
            GentleStrengthDb = SelectedStrength.Db
        };

        try
        {
            foreach (var vm in Jobs.ToList())
            {
                if (_cts.IsCancellationRequested) break;
                if (vm.Job.State != JobState.Waiting) continue;

                vm.SetState(JobState.Running, "Starting…");

                try
                {
                    var progress = new Progress<double>(p =>
                    {
                        vm.Job.Progress = p;
                        vm.Refresh();
                    });

                    var result = await _engine.RunAsync(
                        vm.Job.SourcePath, options, progress,
                        stage => vm.SetState(JobState.Running, stage),
                        _cts.Token);

                    vm.Job.OutputPath = result.OutputPath;
                    vm.Job.Before = result.Before;
                    vm.Job.After = result.After;
                    vm.Job.Duration ??= null;

                    vm.SetState(JobState.Done, vm.Summary);
                }
                catch (OperationCanceledException)
                {
                    vm.SetState(JobState.Cancelled, "Stopped before it finished.");
                }
                catch (MediaDecodeException ex)
                {
                    vm.SetState(JobState.Failed, ex.Message);
                }
                catch (AudioFormatException ex)
                {
                    vm.SetState(JobState.Failed, ex.Message);
                }
                catch (Exception ex)
                {
                    Log.Write($"{vm.Job.FileName}: {ex}");
                    vm.SetState(JobState.Failed, ex.Message);
                }

                OnPropertyChanged(nameof(StatusLine));
            }
        }
        finally
        {
            IsRunning = false;
            _cts?.Dispose();
            _cts = null;
            OnPropertyChanged(nameof(StatusLine));
        }
    }

    public void Cancel() => _cts?.Cancel();

    /// <summary>Called when the window closes, so the loaded model is let go of properly.</summary>
    public void Shutdown()
    {
        _cts?.Cancel();
        _engine.Dispose();
    }
}

/// <summary>How hard to clean, and the honest sentence that goes with each choice.</summary>
public sealed record ModeOption(CleanupMode Mode, string Name, string Explanation)
{
    public override string ToString() => Name;

    public static readonly ModeOption[] All =
    {
        new(CleanupMode.LevelsOnly, "Levels and hum only",
            "Nothing touches the background noise. Mains hum and rumble come out, the loudness is " +
            "evened out, and the recording is otherwise exactly as it was. The safest option, and " +
            "the right one when the recording is already clean and just too quiet."),

        new(CleanupMode.Gentle, "Reduce the noise, keep the quality",
            "Filtering only. The recording comes back at the sample rate and channel count it went " +
            "in at, so nothing about its quality changes. It will take several dB off steady hiss, " +
            "a fan or traffic. It will not perform miracles on a bad recording."),

        new(CleanupMode.Voice, "Remove the noise, speech only",
            "The speech model, and much the strongest option. It comes back at 16 kHz in mono, " +
            "because that is what the model works at. That is right for an interview, a meeting or " +
            "a voice note, and wrong for music or for anything where the recording itself matters.")
    };
}

/// <summary>How much noise reduction, for the gentle mode only.</summary>
public sealed record StrengthOption(double Db, string Name)
{
    public override string ToString() => Name;

    public static readonly StrengthOption[] All =
    {
        new(8, "Light"),
        new(12, "Medium"),
        new(18, "Strong")
    };
}

/// <summary>What loudness to aim for. The numbers are the ones the platforms actually use.</summary>
public sealed record TargetOption(double Lufs, string Name)
{
    public override string ToString() => Name;

    public static readonly TargetOption[] All =
    {
        new(-16, "Normal, for online and podcasts (-16 LUFS)"),
        new(-19, "Quieter, for audiobooks (-19 LUFS)"),
        new(-23, "Broadcast (-23 LUFS)"),
        new(-14, "Louder, for music streaming (-14 LUFS)")
    };
}

/// <summary>Minimal INotifyPropertyChanged, matching the hand-rolled one in the other apps.</summary>
public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }
}

/// <summary>A command with no parameter. Enough for this app; no need for a toolkit.</summary>
public sealed class RelayCommand : ICommand
{
    private readonly Action _run;
    private readonly Func<bool>? _can;

    public RelayCommand(Action run, Func<bool>? can = null) { _run = run; _can = can; }

    public bool CanExecute(object? parameter) => _can?.Invoke() ?? true;
    public void Execute(object? parameter) => _run();
    public event EventHandler? CanExecuteChanged;
    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
