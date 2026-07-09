[CmdletBinding()]
param(
    [ValidateSet("Folder", "Msix", "All")]
    [string]$Mode = "Folder",

    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",

    [ValidateSet("win-x64", "win-x86", "win-arm64")]
    [string]$RuntimeIdentifier = "win-x64",

    [string]$Version = "1.0.0",
    [string]$PackageVersion = "",
    [string]$OutputRoot = "",
    [switch]$Clean,
    [switch]$NoZip,

    [switch]$CreateTestCertificate,
    [string]$CertificateSubject = "CN=AppPublisher",
    [string]$CertificatePath = "",
    [string]$CertificatePassword = "",
    [string]$CertificateThumbprint = "",
    [switch]$InstallCertificate
)

$ErrorActionPreference = "Stop"

function Invoke-Checked {
    param(
        [Parameter(Mandatory = $true)]
        [string]$FilePath,

        [Parameter(Mandatory = $true)]
        [string[]]$Arguments
    )

    Write-Host ">> $FilePath $($Arguments -join ' ')" -ForegroundColor Cyan
    & $FilePath @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Command failed with exit code $LASTEXITCODE."
    }
}

function Convert-ToPackageVersion {
    param([Parameter(Mandatory = $true)][string]$Value)

    $parts = $Value.Split(".")
    if ($parts.Count -gt 4) {
        throw "Package version must contain at most four numeric parts."
    }

    $normalized = @()
    foreach ($part in $parts) {
        if ($part -notmatch "^\d+$") {
            throw "Package version must be numeric, for example 1.2.3.4."
        }

        $normalized += [int]$part
    }

    while ($normalized.Count -lt 4) {
        $normalized += 0
    }

    return ($normalized -join ".")
}

function Resolve-SafeOutputRoot {
    param(
        [Parameter(Mandatory = $true)][string]$RepoRoot,
        [Parameter(Mandatory = $true)][string]$RequestedOutputRoot
    )

    $resolvedRepo = [System.IO.Path]::GetFullPath($RepoRoot)
    $resolvedOutput = [System.IO.Path]::GetFullPath($RequestedOutputRoot)

    if (-not $resolvedOutput.StartsWith($resolvedRepo, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "OutputRoot must stay inside the repository: $resolvedRepo"
    }

    return $resolvedOutput
}

function New-ReleaseCertificate {
    param(
        [Parameter(Mandatory = $true)][string]$Subject,
        [Parameter(Mandatory = $true)][string]$Password,
        [Parameter(Mandatory = $true)][string]$CertificateDirectory,
        [switch]$InstallToTrustedPeople
    )

    if ([string]::IsNullOrWhiteSpace($Password)) {
        throw "CertificatePassword is required when CreateTestCertificate is used."
    }

    New-Item -ItemType Directory -Force -Path $CertificateDirectory | Out-Null

    $cert = New-SelfSignedCertificate `
        -Type Custom `
        -Subject $Subject `
        -KeyUsage DigitalSignature `
        -FriendlyName "AutoLock test signing certificate" `
        -CertStoreLocation "Cert:\CurrentUser\My" `
        -TextExtension @("2.5.29.37={text}1.3.6.1.5.5.7.3.3") `
        -NotAfter (Get-Date).AddYears(3)

    $securePassword = ConvertTo-SecureString -String $Password -Force -AsPlainText
    $pfxPath = Join-Path $CertificateDirectory "AutoLock-TestSigning.pfx"
    $cerPath = Join-Path $CertificateDirectory "AutoLock-TestSigning.cer"

    Export-PfxCertificate -Cert $cert -FilePath $pfxPath -Password $securePassword | Out-Null
    Export-Certificate -Cert $cert -FilePath $cerPath | Out-Null

    if ($InstallToTrustedPeople) {
        Import-Certificate -FilePath $cerPath -CertStoreLocation "Cert:\CurrentUser\TrustedPeople" | Out-Null
    }

    return [pscustomobject]@{
        Path = $pfxPath
        Thumbprint = $cert.Thumbprint
    }
}

function Set-AppxManifestVersion {
    param(
        [Parameter(Mandatory = $true)][string]$ManifestPath,
        [Parameter(Mandatory = $true)][string]$Version
    )

    [xml]$manifest = Get-Content -Raw -Path $ManifestPath
    $manifest.Package.Identity.Version = $Version
    $manifest.Save($ManifestPath)
}

$repoRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repoRoot "src\AutoLock.WinUI\AutoLock.WinUI.csproj"
$manifestPath = Join-Path $repoRoot "src\AutoLock.WinUI\Package.appxmanifest"

if ([string]::IsNullOrWhiteSpace($PackageVersion)) {
    $PackageVersion = Convert-ToPackageVersion $Version
} else {
    $PackageVersion = Convert-ToPackageVersion $PackageVersion
}

if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $repoRoot "artifacts\release\AutoLock-$Version-$RuntimeIdentifier"
}

$OutputRoot = Resolve-SafeOutputRoot -RepoRoot $repoRoot -RequestedOutputRoot $OutputRoot

if ($Clean -and (Test-Path $OutputRoot)) {
    Remove-Item -LiteralPath $OutputRoot -Recurse -Force
}

New-Item -ItemType Directory -Force -Path $OutputRoot | Out-Null

if ($CreateTestCertificate) {
    $certificate = New-ReleaseCertificate `
        -Subject $CertificateSubject `
        -Password $CertificatePassword `
        -CertificateDirectory (Join-Path $OutputRoot "certificates") `
        -InstallToTrustedPeople:$InstallCertificate
    $CertificatePath = $certificate.Path
    $CertificateThumbprint = $certificate.Thumbprint
}

$publishFolder = Join-Path $OutputRoot "folder"
$msixFolder = Join-Path $OutputRoot "msix"
$platform = switch ($RuntimeIdentifier) {
    "win-x64" { "x64" }
    "win-x86" { "x86" }
    "win-arm64" { "ARM64" }
}

if ($Mode -eq "Folder" -or $Mode -eq "All") {
    New-Item -ItemType Directory -Force -Path $publishFolder | Out-Null

    Invoke-Checked "dotnet" @(
        "publish",
        $projectPath,
        "--configuration", $Configuration,
        "--runtime", $RuntimeIdentifier,
        "--self-contained", "true",
        "-p:PublishSingleFile=false",
        "-p:Version=$Version",
        "-p:AppxPackageVersion=$PackageVersion",
        "-p:PublishDir=$publishFolder\"
    )

    if (-not $NoZip) {
        $zipPath = Join-Path $OutputRoot "AutoLock-$Version-$RuntimeIdentifier-folder.zip"
        if (Test-Path $zipPath) {
            Remove-Item -LiteralPath $zipPath -Force
        }

        Compress-Archive -Path (Join-Path $publishFolder "*") -DestinationPath $zipPath -Force
        Write-Host "Folder package: $zipPath" -ForegroundColor Green
    }

    Write-Host "Folder publish: $publishFolder" -ForegroundColor Green
}

if ($Mode -eq "Msix" -or $Mode -eq "All") {
    New-Item -ItemType Directory -Force -Path $msixFolder | Out-Null

    $signingEnabled = -not [string]::IsNullOrWhiteSpace($CertificatePath) -or
        -not [string]::IsNullOrWhiteSpace($CertificateThumbprint)

    $msixArgs = @(
        "msbuild",
        $projectPath,
        "/restore",
        "/t:Publish",
        "/p:Configuration=$Configuration",
        "/p:Platform=$platform",
        "/p:RuntimeIdentifier=$RuntimeIdentifier",
        "/p:Version=$Version",
        "/p:AppxPackageVersion=$PackageVersion",
        "/p:GenerateAppxPackageOnBuild=true",
        "/p:UapAppxPackageBuildMode=SideloadOnly",
        "/p:AppxBundle=Never",
        "/p:AppxPackageDir=$msixFolder\",
        "/p:AppxPackageSigningEnabled=$signingEnabled"
    )

    if (-not [string]::IsNullOrWhiteSpace($CertificatePath)) {
        $msixArgs += "/p:PackageCertificateKeyFile=$CertificatePath"
    }

    if (-not [string]::IsNullOrWhiteSpace($CertificatePassword)) {
        $msixArgs += "/p:PackageCertificatePassword=$CertificatePassword"
    }

    if (-not [string]::IsNullOrWhiteSpace($CertificateThumbprint)) {
        $msixArgs += "/p:PackageCertificateThumbprint=$CertificateThumbprint"
    }

    $originalManifest = Get-Content -Raw -Path $manifestPath
    try {
        Set-AppxManifestVersion -ManifestPath $manifestPath -Version $PackageVersion
        Invoke-Checked "dotnet" $msixArgs
    }
    finally {
        [System.IO.File]::WriteAllText(
            $manifestPath,
            $originalManifest,
            [System.Text.UTF8Encoding]::new($false))
    }

    $packages = Get-ChildItem -Path $msixFolder -Recurse -File |
        Where-Object { $_.Extension -in ".msix", ".msixbundle", ".appinstaller", ".cer", ".pfx" }

    if ($packages.Count -eq 0) {
        Write-Warning "MSIX publish finished, but no MSIX package was found under $msixFolder."
    } else {
        Write-Host "MSIX output:" -ForegroundColor Green
        $packages | ForEach-Object { Write-Host "  $($_.FullName)" -ForegroundColor Green }
    }
}

Write-Host "Release artifacts: $OutputRoot" -ForegroundColor Green
