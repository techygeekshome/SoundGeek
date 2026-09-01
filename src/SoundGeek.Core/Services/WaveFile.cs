using SoundGeek.Core.Models;

namespace SoundGeek.Core.Services;

/// <summary>
/// Reads and writes WAV files.
///
/// Hand written rather than taken from a library because the requirement is small and exact:
/// read 8, 16, 24 and 32 bit PCM and 32 bit float at any rate and channel count, and write back
/// 16 bit PCM or 24 bit PCM. A library that does everything would be a dependency carried for
/// two format cases.
///
/// Anything that is not one of those goes through ffmpeg, the same way it does in TranscribeGeek.
/// </summary>
public static class WaveFile
{
    private const int WaveFormatPcm = 1;
    private const int WaveFormatFloat = 3;
    private const int WaveFormatExtensible = 0xFFFE;

    public static bool CanRead(string path)
    {
        try
        {
            Read(path, headerOnly: true);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public static AudioBuffer Read(string path) => Read(path, headerOnly: false)!;

    private static AudioBuffer? Read(string path, bool headerOnly)
    {
        using var fs = File.OpenRead(path);
        using var br = new BinaryReader(fs);

        if (new string(br.ReadChars(4)) != "RIFF") throw new AudioFormatException("That is not a WAV file.");
        br.ReadInt32();
        if (new string(br.ReadChars(4)) != "WAVE") throw new AudioFormatException("That is not a WAV file.");

        int format = 0, channels = 0, rate = 0, bits = 0;
        var haveFormat = false;

        while (fs.Position + 8 <= fs.Length)
        {
            var id = new string(br.ReadChars(4));
            var size = br.ReadInt32();
            if (size < 0) throw new AudioFormatException("That WAV file's header is damaged.");

            if (id == "fmt ")
            {
                var start = fs.Position;
                format = br.ReadInt16();
                channels = br.ReadInt16();
                rate = br.ReadInt32();
                br.ReadInt32();                       // byte rate
                br.ReadInt16();                       // block align
                bits = br.ReadInt16();

                // WAVE_FORMAT_EXTENSIBLE hides the real format in a sub-chunk at the end.
                if (format == WaveFormatExtensible && size >= 40)
                {
                    fs.Seek(start + 24, SeekOrigin.Begin);
                    format = br.ReadInt16();
                }

                haveFormat = true;
                fs.Seek(start + size + (size & 1), SeekOrigin.Begin);
                continue;
            }

            if (id == "data")
            {
                if (!haveFormat) throw new AudioFormatException("That WAV file has no format header.");
                if (channels is < 1 or > 8) throw new AudioFormatException($"{channels} channels is more than SoundGeek handles.");

                if (headerOnly) return null;

                var bytesPerSample = bits / 8;
                var frame = bytesPerSample * channels;
                if (frame == 0) throw new AudioFormatException("That WAV file's header is damaged.");

                var frames = (int)Math.Min(size, fs.Length - fs.Position) / frame;
                var chans = new float[channels][];
                for (var c = 0; c < channels; c++) chans[c] = new float[frames];

                var raw = br.ReadBytes(frames * frame);

                for (var f = 0; f < frames; f++)
                for (var c = 0; c < channels; c++)
                    chans[c][f] = Sample(raw, (f * channels + c) * bytesPerSample, bits, format);

                return new AudioBuffer { Channels = chans, SampleRate = rate };
            }

            fs.Seek(size + (size & 1), SeekOrigin.Current);
        }

        throw new AudioFormatException("That WAV file has no audio in it.");
    }

    private static float Sample(byte[] b, int at, int bits, int format) => bits switch
    {
        8 => (b[at] - 128) / 128f,                                                  // 8 bit WAV is unsigned
        16 => BitConverter.ToInt16(b, at) / 32768f,
        24 => ((b[at] | (b[at + 1] << 8) | ((sbyte)b[at + 2] << 16)) / 8388608f),
        32 when format == WaveFormatFloat => BitConverter.ToSingle(b, at),
        32 => BitConverter.ToInt32(b, at) / 2147483648f,
        _ => throw new AudioFormatException($"{bits} bit audio is not something SoundGeek reads.")
    };

    /// <summary>
    /// Writes 16 or 24 bit PCM. Samples outside the range are clipped rather than wrapped, since
    /// a wrapped sample is a loud click and a clipped one is at worst a moment of distortion.
    /// </summary>
    public static void Write(string path, AudioBuffer audio, int bitsPerSample = 24)
    {
        if (bitsPerSample is not (16 or 24))
            throw new ArgumentOutOfRangeException(nameof(bitsPerSample), bitsPerSample, "16 or 24 only.");

        var channels = audio.ChannelCount;
        var frames = audio.Length;
        var bytes = bitsPerSample / 8;
        var dataSize = frames * channels * bytes;

        using var fs = File.Create(path);
        using var bw = new BinaryWriter(fs);

        bw.Write("RIFF"u8.ToArray());
        bw.Write(36 + dataSize);
        bw.Write("WAVE"u8.ToArray());

        bw.Write("fmt "u8.ToArray());
        bw.Write(16);
        bw.Write((short)WaveFormatPcm);
        bw.Write((short)channels);
        bw.Write(audio.SampleRate);
        bw.Write(audio.SampleRate * channels * bytes);
        bw.Write((short)(channels * bytes));
        bw.Write((short)bitsPerSample);

        bw.Write("data"u8.ToArray());
        bw.Write(dataSize);

        var buffer = new byte[dataSize];
        var at = 0;

        for (var f = 0; f < frames; f++)
        for (var c = 0; c < channels; c++)
        {
            var v = Math.Clamp(audio.Channels[c][f], -1f, 1f);

            if (bitsPerSample == 16)
            {
                var s = (short)Math.Clamp(Math.Round(v * 32767.0), short.MinValue, short.MaxValue);
                buffer[at++] = (byte)(s & 0xFF);
                buffer[at++] = (byte)((s >> 8) & 0xFF);
            }
            else
            {
                var s = (int)Math.Clamp(Math.Round(v * 8388607.0), -8388608, 8388607);
                buffer[at++] = (byte)(s & 0xFF);
                buffer[at++] = (byte)((s >> 8) & 0xFF);
                buffer[at++] = (byte)((s >> 16) & 0xFF);
            }
        }

        bw.Write(buffer);
    }
}

public sealed class AudioFormatException : Exception
{
    public AudioFormatException(string message) : base(message) { }
}
