using TechyGeeksHome.Common;

namespace SoundGeek;

/// <summary>
/// Everything the shared About window and update check need to know about this app. One place,
/// so the wording here and the wording on the product page can be kept in step.
/// </summary>
internal static class AppFacts
{
    public static readonly AppInfo Info = new()
    {
        Name = "SoundGeek",
        Tagline = "Cleans up a recording, on your own machine",
        Description =
            "Drop in a recording and SoundGeek writes a cleaned copy beside it: background noise " +
            "gone, mains hum gone, levels evened out. It runs entirely on this machine, so an " +
            "interview or a meeting never leaves the computer it is on. No account, no upload, " +
            "no per-minute limit and no watermark.",
        GitHubOwner = "techygeekshome",
        GitHubRepo = "SoundGeek",
        ProductUrl = "https://techygeekshome.info/soundgeek/",
        IconUri = "avares://SoundGeek/Assets/soundgeek.png",
        LicenceLine = "Free to use, including at work. GPL-3.0. No paid tier, ever.",
        Credits = new[]
        {
            new Credit("sherpa-onnx", "Apache-2.0", "https://github.com/k2-fsa/sherpa-onnx"),
            new Credit("GTCRN speech enhancement", "Apache-2.0", "https://github.com/Xiaobin-Rong/gtcrn"),
            new Credit("ONNX Runtime", "MIT", "https://onnxruntime.ai"),
            new Credit("Avalonia", "MIT", "https://avaloniaui.net"),
            new Credit("ffmpeg", "Used as a separate program, never linked", "https://ffmpeg.org")
        }
    };
}
