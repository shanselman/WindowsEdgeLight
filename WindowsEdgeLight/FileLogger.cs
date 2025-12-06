using System.Diagnostics;
using System.IO;

namespace WindowsEdgeLight;

/// <summary>
/// Simple file-based logger that captures Debug.WriteLine output.
/// Log file is written to %LOCALAPPDATA%\WindowsEdgeLight\debug.log
/// </summary>
public class FileLogger : TraceListener
{
    private static FileLogger? _instance;
    private readonly StreamWriter _writer;
    private readonly object _lock = new();
    
    public static string LogFilePath { get; private set; } = string.Empty;

    private FileLogger(string logPath)
    {
        LogFilePath = logPath;
        
        // Ensure directory exists
        var dir = Path.GetDirectoryName(logPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }
        
        // Overwrite log each run for simplicity
        _writer = new StreamWriter(logPath, append: false) { AutoFlush = true };
        _writer.WriteLine($"=== WindowsEdgeLight Debug Log ===");
        _writer.WriteLine($"Started: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        _writer.WriteLine(new string('=', 40));
        _writer.WriteLine();
    }

    /// <summary>
    /// Initialize file logging. Call once at app startup.
    /// </summary>
    public static void Initialize()
    {
        if (_instance != null) return;
        
        var logPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WindowsEdgeLight",
            "debug.log");
        
        _instance = new FileLogger(logPath);
        Trace.Listeners.Add(_instance);
        
        Debug.WriteLine($"FileLogger initialized. Log file: {logPath}");
    }

    /// <summary>
    /// Flush and close the log file.
    /// </summary>
    public static void Shutdown()
    {
        if (_instance == null) return;
        
        Debug.WriteLine($"FileLogger shutting down at {DateTime.Now:HH:mm:ss}");
        Trace.Listeners.Remove(_instance);
        _instance._writer.Flush();
        _instance._writer.Close();
        _instance._writer.Dispose();
        _instance = null;
    }

    public override void Write(string? message)
    {
        if (message == null) return;
        lock (_lock)
        {
            _writer.Write(message);
        }
    }

    public override void WriteLine(string? message)
    {
        if (message == null) return;
        lock (_lock)
        {
            _writer.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] {message}");
        }
    }
}
