namespace SoundGeek;

/// <summary>
/// A last-resort log for things that would otherwise vanish. It is not telemetry: it never
/// leaves the machine, it is written only when something has gone wrong, and it lives next to
/// the model rather than anywhere hidden.
/// </summary>
internal static class Log
{
    private static readonly string Path = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TechyGeeksHome", "SoundGeek", "error.log");

    public static void Write(string message)
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
            File.AppendAllText(Path, $"{DateTime.Now:u}  {message}{Environment.NewLine}");
        }
        catch (Exception)
        {
            // If we cannot even write the log, there is nowhere left to complain to.
        }
    }
}
