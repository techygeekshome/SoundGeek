namespace SoundGeek.Core.Models;

/// <summary>
/// Audio in memory, as floats between -1 and 1, one array per channel.
///
/// Interleaved samples are how files store audio and are miserable to process, so everything
/// past the reader works on separate channels and the writer puts them back together.
/// </summary>
public sealed class AudioBuffer
{
    public required float[][] Channels { get; init; }
    public required int SampleRate { get; init; }

    public int ChannelCount => Channels.Length;
    public int Length => Channels.Length == 0 ? 0 : Channels[0].Length;
    public TimeSpan Duration => TimeSpan.FromSeconds(Length / (double)SampleRate);

    public static AudioBuffer Mono(float[] samples, int sampleRate) =>
        new() { Channels = new[] { samples }, SampleRate = sampleRate };

    /// <summary>Every channel averaged into one. Used for measuring, never for output.</summary>
    public float[] ToMono()
    {
        if (ChannelCount == 1) return Channels[0];

        var mixed = new float[Length];
        for (var i = 0; i < Length; i++)
        {
            double sum = 0;
            foreach (var ch in Channels) sum += ch[i];
            mixed[i] = (float)(sum / ChannelCount);
        }
        return mixed;
    }

    public AudioBuffer WithChannels(float[][] channels) =>
        new() { Channels = channels, SampleRate = SampleRate };
}

/// <summary>What a recording measured like, before or after work was done to it.</summary>
/// <param name="LoudnessLufs">Integrated loudness to ITU-R BS.1770. Broadcast uses -23, the web uses -16.</param>
/// <param name="PeakDbfs">The loudest single sample, in dB below full scale.</param>
/// <param name="NoiseFloorDbfs">The level of the quietest tenth of the recording. What hiss sounds like.</param>
/// <param name="HumHz">50 or 60 if mains hum was found, otherwise null.</param>
public sealed record AudioReport(
    double LoudnessLufs,
    double PeakDbfs,
    double NoiseFloorDbfs,
    int? HumHz)
{
    /// <summary>
    /// How far the voice sits above the background, in dB. This, not the noise floor on its own,
    /// is the number that matters.
    ///
    /// A raw noise floor is easy to misread. Turning a quiet recording up makes the noise floor
    /// worse by exactly as many dB as it makes everything else better, so a cleanup that also
    /// evened out the levels would look like it had added noise. The gap between the two is what
    /// a listener actually hears, and it does not move when the volume does.
    /// </summary>
    public double SignalToNoiseDb =>
        double.IsNegativeInfinity(LoudnessLufs) || double.IsNegativeInfinity(NoiseFloorDbfs)
            ? 0
            : LoudnessLufs - NoiseFloorDbfs;

    /// <summary>How much cleaner it got. The number the whole app exists to move.</summary>
    public static double Improvement(AudioReport before, AudioReport after) =>
        after.SignalToNoiseDb - before.SignalToNoiseDb;
}

/// <summary>How hard to clean, chosen on the Clean screen.</summary>
public enum CleanupMode
{
    /// <summary>The neural speech model. Strongest by far, and 16 kHz mono out.</summary>
    Voice,

    /// <summary>Filtering only, at the original sample rate and channel count.</summary>
    Gentle,

    /// <summary>Levels and hum only. Nothing touches the noise.</summary>
    LevelsOnly
}

/// <summary>A file waiting to be cleaned, or one that has been.</summary>
public sealed class CleanupJob
{
    public required string SourcePath { get; init; }
    public string FileName => Path.GetFileName(SourcePath);

    public JobState State { get; set; } = JobState.Waiting;
    public string Status { get; set; } = "Waiting";
    public double Progress { get; set; }

    public TimeSpan? Duration { get; set; }
    public string? OutputPath { get; set; }

    public AudioReport? Before { get; set; }
    public AudioReport? After { get; set; }

    public string? Error { get; set; }
}

public enum JobState
{
    Waiting,
    Running,
    Done,
    Failed,
    Cancelled
}
