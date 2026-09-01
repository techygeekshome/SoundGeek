using System.Security.Cryptography;

namespace SoundGeek.Core.Services;

/// <summary>
/// The one model SoundGeek uses, and where it lives.
///
/// Half a megabyte, which is small enough that bundling it would be defensible. It is downloaded
/// anyway, for two reasons: the gentle and levels modes work without it, so somebody who only
/// wants those never has to have it; and every app in this range downloads its models the same
/// way, on request, checked against a hash, so there is one story rather than an exception.
/// </summary>
public static class ModelCatalog
{
    public static string Directory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TechyGeeksHome", "SoundGeek", "models");

    public const string FileName = "gtcrn-speech-denoiser.onnx";
    public const long Bytes = 535_638;
    public const string Sha256Hex = "e77603ac0c23dac3227dd2d7135b3a585cbee2679048aecfa886657d3ae1b534";
    public const string Origin = "GTCRN, run through sherpa-onnx, Apache-2.0";

    private const string Url =
        "https://github.com/k2-fsa/sherpa-onnx/releases/download/speech-enhancement-models/gtcrn_simple.onnx";

    public static string ModelPath { get; } = System.IO.Path.Combine(Directory, FileName);

    public static bool IsDownloaded => File.Exists(ModelPath) && new FileInfo(ModelPath).Length == Bytes;

    /// <summary>
    /// Fetches the model and keeps it only if it is byte for byte what it should be. A file that
    /// does not match is deleted rather than used, so SoundGeek runs the exact model it was
    /// tested with or it runs none at all.
    /// </summary>
    public static async Task DownloadAsync(IProgress<double>? progress = null, CancellationToken ct = default)
    {
        System.IO.Directory.CreateDirectory(Directory);

        var part = ModelPath + ".part";
        if (File.Exists(part)) File.Delete(part);

        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("SoundGeek");

        using (var response = await http.GetAsync(Url, HttpCompletionOption.ResponseHeadersRead, ct)
                   .ConfigureAwait(false))
        {
            response.EnsureSuccessStatusCode();

            await using var source = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            await using var dest = File.Create(part);

            var buffer = new byte[1 << 16];
            long got = 0;
            int read;
            while ((read = await source.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
            {
                await dest.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                got += read;
                progress?.Report(Math.Min(1.0, got / (double)Bytes));
            }
        }

        var actual = new FileInfo(part).Length;
        if (actual != Bytes)
        {
            MediaDecoder.TryDelete(part);
            throw new InvalidDataException(
                $"The model came down as {actual:N0} bytes instead of {Bytes:N0}. " +
                "It has been deleted rather than used. Try again.");
        }

        if (!Sha256(part).Equals(Sha256Hex, StringComparison.OrdinalIgnoreCase))
        {
            MediaDecoder.TryDelete(part);
            throw new InvalidDataException(
                "The model did not match the checksum recorded in SoundGeek. It has been deleted rather than used.");
        }

        if (File.Exists(ModelPath)) File.Delete(ModelPath);
        File.Move(part, ModelPath);
        progress?.Report(1.0);
    }

    public static string Sha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    public static void Delete()
    {
        if (File.Exists(ModelPath)) File.Delete(ModelPath);
    }
}
