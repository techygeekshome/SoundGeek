namespace SoundGeek.Core.Services;

/// <summary>
/// Window functions. Chopping audio into blocks and transforming each one produces clicks at the
/// joins unless every block is faded in and out first, which is all a window is.
/// </summary>
public static class Windows
{
    public static double[] Hann(int size)
    {
        var w = new double[size];
        for (var i = 0; i < size; i++)
            w[i] = 0.5 * (1 - Math.Cos(2 * Math.PI * i / size));
        return w;
    }

    /// <summary>
    /// The square root of a Hann window. Applied once going in and once coming out, the two
    /// square roots multiply back to a full Hann, which sums to a flat one at 75 percent overlap.
    /// That is what makes the reconstruction seamless.
    /// </summary>
    public static double[] SqrtHann(int size)
    {
        var w = Hann(size);
        for (var i = 0; i < size; i++) w[i] = Math.Sqrt(w[i]);
        return w;
    }
}
