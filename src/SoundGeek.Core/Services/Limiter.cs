using SoundGeek.Core.Models;

namespace SoundGeek.Core.Services;

/// <summary>
/// Holds the peaks below a ceiling without the clicks that simple clipping causes.
///
/// It looks ahead a few milliseconds, works out the gain each sample needs, then smooths that
/// gain curve before applying it. Reducing gain instantly on a peak makes an audible tick; easing
/// into it and back out does not. The look-ahead is what lets the easing start before the peak
/// arrives rather than after it.
/// </summary>
public static class Limiter
{
    public static AudioBuffer Apply(AudioBuffer audio, double ceilingDbfs = -1.0)
    {
        if (audio.Length == 0) return audio;

        var ceiling = Math.Pow(10, ceilingDbfs / 20);
        var lookAhead = Math.Max(1, audio.SampleRate / 200);       // 5 ms
        var release = Math.Max(1, audio.SampleRate / 20);          // 50 ms

        // The loudest sample across all channels at each point, so a stereo pair keeps its image
        // rather than one side ducking on its own.
        var peak = new float[audio.Length];
        foreach (var ch in audio.Channels)
            for (var i = 0; i < audio.Length; i++)
                peak[i] = Math.Max(peak[i], Math.Abs(ch[i]));

        var gain = new double[audio.Length];
        for (var i = 0; i < audio.Length; i++)
            gain[i] = peak[i] > ceiling ? ceiling / peak[i] : 1.0;

        // Pull each reduction earlier by the look-ahead, so it is already in place when the peak
        // lands rather than catching up afterwards.
        var target = new double[audio.Length];
        Array.Fill(target, 1.0);
        for (var i = 0; i < audio.Length; i++)
        {
            if (gain[i] >= 1.0) continue;
            var from = Math.Max(0, i - lookAhead);
            for (var j = from; j <= i; j++) target[j] = Math.Min(target[j], gain[i]);
        }

        // Attack instantly, release slowly. A fast release pumps; this is a one pole smoother.
        var releaseCoefficient = Math.Exp(-1.0 / release);
        var smoothed = new double[audio.Length];
        var g = 1.0;
        for (var i = 0; i < audio.Length; i++)
        {
            g = target[i] < g ? target[i] : target[i] + (g - target[i]) * releaseCoefficient;
            smoothed[i] = g;
        }

        var channels = audio.Channels
            .Select(ch =>
            {
                var outCh = new float[ch.Length];
                for (var i = 0; i < ch.Length; i++) outCh[i] = (float)(ch[i] * smoothed[i]);
                return outCh;
            })
            .ToArray();

        return audio.WithChannels(channels);
    }
}
