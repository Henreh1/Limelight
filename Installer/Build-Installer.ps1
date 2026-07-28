[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",

    [ValidateSet("win-x64")]
    [string]$Runtime = "win-x64",

    [switch]$SkipPublish,

    [string]$SignToolPath,

    [string]$CertificateThumbprint,

    [string]$TimestampUrl = "http://timestamp.digicert.com"
)

$ErrorActionPreference = "Stop"

$projectDirectory = Split-Path -Parent $PSScriptRoot
$projectFile = Join-Path $projectDirectory "Limelight.csproj"
$artifactsDirectory = Join-Path $projectDirectory "artifacts"
$stagingDirectory = Join-Path $artifactsDirectory "installer-staging"
$publishDirectory = Join-Path $stagingDirectory "publish"
$installerAssetsDirectory = Join-Path $stagingDirectory "installer-assets"
$outputDirectory = Join-Path $artifactsDirectory "installers"

[xml]$project = Get-Content -LiteralPath $projectFile
$version = [string]$project.Project.PropertyGroup.Version

if ([string]::IsNullOrWhiteSpace($version)) {
    throw "Limelight.csproj does not contain a Version value."
}

function Resolve-InnoCompiler {
    $candidates = @(
        (Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 6\ISCC.exe"),
        (Join-Path $env:ProgramFiles "Inno Setup 6\ISCC.exe"),
        (Join-Path ${env:ProgramFiles(x86)} "Inno Setup 6\ISCC.exe")
    )

    foreach ($candidate in $candidates) {
        if (Test-Path -LiteralPath $candidate) {
            return $candidate
        }
    }

    throw "Inno Setup 6 was not found. Install JRSoftware.InnoSetup with winget, then run this script again."
}

function Assert-SafeWorkspacePath {
    param([string]$Path)

    $resolvedProject = [System.IO.Path]::GetFullPath($projectDirectory).TrimEnd('\')
    $resolvedPath = [System.IO.Path]::GetFullPath($Path)

    if (-not $resolvedPath.StartsWith(
        $resolvedProject + '\',
        [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to clean a path outside the Limelight project: $resolvedPath"
    }
}

function Invoke-LimelightSigning {
    param([string]$FilePath)

    if ([string]::IsNullOrWhiteSpace($SignToolPath)) {
        return
    }

    if ([string]::IsNullOrWhiteSpace($CertificateThumbprint)) {
        throw "CertificateThumbprint is required when SignToolPath is supplied."
    }

    & $SignToolPath sign `
        /sha1 $CertificateThumbprint `
        /fd SHA256 `
        /tr $TimestampUrl `
        /td SHA256 `
        $FilePath

    if ($LASTEXITCODE -ne 0) {
        throw "Signing failed for $FilePath."
    }
}

Assert-SafeWorkspacePath -Path $stagingDirectory

if (-not $SkipPublish) {
    if (Test-Path -LiteralPath $stagingDirectory) {
        Remove-Item -LiteralPath $stagingDirectory -Recurse -Force
    }

    [System.IO.Directory]::CreateDirectory($publishDirectory) | Out-Null

    dotnet restore $projectFile -r $Runtime
    if ($LASTEXITCODE -ne 0) {
        throw "The Limelight restore failed."
    }

    dotnet publish $projectFile `
        -c $Configuration `
        -r $Runtime `
        --self-contained true `
        --no-restore `
        -o $publishDirectory `
        /p:DebugType=None `
        /p:DebugSymbols=false

    if ($LASTEXITCODE -ne 0) {
        throw "The Limelight publish failed."
    }
}
elseif (-not (Test-Path -LiteralPath (Join-Path $publishDirectory "Limelight.exe"))) {
    throw "SkipPublish was selected, but no prepared publish output exists."
}

$forbiddenFolders = Get-ChildItem `
    -LiteralPath $publishDirectory `
    -Directory `
    -Recurse `
    -Force | Where-Object { $_.Name -eq ".agents" }

if ($forbiddenFolders) {
    throw "The publish output contains a forbidden .agents folder. Packaging has stopped."
}

# Only first-party binaries are signed. UE4SS and its third-party dependencies remain untouched.
if (-not [string]::IsNullOrWhiteSpace($SignToolPath)) {
    $firstPartyBinaries = @(
        (Join-Path $publishDirectory "Limelight.exe"),
        (Join-Path $publishDirectory "Limelight.dll"),
        (Join-Path $publishDirectory "Payloads\NativeBridge\LimelightNativeBridge.dll")
    ) | Where-Object { Test-Path -LiteralPath $_ }

    foreach ($binary in $firstPartyBinaries) {
        Invoke-LimelightSigning -FilePath $binary
    }
}

& (Join-Path $PSScriptRoot "Generate-InstallerAssets.ps1") `
    -OutputDirectory $installerAssetsDirectory

[System.IO.Directory]::CreateDirectory($outputDirectory) | Out-Null

$compiler = Resolve-InnoCompiler
$scriptPath = Join-Path $PSScriptRoot "Limelight.iss"
$wizardImage = Join-Path $installerAssetsDirectory "LimelightWizard.bmp"
$wizardSmallImage = Join-Path $installerAssetsDirectory "LimelightWizardSmall.bmp"

$compilerArguments = @(
    "/DMyAppVersion=$version",
    "/DPublishDir=$publishDirectory",
    "/DOutputDir=$outputDirectory",
    "/DWizardImagePath=$wizardImage",
    "/DWizardSmallImagePath=$wizardSmallImage"
)

if (-not [string]::IsNullOrWhiteSpace($SignToolPath)) {
    $signCommand = '"' + $SignToolPath + '" sign /sha1 ' +
        $CertificateThumbprint + ' /fd SHA256 /tr "' +
        $TimestampUrl + '" /td SHA256 $f'

    $compilerArguments += "/DEnableSigning=1"
    $compilerArguments += "/Slimelight=$signCommand"
}

$compilerArguments += $scriptPath

& $compiler @compilerArguments
if ($LASTEXITCODE -ne 0) {
    throw "Inno Setup could not compile the Limelight installer."
}

$installerPath = Join-Path $outputDirectory "LimelightSetup-$version.exe"
if (-not (Test-Path -LiteralPath $installerPath)) {
    throw "The installer compiler completed without producing the expected file."
}

$hash = Get-FileHash -LiteralPath $installerPath -Algorithm SHA256
$hashPath = "$installerPath.sha256"
"$($hash.Hash.ToLowerInvariant()) *$([System.IO.Path]::GetFileName($installerPath))" |
    Set-Content -LiteralPath $hashPath -Encoding ascii

Write-Host ""
Write-Host "Limelight Preview installer is ready." -ForegroundColor Cyan
Write-Host $installerPath
Write-Host $hashPath
