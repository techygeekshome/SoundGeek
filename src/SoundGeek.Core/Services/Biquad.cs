namespace SoundGeek.Core.Services;

/// <summary>
/// A two pole, two zero filter. The building block for everything in this file: the loudness
/// weighting curves, the rumble filter and the notches that take mains hum out.
///
/// Applied forwards only, which shifts the phase a little. That is inaudible on the filters used
/// here and it means the whole recording does not have to be in memory twice.
/// </summary>
public sealed class Biquad
{
    private readonly double _b0, _b1, _b2, _a1, _a2;

    private Biquad(double b0, double b1, double b2, double a0, double a1, double a2)
    {
        _b0 = b0 / a0; _b1 = b1 / a0; _b2 = b2 / a0;
        _a1 = a1 / a0; _a2 = a2 / a0;
    }

    public float[] Apply(float[] input)
    {
        var output = new float[input.Length];
        double x1 = 0, x2 = 0, y1 = 0, y2 = 0;

        for (var i = 0; i < input.Length; i++)
        {
            double x = input[i];
            var y = _b0 * x + _b1 * x1 + _b2 * x2 - _a1 * y1 - _a2 * y2;
            x2 = x1; x1 = x;
            y2 = y1; y1 = y;
            output[i] = (float)y;
        }

        return output;
    }

    /// <summary>
    /// A very narrow cut at one frequency. Q of 30 is about 2 Hz wide at 50 Hz, which removes
    /// hum without taking a bite out of the voice sitting above it.
    /// </summary>
    public static Biquad Notch(int sampleRate, double frequency, double q = 30)
    {
        var w = 2 * Math.PI * frequency / sampleRate;
        var alpha = Math.Sin(w) / (2 * q);
        var cos = Math.Cos(w);

        return new Biquad(1, -2 * cos, 1, 1 + alpha, -2 * cos, 1 - alpha);
    }

    /// <summary>Rolls off everything below the given frequency. Takes out desk thumps and traffic.</summary>
    public static Biquad HighPass(int sampleRate, double frequency, double q = 0.707)
    {
        var w = 2 * Math.PI * frequency / sampleRate;
        var alpha = Math.Sin(w) / (2 * q);
        var cos = Math.Cos(w);

        return new Biquad((1 + cos) / 2, -(1 + cos), (1 + cos) / 2, 1 + alpha, -2 * cos, 1 - alpha);
    }

    /// <summary>
    /// The first stage of BS.1770's K-weighting: a shelf that lifts the treble, standing in for
    /// the way a head in a sound field boosts the frequencies a voice lives in. Coefficients are
    /// the ones in the standard, derived at 48 kHz and refitted for other rates.
    /// </summary>
    public static Biquad HighShelf1770(int sampleRate)
    {
        const double gainDb = 3.999843853973347;
        const double f0 = 1681.974450955533;
        const double q = 0.7071752369554196;

        var k = Math.Tan(Math.PI * f0 / sampleRate);
        var vh = Math.Pow(10, gainDb / 20);
        var vb = Math.Pow(vh, 0.4996667741545416);
        var denominator = 1 + k / q + k * k;

        return new Biquad(
            (vh + vb * k / q + k * k) / denominator,
            2 * (k * k - vh) / denominator,
            (vh - vb * k / q + k * k) / denominator,
            1,
            2 * (k * k - 1) / denominator,
            (1 - k / q + k * k) / denominator);
    }

    /// <summary>The second stage of K-weighting: a high pass at about 38 Hz.</summary>
    public static Biquad HighPass1770(int sampleRate)
    {
        const double f0 = 38.13547087602444;
        const double q = 0.5003270373238773;

        var k = Math.Tan(Math.PI * f0 / sampleRate);

        return new Biquad(
            1, -2, 1,
            1,
            2 * (k * k - 1) / (1 + k / q + k * k),
            (1 - k / q + k * k) / (1 + k / q + k * k));
    }
}
