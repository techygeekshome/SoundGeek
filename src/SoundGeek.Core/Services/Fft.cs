namespace SoundGeek.Core.Services;

/// <summary>
/// An in-place radix-2 FFT. Small enough to own rather than take a dependency for, and the only
/// maths in SoundGeek that has a single right answer, so it is tested against a known signal.
/// </summary>
public static class Fft
{
    /// <summary>Transforms in place. <paramref name="re"/> and <paramref name="im"/> must be a power of two long.</summary>
    public static void Forward(double[] re, double[] im) => Transform(re, im, inverse: false);

    public static void Inverse(double[] re, double[] im)
    {
        Transform(re, im, inverse: true);
        var n = re.Length;
        for (var i = 0; i < n; i++) { re[i] /= n; im[i] /= n; }
    }

    private static void Transform(double[] re, double[] im, bool inverse)
    {
        var n = re.Length;
        if (n != im.Length || (n & (n - 1)) != 0)
            throw new ArgumentException("The FFT needs two arrays of the same power of two length.");

        // Bit reversal, so the butterflies below can run over neighbouring pairs.
        for (int i = 1, j = 0; i < n; i++)
        {
            var bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1) j ^= bit;
            j ^= bit;
            if (i < j) { (re[i], re[j]) = (re[j], re[i]); (im[i], im[j]) = (im[j], im[i]); }
        }

        for (var len = 2; len <= n; len <<= 1)
        {
            var angle = 2 * Math.PI / len * (inverse ? 1 : -1);
            var wRe = Math.Cos(angle);
            var wIm = Math.Sin(angle);

            for (var i = 0; i < n; i += len)
            {
                double curRe = 1, curIm = 0;
                for (var j = 0; j < len / 2; j++)
                {
                    var uRe = re[i + j];
                    var uIm = im[i + j];
                    var vRe = re[i + j + len / 2] * curRe - im[i + j + len / 2] * curIm;
                    var vIm = re[i + j + len / 2] * curIm + im[i + j + len / 2] * curRe;

                    re[i + j] = uRe + vRe;
                    im[i + j] = uIm + vIm;
                    re[i + j + len / 2] = uRe - vRe;
                    im[i + j + len / 2] = uIm - vIm;

                    var nextRe = curRe * wRe - curIm * wIm;
                    curIm = curRe * wIm + curIm * wRe;
                    curRe = nextRe;
                }
            }
        }
    }
}
