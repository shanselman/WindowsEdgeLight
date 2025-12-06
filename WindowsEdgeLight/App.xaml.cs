using System.Configuration;
using System.Data;
using System.Windows;
using Updatum;
using WindowsEdgeLight.AI;
using MessageBox = System.Windows.MessageBox;

namespace WindowsEdgeLight;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : System.Windows.Application
{
    private static System.Threading.Mutex? _mutex;
    
    internal static readonly UpdatumManager AppUpdater = new("shanselman", "WindowsEdgeLight")
    {
        // Default pattern (win-x64) will match our ZIP assets
        // ZIP files are portable apps with exe and README for proper update handling
        FetchOnlyLatestRelease = true, // Saves GitHub API rate limits
        // Specify the executable name for single-file app (without .exe extension)
        InstallUpdateSingleFileExecutableName = "WindowsEdgeLight",
    };

    protected override async void OnStartup(StartupEventArgs e)
    {
        // Check Windows version first - we need Windows 10 2004 (build 19041) or later
        if (!IsWindowsVersionSupported())
        {
            MessageBox.Show(
                "Windows Edge Light requires Windows 10 version 2004 (May 2020 Update) or later.\n\n" +
                "Your current Windows version is not supported.\n\n" +
                "Please update Windows or use an older version of this application.",
                "Windows Version Not Supported",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown();
            return;
        }
        
        // Initialize file logging first so we capture everything
        FileLogger.Initialize();
        
        // Add global exception handler to see crashes
        AppDomain.CurrentDomain.UnhandledException += (s, args) =>
        {
            var ex = args.ExceptionObject as Exception;
            System.Diagnostics.Debug.WriteLine($"UNHANDLED EXCEPTION: {ex?.Message}\n{ex?.StackTrace}");
        };
        
        DispatcherUnhandledException += (s, args) =>
        {
            System.Diagnostics.Debug.WriteLine($"DISPATCHER EXCEPTION: {args.Exception.Message}\n{args.Exception.StackTrace}");
            args.Handled = true;
        };
        
        // Check for hardware test mode
        if (e.Args.Length > 0 && e.Args[0] == "--test-hardware")
        {
            TestHardwareCapabilities();
            Shutdown();
            return;
        }

        // Check for single instance using a named mutex
        const string mutexName = "WindowsEdgeLight_SingleInstance_Mutex";
        bool createdNew;
        
        _mutex = new System.Threading.Mutex(true, mutexName, out createdNew);
        
        if (!createdNew)
        {
            // Another instance is already running
            MessageBox.Show("Windows Edge Light is already running.\n\nCheck your system tray for the application icon.",
                "Already Running", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }
        
        base.OnStartup(e);

        // Check for updates asynchronously
        _ = CheckForUpdatesAsync();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        FileLogger.Shutdown();
        _mutex?.ReleaseMutex();
        _mutex?.Dispose();
        base.OnExit(e);
    }

    private async Task CheckForUpdatesAsync()
    {
        try
        {
            // Wait a bit before checking to let the main window load
            await Task.Delay(2000);

            var updateFound = await AppUpdater.CheckForUpdatesAsync();

            if (!updateFound) return;

            // Show update dialog on UI thread
            await Dispatcher.InvokeAsync(async () =>
            {
                var release = AppUpdater.LatestRelease!;
                var changelog = AppUpdater.GetChangelog(true) ?? "No release notes available.";

                var dialog = new UpdateDialog(release.TagName, changelog);
                var result = dialog.ShowDialog();

                if (dialog.Result == UpdateDialog.UpdateDialogResult.Download)
                {
                    await DownloadAndInstallUpdateAsync();
                }
            });
        }
        catch (Exception ex)
        {
            // Silently fail - don't interrupt the user experience
            System.Diagnostics.Debug.WriteLine($"Update check failed: {ex.Message}");
        }
    }

    private async Task DownloadAndInstallUpdateAsync()
    {
        DownloadProgressDialog? progressDialog = null;
        try
        {
            progressDialog = new DownloadProgressDialog(AppUpdater);
            progressDialog.Show();

            var downloadedAsset = await AppUpdater.DownloadUpdateAsync();

            // Close progress dialog before showing message boxes
            if (progressDialog != null)
            {
                progressDialog.Close();
                progressDialog = null;
            }

            if (downloadedAsset == null)
            {
                MessageBox.Show("Failed to download the update. Please try again later.",
                    "Download Failed", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // Verify the file still exists
            if (!System.IO.File.Exists(downloadedAsset.FilePath))
            {
                MessageBox.Show($"Update file was deleted or is inaccessible:\n{downloadedAsset.FilePath}\n\nThis may be caused by antivirus software.",
                    "Update File Missing", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // Ask for confirmation before installing
            var confirmResult = MessageBox.Show(
                "The update has been downloaded. The application will now close and install the update.\n\nDo you want to continue?",
                "Install Update",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (confirmResult == MessageBoxResult.Yes)
            {
                await AppUpdater.InstallUpdateAsync(downloadedAsset);
                // If installation succeeds, the app will be terminated
            }
        }
        catch (UnauthorizedAccessException ex)
        {
            MessageBox.Show($"Access denied when accessing update file. Please check:\n\n1. Antivirus may be blocking the update\n2. Windows SmartScreen may need approval\n3. Temp folder permissions\n\nError: {ex.Message}",
                "Access Denied", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to download or install update: {ex.Message}\n\nTry running as administrator or check antivirus settings.",
                "Update Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            if (progressDialog != null)
            {
                progressDialog.Close();
            }
        }
    }

    private void TestHardwareCapabilities()
    {
        // Test using native Windows FaceTracker API
        string faceTrackerStatus;
        try
        {
            faceTrackerStatus = global::Windows.Media.FaceAnalysis.FaceTracker.IsSupported 
                ? "✅ Supported" 
                : "❌ Not supported on this device";
        }
        catch (Exception ex)
        {
            faceTrackerStatus = $"❌ Error: {ex.Message}";
        }
        
        var message = $"""
            === Windows AI Capability Detection ===
            
            Windows FaceTracker API: {faceTrackerStatus}
            
            Note: This app uses Windows built-in face detection.
            No additional models or hardware required.
            Camera sharing is supported (works alongside Teams/Zoom).
            """;
        
        // Also write to console for command-line visibility
        Console.WriteLine(message);
        
        MessageBox.Show(message, "Hardware Detection Test", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    /// <summary>
    /// Checks if the current Windows version meets minimum requirements.
    /// Requires Windows 10 version 2004 (build 19041) or later for WinRT APIs.
    /// </summary>
    private static bool IsWindowsVersionSupported()
    {
        // Windows 10 2004 is build 10.0.19041
        // OperatingSystem.IsWindowsVersionAtLeast is the modern way to check
        return OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041);
    }
}

