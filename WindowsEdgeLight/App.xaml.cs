using System.Configuration;
using System.Data;
using System.IO;
using System.Windows;
using Updatum;
using MessageBox = System.Windows.MessageBox;

namespace WindowsEdgeLight;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : System.Windows.Application
{
    private static System.Threading.Mutex? _mutex;
    
    public static UserSettings CurrentSettings { get; set; } = UserSettings.Default;
    
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
        
        // Load user settings
        CurrentSettings = SettingsManager.LoadSettings();
        
        base.OnStartup(e);

        // Check for updates asynchronously
        _ = CheckForUpdatesAsync();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // Save settings synchronously on exit to ensure it completes
        var settings = new UserSettings
        {
            IsLightOn = CurrentSettings.IsLightOn,
            Brightness = CurrentSettings.Brightness,
            ColorTemperature = CurrentSettings.ColorTemperature,
            CurrentMonitorIndex = CurrentSettings.CurrentMonitorIndex,
            ShowOnAllMonitors = CurrentSettings.ShowOnAllMonitors,
            IsControlWindowVisible = CurrentSettings.IsControlWindowVisible
        };
        
        // Synchronous save on exit
        try
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WindowsEdgeLight");
            Directory.CreateDirectory(dir);
            var json = System.Text.Json.JsonSerializer.Serialize(settings, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(Path.Combine(dir, "settings.json"), json);
        }
        catch { }
        
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
}

