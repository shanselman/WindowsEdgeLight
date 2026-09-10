namespace WindowsEdgeLight;

// TEMPORARY diagnostic instrumentation - remove once the ~3s UI response delay is
// root-caused. Logs to %APPDATA%\WindowsEdgeLight\perf.log so timings can be read
// back without needing a console window or attached debugger.
internal static class PerfLog
{
    private static readonly System.Diagnostics.Stopwatch Clock = System.Diagnostics.Stopwatch.StartNew();

    public static void Log(string message)
    {
        try
        {
            var dir = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "WindowsEdgeLight");
            System.IO.Directory.CreateDirectory(dir);
            var path = System.IO.Path.Combine(dir, "perf.log");
            System.IO.File.AppendAllText(path, $"{DateTime.Now:HH:mm:ss.fff} [{Clock.ElapsedMilliseconds,8}] {message}\n");
        }
        catch
        {
            // Diagnostic only - never let logging failures affect app behavior.
        }
    }
}
