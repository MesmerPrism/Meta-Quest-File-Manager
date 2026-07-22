using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MetaQuestFileManager.App;
using MetaQuestFileManager.Core;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        const int PreviewWidth = 1180;
        const int PreviewHeight = 820;

        var outputDirectory = args.Length > 0
            ? Path.GetFullPath(args[0])
            : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "onboarding-previews"));
        Directory.CreateDirectory(outputDirectory);

        var application = new MetaQuestFileManager.App.App();
        application.InitializeComponent();
        application.ShutdownMode = ShutdownMode.OnExplicitShutdown;

        RenderFilesPreview(Path.Combine(outputDirectory, "file-manager-files.png"));
        RenderKioskPreview(Path.Combine(outputDirectory, "file-manager-kiosk-setup.png"), showApps: false);
        RenderKioskPreview(Path.Combine(outputDirectory, "file-manager-kiosk-apps.png"), showApps: true);

        static void RenderFilesPreview(string path)
        {
            var window = CreateWindow(tabIndex: 0);
            var remotePath = Find<TextBox>(window, "RemotePathBox");
            remotePath.Text = "/sdcard/Download";

            var entries = new[]
            {
                new RemoteEntry("Movies", "/sdcard/Download/Movies", IsDirectory: true),
                new RemoteEntry("Pictures", "/sdcard/Download/Pictures", IsDirectory: true),
                new RemoteEntry("demo-build.apk", "/sdcard/Download/demo-build.apk", IsDirectory: false),
                new RemoteEntry("session-notes.txt", "/sdcard/Download/session-notes.txt", IsDirectory: false)
            };
            var entriesGrid = Find<DataGrid>(window, "RemoteEntriesGrid");
            entriesGrid.Columns[0].Width = new DataGridLength(280);
            entriesGrid.Columns[1].Width = new DataGridLength(120);
            entriesGrid.Columns[2].Width = new DataGridLength(650);
            entriesGrid.ItemsSource = entries;
            Find<TextBlock>(window, "StatusText").Text = "Ready. Double-click a folder to open it, or select a file to copy it.";

            Render(window, path);
        }

        static void RenderKioskPreview(string path, bool showApps)
        {
            var window = CreateWindow(tabIndex: 3);

            Find<TextBox>(window, "KioskBundlePathBox").Text = "Included Rusty Kiosk release bundle";
            Find<TextBlock>(window, "KioskBundleStatusText").Text = "Bundle ready: Kiosk app and setup helper found.";
            Find<TextBox>(window, "KioskDirectEndpointBox").Text = "http://quest-headset.local:39873";
            Find<TextBox>(window, "KioskDirectPairingCodeBox").Text = "482913";
            Find<TextBlock>(window, "KioskDirectStatusText").Text = "Direct link: connected on the shared network";
            Find<TextBlock>(window, "KioskInstallStatusText").Text = "Rusty Kiosk: installed";
            Find<TextBlock>(window, "KioskSetupStatusText").Text = "USB setup: helper ready";
            Find<TextBlock>(window, "KioskWifiStatusText").Text = "Wireless debugging: off (not required for direct link)";
            Find<TextBlock>(window, "KioskAutoWifiStatusText").Text = "Request after restart: off";
            Find<TextBlock>(window, "KioskAccessibilityStatusText").Text = "Accessibility guard: enabled";
            Find<TextBlock>(window, "KioskSyncStatusText").Text = "PC/headset sync: confirmed";

            var apps = new[]
            {
                new RustyKioskAppEntry(
                    "package:io.github.mesmerprism.rustyquestspatialvrstrobe",
                    "Rusty Quest Spatial VR Strobe",
                    "io.github.mesmerprism.rustyquestspatialvrstrobe",
                    Installed: true,
                    Launchable: true,
                    Tags: new[] { "demo", "strobe" }),
                new RustyKioskAppEntry(
                    "name:gallery-strobe",
                    "Gallery Strobe",
                    PackageName: null,
                    Installed: false,
                    Launchable: false,
                    Tags: new[] { "strobe" })
            };
            Find<TextBox>(window, "KioskSearchBox").Text = "strobe";
            var tagFilter = Find<ComboBox>(window, "KioskTagFilterBox");
            tagFilter.ItemsSource = new[] { "All tags", "demo", "strobe" };
            tagFilter.SelectedItem = "strobe";
            var appsList = Find<ListBox>(window, "KioskAppsList");
            appsList.ItemsSource = apps;
            appsList.SelectedIndex = 0;
            Find<TextBlock>(window, "StatusText").Text = showApps
                ? "Tag filter confirmed. Two matching entries are shown; one is not installed."
                : "Rusty Kiosk is installed, connected, and synchronized with the headset.";

            Layout(window);
            if (showApps)
            {
                var scroller = Find<ScrollViewer>(window, "KioskScrollViewer");
                scroller.ScrollToVerticalOffset(430);
                window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Render);
                Layout(window);
            }

            Render(window, path, layoutFirst: false);
        }

        static MainWindow CreateWindow(int tabIndex)
        {
            var window = new MainWindow
            {
                Width = PreviewWidth,
                Height = PreviewHeight,
                ShowActivated = false
            };
            var deviceBox = Find<ComboBox>(window, "DeviceBox");
            deviceBox.ItemsSource = new[]
            {
                new QuestDevice("USB connected", "device", "Quest 3", "Quest")
            };
            deviceBox.SelectedIndex = 0;
            Find<TextBlock>(window, "AdbPathText").Text = "Connection: authorized USB-C";
            Find<TabControl>(window, "MainTabs").SelectedIndex = tabIndex;
            return window;
        }

        static T Find<T>(FrameworkElement root, string name) where T : FrameworkElement =>
            root.FindName(name) as T ?? throw new InvalidOperationException($"Could not find {name}.");

        static void Render(MainWindow window, string path, bool layoutFirst = true)
        {
            if (layoutFirst)
            {
                Layout(window);
            }

            var root = (FrameworkElement)window.Content;
            var bitmap = new RenderTargetBitmap(
                PreviewWidth,
                PreviewHeight,
                96,
                96,
                PixelFormats.Pbgra32);
            bitmap.Render(root);

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = File.Create(path);
            encoder.Save(stream);
            window.Close();
        }

        static void Layout(MainWindow window)
        {
            var root = (FrameworkElement)window.Content;
            root.Measure(new Size(PreviewWidth, PreviewHeight));
            root.Arrange(new Rect(0, 0, PreviewWidth, PreviewHeight));
            root.UpdateLayout();
            window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Render);
        }
    }
}
