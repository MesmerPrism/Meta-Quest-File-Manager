using System.IO;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using MetaQuestFileManager.Core;
using Microsoft.Win32;

namespace MetaQuestFileManager.App;

public partial class MainWindow : Window
{
    private readonly AdbClient? _client;
    private readonly OperatorCommandExecutor? _operator;
    private readonly ObservableCollection<WifiInstallTargetChoice> _wifiInstallTargets = [];
    private IProgress<OperatorProgress>? _activeProgress;
    private int _progressGeneration;
    private bool _busy;

    public MainWindow()
    {
        InitializeComponent();
        WifiInstallTargetsList.ItemsSource = _wifiInstallTargets;

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
            await RunBusyAsync(() => RefreshDevicesAsync(), "Looking for authorized headsets…");
        }
    }

    private async void OnRefreshDevices(object sender, RoutedEventArgs eventArgs) =>
        await RunBusyAsync(() => RefreshDevicesAsync(), "Refreshing devices…");

    private void OnDeviceSelectionChanged(object sender, SelectionChangedEventArgs eventArgs)
    {
        RemoteEntriesGrid.ItemsSource = null;
        PackagesList.ItemsSource = null;
        if (DeviceBox.SelectedItem is QuestDevice device)
        {
            if (AndroidInput.TryParseWifiEndpoint(device.Serial, out var host, out var port))
            {
                WifiHostBox.Text = host;
                WifiConnectPortBox.Text = port.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }

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
                await ExecuteOperatorAsync(command);
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
        await RunBusyAsync(
            async () =>
            {
                await ExecuteOperatorAsync(command);
                await RefreshRemoteDirectoryAsync();
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

        var command = OperatorCommands.InstallApk(device.Serial, apkPath, ReadInstallOptions());
        await RunBusyAsync(
            async () =>
            {
                await ExecuteOperatorAsync(command);
                StatusText.Text = $"Installed {Path.GetFileName(apkPath)} on {device.Serial}.";
            },
            $"Installing {Path.GetFileName(apkPath)}…");
    }

    private async void OnInstallApkMany(object sender, RoutedEventArgs eventArgs)
    {
        var apkPath = InstallApkPathBox.Text.Trim();
        if (!File.Exists(apkPath))
        {
            ShowInputMessage("Choose an existing APK file first.");
            return;
        }

        OperatorCommand command;
        try
        {
            command = OperatorCommands.InstallApkMany(
                GetSelectedWifiSerials(),
                apkPath,
                ReadInstallOptions(),
                ReadParallelism());
        }
        catch (Exception exception) when (exception is ArgumentException or IOException)
        {
            ShowInputMessage(exception.Message);
            return;
        }

        var targets = command.Serials ??
            throw new InvalidOperationException("The parallel install command contains no targets.");
        if (!ConfirmParallelInstall(Path.GetFileName(apkPath), targets))
        {
            return;
        }

        await RunBusyAsync(
            async () =>
            {
                var execution = await ExecuteOperatorAsync(command);
                ShowParallelInstallResult(
                    execution.ParallelApkInstallResult ??
                    throw new InvalidOperationException("Parallel APK installation returned no result."));
            },
            $"Installing {Path.GetFileName(apkPath)} on {targets.Count} headsets…");
    }

    private void OnBrowseInstallApkBundle(object sender, RoutedEventArgs eventArgs)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Choose a folder containing one APK package set",
            Multiselect = false
        };
        if (dialog.ShowDialog(this) == true)
        {
            InstallApkBundlePathBox.Text = dialog.FolderName;
        }
    }

    private async void OnInstallApkBundle(object sender, RoutedEventArgs eventArgs)
    {
        if (!TryGetReadyDevice(out var device))
        {
            return;
        }

        OperatorCommand command;
        try
        {
            command = OperatorCommands.InstallApkBundle(
                device.Serial,
                InstallApkBundlePathBox.Text.Trim(),
                ReadInstallOptions());
        }
        catch (Exception exception) when (
            exception is ArgumentException or IOException)
        {
            ShowInputMessage(exception.Message);
            return;
        }

        var bundle = command.ApkBundle ??
            throw new InvalidOperationException("The APK bundle command contains no APK set.");
        var confirmation = MessageBox.Show(
            this,
            $"Install all {bundle.ApkPaths.Count} APK parts from\n{bundle.FolderPath}\n\n" +
            $"as one package set on {device.Serial}?",
            "Confirm APK bundle install",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Question);
        if (confirmation != MessageBoxResult.OK)
        {
            return;
        }

        await RunBusyAsync(
            async () =>
            {
                var execution = await ExecuteOperatorAsync(command);
                var result = execution.ApkBundleInstallResult ??
                    throw new InvalidOperationException("APK bundle installation returned no result.");
                StatusText.Text =
                    $"Installed {result.ApkPaths.Count} APK parts as one package set on {device.Serial}.";
            },
            $"Installing {bundle.ApkPaths.Count} APK parts…");
    }

    private async void OnInstallApkBundleMany(object sender, RoutedEventArgs eventArgs)
    {
        OperatorCommand command;
        try
        {
            command = OperatorCommands.InstallApkBundleMany(
                GetSelectedWifiSerials(),
                InstallApkBundlePathBox.Text.Trim(),
                ReadInstallOptions(),
                ReadParallelism());
        }
        catch (Exception exception) when (exception is ArgumentException or IOException)
        {
            ShowInputMessage(exception.Message);
            return;
        }

        var bundle = command.ApkBundle ??
            throw new InvalidOperationException("The parallel bundle command contains no APK set.");
        var targets = command.Serials ??
            throw new InvalidOperationException("The parallel bundle command contains no targets.");
        if (!ConfirmParallelInstall($"{bundle.ApkPaths.Count}-part APK bundle", targets))
        {
            return;
        }

        await RunBusyAsync(
            async () =>
            {
                var execution = await ExecuteOperatorAsync(command);
                ShowParallelInstallResult(
                    execution.ParallelApkInstallResult ??
                    throw new InvalidOperationException("Parallel APK bundle installation returned no result."));
            },
            $"Installing {bundle.ApkPaths.Count} APK parts on {targets.Count} headsets…");
    }

    private async void OnEnableWifiAdb(object sender, RoutedEventArgs eventArgs)
    {
        QuestDevice device;
        int port;
        try
        {
            device = RequireReadyUsbDevice();
            port = ReadPort(WifiEnablePortBox);
        }
        catch (ArgumentException exception)
        {
            ShowInputMessage(exception.Message);
            return;
        }
        catch (InvalidOperationException exception)
        {
            ShowInputMessage(exception.Message);
            return;
        }

        var confirmation = MessageBox.Show(
            this,
            $"Enable Wi-Fi ADB on the selected USB headset using TCP port {port}?\n\n" +
            "The app will read its Wi-Fi address, change its debugging transport, and connect from this PC.",
            "Confirm Wi-Fi ADB change",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Warning);
        if (confirmation != MessageBoxResult.OK)
        {
            return;
        }

        var command = OperatorCommands.EnableWifiAdb(device.Serial, port, operatorConfirmed: true);
        await RunBusyAsync(
            async () =>
            {
                var execution = await ExecuteOperatorAsync(command);
                var result = execution.WifiAdbEnableResult ??
                    throw new InvalidOperationException("Wi-Fi ADB enablement returned no result.");
                WifiHostBox.Text = result.Host;
                WifiConnectPortBox.Text = result.Port.ToString(System.Globalization.CultureInfo.InvariantCulture);
                await RefreshDevicesAsync(result.Endpoint);
                StatusText.Text = $"Connected to {result.Endpoint} over Wi-Fi ADB.";
            },
            "Enabling and connecting Wi-Fi ADB…");
    }

    private async void OnConnectWifiAdb(object sender, RoutedEventArgs eventArgs)
    {
        string host;
        int port;
        try
        {
            host = AndroidInput.RequireWifiHost(WifiHostBox.Text);
            port = ReadPort(WifiConnectPortBox);
        }
        catch (ArgumentException exception)
        {
            ShowInputMessage(exception.Message);
            return;
        }

        var endpoint = AndroidInput.CreateWifiEndpoint(host, port);
        var confirmation = MessageBox.Show(
            this,
            $"Connect this PC to Wi-Fi ADB at {endpoint}?",
            "Confirm Wi-Fi ADB connection",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Question);
        if (confirmation != MessageBoxResult.OK)
        {
            return;
        }

        var command = OperatorCommands.ConnectWifiAdb(host, port, operatorConfirmed: true);
        await RunBusyAsync(
            async () =>
            {
                var execution = await ExecuteOperatorAsync(command);
                var result = execution.WifiAdbConnectionResult ??
                    throw new InvalidOperationException("Wi-Fi ADB connection returned no result.");
                await RefreshDevicesAsync(result.Endpoint);
                StatusText.Text = $"Connected to {result.Endpoint} over Wi-Fi ADB.";
            },
            $"Connecting to {endpoint}…");
    }

    private async void OnDisconnectWifiAdb(object sender, RoutedEventArgs eventArgs)
    {
        if (DeviceBox.SelectedItem is not QuestDevice device ||
            !AndroidInput.TryParseWifiEndpoint(device.Serial, out var host, out var port))
        {
            ShowInputMessage("Select a Wi-Fi headset above before disconnecting it.");
            return;
        }

        var confirmation = MessageBox.Show(
            this,
            $"Disconnect this PC from Wi-Fi ADB at {device.Serial}?",
            "Confirm Wi-Fi ADB disconnection",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Question);
        if (confirmation != MessageBoxResult.OK)
        {
            return;
        }

        var command = OperatorCommands.DisconnectWifiAdb(host, port, operatorConfirmed: true);
        await RunBusyAsync(
            async () =>
            {
                await ExecuteOperatorAsync(command);
                await RefreshDevicesAsync();
                StatusText.Text = $"Disconnected {device.Serial}.";
            },
            $"Disconnecting {device.Serial}…");
    }

    private void OnSelectAllWifiTargets(object sender, RoutedEventArgs eventArgs)
    {
        foreach (var target in _wifiInstallTargets)
        {
            target.IsSelected = true;
        }
    }

    private void OnClearWifiTargets(object sender, RoutedEventArgs eventArgs)
    {
        foreach (var target in _wifiInstallTargets)
        {
            target.IsSelected = false;
        }
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
        await RunBusyAsync(
            async () =>
            {
                var execution = await ExecuteOperatorAsync(command);
                var result = execution.ApkExportResult ??
                    throw new InvalidOperationException("APK export returned no result.");
                StatusText.Text = $"Exported {packageName}. SHA-256 {result.Sha256[..12]}…";
            },
            $"Exporting {packageName}…");
    }

    private async Task RefreshDevicesAsync(string? preferredSerial = null)
    {
        var previousPrimarySerial = preferredSerial ?? (DeviceBox.SelectedItem as QuestDevice)?.Serial;
        var selectedWifiSerials = _wifiInstallTargets
            .Where(static target => target.IsSelected)
            .Select(static target => target.Device.Serial)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var command = OperatorCommands.DiscoverDevices();
        var execution = await ExecuteOperatorAsync(command);
        var devices = execution.Devices ??
            throw new InvalidOperationException("Device discovery returned no device collection.");
        DeviceBox.ItemsSource = devices;
        DeviceBox.SelectedItem = devices.FirstOrDefault(device =>
            string.Equals(device.Serial, previousPrimarySerial, StringComparison.OrdinalIgnoreCase)) ??
            devices.FirstOrDefault(static device => device.IsReady) ??
            devices.FirstOrDefault();

        _wifiInstallTargets.Clear();
        foreach (var device in devices.Where(static candidate => candidate.IsReady && candidate.IsWifiConnection))
        {
            _wifiInstallTargets.Add(new WifiInstallTargetChoice(
                device,
                selectedWifiSerials.Contains(device.Serial)));
        }

        StatusText.Text = devices.Count == 0
            ? "No ADB devices found. Connect a Quest and approve USB debugging in-headset."
            : $"Found {devices.Count} ADB device{(devices.Count == 1 ? string.Empty : "s")}.";
    }

    private async Task RefreshRemoteDirectoryAsync()
    {
        var path = AndroidInput.RequireRemotePath(RemotePathBox.Text);
        RemotePathBox.Text = path;
        var command = OperatorCommands.ListFiles(RequireReadyDevice().Serial, path);
        var execution = await ExecuteOperatorAsync(command);
        var entries = execution.RemoteEntries ??
            throw new InvalidOperationException("File listing returned no entry collection.");
        RemoteEntriesGrid.ItemsSource = entries;
        StatusText.Text = $"{entries.Count} entries in {path}.";
    }

    private async Task RefreshPackagesAsync()
    {
        var command = OperatorCommands.ListPackages(RequireReadyDevice().Serial);
        var execution = await ExecuteOperatorAsync(command);
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

    private QuestDevice RequireReadyUsbDevice()
    {
        var device = RequireReadyDevice();
        if (device.IsWifiConnection)
        {
            throw new InvalidOperationException(
                "Select the directly connected USB headset before enabling Wi-Fi ADB.");
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

    private OperatorCommandExecutor RequireOperator() =>
        _operator ?? throw new InvalidOperationException(
            "ADB was not found. Install Android Platform Tools or configure META_QUEST_FILE_MANAGER_ADB.");

    private Task<OperatorExecutionResult> ExecuteOperatorAsync(OperatorCommand command) =>
        RequireOperator().ExecuteAsync(command, progress: _activeProgress);

    private ApkInstallOptions ReadInstallOptions() =>
        new(
            ReplaceExisting: ReplaceExistingBox.IsChecked == true,
            AllowDowngrade: AllowDowngradeBox.IsChecked == true,
            GrantRuntimePermissions: GrantPermissionsBox.IsChecked == true,
            AllowTestPackages: AllowTestPackageBox.IsChecked == true);

    private IReadOnlyList<string> GetSelectedWifiSerials() =>
        _wifiInstallTargets
            .Where(static target => target.IsSelected)
            .Select(static target => target.Device.Serial)
            .ToArray();

    private int ReadParallelism()
    {
        if (!int.TryParse(ParallelismBox.Text, out var value))
        {
            throw new ArgumentException("Parallel limit must be a whole number between 1 and 16.");
        }

        return AndroidInput.RequireParallelism(value);
    }

    private static int ReadPort(TextBox textBox)
    {
        if (!int.TryParse(textBox.Text, out var value))
        {
            throw new ArgumentException("The Wi-Fi ADB port must be a whole number between 1 and 65535.");
        }

        return AndroidInput.RequireTcpPort(value);
    }

    private bool ConfirmParallelInstall(string artifactLabel, IReadOnlyList<string> targets)
    {
        var targetList = string.Join(Environment.NewLine, targets.Select(static serial => $"• {serial}"));
        return MessageBox.Show(
            this,
            $"Install {artifactLabel} on all {targets.Count} checked Wi-Fi headsets in parallel?\n\n" +
            targetList,
            "Confirm parallel APK install",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Question) == MessageBoxResult.OK;
    }

    private void ShowParallelInstallResult(ParallelApkInstallResult result)
    {
        StatusText.Text =
            $"Installed successfully on {result.SucceededCount} of {result.Targets.Count} Wi-Fi headsets.";
        var details = string.Join(
            Environment.NewLine,
            result.Targets.Select(target =>
                $"{(target.Succeeded ? "✓" : "✗")} {target.Serial}: {target.Summary}"));
        MessageBox.Show(
            this,
            $"{StatusText.Text}\n\n{details}",
            result.Succeeded ? "Parallel install complete" : "Parallel install completed with failures",
            MessageBoxButton.OK,
            result.Succeeded ? MessageBoxImage.Information : MessageBoxImage.Warning);
    }

    private async Task RunBusyAsync(Func<Task> action, string progressMessage)
    {
        if (_busy)
        {
            return;
        }

        _busy = true;
        var progressGeneration = ++_progressGeneration;
        _activeProgress = new Progress<OperatorProgress>(progress =>
        {
            if (_busy && progressGeneration == _progressGeneration)
            {
                UpdateOperationProgress(progress);
            }
        });
        StatusText.Text = progressMessage;
        OperationProgressBar.Value = 0;
        OperationProgressBar.IsIndeterminate = true;
        OperationProgressBar.ToolTip = progressMessage;
        AutomationProperties.SetName(OperationProgressBar, progressMessage);
        OperationProgressBar.Visibility = Visibility.Visible;
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
            _activeProgress = null;
            _progressGeneration++;
            OperationProgressBar.IsIndeterminate = false;
            OperationProgressBar.Value = 0;
            OperationProgressBar.ToolTip = null;
            OperationProgressBar.Visibility = Visibility.Collapsed;
            Mouse.OverrideCursor = null;
            MainTabs.IsEnabled = _client is not null;
            RefreshDevicesButton.IsEnabled = _client is not null;
            _busy = false;
        }
    }

    private void UpdateOperationProgress(OperatorProgress progress)
    {
        StatusText.Text = progress.Message;
        AutomationProperties.SetName(OperationProgressBar, progress.Message);
        if (progress.IsIndeterminate)
        {
            OperationProgressBar.IsIndeterminate = true;
            OperationProgressBar.Value = 0;
            OperationProgressBar.ToolTip = progress.Message;
            return;
        }

        OperationProgressBar.IsIndeterminate = false;
        OperationProgressBar.Value = progress.Percentage;
        OperationProgressBar.ToolTip =
            $"{progress.Message} ({progress.CompletedUnits} of {progress.TotalUnits})";
    }

    private void ShowInputMessage(string message) =>
        MessageBox.Show(this, message, "Meta Quest File Manager", MessageBoxButton.OK, MessageBoxImage.Information);

    private sealed class WifiInstallTargetChoice : INotifyPropertyChanged
    {
        private bool _isSelected;

        public WifiInstallTargetChoice(QuestDevice device, bool isSelected)
        {
            Device = device;
            _isSelected = isSelected;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public QuestDevice Device { get; }

        public string DisplayName => Device.DisplayName;

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected == value)
                {
                    return;
                }

                _isSelected = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
            }
        }
    }
}
