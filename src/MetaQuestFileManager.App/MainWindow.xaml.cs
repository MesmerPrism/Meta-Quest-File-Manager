using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MetaQuestFileManager.Core;
using Microsoft.Win32;

namespace MetaQuestFileManager.App;

public partial class MainWindow : Window
{
    private readonly AdbClient? _client;
    private readonly OperatorCommandExecutor? _operator;
    private bool _busy;

    public MainWindow()
    {
        InitializeComponent();

        var adbPath = AdbLocator.Find();
        if (adbPath is null)
        {
            AdbPathText.Text = "ADB not found";
            StatusText.Text = "Install Android Platform Tools or configure META_QUEST_FILE_MANAGER_ADB.";
            MainTabs.IsEnabled = false;
            RefreshDevicesButton.IsEnabled = false;
            return;
        }

        _client = new AdbClient(adbPath);
        _operator = new OperatorCommandExecutor(_client);
        AdbPathText.Text = $"ADB: {adbPath}";
    }

    private async void OnLoaded(object sender, RoutedEventArgs eventArgs)
    {
        if (_client is not null)
        {
            await RunBusyAsync(RefreshDevicesAsync, "Looking for authorized headsets…");
        }
    }

    private async void OnRefreshDevices(object sender, RoutedEventArgs eventArgs) =>
        await RunBusyAsync(RefreshDevicesAsync, "Refreshing devices…");

    private void OnDeviceSelectionChanged(object sender, SelectionChangedEventArgs eventArgs)
    {
        RemoteEntriesGrid.ItemsSource = null;
        PackagesList.ItemsSource = null;
        if (DeviceBox.SelectedItem is QuestDevice device)
        {
            StatusText.Text = device.IsReady
                ? $"Selected {device.DisplayName}."
                : $"{device.Serial} is {device.State}; authorize or reconnect it before operations.";
        }
    }

    private async void OnGoToRemotePath(object sender, RoutedEventArgs eventArgs) =>
        await RunBusyAsync(RefreshRemoteDirectoryAsync, "Listing device path…");

    private async void OnGoUp(object sender, RoutedEventArgs eventArgs)
    {
        await RunBusyAsync(
            async () =>
            {
                var current = AndroidInput.RequireRemotePath(RemotePathBox.Text);
                if (current == "/")
                {
                    StatusText.Text = "Already at the device root.";
                    return;
                }

                var lastSlash = current.LastIndexOf('/');
                RemotePathBox.Text = lastSlash <= 0 ? "/" : current[..lastSlash];
                await RefreshRemoteDirectoryAsync();
            },
            "Opening parent folder…");
    }

    private async void OnRemoteEntryDoubleClick(object sender, MouseButtonEventArgs eventArgs)
    {
        if (RemoteEntriesGrid.SelectedItem is not RemoteEntry { IsDirectory: true } entry)
        {
            return;
        }

        RemotePathBox.Text = entry.FullPath;
        await RunBusyAsync(RefreshRemoteDirectoryAsync, $"Opening {entry.Name}…");
    }

    private async void OnPullSelected(object sender, RoutedEventArgs eventArgs)
    {
        if (RemoteEntriesGrid.SelectedItem is not RemoteEntry entry)
        {
            ShowInputMessage("Select a file to pull first.");
            return;
        }

        if (entry.IsDirectory)
        {
            ShowInputMessage("Folder pull is not part of the first slice. Open the folder and select a file.");
            return;
        }

        var dialog = new SaveFileDialog
        {
            FileName = entry.Name,
            Title = "Save file from Quest",
            OverwritePrompt = true
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        await RunBusyAsync(
            async () =>
            {
                var command = OperatorCommands.PullFile(
                    RequireReadyDevice().Serial,
                    entry.FullPath,
                    dialog.FileName);
                SetOperatorCommand(command);
                await RequireOperator().ExecuteAsync(command);
                StatusText.Text = $"Saved {entry.Name} to {dialog.FileName}.";
            },
            $"Pulling {entry.Name}…");
    }

    private async void OnPushFile(object sender, RoutedEventArgs eventArgs)
    {
        if (!TryGetReadyDevice(out var device))
        {
            return;
        }

        var dialog = new OpenFileDialog
        {
            Title = "Choose a file to copy to Quest",
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        string remotePath;
        try
        {
            remotePath = AndroidInput.CombineRemotePath(
                AndroidInput.RequireRemotePath(RemotePathBox.Text),
                Path.GetFileName(dialog.FileName));
        }
        catch (ArgumentException exception)
        {
            ShowInputMessage(exception.Message);
            return;
        }

        var confirmation = MessageBox.Show(
            this,
            $"Copy {Path.GetFileName(dialog.FileName)} to\n{remotePath}\n\non {device.Serial}? " +
            "ADB may replace a file with the same name.",
            "Confirm file push",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Question);
        if (confirmation != MessageBoxResult.OK)
        {
            return;
        }

        var command = OperatorCommands.PushFile(device.Serial, dialog.FileName, remotePath);
        SetOperatorCommand(command);
        await RunBusyAsync(
            async () =>
            {
                await RequireOperator().ExecuteAsync(command);
                await RefreshRemoteDirectoryAsync();
                SetOperatorCommand(command);
                StatusText.Text = $"Copied {Path.GetFileName(dialog.FileName)} to {remotePath}.";
            },
            $"Pushing {Path.GetFileName(dialog.FileName)}…");
    }

    private void OnBrowseInstallApk(object sender, RoutedEventArgs eventArgs)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Choose an APK",
            Filter = "Android packages (*.apk)|*.apk",
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) == true)
        {
            InstallApkPathBox.Text = dialog.FileName;
        }
    }

    private async void OnInstallApk(object sender, RoutedEventArgs eventArgs)
    {
        if (!TryGetReadyDevice(out var device))
        {
            return;
        }

        var apkPath = InstallApkPathBox.Text.Trim();
        if (!File.Exists(apkPath))
        {
            ShowInputMessage("Choose an existing APK file first.");
            return;
        }

        var confirmation = MessageBox.Show(
            this,
            $"Install {Path.GetFileName(apkPath)} on {device.Serial}?",
            "Confirm APK install",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Question);
        if (confirmation != MessageBoxResult.OK)
        {
            return;
        }

        var options = new ApkInstallOptions(
            ReplaceExisting: ReplaceExistingBox.IsChecked == true,
            AllowDowngrade: AllowDowngradeBox.IsChecked == true,
            GrantRuntimePermissions: GrantPermissionsBox.IsChecked == true,
            AllowTestPackages: AllowTestPackageBox.IsChecked == true);

        var command = OperatorCommands.InstallApk(device.Serial, apkPath, options);
        SetOperatorCommand(command);
        await RunBusyAsync(
            async () =>
            {
                await RequireOperator().ExecuteAsync(command);
                StatusText.Text = $"Installed {Path.GetFileName(apkPath)} on {device.Serial}.";
            },
            $"Installing {Path.GetFileName(apkPath)}…");
    }

    private async void OnRefreshPackages(object sender, RoutedEventArgs eventArgs) =>
        await RunBusyAsync(RefreshPackagesAsync, "Loading third-party packages…");

    private async void OnExportPackage(object sender, RoutedEventArgs eventArgs)
    {
        if (PackagesList.SelectedItem is not string packageName)
        {
            ShowInputMessage("Select a package to export first.");
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "Export installed APK",
            Filter = "Android packages (*.apk)|*.apk",
            DefaultExt = ".apk",
            AddExtension = true,
            FileName = packageName + ".apk",
            OverwritePrompt = true
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        var command = OperatorCommands.ExportApk(
            RequireReadyDevice().Serial,
            packageName,
            dialog.FileName,
            overwrite: true);
        SetOperatorCommand(command);
        await RunBusyAsync(
            async () =>
            {
                var execution = await RequireOperator().ExecuteAsync(command);
                var result = execution.ApkExportResult ??
                    throw new InvalidOperationException("APK export returned no result.");
                StatusText.Text = $"Exported {packageName}. SHA-256 {result.Sha256[..12]}…";
            },
            $"Exporting {packageName}…");
    }

    private async Task RefreshDevicesAsync()
    {
        var command = OperatorCommands.DiscoverDevices();
        SetOperatorCommand(command);
        var execution = await RequireOperator().ExecuteAsync(command);
        var devices = execution.Devices ??
            throw new InvalidOperationException("Device discovery returned no device collection.");
        DeviceBox.ItemsSource = devices;
        DeviceBox.SelectedItem = devices.FirstOrDefault(static device => device.IsReady) ?? devices.FirstOrDefault();
        StatusText.Text = devices.Count == 0
            ? "No ADB devices found. Connect a Quest and approve USB debugging in-headset."
            : $"Found {devices.Count} ADB device{(devices.Count == 1 ? string.Empty : "s")}.";
    }

    private async Task RefreshRemoteDirectoryAsync()
    {
        var path = AndroidInput.RequireRemotePath(RemotePathBox.Text);
        RemotePathBox.Text = path;
        var command = OperatorCommands.ListFiles(RequireReadyDevice().Serial, path);
        SetOperatorCommand(command);
        var execution = await RequireOperator().ExecuteAsync(command);
        var entries = execution.RemoteEntries ??
            throw new InvalidOperationException("File listing returned no entry collection.");
        RemoteEntriesGrid.ItemsSource = entries;
        StatusText.Text = $"{entries.Count} entries in {path}.";
    }

    private async Task RefreshPackagesAsync()
    {
        var command = OperatorCommands.ListPackages(RequireReadyDevice().Serial);
        SetOperatorCommand(command);
        var execution = await RequireOperator().ExecuteAsync(command);
        var packages = execution.Packages ??
            throw new InvalidOperationException("Package listing returned no package collection.");
        PackagesList.ItemsSource = packages;
        StatusText.Text = $"Loaded {packages.Count} third-party packages.";
    }

    private QuestDevice RequireReadyDevice()
    {
        if (DeviceBox.SelectedItem is not QuestDevice device)
        {
            throw new InvalidOperationException("Select a headset first.");
        }

        if (!device.IsReady)
        {
            throw new InvalidOperationException(
                $"{device.Serial} is {device.State}. Authorize or reconnect it before continuing.");
        }

        return device;
    }

    private bool TryGetReadyDevice(out QuestDevice device)
    {
        try
        {
            device = RequireReadyDevice();
            return true;
        }
        catch (InvalidOperationException exception)
        {
            device = null!;
            ShowInputMessage(exception.Message);
            return false;
        }
    }

    private AdbClient RequireClient() =>
        _client ?? throw new InvalidOperationException(
            "ADB was not found. Install Android Platform Tools or configure META_QUEST_FILE_MANAGER_ADB.");

    private OperatorCommandExecutor RequireOperator() =>
        _operator ?? throw new InvalidOperationException(
            "ADB was not found. Install Android Platform Tools or configure META_QUEST_FILE_MANAGER_ADB.");

    private void SetOperatorCommand(OperatorCommand command)
    {
        OperatorCommandTextBox.Text = command.ToPowerShellCommand(
            ".\\meta-quest-file-manager.exe",
            RequireClient().AdbPath);
        CopyOperatorCommandButton.IsEnabled = true;
    }

    private void OnCopyOperatorCommand(object sender, RoutedEventArgs eventArgs)
    {
        if (string.IsNullOrWhiteSpace(OperatorCommandTextBox.Text))
        {
            return;
        }

        Clipboard.SetText(OperatorCommandTextBox.Text);
        StatusText.Text = "Copied the exact equivalent CLI command.";
    }

    private async Task RunBusyAsync(Func<Task> action, string progressMessage)
    {
        if (_busy)
        {
            return;
        }

        _busy = true;
        StatusText.Text = progressMessage;
        MainTabs.IsEnabled = false;
        RefreshDevicesButton.IsEnabled = false;
        Mouse.OverrideCursor = Cursors.Wait;

        try
        {
            await action();
        }
        catch (Exception exception)
        {
            StatusText.Text = exception.Message;
            MessageBox.Show(
                this,
                exception.Message,
                "Operation failed",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        finally
        {
            Mouse.OverrideCursor = null;
            MainTabs.IsEnabled = _client is not null;
            RefreshDevicesButton.IsEnabled = _client is not null;
            _busy = false;
        }
    }

    private void ShowInputMessage(string message) =>
        MessageBox.Show(this, message, "Meta Quest File Manager", MessageBoxButton.OK, MessageBoxImage.Information);
}
