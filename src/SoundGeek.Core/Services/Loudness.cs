using SoundGeek.Core.Models;

namespace SoundGeek.Core.Services;

/// <summary>
/// Measures how loud a recording actually sounds, and evens it out.
///
/// Peak level is nearly useless for this. A recording can peak at 0 dB and still sound quiet,
/// because loudness is about how much energy there is across the whole thing and which
/// frequencies carry it. ITU-R BS.1770 is the standard that gets this right and is what
/// broadcasters, YouTube and Spotify all use, so it is what SoundGeek uses.
///
/// The measurement is: filter the signal the way a head and ear change it, take the mean square
/// over 400 ms blocks, throw away the blocks that are effectively silence, and average what is
/// left. The gating is the part that matters. Without it, the pauses in a conversation drag the
/// answer down and quiet recordings get made far too loud.
/// </summary>
public static class Loudness
{
    /// <summary>What most online audio is mastered to. Broadcast is -23.</summary>
    public const double WebTargetLufs = -16.0;

    private const double AbsoluteGateLufs = -70.0;
    private const double RelativeGateDb = -10.0;

    /// <summary>Integrated loudness in LUFS, or negative infinity for silence.</summary>
    public static double Measure(AudioBuffer audio)
    {
        var blockSize = (int)(audio.SampleRate * 0.4);            // 400 ms
        var hop = blockSize / 4;                                   // 75 percent overlap, as specified
        if (blockSize <= 0 || audio.Length < blockSize) return double.NegativeInfinity;

        // Every channel is K-weighted, then the block powers are summed across channels.
        var weighted = audio.Channels
            .Select(ch => Biquad.HighShelf1770(audio.SampleRate).Apply(
                          Biquad.HighPass1770(audio.SampleRate).Apply(ch)))
            .ToArray();

        var blocks = new List<double>();
        for (var start = 0; start + blockSize <= audio.Length; start += hop)
        {
            double power = 0;
            foreach (var ch in weighted)
            {
                double sum = 0;
                for (var i = start; i < start + blockSize; i++) sum += (double)ch[i] * ch[i];
                power += sum / blockSize;                           // channel weights are 1.0 for mono and stereo
            }
            blocks.Add(power);
        }

        if (blocks.Count == 0) return double.NegativeInfinity;

        static double Lufs(double power) => power <= 0 ? double.NegativeInfinity : -0.691 + 10 * Math.Log10(power);

        // Absolute gate first, then a relative gate 10 dB below whatever survived it.
        var loud = blocks.Where(p => Lufs(p) > AbsoluteGateLufs).ToList();
        if (loud.Count == 0) return double.NegativeInfinity;

        var relative = Lufs(loud.Average()) + RelativeGateDb;
        var kept = loud.Where(p => Lufs(p) > relative).ToList();
        if (kept.Count == 0) kept = loud;

        return Lufs(kept.Average());
    }

    public static double PeakDbfs(AudioBuffer audio)
    {
        var peak = 0f;
        foreach (var ch in audio.Channels)
        foreach (var v in ch)
            peak = Math.Max(peak, Math.Abs(v));

        return peak <= 0 ? double.NegativeInfinity : 20 * Math.Log10(peak);
    }

    /// <summary>
    /// The level of the quietest tenth of the recording, which is a fair proxy for what the
    /// listener hears as hiss and hum between the words.
    /// </summary>
    public static double NoiseFloorDbfs(AudioBuffer audio)
    {
        var mono = audio.ToMono();
        var frame = Math.Max(1, audio.SampleRate / 50);            // 20 ms
        if (mono.Length < frame) return double.NegativeInfinity;

        var levels = new List<double>();
        for (var i = 0; i + frame <= mono.Length; i += frame)
        {
            double sum = 0;
            for (var j = i; j < i + frame; j++) sum += (double)mono[j] * mono[j];
            levels.Add(Math.Sqrt(sum / frame));
        }

        levels.Sort();
        var quiet = levels.Take(Math.Max(1, levels.Count / 10)).ToList();
        var rms = quiet.Average();
        return rms <= 0 ? double.NegativeInfinity : 20 * Math.Log10(rms);
    }

    /// <summary>
    /// Brings the recording to a target loudness, then holds the peaks down.
    ///
    /// The order matters. Gain first so the whole thing sits where it should, then a limiter,
    /// because gain applied after limiting would push the peaks straight back through the
    /// ceiling and there would have been no point limiting at all.
    /// </summary>
    public static AudioBuffer Normalise(AudioBuffer audio, double targetLufs, double ceilingDbfs = -1.0)
    {
        var current = Measure(audio);
        if (double.IsNegativeInfinity(current)) return audio;

        // More than 30 dB of gain on a recording this quiet would be amplifying the hiss and
        // nothing else, so it is capped and the app says what it did rather than ruining the file.
        var gainDb = Math.Clamp(targetLufs - current, -30, 30);
        var gain = Math.Pow(10, gainDb / 20);

        var channels = audio.Channels
            .Select(ch => ch.Select(v => (float)(v * gain)).ToArray())
            .ToArray();

        return Limiter.Apply(audio.WithChannels(channels), ceilingDbfs);
    }
}
