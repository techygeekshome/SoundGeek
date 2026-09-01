using SoundGeek.Core.Models;

namespace SoundGeek.Core.Services;

/// <summary>
/// Cleans one recording, and reports what it found and what it changed.
///
/// The order of operations is not arbitrary:
///
/// 1. Measure first, so there is a before to compare against.
/// 2. Rumble out. Everything below 60 Hz on a voice recording is desk thumps, traffic and wind.
///    Removing it first means the noise estimate that follows is not dominated by it.
/// 3. Hum out, if there is any. It is at exact frequencies, so it can go completely.
/// 4. Noise down, by whichever method was chosen.
/// 5. Levels last, because the level you want is the level of the finished thing, and normalising
///    before removing noise would set it from a recording that no longer exists.
/// 6. Measure again.
/// </summary>
public sealed class CleanupEngine : IDisposable
{
    private readonly SpeechDenoiser _speech = new();

    public sealed record Options
    {
        public CleanupMode Mode { get; init; } = CleanupMode.Voice;
        public bool RemoveRumble { get; init; } = true;
        public bool RemoveHum { get; init; } = true;
        public bool EvenOutLevels { get; init; } = true;
        public double TargetLufs { get; init; } = Loudness.WebTargetLufs;

        /// <summary>Only used in Gentle mode. Higher removes more noise and sounds less natural.</summary>
        public double GentleStrengthDb { get; init; } = 12.0;
    }

    public sealed record Result(string OutputPath, AudioReport Before, AudioReport After);

    /// <summary>
    /// Cleans <paramref name="sourcePath"/> and writes a new file beside it. The original is only
    /// ever read, and an existing cleaned file is never replaced.
    /// </summary>
    public async Task<Result> RunAsync(
        string sourcePath,
        Options options,
        IProgress<double>? progress = null,
        Action<string>? onStage = null,
        CancellationToken ct = default)
    {
        onStage?.Invoke("Reading the audio…");
        var (wavPath, isTemp) = await MediaDecoder.ToReadableWavAsync(sourcePath, ct).ConfigureAwait(false);

        try
        {
            var audio = WaveFile.Read(wavPath);
            if (audio.Length == 0) throw new AudioFormatException("There is no audio in that file.");

            progress?.Report(0.10);
            onStage?.Invoke("Listening to what is there…");

            var hum = options.RemoveHum ? HumFinder.Detect(audio) : null;
            var before = Measure(audio, hum);

            ct.ThrowIfCancellationRequested();
            progress?.Report(0.20);

            var work = audio;

            if (options.RemoveRumble)
            {
                onStage?.Invoke("Taking out the rumble…");
                work = work.WithChannels(work.Channels
                    .Select(ch => Biquad.HighPass(work.SampleRate, 60).Apply(ch))
                    .ToArray());
            }

            if (hum is not null)
            {
                onStage?.Invoke($"Taking out {hum} Hz mains hum…");
                work = HumFinder.Remove(work, hum.Value);
            }

            progress?.Report(0.30);
            ct.ThrowIfCancellationRequested();

            switch (options.Mode)
            {
                case CleanupMode.Voice:
                    onStage?.Invoke("Removing the background noise…");
                    work = await _speech.RunAsync(work, ct).ConfigureAwait(false);
                    break;

                case CleanupMode.Gentle:
                    onStage?.Invoke("Reducing the background noise…");
                    work = SpectralDenoiser.Reduce(work, options.GentleStrengthDb,
                        new Progress<double>(p => progress?.Report(0.30 + p * 0.50)), ct);
                    break;

                case CleanupMode.LevelsOnly:
                default:
                    break;
            }

            progress?.Report(0.80);
            ct.ThrowIfCancellationRequested();

            if (options.EvenOutLevels)
            {
                onStage?.Invoke("Evening out the levels…");
                work = Loudness.Normalise(work, options.TargetLufs);
            }

            progress?.Report(0.90);
            onStage?.Invoke("Writing the file…");

            var outputPath = NextFreePath(sourcePath);
            WaveFile.Write(outputPath, work, bitsPerSample: 24);

            var after = Measure(work, options.RemoveHum ? HumFinder.Detect(work) : null);
            progress?.Report(1.0);

            return new Result(outputPath, before, after);
        }
        finally
        {
            if (isTemp) MediaDecoder.TryDelete(wavPath);
        }
    }

    private static AudioReport Measure(AudioBuffer audio, int? hum) => new(
        Loudness.Measure(audio),
        Loudness.PeakDbfs(audio),
        Loudness.NoiseFloorDbfs(audio),
        hum);

    /// <summary>
    /// "interview cleaned.wav" beside "interview.mp3", numbered if that is taken.
    ///
    /// The source file is never touched and an existing cleaned file is never replaced. Somebody
    /// who runs a file twice by accident should not lose the version they kept.
    /// </summary>
    public static string NextFreePath(string sourcePath)
    {
        var dir = Path.GetDirectoryName(sourcePath) ?? ".";
        var stem = Path.GetFileNameWithoutExtension(sourcePath);

        var candidate = Path.Combine(dir, $"{stem} cleaned.wav");
        if (!File.Exists(candidate)) return candidate;

        for (var n = 2; n < 1000; n++)
        {
            candidate = Path.Combine(dir, $"{stem} cleaned ({n}).wav");
            if (!File.Exists(candidate)) return candidate;
        }

        return Path.Combine(dir, $"{stem} cleaned {DateTime.Now:yyyyMMdd-HHmmss}.wav");
    }

    public void Dispose() => _speech.Dispose();
}
