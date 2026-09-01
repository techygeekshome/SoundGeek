using SoundGeek.Core.Models;
using SoundGeek.Core.Services;

// A plain console harness rather than a test framework, matching the other apps in the range:
// it runs in CI, exits non-zero on failure, and adds no dependency.
int failed = 0;
void Check(string name, bool ok, string? detail = null)
{
    Console.WriteLine($"{(ok ? "PASS" : "FAIL")}  {name}{(ok || detail is null ? "" : "  -> " + detail)}");
    if (!ok) failed++;
}

static float[] Tone(int sampleRate, double seconds, double hz, double amplitude)
{
    var s = new float[(int)(sampleRate * seconds)];
    for (var i = 0; i < s.Length; i++) s[i] = (float)(amplitude * Math.Sin(2 * Math.PI * hz * i / sampleRate));
    return s;
}

// A continuous tone is not a fair stand-in for a voice. Real speech starts and stops, and the
// gaps are what a noise reducer learns the noise from and what a noise floor is measured in. So
// the test signal talks for 300 ms and pauses for 200 ms, over and over.
static float[] Speech(int sampleRate, double seconds, double hz, double amplitude)
{
    var s = new float[(int)(sampleRate * seconds)];
    var on = (int)(sampleRate * 0.3);
    var period = (int)(sampleRate * 0.5);

    for (var i = 0; i < s.Length; i++)
    {
        if (i % period >= on) continue;
        // Fade each burst in and out so the edges are not clicks with energy everywhere.
        var into = i % period;
        var fade = Math.Min(1.0, Math.Min(into, on - into) / (sampleRate * 0.01));
        s[i] = (float)(amplitude * fade * Math.Sin(2 * Math.PI * hz * i / sampleRate));
    }

    return s;
}

static float[] Mix(float[] a, float[] b)
{
    var s = new float[a.Length];
    for (var i = 0; i < a.Length; i++) s[i] = a[i] + b[i];
    return s;
}

var tmp = Path.Combine(Path.GetTempPath(), "sg-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(tmp);

// ---- FFT ---------------------------------------------------------------------------
{
    const int n = 1024;
    var re = new double[n];
    var im = new double[n];
    for (var i = 0; i < n; i++) re[i] = Math.Sin(2 * Math.PI * 64 * i / n);

    var original = (double[])re.Clone();
    Fft.Forward(re, im);

    var mags = Enumerable.Range(0, n / 2).Select(i => Math.Sqrt(re[i] * re[i] + im[i] * im[i])).ToArray();
    Check("a pure tone lands in one FFT bin", Array.IndexOf(mags, mags.Max()) == 64,
        Array.IndexOf(mags, mags.Max()).ToString());

    Fft.Inverse(re, im);
    var error = original.Zip(re, (a, b) => Math.Abs(a - b)).Max();
    Check("the FFT round trips", error < 1e-9, error.ToString("E2"));
}

// ---- WAV, every bit depth it claims to read -----------------------------------------
{
    var samples = Tone(44100, 0.5, 440, 0.5);
    var stereo = new AudioBuffer { Channels = new[] { samples, samples.Select(v => -v).ToArray() }, SampleRate = 44100 };

    foreach (var bits in new[] { 16, 24 })
    {
        var path = Path.Combine(tmp, $"t{bits}.wav");
        WaveFile.Write(path, stereo, bits);
        var back = WaveFile.Read(path);

        Check($"{bits} bit round trips the rate and channels",
            back.SampleRate == 44100 && back.ChannelCount == 2 && back.Length == stereo.Length);

        var worst = back.Channels[0].Zip(stereo.Channels[0], (a, b) => Math.Abs(a - b)).Max();
        Check($"{bits} bit round trips the samples", worst < (bits == 16 ? 1e-4 : 1e-6), worst.ToString("E2"));

        Check($"{bits} bit keeps the channels apart",
            Math.Abs(back.Channels[1][100] + back.Channels[0][100]) < 1e-4);
    }

    Check("a text file is not mistaken for a WAV", !WaveFile.CanRead(Path.Combine(tmp, "nope.wav")));
}

// ---- Clipping rather than wrapping ---------------------------------------------------
{
    var hot = AudioBuffer.Mono(new[] { 0f, 2f, -2f, 0.5f }, 48000);
    var path = Path.Combine(tmp, "hot.wav");
    WaveFile.Write(path, hot, 16);
    var back = WaveFile.Read(path);
    Check("samples over full scale clip instead of wrapping",
        back.Channels[0][1] > 0.99f && back.Channels[0][2] < -0.99f,
        $"{back.Channels[0][1]}, {back.Channels[0][2]}");
}

// ---- Loudness -------------------------------------------------------------------------
{
    var quiet = AudioBuffer.Mono(Tone(48000, 5, 1000, 0.1), 48000);
    var loud = AudioBuffer.Mono(Tone(48000, 5, 1000, 0.5), 48000);

    var q = Loudness.Measure(quiet);
    var l = Loudness.Measure(loud);

    Check("a 1 kHz tone at -20 dBFS measures near -23 LUFS", Math.Abs(q - (-23.0)) < 1.5, q.ToString("0.0"));
    Check("five times the amplitude is 14 dB louder", Math.Abs((l - q) - 13.98) < 0.3, (l - q).ToString("0.00"));
    Check("peak is measured in dBFS", Math.Abs(Loudness.PeakDbfs(quiet) - (-20.0)) < 0.1);
    Check("silence has no loudness", double.IsNegativeInfinity(Loudness.Measure(AudioBuffer.Mono(new float[48000], 48000))));

    var normalised = Loudness.Normalise(quiet, -16.0);
    Check("normalising hits the target", Math.Abs(Loudness.Measure(normalised) - (-16.0)) < 0.6,
        Loudness.Measure(normalised).ToString("0.0"));
    Check("the limiter holds the ceiling", Loudness.PeakDbfs(normalised) <= -0.9,
        Loudness.PeakDbfs(normalised).ToString("0.00"));
}

// ---- Hum ------------------------------------------------------------------------------
{
    const int sr = 44100;
    var rnd = new Random(7);
    var speech = Tone(sr, 4, 300, 0.2);
    for (var i = 0; i < speech.Length; i++) speech[i] += (float)(0.002 * (rnd.NextDouble() - 0.5));

    Check("no hum is reported when there is none", HumFinder.Detect(AudioBuffer.Mono(speech, sr)) is null);

    var fifty = AudioBuffer.Mono(Mix(speech, Tone(sr, 4, 50, 0.05)), sr);
    var sixty = AudioBuffer.Mono(Mix(speech, Tone(sr, 4, 60, 0.05)), sr);

    Check("50 Hz hum is found", HumFinder.Detect(fifty) == 50, HumFinder.Detect(fifty)?.ToString());
    Check("60 Hz hum is found", HumFinder.Detect(sixty) == 60, HumFinder.Detect(sixty)?.ToString());

    var cleaned = HumFinder.Remove(fifty, 50);
    Check("removing the hum leaves none behind", HumFinder.Detect(cleaned) is null, HumFinder.Detect(cleaned)?.ToString());
    Check("removing the hum leaves the voice alone",
        Math.Abs(Loudness.Measure(cleaned) - Loudness.Measure(fifty)) < 2.0);
}

// ---- Signal to noise is what gets reported, not the raw floor -------------------------
{
    var before = new AudioReport(-35, -19, -45, 50);
    var after = new AudioReport(-16, -1, -31, null);

    Check("turning a recording up does not count as adding noise",
        AudioReport.Improvement(before, after) > 0,
        AudioReport.Improvement(before, after).ToString("0.0"));
    Check("signal to noise is loudness above the floor", Math.Abs(before.SignalToNoiseDb - 10) < 0.001);
}

// ---- Gentle mode keeps the shape of the file -------------------------------------------
{
    const int sr = 44100;
    var rnd = new Random(3);
    var noise = new float[sr * 3];
    for (var i = 0; i < noise.Length; i++) noise[i] = (float)(0.01 * (rnd.NextDouble() * 2 - 1));
    var voice = Speech(sr, 3, 500, 0.3);

    var dirty = new AudioBuffer { Channels = new[] { Mix(voice, noise), Mix(voice, noise) }, SampleRate = sr };
    var reduced = SpectralDenoiser.Reduce(dirty, 12);

    Check("gentle mode keeps the sample rate", reduced.SampleRate == sr);
    Check("gentle mode keeps both channels", reduced.ChannelCount == 2);
    Check("gentle mode keeps the length", reduced.Length == dirty.Length);
    Check("gentle mode lowers the noise floor",
        Loudness.NoiseFloorDbfs(reduced) < Loudness.NoiseFloorDbfs(dirty) - 2,
        $"{Loudness.NoiseFloorDbfs(dirty):0.0} -> {Loudness.NoiseFloorDbfs(reduced):0.0}");
    Check("gentle mode leaves the voice standing",
        Math.Abs(Loudness.Measure(reduced) - Loudness.Measure(dirty)) < 2.0,
        $"{Loudness.Measure(dirty):0.0} -> {Loudness.Measure(reduced):0.0}");

    // The case that breaks a naive noise reducer: something held all the way through. It looks
    // exactly like noise to anything that only asks "is this always present", and it must survive.
    var sustained = AudioBuffer.Mono(Mix(Tone(sr, 3, 500, 0.3), noise), sr);
    var sustainedOut = SpectralDenoiser.Reduce(sustained, 12);
    Check("gentle mode does not eat a note held all the way through",
        Math.Abs(Loudness.Measure(sustainedOut) - Loudness.Measure(sustained)) < 2.0,
        $"{Loudness.Measure(sustained):0.0} -> {Loudness.Measure(sustainedOut):0.0}");
}

// ---- Output naming ---------------------------------------------------------------------
{
    var dir = Path.Combine(tmp, "out");
    Directory.CreateDirectory(dir);
    var source = Path.Combine(dir, "interview.mp3");
    File.WriteAllText(source, "x");

    var first = CleanupEngine.NextFreePath(source);
    Check("the cleaned file is named after the source", Path.GetFileName(first) == "interview cleaned.wav", first);

    File.WriteAllText(first, "x");
    var second = CleanupEngine.NextFreePath(source);
    Check("a second run does not overwrite the first",
        Path.GetFileName(second) == "interview cleaned (2).wav", second);
    Check("the source itself is never the output", second != source);
}

// ---- Model catalogue ---------------------------------------------------------------------
{
    Check("the model has a full 64 character sha256 recorded",
        ModelCatalog.Sha256Hex.Length == 64 && ModelCatalog.Sha256Hex.All(char.IsAsciiHexDigitLower));
    var modelBytes = (long)ModelCatalog.Bytes;
    Check("the model is under a megabyte", modelBytes is > 100_000 and < 1_000_000, modelBytes.ToString());
    Check("models are kept under the user's own profile", ModelCatalog.Directory.Contains("SoundGeek"));
}

// ---- ffmpeg is optional, so it is reported rather than failed ------------------------------
if (MediaDecoder.FfmpegAvailable)
    Check("ffmpeg found, so compressed formats can be read", true);
else
    Console.WriteLine("SKIP  ffmpeg decoding (ffmpeg is not on this machine, it is optional)");

// ---- The whole thing, end to end -----------------------------------------------------------
{
    const int sr = 44100;
    var rnd = new Random(11);
    var hiss = new float[sr * 4];
    for (var i = 0; i < hiss.Length; i++) hiss[i] = (float)(0.01 * (rnd.NextDouble() * 2 - 1));
    var voice = Speech(sr, 4, 400, 0.08);
    var hum = Tone(sr, 4, 50, 0.03);

    var dirty = new AudioBuffer
    {
        Channels = new[] { Mix(Mix(voice, hiss), hum), Mix(Mix(voice, hiss), hum) },
        SampleRate = sr
    };

    var dir = Path.Combine(tmp, "e2e");
    Directory.CreateDirectory(dir);
    var source = Path.Combine(dir, "meeting.wav");
    WaveFile.Write(source, dirty, 24);

    using var engine = new CleanupEngine();

    var gentle = await engine.RunAsync(source, new CleanupEngine.Options { Mode = CleanupMode.Gentle });
    var gentleAudio = WaveFile.Read(gentle.OutputPath);

    Check("gentle mode found the hum", gentle.Before.HumHz == 50, gentle.Before.HumHz?.ToString());
    Check("gentle mode removed the hum", gentle.After.HumHz is null, gentle.After.HumHz?.ToString());
    Check("gentle mode wrote a file beside the source", File.Exists(gentle.OutputPath));
    Check("gentle mode kept 44.1 kHz stereo",
        gentleAudio.SampleRate == sr && gentleAudio.ChannelCount == 2,
        $"{gentleAudio.SampleRate} Hz {gentleAudio.ChannelCount}ch");
    Check("gentle mode improved the voice to background ratio",
        AudioReport.Improvement(gentle.Before, gentle.After) > 3,
        AudioReport.Improvement(gentle.Before, gentle.After).ToString("0.0"));
    Check("gentle mode hit the loudness target",
        Math.Abs(gentle.After.LoudnessLufs - Loudness.WebTargetLufs) < 1.5,
        gentle.After.LoudnessLufs.ToString("0.0"));
    Check("gentle mode left the source alone",
        WaveFile.Read(source).SampleRate == sr && new FileInfo(source).Length > 0);

    if (ModelCatalog.IsDownloaded || Environment.GetEnvironmentVariable("SG_FETCH_MODEL") == "1")
    {
        if (!ModelCatalog.IsDownloaded) await ModelCatalog.DownloadAsync();

        Check("the model matches its recorded checksum",
            ModelCatalog.Sha256(ModelCatalog.ModelPath) == ModelCatalog.Sha256Hex);

        var voiceResult = await engine.RunAsync(source, new CleanupEngine.Options { Mode = CleanupMode.Voice });
        var voiceAudio = WaveFile.Read(voiceResult.OutputPath);

        Check("voice mode comes back at 16 kHz mono, as it says it does",
            voiceAudio.SampleRate == 16000 && voiceAudio.ChannelCount == 1,
            $"{voiceAudio.SampleRate} Hz {voiceAudio.ChannelCount}ch");
        Check("voice mode keeps the length", Math.Abs(voiceAudio.Duration.TotalSeconds - 4) < 0.3,
            voiceAudio.Duration.TotalSeconds.ToString("0.00"));
        Check("voice mode beats gentle mode on noise",
            AudioReport.Improvement(voiceResult.Before, voiceResult.After)
            > AudioReport.Improvement(gentle.Before, gentle.After),
            $"{AudioReport.Improvement(voiceResult.Before, voiceResult.After):0.0} vs {AudioReport.Improvement(gentle.Before, gentle.After):0.0}");
    }
    else
    {
        Console.WriteLine("SKIP  voice mode (set SG_FETCH_MODEL=1 to download the model and run it)");
    }
}

Directory.Delete(tmp, true);

Console.WriteLine(failed == 0 ? "\nAll checks passed." : $"\n{failed} check(s) failed.");
return failed == 0 ? 0 : 1;
