param(
    [switch]$SkipPublish,
    [switch]$RestartCommandPalette
)

$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\")).Path
$project = Join-Path $repoRoot "src\GHubTray.CmdPal\GHubTray.CmdPal.csproj"
$manifestPath = Join-Path $repoRoot "src\GHubTray.CmdPal\Package.appxmanifest"
$packagesRoot = Join-Path $repoRoot "src\GHubTray.CmdPal\AppPackages"

function Update-PackageVersion([string]$path) {
    $manifest = Get-Content -LiteralPath $path -Raw
    $identity = [regex]::Match(
        $manifest,
        '<Identity\b[^>]*\bVersion="(?<version>\d+\.\d+\.\d+\.\d+)"')
    if (-not $identity.Success) {
        throw "Package identity version was not found: $path"
    }

    $sourceVersion = [version]$identity.Groups['version'].Value
    $installed = Get-AppxPackage -Name "GHubTrayCmdPal" -ErrorAction SilentlyContinue |
        Sort-Object Version -Descending |
        Select-Object -First 1
    $highestVersion = $sourceVersion
    if ($installed) {
        $installedVersion = [version]$installed.Version
        if ($installedVersion -gt $highestVersion) {
            $highestVersion = $installedVersion
        }
    }

    if ($highestVersion.Revision -ge 65535) {
        throw "Package revision cannot be incremented: $highestVersion"
    }

    $nextVersion = [version]::new(
        $highestVersion.Major,
        $highestVersion.Minor,
        $highestVersion.Build,
        $highestVersion.Revision + 1)
    $oldVersionText = $identity.Groups['version'].Value
    $nextVersionText = $nextVersion.ToString()
    $updatedManifest = $manifest.Replace(
        "Version=""$oldVersionText""",
        "Version=""$nextVersionText""")
    if ($updatedManifest -eq $manifest) {
        throw "Package identity version could not be updated: $path"
    }

    [System.IO.File]::WriteAllText(
        $path,
        $updatedManifest,
        [System.Text.UTF8Encoding]::new($false))
    return $nextVersionText
}

$packageVersion = $null
if (-not $SkipPublish) {
    $packageVersion = Update-PackageVersion $manifestPath
    & dotnet publish $project -c Release -p:Platform=x64 -p:GenerateAppxPackageOnBuild=true --no-restore
    if ($LASTEXITCODE -ne 0) {
        throw "Command Palette publish failed with exit code $LASTEXITCODE"
    }
}

$package = Get-ChildItem -LiteralPath $packagesRoot -Recurse -Filter "*.msix" -File |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1
if (-not $package) {
    throw "No MSIX was found under $packagesRoot"
}

$signtool = Get-ChildItem -LiteralPath (Join-Path ${env:ProgramFiles(x86)} "Windows Kits\10\bin") -Recurse -Filter "signtool.exe" -File |
    Where-Object { $_.FullName -match "\\x64\\signtool\.exe$" } |
    Sort-Object FullName -Descending |
    Select-Object -First 1
if (-not $signtool) {
    throw "The x64 Windows SDK signtool.exe was not found"
}

$subject = "CN=GHubTray"
$friendlyName = "GHubTray Development Signing"
$minimumLifetime = (Get-Date).AddDays(7)
$certificate = Get-ChildItem -LiteralPath "Cert:\CurrentUser\My" |
    Where-Object {
        $_.Subject -eq $subject -and
        $_.FriendlyName -eq $friendlyName -and
        $_.HasPrivateKey -and
        $_.NotAfter -gt $minimumLifetime
    } |
    Sort-Object NotAfter -Descending |
    Select-Object -First 1

if (-not $certificate) {
    $certificate = New-SelfSignedCertificate `
        -Type CodeSigningCert `
        -Subject $subject `
        -FriendlyName $friendlyName `
        -KeyAlgorithm RSA `
        -KeyLength 2048 `
        -HashAlgorithm SHA256 `
        -CertStoreLocation "Cert:\CurrentUser\My" `
        -NotAfter (Get-Date).AddDays(90)
}

$packageDirectory = $package.Directory.FullName
$certificatePath = Join-Path $packageDirectory "GHubTray.cer"
Export-Certificate -Cert $certificate -FilePath $certificatePath -Force | Out-Null

& $signtool.FullName sign /fd SHA256 /sha1 $certificate.Thumbprint /d "GHubTray Command Palette" $package.FullName
if ($LASTEXITCODE -ne 0) {
    throw "MSIX signing failed with exit code $LASTEXITCODE"
}

$installer = Join-Path $packageDirectory "Add-AppDevPackage.ps1"
if (-not (Test-Path -LiteralPath $installer)) {
    throw "Generated development installer was not found: $installer"
}

$null = & certutil.exe -verifystore TrustedPeople $certificate.Thumbprint 2>$null
$machineTrustExists = ($LASTEXITCODE -eq 0)
if ($machineTrustExists) {
    & $installer -Force -SkipLoggingTelemetry
}
else {
    & $installer -Force -SkipLoggingTelemetry -CertificatePath $certificatePath
}
if ($LASTEXITCODE -ne 0) {
    throw "AppX installation failed with exit code $LASTEXITCODE"
}

$installed = Get-AppxPackage -Name "GHubTrayCmdPal" | Select-Object -First 1
if (-not $installed -or $installed.Status -ne "Ok") {
    throw "GHubTrayCmdPal was not registered successfully"
}

if ($RestartCommandPalette) {
    Get-Process -Name "Microsoft.CmdPal.UI" -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Milliseconds 700
    $commandPalette = (Get-AppxPackage -Name "Microsoft.CommandPalette").InstallLocation |
        Join-Path -ChildPath "Microsoft.CmdPal.UI.exe"
    Start-Process -FilePath $commandPalette -ArgumentList "x-cmdpal://background/" | Out-Null
}

[pscustomobject]@{
    Package = $package.FullName
    PackageVersion = $packageVersion
    PackageFullName = $installed.PackageFullName
    Publisher = $installed.Publisher
    CertificateThumbprint = $certificate.Thumbprint
    CommandPaletteRestarted = $RestartCommandPalette.IsPresent
}
