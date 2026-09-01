using System.Diagnostics;

namespace SoundGeek.Core.Services;

/// <summary>
/// Turns whatever the user dropped in into a WAV that SoundGeek can read.
///
/// Two paths, deliberately:
///
/// 1. A WAV that can be read as it is is used as it is. No decode, no temp file, and crucially
///    no resampling, because this app's whole promise in gentle mode is that the recording comes
///    back the shape it went in.
/// 2. Anything else - MP3, M4A, MP4, MKV, FLAC, OGG, WMA, MOV - needs ffmpeg, and is decoded at
///    its own sample rate and channel count rather than being flattened on the way in.
///
/// **ffmpeg is invoked as a separate process, never linked.** That is not a style choice: the
/// commonly distributed ffmpeg builds are GPL, and linking one into this application would
/// impose licence terms on it that we have not signed up to. Running it as a child process and
/// reading its output is explicitly fine, and is what every well-behaved application does.
///
/// It is also not bundled. If ffmpeg is not present, the app says so plainly and still handles
/// WAV, rather than silently downloading a 90 MB binary the user did not ask for.
/// </summary>
public sealed class MediaDecoder
{
    /// <summary>The rate the speech model works at. Only the Voice mode ever resamples to it.</summary>
    public const int SpeechModelSampleRate = 16_000;

    /// <summary>Extensions we will accept on the drop target.</summary>
    public static readonly string[] SupportedExtensions =
    {
        ".wav", ".mp3", ".m4a", ".mp4", ".mkv", ".mov", ".avi",
        ".flac", ".ogg", ".opus", ".wma", ".aac", ".webm", ".m4v"
    };

    public static bool IsSupported(string path) =>
        SupportedExtensions.Contains(Path.GetExtension(path).ToLowerInvariant());

    private static string? _ffmpegPath;
    private static bool _ffmpegChecked;

    /// <summary>
    /// Where ffmpeg is, or null. Looks beside the executable first so a portable copy can be
    /// dropped next to the app, then falls back to PATH.
    /// </summary>
    public static string? FindFfmpeg()
    {
        if (_ffmpegChecked) return _ffmpegPath;
        _ffmpegChecked = true;

        var exe = OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg";

        var beside = Path.Combine(AppContext.BaseDirectory, exe);
        if (File.Exists(beside)) { _ffmpegPath = beside; return _ffmpegPath; }

        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "")
                     .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var candidate = Path.Combine(dir.Trim(), exe);
                if (File.Exists(candidate)) { _ffmpegPath = candidate; return _ffmpegPath; }
            }
            catch (ArgumentException) { /* a malformed PATH entry is not our problem */ }
        }

        return _ffmpegPath;
    }

    public static bool FfmpegAvailable => FindFfmpeg() is not null;

    /// <summary>
    /// Produces a path to a WAV that <see cref="WaveFile"/> can read. The second value says
    /// whether it is a temporary file the caller must delete.
    ///
    /// Nothing about the audio is changed here: no resampling, no downmixing, no gain. This is a
    /// container problem, not an audio one, and treating it as an audio one is how tools quietly
    /// turn a 48 kHz stereo recording into something else before the user has chosen anything.
    /// </summary>
    public static async Task<(string Path, bool IsTemporary)> ToReadableWavAsync(
        string sourcePath, CancellationToken ct = default)
    {
        if (WaveFile.CanRead(sourcePath))
            return (sourcePath, false);

        var ffmpeg = FindFfmpeg()
            ?? throw new MediaDecodeException(
                $"{Path.GetExtension(sourcePath)} files need ffmpeg, and it was not found on this machine. " +
                "Put ffmpeg.exe next to SoundGeek, or convert the file to a WAV first.");

        var temp = Path.Combine(Path.GetTempPath(), $"soundgeek-{Guid.NewGuid():N}.wav");

        var psi = new ProcessStartInfo(ffmpeg)
        {
            // -vn drops any video stream. No -ar and no -ac on purpose: the file keeps its own
            // sample rate and channels. 24 bit so a quiet recording does not lose detail on the
            // way in and then get amplified.
            Arguments = $"-hide_banner -loglevel error -nostdin -y -i \"{sourcePath}\" " +
                        $"-vn -c:a pcm_s24le -f wav \"{temp}\"",
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var proc = Process.Start(psi)
            ?? throw new MediaDecodeException("ffmpeg would not start.");

        var stderr = await proc.StandardError.ReadToEndAsync(ct).ConfigureAwait(false);
        await proc.WaitForExitAsync(ct).ConfigureAwait(false);

        if (proc.ExitCode != 0 || !File.Exists(temp))
        {
            TryDelete(temp);
            var detail = string.IsNullOrWhiteSpace(stderr) ? "" : " " + stderr.Trim().Split('\n').Last();
            throw new MediaDecodeException($"ffmpeg could not read that file.{detail}");
        }

        return (temp, true);
    }

    /// <summary>Media length, via ffprobe if it is there. Null rather than throwing - it is only used for display.</summary>
    public static async Task<TimeSpan?> TryGetDurationAsync(string path, CancellationToken ct = default)
    {
        var ffmpeg = FindFfmpeg();
        if (ffmpeg is null) return null;

        var probe = Path.Combine(Path.GetDirectoryName(ffmpeg)!,
            OperatingSystem.IsWindows() ? "ffprobe.exe" : "ffprobe");
        if (!File.Exists(probe)) return null;

        try
        {
            var psi = new ProcessStartInfo(probe)
            {
                Arguments = $"-v error -show_entries format=duration -of default=nw=1:nk=1 \"{path}\"",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var p = Process.Start(psi);
            if (p is null) return null;
            var s = await p.StandardOutput.ReadToEndAsync(ct).ConfigureAwait(false);
            await p.WaitForExitAsync(ct).ConfigureAwait(false);
            return double.TryParse(s.Trim(), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var secs)
                ? TimeSpan.FromSeconds(secs)
                : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (IOException) { /* a temp file we cannot delete is not worth failing a job over */ }
        catch (UnauthorizedAccessException) { }
    }
}

public sealed class MediaDecodeException : Exception
{
    public MediaDecodeException(string message) : base(message) { }
}
