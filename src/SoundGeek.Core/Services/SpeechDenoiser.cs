using SherpaOnnx;
using SoundGeek.Core.Models;

namespace SoundGeek.Core.Services;

/// <summary>
/// The neural noise remover. Far stronger than filtering, and it comes with a real cost that is
/// stated on screen rather than buried: it is a speech model, so it works at 16 kHz in mono and
/// that is what comes back out.
///
/// For an interview, a meeting, a lecture or a voice note that is exactly right, and it is also
/// exactly what TranscribeGeek wants next. For music, or for anything where the recording itself
/// is the product, it is the wrong tool and the app says so.
/// </summary>
public sealed class SpeechDenoiser : IDisposable
{
    private OfflineSpeechDenoiser? _denoiser;

    private OfflineSpeechDenoiser Get()
    {
        if (_denoiser is not null) return _denoiser;

        if (!ModelCatalog.IsDownloaded)
            throw new FileNotFoundException(
                "The speech model has not been downloaded yet. Open Model and download it first.",
                ModelCatalog.ModelPath);

        var config = new OfflineSpeechDenoiserConfig();
        config.Model.Gtcrn.Model = ModelCatalog.ModelPath;
        config.Model.NumThreads = Math.Clamp(Environment.ProcessorCount / 2, 1, 4);
        config.Model.Provider = "cpu";

        _denoiser = new OfflineSpeechDenoiser(config);
        return _denoiser;
    }

    /// <summary>
    /// Returns 16 kHz mono audio, whatever went in. The model resamples the input itself, so
    /// there is nothing to do here beyond flattening a stereo recording to one channel first.
    /// </summary>
    public async Task<AudioBuffer> RunAsync(AudioBuffer audio, CancellationToken ct = default)
    {
        var denoiser = Get();
        var mono = audio.ToMono();

        // One long call into native code that cannot be interrupted part way, so it goes on a
        // background thread and cancellation is honoured either side of it rather than being
        // claimed and then not delivered.
        ct.ThrowIfCancellationRequested();

        float[] cleaned = null!;
        var rate = 0;

        await Task.Run(() =>
        {
            var result = denoiser.Run(mono, audio.SampleRate);
            try
            {
                cleaned = result.Samples;
                rate = result.SampleRate;
            }
            finally
            {
                // DenoisedAudio owns native memory and has a Dispose, but does not implement
                // IDisposable, so it cannot go in a using and has to be released by hand.
                result.Dispose();
            }
        }, ct).ConfigureAwait(false);

        return AudioBuffer.Mono(cleaned, rate);
    }

    public void Dispose()
    {
        _denoiser?.Dispose();
        _denoiser = null;
    }
}
