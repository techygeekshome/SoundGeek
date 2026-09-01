using SoundGeek.Core.Models;

namespace SoundGeek.Core.Services;

/// <summary>
/// Finds and removes mains hum.
///
/// Mains hum is 50 Hz in most of the world and 60 Hz in North America, and it comes with
/// harmonics: 100, 150, 200 and so on. It is a badly earthed cable or a laptop charger, it is in
/// an enormous number of home recordings, and it is the one kind of noise that can be removed
/// completely rather than merely reduced, because it sits at exactly known frequencies.
///
/// Which of 50 or 60 it is gets measured rather than assumed from the machine's region. A
/// recording made abroad, or downloaded, does not care where it is being cleaned.
/// </summary>
public static class HumFinder
{
    private const int Harmonics = 5;

    /// <summary>50, 60, or null when neither stands far enough above its neighbours to be real.</summary>
    public static int? Detect(AudioBuffer audio)
    {
        var mono = audio.ToMono();
        if (mono.Length < 4096) return null;

        var spectrum = QuietSpectrum(mono, audio.SampleRate, out var binHz);
        if (spectrum is null) return null;

        var fifty = HumScore(spectrum, binHz, 50);
        var sixty = HumScore(spectrum, binHz, 60);

        // 8 dB above the surrounding noise is the point at which a peak is a tone rather than a
        // bump. Below that, filtering would be taking something out that nobody can hear.
        const double threshold = 8.0;
        if (fifty < threshold && sixty < threshold) return null;
        return fifty >= sixty ? 50 : 60;
    }

    /// <summary>Notches out the fundamental and its harmonics, on every channel.</summary>
    public static AudioBuffer Remove(AudioBuffer audio, int humHz)
    {
        var channels = audio.Channels.Select(ch =>
        {
            var work = ch;
            for (var h = 1; h <= Harmonics; h++)
            {
                var f = humHz * h;
                if (f >= audio.SampleRate / 2.0 - 20) break;
                work = Biquad.Notch(audio.SampleRate, f).Apply(work);
            }
            return work;
        }).ToArray();

        return audio.WithChannels(channels);
    }

    /// <summary>
    /// How far the hum frequency and its harmonics stand above the noise around them, in dB.
    ///
    /// The neighbourhood deliberately starts four bins away and skips anything below 25 Hz. Right
    /// next to 50 Hz sits the low end of the voice, and the rumble under it is loud on almost
    /// every real recording, so a naive window either side would compare the hum against the
    /// rumble and conclude there was no hum.
    /// </summary>
    private static double HumScore(double[] spectrum, double binHz, int humHz)
    {
        // The fundamental carries the decision. The harmonics only add to it, and only when they
        // are there: a clean sine from a bad earth has no harmonics at all, and averaging its
        // absent second and third across the score would talk the detector out of a hum that is
        // plainly 24 dB above everything around it.
        var weights = new[] { 1.0, 0.5, 0.25 };
        var total = 0.0;

        for (var h = 1; h <= 3; h++)
        {
            var bin = (int)Math.Round(humHz * h / binHz);
            if (bin < 3 || bin >= spectrum.Length - 12) break;

            var peak = Math.Max(spectrum[bin - 1], Math.Max(spectrum[bin], spectrum[bin + 1]));

            var lowest = (int)Math.Ceiling(25 / binHz);
            var around = new List<double>();
            for (var i = bin - 20; i <= bin + 20; i++)
                if (i >= lowest && i < spectrum.Length && Math.Abs(i - bin) is > 3 and <= 20)
                    around.Add(spectrum[i]);

            if (around.Count == 0) continue;
            around.Sort();
            var median = around[around.Count / 2];

            var score = 20 * Math.Log10(Math.Max(peak, 1e-12) / Math.Max(median, 1e-12));
            total += h == 1 ? score : weights[h - 1] * Math.Max(0, score);
        }

        return total;
    }

    /// <summary>
    /// The spectrum of the quiet parts of the recording.
    ///
    /// Averaging the whole thing does not work: speech is far louder than hum and drowns it. Hum
    /// is the opposite, it is present at the same level all the way through, including in the
    /// gaps. So each frequency takes its value from the quietest quarter of the blocks, which is
    /// the recording with the talking taken out of it, and hum is then the loudest thing left.
    /// </summary>
    private static double[]? QuietSpectrum(float[] mono, int sampleRate, out double binHz)
    {
        const int size = 16384;                                   // about 3 Hz per bin at 48 kHz
        binHz = sampleRate / (double)size;

        if (mono.Length < size) return null;

        var window = Windows.Hann(size);
        var blocks = new List<double[]>();

        var hop = Math.Max(size / 2, (mono.Length - size) / 60);   // up to about 60 blocks
        for (var start = 0; start + size <= mono.Length; start += hop)
        {
            var re = new double[size];
            var im = new double[size];
            for (var i = 0; i < size; i++) re[i] = mono[start + i] * window[i];

            Fft.Forward(re, im);

            var m = new double[size / 2];
            for (var i = 0; i < size / 2; i++) m[i] = Math.Sqrt(re[i] * re[i] + im[i] * im[i]);
            blocks.Add(m);
        }

        if (blocks.Count < 3) return null;

        var quiet = new double[size / 2];
        var column = new double[blocks.Count];
        for (var i = 0; i < size / 2; i++)
        {
            for (var b = 0; b < blocks.Count; b++) column[b] = blocks[b][i];
            Array.Sort(column);
            quiet[i] = column[blocks.Count / 4];
        }

        return quiet;
    }
}
