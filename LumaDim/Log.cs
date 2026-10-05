namespace LumaDim;

internal static class Log
{
    private static readonly object Gate = new();

    public static void Write(string message)
    {
        lock (Gate)
        {
            try
            {
                File.AppendAllText(
                    Path.Combine(Path.GetTempPath(), "lumadim.log"),
                    $"{DateTime.Now:HH:mm:ss.fff} {message}{Environment.NewLine}");
            }
            catch { }
        }
    }
}
