using System.Diagnostics;
using System.Windows.Input;
using Avalonia.Media;
using Avalonia.Threading;
using SoundGeek.Core.Models;

namespace SoundGeek.ViewModels;

/// <summary>
/// One row in the queue. Wraps a <see cref="CleanupJob"/> rather than replacing it, so the Core
/// project stays free of anything to do with the screen.
/// </summary>
public sealed class JobViewModel : ObservableObject
{
    public JobViewModel(CleanupJob job)
    {
        Job = job;
        OpenFile = new RelayCommand(() => OpenPath(Job.OutputPath));
        OpenFolder = new RelayCommand(() => OpenPath(Path.GetDirectoryName(Job.SourcePath)));
    }

    public CleanupJob Job { get; }

    public ICommand OpenFile { get; }
    public ICommand OpenFolder { get; }

    public string FileName => Job.FileName;
    public string Status => Job.Status;
    public double Progress => Job.Progress * 100;

    public bool ShowProgress => Job.State == JobState.Running;
    public bool HasOutput => Job.OutputPath is not null && File.Exists(Job.OutputPath);

    public string StateText => Job.State switch
    {
        JobState.Waiting => "Waiting",
        JobState.Running => "Working",
        JobState.Done => "Done",
        JobState.Failed => "Failed",
        JobState.Cancelled => "Stopped",
        _ => ""
    };

    public IBrush StateBrush => Job.State switch
    {
        JobState.Done => Brush.Parse("#4ED17E"),
        JobState.Failed => Brush.Parse("#E86A6A"),
        JobState.Running => Brush.Parse("#7BA9F6"),
        JobState.Cancelled => Brush.Parse("#E8B45A"),
        _ => Brush.Parse("#39405A")
    };

    /// <summary>
    /// The before and after, in one line. This is the whole point of the app, so it is stated in
    /// numbers rather than left as "Done".
    /// </summary>
    public string Summary
    {
        get
        {
            if (Job.Before is not { } before || Job.After is not { } after || Job.OutputPath is null)
                return Job.Status;

            var parts = new List<string> { $"Saved {Path.GetFileName(Job.OutputPath)}" };

            var improvement = AudioReport.Improvement(before, after);
            if (improvement >= 1)
                parts.Add($"voice sits {improvement:0} dB further above the background");

            if (before.HumHz is { } hz) parts.Add($"{hz} Hz hum removed");

            if (Math.Abs(after.LoudnessLufs - before.LoudnessLufs) >= 1
                && !double.IsNegativeInfinity(after.LoudnessLufs))
                parts.Add($"levelled to {after.LoudnessLufs:0.0} LUFS");

            return string.Join(", ", parts) + ".";
        }
    }

    /// <summary>The detail line under the summary. Empty until there is something to say.</summary>
    public string Detail
    {
        get
        {
            if (Job.Before is not { } before || Job.After is not { } after) return "";

            return $"background {before.NoiseFloorDbfs:0.0} to {after.NoiseFloorDbfs:0.0} dBFS   ·   " +
                   $"loudness {before.LoudnessLufs:0.0} to {after.LoudnessLufs:0.0} LUFS   ·   " +
                   $"peak {before.PeakDbfs:0.0} to {after.PeakDbfs:0.0} dBFS";
        }
    }

    public bool HasDetail => Job.Before is not null && Job.After is not null;

    public void SetState(JobState state, string status)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => SetState(state, status));
            return;
        }

        Job.State = state;
        Job.Status = status;
        Refresh();
    }

    public void Refresh()
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(Refresh);
            return;
        }

        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(Progress));
        OnPropertyChanged(nameof(ShowProgress));
        OnPropertyChanged(nameof(StateText));
        OnPropertyChanged(nameof(StateBrush));
        OnPropertyChanged(nameof(HasOutput));
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(Detail));
        OnPropertyChanged(nameof(HasDetail));
    }

    /// <summary>
    /// Hands the path to the shell. UseShellExecute is what makes Windows open it in whatever the
    /// user has chosen for that file type rather than trying to run it.
    /// </summary>
    private static void OpenPath(string? path)
    {
        if (string.IsNullOrEmpty(path)) return;

        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Write($"Opening {path}: {ex.Message}");
        }
    }
}
