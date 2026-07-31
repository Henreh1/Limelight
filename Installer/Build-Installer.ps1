[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",

    [ValidateSet("win-x64")]
    [string]$Runtime = "win-x64",

    [switch]$SkipPublish,

    [switch]$SkipInstaller,

    [string]$NativeBridgeBinaryPath,

    [string]$SignToolPath,

    [string]$CertificateThumbprint,

    [string]$TimestampUrl = "http://timestamp.digicert.com"
)

$ErrorActionPreference = "Stop"

# Windows PowerShell does not load the ZIP helper assembly until it is requested.
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

$projectDirectory = Split-Path -Parent $PSScriptRoot
$projectFile = Join-Path $projectDirectory "Limelight.csproj"
$artifactsDirectory = Join-Path $projectDirectory "artifacts"
$stagingDirectory = Join-Path $artifactsDirectory "installer-staging"
$publishDirectory = Join-Path $stagingDirectory "publish"
$portablePublishDirectory = Join-Path $stagingDirectory "portable-publish"
$installerAssetsDirectory = Join-Path $stagingDirectory "installer-assets"
$outputDirectory = Join-Path $artifactsDirectory "installers"
$portableOutputDirectory = Join-Path $artifactsDirectory "portable"
$nativeBridgeOutputDirectory = Join-Path $artifactsDirectory "native-bridge"
$nativeBridgeManifestPath = Join-Path $projectDirectory "Payloads\NativeBridge\bridge-manifest.json"

[xml]$project = Get-Content -LiteralPath $projectFile
$version = [string]$project.Project.PropertyGroup.Version
$nativeBridgeResource =
    $project.Project.ItemGroup.EmbeddedResource |
    Where-Object {
        $_.LogicalName -eq
        "Limelight.Payloads.NativeBridge.LimelightNativeBridge.dll"
    } |
    Select-Object -First 1

if ($null -eq $nativeBridgeResource -or
    [string]::IsNullOrWhiteSpace(
        [string]$nativeBridgeResource.Include)) {
    throw "Limelight.csproj does not contain the embedded native bridge payload."
}

$nativeBridgePayloadPath =
    Join-Path $projectDirectory ([string]$nativeBridgeResource.Include)

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

    & $SignToolPath verify /pa /all $FilePath

    if ($LASTEXITCODE -ne 0) {
        throw "Signature verification failed for $FilePath."
    }
}

function Update-NativeBridgeManifest {
    if (-not (Test-Path -LiteralPath $nativeBridgeManifestPath)) {
        throw "The native bridge manifest could not be found."
    }

    if (-not (Test-Path -LiteralPath $nativeBridgePayloadPath)) {
        throw "The native bridge payload could not be found."
    }

    $manifest =
        Get-Content -LiteralPath $nativeBridgeManifestPath -Raw |
        ConvertFrom-Json
    $payload =
        Get-Item -LiteralPath $nativeBridgePayloadPath
    $payloadHash =
        Get-FileHash -LiteralPath $nativeBridgePayloadPath -Algorithm SHA256

    $manifest.payloadSize = $payload.Length
    $manifest.payloadSha256 = $payloadHash.Hash.ToUpperInvariant()

    $manifest |
        ConvertTo-Json -Depth 8 |
        Set-Content -LiteralPath $nativeBridgeManifestPath -Encoding utf8
}

Assert-SafeWorkspacePath -Path $stagingDirectory

$signingRequested =
    -not [string]::IsNullOrWhiteSpace($SignToolPath) -or
    -not [string]::IsNullOrWhiteSpace($CertificateThumbprint)

if ($signingRequested -and
    ([string]::IsNullOrWhiteSpace($SignToolPath) -or
     [string]::IsNullOrWhiteSpace($CertificateThumbprint))) {
    throw "SignToolPath and CertificateThumbprint must be supplied together."
}

if (-not [string]::IsNullOrWhiteSpace($SignToolPath) -and
    -not (Test-Path -LiteralPath $SignToolPath)) {
    throw "SignToolPath does not point to signtool.exe."
}

if ($SkipPublish -and
    (-not [string]::IsNullOrWhiteSpace($NativeBridgeBinaryPath) -or
     $signingRequested)) {
    throw "SkipPublish cannot be combined with native bridge preparation or signing."
}

if (-not $SkipPublish) {
    if (Test-Path -LiteralPath $stagingDirectory) {
        Remove-Item -LiteralPath $stagingDirectory -Recurse -Force
    }

    [System.IO.Directory]::CreateDirectory($publishDirectory) | Out-Null
    [System.IO.Directory]::CreateDirectory($portablePublishDirectory) | Out-Null

    if (-not [string]::IsNullOrWhiteSpace($NativeBridgeBinaryPath)) {
        if (-not (Test-Path -LiteralPath $NativeBridgeBinaryPath)) {
            throw "NativeBridgeBinaryPath does not point to a built bridge DLL."
        }

        # I copy the freshly built bridge into the payload before Limelight embeds it.
        Copy-Item `
            -LiteralPath $NativeBridgeBinaryPath `
            -Destination $nativeBridgePayloadPath `
            -Force
    }

    if ($signingRequested) {
        # I sign the bridge before publishing so the embedded copy is signed too.
        Invoke-LimelightSigning -FilePath $nativeBridgePayloadPath
    }

    Update-NativeBridgeManifest

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

    # I make a second publish just for the portable download. .NET keeps every
    # framework and language resource inside Limelight.exe, so the tester sees
    # one application instead of a folder full of runtime files.
    dotnet publish $projectFile `
        -c $Configuration `
        -r $Runtime `
        --self-contained true `
        --no-restore `
        -o $portablePublishDirectory `
        /p:PublishSingleFile=true `
        /p:IncludeNativeLibrariesForSelfExtract=true `
        /p:EnableCompressionInSingleFile=true `
        /p:PublishTrimmed=false `
        /p:DebugType=None `
        /p:DebugSymbols=false

    if ($LASTEXITCODE -ne 0) {
        throw "The Limelight single-file publish failed."
    }
}
elseif (-not (Test-Path -LiteralPath (Join-Path $publishDirectory "Limelight.exe")) -or
        -not (Test-Path -LiteralPath (Join-Path $portablePublishDirectory "Limelight.exe"))) {
    throw "SkipPublish was selected, but the prepared installer and portable outputs do not both exist."
}

$forbiddenFolders =
    @($publishDirectory, $portablePublishDirectory) |
    ForEach-Object {
        Get-ChildItem `
            -LiteralPath $_ `
            -Directory `
            -Recurse `
            -Force
    } |
    Where-Object { $_.Name -eq ".agents" }

if ($forbiddenFolders) {
    throw "The publish output contains a forbidden .agents folder. Packaging has stopped."
}

# Only first-party binaries are signed. UE4SS and its third-party dependencies remain untouched.
if ($signingRequested) {
    $firstPartyBinaries = @(
        (Join-Path $publishDirectory "Limelight.exe"),
        (Join-Path $publishDirectory "Limelight.dll"),
        (Join-Path $portablePublishDirectory "Limelight.exe")
    ) | Where-Object { Test-Path -LiteralPath $_ }

    foreach ($binary in $firstPartyBinaries) {
        Invoke-LimelightSigning -FilePath $binary
    }
}

[System.IO.Directory]::CreateDirectory($portableOutputDirectory) | Out-Null

$portablePath =
    Join-Path $portableOutputDirectory "Limelight-$version-$Runtime.zip"

if (Test-Path -LiteralPath $portablePath) {
    Remove-Item -LiteralPath $portablePath -Force
}

# The portable archive deliberately contains one visible file. Limelight.exe
# extracts its bundled native runtime privately when Windows starts it.
$portableExecutable =
    Join-Path $portablePublishDirectory "Limelight.exe"

if (-not (Test-Path -LiteralPath $portableExecutable)) {
    throw "The single-file portable executable could not be found."
}

$portableArchive =
    [System.IO.Compression.ZipFile]::Open(
        $portablePath,
        [System.IO.Compression.ZipArchiveMode]::Create)

try {
    [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
        $portableArchive,
        $portableExecutable,
        "Limelight.exe",
        [System.IO.Compression.CompressionLevel]::Optimal) |
        Out-Null
}
finally {
    $portableArchive.Dispose()
}

$portableHash =
    Get-FileHash -LiteralPath $portablePath -Algorithm SHA256
$portableHashPath =
    "$portablePath.sha256"
"$($portableHash.Hash.ToLowerInvariant()) *$([System.IO.Path]::GetFileName($portablePath))" |
    Set-Content -LiteralPath $portableHashPath -Encoding ascii

[System.IO.Directory]::CreateDirectory($nativeBridgeOutputDirectory) | Out-Null

$nativeBridgeManifest =
    Get-Content -LiteralPath $nativeBridgeManifestPath -Raw |
    ConvertFrom-Json
$nativeBridgeVersion =
    [string]$nativeBridgeManifest.bridgeVersion
$nativeBridgeReleasePath =
    Join-Path `
        $nativeBridgeOutputDirectory `
        "LimelightNativeBridge-$nativeBridgeVersion.dll"

Copy-Item `
    -LiteralPath $nativeBridgePayloadPath `
    -Destination $nativeBridgeReleasePath `
    -Force

$nativeBridgeHash =
    Get-FileHash -LiteralPath $nativeBridgeReleasePath -Algorithm SHA256
$nativeBridgeHashPath =
    "$nativeBridgeReleasePath.sha256"
"$($nativeBridgeHash.Hash.ToLowerInvariant()) *$([System.IO.Path]::GetFileName($nativeBridgeReleasePath))" |
    Set-Content -LiteralPath $nativeBridgeHashPath -Encoding ascii

$installerPath = $null
$hashPath = $null

if (-not $SkipInstaller) {
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

    if ($signingRequested) {
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

    if ($signingRequested) {
        & $SignToolPath verify /pa /all $installerPath

        if ($LASTEXITCODE -ne 0) {
            throw "Signature verification failed for $installerPath."
        }
    }

    $hash = Get-FileHash -LiteralPath $installerPath -Algorithm SHA256
    $hashPath = "$installerPath.sha256"
    "$($hash.Hash.ToLowerInvariant()) *$([System.IO.Path]::GetFileName($installerPath))" |
        Set-Content -LiteralPath $hashPath -Encoding ascii
}

Write-Host ""
if ($signingRequested) {
    Write-Host "Limelight Early Access signed release is ready." -ForegroundColor Cyan
}
else {
    Write-Host "Limelight Early Access unsigned test build is ready." -ForegroundColor Yellow
}

if (-not $SkipInstaller) {
    Write-Host $installerPath
    Write-Host $hashPath
}
Write-Host $portablePath
Write-Host $portableHashPath
Write-Host $nativeBridgeReleasePath
Write-Host $nativeBridgeHashPath
