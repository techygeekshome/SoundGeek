using SoundGeek.Core.Models;

namespace SoundGeek.Core.Services;

/// <summary>
/// Reduces steady background noise without changing the sample rate or the number of channels.
///
/// This is the gentle option, and it exists because the neural model is speech only and comes
/// back at 16 kHz mono. That is right for an interview and wrong for anything where the recording
/// itself matters. This one leaves the file the shape it was.
///
/// How it works: the recording is cut into overlapping blocks and each is turned into a spectrum.
/// Steady noise looks the same in every block; speech and music do not. So the quietest tenth of
/// the blocks, per frequency, is taken as the noise, and every block is then reduced by how much
/// it looks like that noise and no more.
///
/// The reduction is deliberately soft. Pushing it harder produces the warbling, underwater sound
/// that makes people turn noise reduction off, and a recording with a little hiss left in it is
/// better than one that sounds broken.
/// </summary>
public static class SpectralDenoiser
{
    private const int Size = 2048;
    private const int Hop = Size / 4;                              // 75 percent overlap

    /// <summary>
    /// How much the noise estimate is scaled up before it is subtracted.
    ///
    /// It has to be more than one. The estimate comes from the quietest blocks, and the level of
    /// noise in any one block wanders about its average by several dB, so the quietest blocks are
    /// well below the average and subtracting them alone leaves most of the noise behind. Two and
    /// a half is about four dB of headroom, which was arrived at by measurement: enough to
    /// actually shift the noise floor, not so much that the quiet ends of words start to go.
    /// </summary>
    private const double OverSubtraction = 2.5;

    /// <param name="strengthDb">
    /// The most any frequency will be reduced by. 12 dB is a clear improvement that stays
    /// natural; past about 20 dB the artefacts start to be worse than the noise.
    /// </param>
    public static AudioBuffer Reduce(AudioBuffer audio, double strengthDb = 12.0,
        IProgress<double>? progress = null, CancellationToken ct = default)
    {
        if (audio.Length < Size * 2) return audio;

        var floor = Math.Pow(10, -Math.Abs(strengthDb) / 20);
        var analysis = Windows.SqrtHann(Size);

        var channels = new float[audio.ChannelCount][];

        for (var c = 0; c < audio.ChannelCount; c++)
        {
            ct.ThrowIfCancellationRequested();
            channels[c] = ReduceChannel(audio.Channels[c], analysis, floor, ct);
            progress?.Report((c + 1) / (double)audio.ChannelCount);
        }

        return audio.WithChannels(channels);
    }

    /// <summary>
    /// Takes the narrow peaks out of the noise estimate.
    ///
    /// Without this, a sustained note or a held vowel is a disaster. It is present in every block,
    /// so it survives the "quietest tenth" test and gets recorded as noise, and then the thing the
    /// recording is actually of is the thing that gets removed.
    ///
    /// What separates the two is shape, not level. Background noise is broad: hiss, a fan, traffic
    /// all spread across many frequencies at once. A note is narrow, one or two bins standing well
    /// above their neighbours. So the noise estimate is replaced by a rolling median of itself,
    /// which leaves anything broad exactly where it was and flattens anything narrow down to the
    /// level around it. The note is then no longer noise, and it survives.
    /// </summary>
    private static double[] FlattenTonalPeaks(double[] noise)
    {
        const int radius = 4;
        var smoothed = new double[noise.Length];
        var window = new double[radius * 2 + 1];

        for (var i = 0; i < noise.Length; i++)
        {
            var n = 0;
            for (var j = i - radius; j <= i + radius; j++)
                if (j >= 0 && j < noise.Length) window[n++] = noise[j];

            Array.Sort(window, 0, n);
            var median = window[n / 2];

            // Only ever lower the estimate. Raising it would start removing things that the
            // neighbours happen to be loud at, which is the opposite of the point.
            smoothed[i] = Math.Min(noise[i], median);
        }

        return smoothed;
    }

    private static float[] ReduceChannel(float[] input, double[] window, double floor, CancellationToken ct)
    {
        var frames = (input.Length - Size) / Hop + 1;
        if (frames < 4) return input;

        var half = Size / 2 + 1;

        // Pass one: every block's spectrum, kept so the noise estimate can be made from all of
        // them before any of them is changed.
        var mags = new double[frames][];
        var res = new double[frames][];
        var ims = new double[frames][];

        for (var f = 0; f < frames; f++)
        {
            ct.ThrowIfCancellationRequested();

            var re = new double[Size];
            var im = new double[Size];
            var at = f * Hop;
            for (var i = 0; i < Size; i++) re[i] = input[at + i] * window[i];

            Fft.Forward(re, im);

            var m = new double[half];
            for (var i = 0; i < half; i++) m[i] = Math.Sqrt(re[i] * re[i] + im[i] * im[i]);

            mags[f] = m; res[f] = re; ims[f] = im;
        }

        // The noise: for each frequency, the level it sits at in the quietest blocks.
        var noise = new double[half];
        var column = new double[frames];
        for (var i = 0; i < half; i++)
        {
            for (var f = 0; f < frames; f++) column[f] = mags[f][i];
            Array.Sort(column);
            noise[i] = column[Math.Max(0, frames / 10)];
        }

        noise = FlattenTonalPeaks(noise);

        // Pass two: attenuate, and add it all back up.
        var output = new double[input.Length];
        var weight = new double[input.Length];

        for (var f = 0; f < frames; f++)
        {
            ct.ThrowIfCancellationRequested();

            var re = res[f];
            var im = ims[f];
            var m = mags[f];

            for (var i = 0; i < half; i++)
            {
                // A Wiener style gain: how much of this bin is signal rather than noise. Squaring
                // makes the decision less twitchy than a straight ratio, which is what stops the
                // musical noise that plain spectral subtraction is notorious for.
                var snr = Math.Max(0, m[i] * m[i] - OverSubtraction * noise[i] * noise[i]);
                var gain = m[i] <= 0 ? 1 : snr / (m[i] * m[i]);
                gain = Math.Max(floor, gain);

                re[i] *= gain; im[i] *= gain;

                // The spectrum of a real signal is a mirror, so the top half follows the bottom.
                if (i > 0 && i < Size / 2)
                {
                    re[Size - i] = re[i];
                    im[Size - i] = -im[i];
                }
            }

            Fft.Inverse(re, im);

            var at = f * Hop;
            for (var i = 0; i < Size; i++)
            {
                output[at + i] += re[i] * window[i];
                weight[at + i] += window[i] * window[i];
            }
        }

        var result = new float[input.Length];
        for (var i = 0; i < input.Length; i++)
            result[i] = weight[i] > 1e-9 ? (float)(output[i] / weight[i]) : input[i];

        // The tail past the last whole block was never processed, so it is left as it was rather
        // than faded to nothing.
        var covered = (frames - 1) * Hop + Size;
        for (var i = covered; i < input.Length; i++) result[i] = input[i];

        return result;
    }
}
