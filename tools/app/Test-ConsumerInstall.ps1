[CmdletBinding()]
param(
    [string]$ReleaseDirectory = (Join-Path $PSScriptRoot '..\..\artifacts\release'),
    [string]$SetupPath,
    [switch]$DirectPackage,
    [switch]$SkipLaunch,
    [switch]$RemoveAfterTest,
    [ValidateRange(20, 300)]
    [int]$LaunchTimeoutSeconds = 60
)

$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$ReleaseDirectory = [IO.Path]::GetFullPath($ReleaseDirectory)
$packageName = 'MesmerPrism.MetaQuestFileManager'
if (-not $SetupPath) { $SetupPath = Join-Path $ReleaseDirectory 'QuestIonAbleFileManager-Setup.exe' }
$setupPath = [IO.Path]::GetFullPath($SetupPath)
$packagePath = Join-Path $ReleaseDirectory 'QuestIonAbleFileManager-win-x64.msix'
$appInstallerPath = Join-Path $ReleaseDirectory 'QuestIonAbleFileManager.appinstaller'
$certificatePath = Join-Path $ReleaseDirectory 'QuestIonAbleFileManager.cer'
$verifyDirectory = Join-Path $repoRoot 'artifacts\verify\consumer-install'
$deploymentStagingDirectory = Join-Path $env:TEMP 'QuestIonAbleFileManagerConsumerInstall'

foreach ($path in @($setupPath, $packagePath, $appInstallerPath, $certificatePath)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Missing test input: $path" }
}
if (Test-Path -LiteralPath $verifyDirectory) { Remove-Item -LiteralPath $verifyDirectory -Recurse -Force }
New-Item -ItemType Directory -Path $verifyDirectory -Force | Out-Null
if (Test-Path -LiteralPath $deploymentStagingDirectory) { Remove-Item -LiteralPath $deploymentStagingDirectory -Recurse -Force }
New-Item -ItemType Directory -Path $deploymentStagingDirectory -Force | Out-Null

$existingBefore = Get-AppxPackage -Name $packageName -ErrorAction SilentlyContinue | Sort-Object Version -Descending | Select-Object -First 1
if ($existingBefore) {
    throw "Package $packageName is already installed. Remove it or test on a clean Windows user profile."
}

$certificate = [Security.Cryptography.X509Certificates.X509CertificateLoader]::LoadCertificateFromFile($certificatePath)
try {
    $thumbprint = $certificate.Thumbprint
}
finally {
    $certificate.Dispose()
}
$trustedBefore = Test-Path -LiteralPath "Cert:\CurrentUser\TrustedPeople\$thumbprint"

$localPackagePath = Join-Path $deploymentStagingDirectory 'QuestIonAbleFileManager-win-x64.msix'
$localCertificatePath = Join-Path $deploymentStagingDirectory 'QuestIonAbleFileManager.cer'
$localAppInstallerPath = Join-Path $deploymentStagingDirectory 'QuestIonAbleFileManager.local.appinstaller'
Copy-Item -LiteralPath $packagePath -Destination $localPackagePath
Copy-Item -LiteralPath $certificatePath -Destination $localCertificatePath

$portProbe = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
$portProbe.Start()
$port = ([Net.IPEndPoint]$portProbe.LocalEndpoint).Port
$portProbe.Stop()
$feedBaseUri = "http://127.0.0.1:$port"
$localAppInstallerUri = "$feedBaseUri/QuestIonAbleFileManager.local.appinstaller"
$localPackageUri = "$feedBaseUri/QuestIonAbleFileManager-win-x64.msix"
$localCertificateUri = "$feedBaseUri/QuestIonAbleFileManager.cer"

[xml]$appInstaller = Get-Content -LiteralPath $appInstallerPath -Raw
$namespace = [Xml.XmlNamespaceManager]::new($appInstaller.NameTable)
$namespace.AddNamespace('ai', $appInstaller.DocumentElement.NamespaceURI)
$mainPackage = $appInstaller.SelectSingleNode('/ai:AppInstaller/ai:MainPackage', $namespace)
$mainPackage.SetAttribute('Uri', $localPackageUri)
$appInstaller.DocumentElement.SetAttribute('Uri', $localAppInstallerUri)
$settings = [Xml.XmlWriterSettings]::new()
$settings.Encoding = [Text.UTF8Encoding]::new($false)
$settings.Indent = $true
$writer = [Xml.XmlWriter]::Create($localAppInstallerPath, $settings)
try { $appInstaller.Save($writer) } finally { $writer.Dispose() }

$serverJob = Start-ThreadJob -ArgumentList $deploymentStagingDirectory, $port -ScriptBlock {
    param($Root, $Port)

    $listener = [Net.HttpListener]::new()
    $listener.Prefixes.Add("http://127.0.0.1:$Port/")
    $listener.Start()
    try {
        while ($listener.IsListening) {
            $context = $listener.GetContext()
            try {
                $relativePath = [Uri]::UnescapeDataString($context.Request.Url.AbsolutePath.TrimStart('/'))
                if ($relativePath -eq '__stop') {
                    $context.Response.StatusCode = 204
                    $context.Response.Close()
                    break
                }
                $candidate = [IO.Path]::GetFullPath((Join-Path $Root $relativePath))
                if (-not $candidate.StartsWith([IO.Path]::GetFullPath($Root), [StringComparison]::OrdinalIgnoreCase) -or
                    -not (Test-Path -LiteralPath $candidate -PathType Leaf)) {
                    $context.Response.StatusCode = 404
                    $context.Response.Close()
                    continue
                }

                $context.Response.ContentType = switch ([IO.Path]::GetExtension($candidate).ToLowerInvariant()) {
                    '.msix' { 'application/msix' }
                    '.appinstaller' { 'application/appinstaller' }
                    '.cer' { 'application/pkix-cert' }
                    default { 'application/octet-stream' }
                }
                $fileInfo = Get-Item -LiteralPath $candidate
                $start = 0L
                $end = $fileInfo.Length - 1
                $rangeHeader = $context.Request.Headers['Range']
                if ($rangeHeader -match '^bytes=(\d+)-(\d*)$') {
                    $start = [long]$Matches[1]
                    if ($Matches[2]) { $end = [Math]::Min([long]$Matches[2], $end) }
                    if ($start -gt $end) {
                        $context.Response.StatusCode = 416
                        $context.Response.Close()
                        continue
                    }
                    $context.Response.StatusCode = 206
                    $context.Response.AddHeader('Content-Range', "bytes $start-$end/$($fileInfo.Length)")
                }
                $length = $end - $start + 1
                $context.Response.AddHeader('Accept-Ranges', 'bytes')
                $context.Response.ContentLength64 = $length
                if ($context.Request.HttpMethod -ne 'HEAD') {
                    $stream = [IO.File]::OpenRead($candidate)
                    try {
                        $stream.Position = $start
                        $buffer = [byte[]]::new(65536)
                        $remaining = $length
                        while ($remaining -gt 0) {
                            $read = $stream.Read($buffer, 0, [Math]::Min($buffer.Length, [int][Math]::Min($remaining, [int]::MaxValue)))
                            if ($read -le 0) { break }
                            $context.Response.OutputStream.Write($buffer, 0, $read)
                            $remaining -= $read
                        }
                    }
                    finally {
                        $stream.Dispose()
                    }
                }
                $statusCode = $context.Response.StatusCode
                $context.Response.Close()
                [pscustomobject]@{ Method = $context.Request.HttpMethod; Path = $relativePath; Bytes = $length; Range = $rangeHeader; Status = $statusCode }
            }
            catch {
                [pscustomobject]@{ Method = $context.Request.HttpMethod; Path = $context.Request.Url.AbsolutePath; Status = 500; Error = $_.Exception.Message }
                try { $context.Response.StatusCode = 500; $context.Response.Close() } catch {}
            }
        }
    }
    finally {
        $listener.Stop()
        $listener.Close()
    }
}

$serverDeadline = [DateTime]::UtcNow.AddSeconds(10)
do {
    try {
        $probe = Invoke-WebRequest -Uri $localAppInstallerUri -UseBasicParsing -TimeoutSec 1
    }
    catch {
        $probe = $null
        Start-Sleep -Milliseconds 100
    }
} while ($null -eq $probe -and [DateTime]::UtcNow -lt $serverDeadline)
if ($null -eq $probe) {
    Stop-Job $serverJob -ErrorAction SilentlyContinue
    Remove-Job $serverJob -Force -ErrorAction SilentlyContinue
    throw 'The local release-feed server did not start.'
}

$stdoutPath = Join-Path $verifyDirectory 'setup.stdout.json'
$stderrPath = Join-Path $verifyDirectory 'setup.stderr.txt'
$arguments = @(
    '--json',
    '--no-launch',
    '--certificate-source', $localCertificateUri,
    '--appinstaller-source', $localAppInstallerUri
)
if ($DirectPackage) {
    $arguments = @('--plan') + $arguments
}
else {
    $arguments = @('--quiet') + $arguments
}
try {
    $process = Start-Process -FilePath $setupPath -ArgumentList $arguments -Wait -PassThru -NoNewWindow `
        -RedirectStandardOutput $stdoutPath -RedirectStandardError $stderrPath
    if ($process.ExitCode -ne 0) {
        throw "Guided setup failed with exit code $($process.ExitCode): $(Get-Content -LiteralPath $stderrPath -Raw)"
    }
}
finally {
    try { Invoke-WebRequest -Uri "$feedBaseUri/__stop" -UseBasicParsing -TimeoutSec 2 | Out-Null } catch {}
    Wait-Job $serverJob -Timeout 3 -ErrorAction SilentlyContinue | Out-Null
    Receive-Job $serverJob -Keep -ErrorAction SilentlyContinue 2>&1 |
        ConvertTo-Json -Depth 4 |
        Set-Content -LiteralPath (Join-Path $verifyDirectory 'feed-server.log') -Encoding utf8
    Stop-Job $serverJob -ErrorAction SilentlyContinue
    Remove-Job $serverJob -Force -ErrorAction SilentlyContinue
}

$setupResult = Get-Content -LiteralPath $stdoutPath -Raw | ConvertFrom-Json
if ($DirectPackage) {
    if ($setupResult.Status -ne 'planned') { throw 'The guided setup plan did not validate successfully.' }
    Add-AppxPackage -Path $localPackagePath -ForceApplicationShutdown -ErrorAction Stop
}
elseif (-not $setupResult.Installed) {
    throw 'The guided setup did not report an installed package.'
}
$installed = Get-AppxPackage -Name $packageName -ErrorAction Stop | Sort-Object Version -Descending | Select-Object -First 1
if (-not $installed) { throw 'The package was not found after guided setup completed.' }

$launched = $false
if (-not $SkipLaunch) {
    Start-Process explorer.exe -ArgumentList "shell:AppsFolder\$($installed.PackageFamilyName)!App"
    # A cold first launch may wait while Windows validates the newly installed
    # package. Observed consumer systems can take longer than 20 seconds here.
    $deadline = [DateTime]::UtcNow.AddSeconds($LaunchTimeoutSeconds)
    do {
        Start-Sleep -Milliseconds 300
        $appProcess = Get-Process -Name 'QuestIonAbleFileManager' -ErrorAction SilentlyContinue |
            Where-Object { $_.Path -like "$($installed.InstallLocation)*" } |
            Select-Object -First 1
    } while (-not $appProcess -and [DateTime]::UtcNow -lt $deadline)
    if (-not $appProcess) { throw "The packaged WPF app did not launch within $LaunchTimeoutSeconds seconds." }
    $launched = $true
    $appProcess.CloseMainWindow() | Out-Null
}

$receipt = [ordered]@{
    schema = 'questionable-file-manager.consumer-install.v1'
    validated_at_utc = [DateTime]::UtcNow.ToString('o')
    package_name = $installed.Name
    package_full_name = $installed.PackageFullName
    package_family_name = $installed.PackageFamilyName
    version = $installed.Version.ToString()
    install_location = $installed.InstallLocation
    setup_status = $setupResult.Status
    install_route = if ($DirectPackage) { 'validated-plan-plus-direct-msix' } else { 'guided-appinstaller' }
    guided_install_validated = -not [bool]$DirectPackage
    certificate_thumbprint = $thumbprint
    certificate_trusted = (Test-Path -LiteralPath "Cert:\CurrentUser\TrustedPeople\$thumbprint")
    launched = $launched
    removed_after_test = [bool]$RemoveAfterTest
}
$receiptPath = Join-Path $verifyDirectory 'consumer-install-receipt.json'
$receipt | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $receiptPath -Encoding utf8

if ($RemoveAfterTest) {
    Get-AppxPackage -Name $packageName | Remove-AppxPackage
    if (-not $trustedBefore -and (Test-Path -LiteralPath "Cert:\CurrentUser\TrustedPeople\$thumbprint")) {
        Remove-Item -LiteralPath "Cert:\CurrentUser\TrustedPeople\$thumbprint" -Force
    }
}

if (Test-Path -LiteralPath $deploymentStagingDirectory) {
    Remove-Item -LiteralPath $deploymentStagingDirectory -Recurse -Force
}

$receipt
