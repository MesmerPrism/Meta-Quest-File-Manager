using System.Diagnostics;
using System.Security.Cryptography.X509Certificates;
using System.Security.Principal;
using System.Text.Json;
using System.Xml.Linq;
using Windows.Foundation;
using Windows.Management.Deployment;

namespace MetaQuestFileManager.Setup;

internal sealed record InstallerOptions(
    string CertificateSource,
    string AppInstallerSource,
    bool PlanOnly,
    bool Quiet,
    bool NoLaunch,
    bool Json)
{
    private const string DefaultCertificateSource =
        "https://github.com/MesmerPrism/Meta-Quest-File-Manager/releases/latest/download/MetaQuestFileManager.cer";

    private const string DefaultAppInstallerSource =
        "https://github.com/MesmerPrism/Meta-Quest-File-Manager/releases/latest/download/MetaQuestFileManager.appinstaller";

    public static InstallerOptions Parse(string[] args)
    {
        var certificateSource = DefaultCertificateSource;
        var appInstallerSource = DefaultAppInstallerSource;
        var planOnly = false;
        var quiet = false;
        var noLaunch = false;
        var json = false;

        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--certificate-source":
                    certificateSource = ReadValue(args, ref index);
                    break;
                case "--appinstaller-source":
                    appInstallerSource = ReadValue(args, ref index);
                    break;
                case "--plan":
                    planOnly = true;
                    break;
                case "--quiet":
                    quiet = true;
                    break;
                case "--no-launch":
                    noLaunch = true;
                    break;
                case "--json":
                    json = true;
                    break;
                case "--help":
                case "-h":
                    throw new InstallerHelpException();
                default:
                    throw new ArgumentException($"Unknown setup option: {args[index]}");
            }
        }

        return new InstallerOptions(certificateSource, appInstallerSource, planOnly, quiet, noLaunch, json);
    }

    public static string HelpText => """
        Meta Quest File Manager Setup

        Usage:
          MetaQuestFileManager-Setup.exe [options]

        Options:
          --certificate-source <uri-or-path>  Override the release certificate source.
          --appinstaller-source <uri-or-path> Override the App Installer feed source.
          --plan                              Stage and validate assets without trusting or installing.
          --quiet                             Run without the guided window.
          --no-launch                         Do not launch the app after installation.
          --json                              Emit a machine-readable result in quiet mode.
          --help                              Show this help.
        """;

    private static string ReadValue(string[] args, ref int index)
    {
        if (++index >= args.Length || string.IsNullOrWhiteSpace(args[index]))
        {
            throw new ArgumentException("An option value is missing.");
        }

        return args[index];
    }
}

internal sealed class InstallerHelpException : Exception;

internal sealed record InstallerProgress(string Status, string Detail, int Percent);

internal sealed record InstallerResult(
    string Status,
    string PackageName,
    string PackageVersion,
    string Publisher,
    string AppInstallerPath,
    string CertificateThumbprint,
    bool CertificateTrusted,
    bool Installed,
    bool Launched);

internal sealed record AppInstallerIdentity(string Name, string Publisher, string Version);

internal sealed class GuidedInstaller
{
    internal const string ExpectedPackageName = "MesmerPrism.MetaQuestFileManager";
    internal const string ExpectedPublisher = "CN=MesmerPrism";
    private const string DownloadDirectoryName = "MetaQuestFileManagerSetup";

    public async Task<InstallerResult> RunAsync(
        InstallerOptions options,
        IProgress<InstallerProgress>? progress,
        CancellationToken cancellationToken)
    {
        var stagingDirectory = Path.Combine(Path.GetTempPath(), DownloadDirectoryName);
        Directory.CreateDirectory(stagingDirectory);
        var certificatePath = Path.Combine(stagingDirectory, "MetaQuestFileManager.cer");
        var appInstallerPath = Path.Combine(stagingDirectory, "MetaQuestFileManager.appinstaller");

        progress?.Report(new InstallerProgress("Preparing setup", "Creating the local staging area.", 5));
        using var httpClient = new HttpClient();
        await StageSourceAsync(httpClient, options.CertificateSource, certificatePath, cancellationToken);
        progress?.Report(new InstallerProgress("Certificate ready", "The public signing certificate was staged.", 25));
        await StageSourceAsync(httpClient, options.AppInstallerSource, appInstallerPath, cancellationToken);
        progress?.Report(new InstallerProgress("Update feed ready", "The App Installer feed was staged.", 45));

        using var certificate = X509CertificateLoader.LoadCertificateFromFile(certificatePath);
        var identity = ParseAndValidateAppInstaller(appInstallerPath);

        if (options.PlanOnly)
        {
            progress?.Report(new InstallerProgress("Plan validated", "No trust or package state was changed.", 100));
            return new InstallerResult(
                "planned",
                identity.Name,
                identity.Version,
                identity.Publisher,
                appInstallerPath,
                certificate.Thumbprint,
                CertificateTrusted: IsCertificateTrusted(certificate),
                Installed: false,
                Launched: false);
        }

        progress?.Report(new InstallerProgress(
            "Trusting certificate",
            "Adding the public certificate to your Windows Trusted People store.",
            55));
        var addedCertificate = TrustCertificateForCurrentUser(certificate);

        progress?.Report(new InstallerProgress(
            "Installing application",
            $"Windows is installing or updating Meta Quest File Manager {identity.Version}.",
            65));
        await InstallFromAppInstallerAsync(appInstallerPath, progress, cancellationToken);

        var package = new PackageManager()
            .FindPackages()
            .Where(candidate =>
                string.Equals(candidate.Id.Name, identity.Name, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(candidate.Id.Publisher, identity.Publisher, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(candidate => candidate.Id.Version)
            .FirstOrDefault()
            ?? throw new InvalidOperationException("Windows completed setup but the installed package registration could not be found.");

        var launched = false;
        if (!options.NoLaunch)
        {
            progress?.Report(new InstallerProgress("Launching application", "Opening the installed app.", 98));
            launched = TryLaunch(package.Id.FamilyName);
        }

        progress?.Report(new InstallerProgress("Setup complete", "Meta Quest File Manager is ready.", 100));
        return new InstallerResult(
            "installed",
            identity.Name,
            identity.Version,
            identity.Publisher,
            appInstallerPath,
            certificate.Thumbprint,
            CertificateTrusted: addedCertificate || IsCertificateTrusted(certificate),
            Installed: true,
            Launched: launched);
    }

    internal static AppInstallerIdentity ParseAndValidateAppInstaller(string path)
    {
        var document = XDocument.Load(path, LoadOptions.PreserveWhitespace);
        var root = document.Root ?? throw new InvalidOperationException("The App Installer feed is empty.");
        var mainPackage = root.Element(root.Name.Namespace + "MainPackage")
            ?? throw new InvalidOperationException("The App Installer feed has no MainPackage entry.");
        var identity = new AppInstallerIdentity(
            mainPackage.Attribute("Name")?.Value.Trim() ?? string.Empty,
            mainPackage.Attribute("Publisher")?.Value.Trim() ?? string.Empty,
            mainPackage.Attribute("Version")?.Value.Trim() ?? string.Empty);

        if (!string.Equals(identity.Name, ExpectedPackageName, StringComparison.Ordinal) ||
            !string.Equals(identity.Publisher, ExpectedPublisher, StringComparison.Ordinal) ||
            !Version.TryParse(identity.Version, out _))
        {
            throw new InvalidOperationException(
                $"The feed identity is not the expected public package ({ExpectedPackageName}, {ExpectedPublisher}).");
        }

        return identity;
    }

    private static async Task StageSourceAsync(
        HttpClient httpClient,
        string source,
        string destination,
        CancellationToken cancellationToken)
    {
        if (Uri.TryCreate(source, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            using var response = await httpClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            await using var output = File.Create(destination);
            await response.Content.CopyToAsync(output, cancellationToken);
            return;
        }

        var localPath = uri is { IsFile: true } ? uri.LocalPath : source;
        File.Copy(Path.GetFullPath(localPath), destination, overwrite: true);
    }

    private static bool TrustCertificateForCurrentUser(X509Certificate2 certificate)
    {
        using var store = new X509Store(StoreName.TrustedPeople, StoreLocation.LocalMachine);
        store.Open(OpenFlags.ReadWrite);
        var exists = store.Certificates
            .Find(X509FindType.FindByThumbprint, certificate.Thumbprint, validOnly: false)
            .Count > 0;
        if (!exists)
        {
            store.Add(certificate);
        }

        return !exists;
    }

    private static bool IsCertificateTrusted(X509Certificate2 certificate)
    {
        foreach (var location in new[] { StoreLocation.CurrentUser, StoreLocation.LocalMachine })
        {
            using var store = new X509Store(StoreName.TrustedPeople, location);
            store.Open(OpenFlags.ReadOnly);
            if (store.Certificates.Find(X509FindType.FindByThumbprint, certificate.Thumbprint, validOnly: false).Count > 0)
            {
                return true;
            }
        }

        return false;
    }

    private static async Task InstallFromAppInstallerAsync(
        string appInstallerPath,
        IProgress<InstallerProgress>? progress,
        CancellationToken cancellationToken)
    {
        var packageManager = new PackageManager();
        var operation = packageManager.AddPackageByAppInstallerFileAsync(
            new Uri(Path.GetFullPath(appInstallerPath)),
            AddPackageByAppInstallerOptions.ForceTargetAppShutdown,
            packageManager.GetDefaultPackageVolume());

        operation.Progress = new AsyncOperationProgressHandler<DeploymentResult, DeploymentProgress>((_, update) =>
        {
            var percent = 65 + (int)Math.Round(update.percentage * 0.32);
            progress?.Report(new InstallerProgress("Installing application", $"Windows package state: {update.state}.", Math.Clamp(percent, 65, 97)));
        });

        using var registration = cancellationToken.Register(operation.Cancel);
        var result = await operation;
        if (!string.IsNullOrWhiteSpace(result.ErrorText))
        {
            var exception = new InvalidOperationException(result.ErrorText, result.ExtendedErrorCode);
            if (result.ExtendedErrorCode is not null)
            {
                exception.HResult = result.ExtendedErrorCode.HResult;
            }

            throw exception;
        }
    }

    private static bool TryLaunch(string? packageFamilyName)
    {
        if (string.IsNullOrWhiteSpace(packageFamilyName))
        {
            return false;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = $@"shell:AppsFolder\{packageFamilyName}!App",
            UseShellExecute = true
        });
        return true;
    }
}

internal sealed class InstallerForm : Form
{
    private readonly InstallerOptions _options;
    private readonly Label _status = new() { AutoSize = false, Font = new Font("Segoe UI", 17, FontStyle.Bold), Height = 38, Dock = DockStyle.Top };
    private readonly Label _detail = new() { AutoSize = false, Font = new Font("Segoe UI", 10), Height = 74, Dock = DockStyle.Top };
    private readonly ProgressBar _progress = new() { Minimum = 0, Maximum = 100, Height = 22, Dock = DockStyle.Top };
    private readonly Button _close = new() { Text = "Close", Width = 110, Height = 34, Enabled = false, Dock = DockStyle.Bottom };

    public InstallerForm(InstallerOptions options)
    {
        _options = options;
        Text = "Meta Quest File Manager Setup";
        Width = 580;
        Height = 300;
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        Padding = new Padding(26);
        Controls.Add(_close);
        Controls.Add(_progress);
        Controls.Add(_detail);
        Controls.Add(_status);
        _close.Click += (_, _) => Close();
        Shown += async (_, _) => await RunAsync();
    }

    public int ExitCode { get; private set; }

    private async Task RunAsync()
    {
        try
        {
            var progress = new Progress<InstallerProgress>(update =>
            {
                _status.Text = update.Status;
                _detail.Text = update.Detail;
                _progress.Value = Math.Clamp(update.Percent, 0, 100);
            });
            await new GuidedInstaller().RunAsync(_options, progress, CancellationToken.None);
            _close.Enabled = true;
        }
        catch (Exception exception)
        {
            ExitCode = 1;
            _status.Text = "Setup could not finish";
            _detail.Text = exception.Message;
            _close.Enabled = true;
        }
    }
}

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            var options = InstallerOptions.Parse(args);
            if (!options.PlanOnly && !IsAdministrator())
            {
                return RelaunchElevated(args);
            }

            if (options.Quiet || options.PlanOnly)
            {
                return RunHeadlessAsync(options).GetAwaiter().GetResult();
            }

            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            using var form = new InstallerForm(options);
            Application.Run(form);
            return form.ExitCode;
        }
        catch (InstallerHelpException)
        {
            Console.WriteLine(InstallerOptions.HelpText);
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 2;
        }
    }

    private static bool IsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    private static int RelaunchElevated(string[] args)
    {
        var processPath = Environment.ProcessPath
            ?? throw new InvalidOperationException("The setup executable path could not be resolved for elevation.");
        var startInfo = new ProcessStartInfo
        {
            FileName = processPath,
            UseShellExecute = true,
            Verb = "runas"
        };
        foreach (var argument in args)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Windows did not start the elevated setup process.");
        process.WaitForExit();
        return process.ExitCode;
    }

    private static async Task<int> RunHeadlessAsync(InstallerOptions options)
    {
        try
        {
            var result = await new GuidedInstaller().RunAsync(options, progress: null, CancellationToken.None);
            Console.WriteLine(options.Json
                ? JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true })
                : $"{result.Status}: {result.PackageName} {result.PackageVersion}");
            return 0;
        }
        catch (Exception exception)
        {
            if (options.Json)
            {
                Console.Error.WriteLine(JsonSerializer.Serialize(new
                {
                    status = "failed",
                    error = exception.Message,
                    error_type = exception.GetType().FullName,
                    hresult = $"0x{exception.HResult:X8}",
                    inner_hresult = exception.InnerException is null ? null : $"0x{exception.InnerException.HResult:X8}"
                }));
            }
            else
            {
                Console.Error.WriteLine(exception.Message);
            }

            return 1;
        }
    }
}
