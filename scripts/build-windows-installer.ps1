[CmdletBinding()]
param(
    [string]$Version = '0.1.0',
    [string]$Runtime = 'win-x64',
    [string]$Configuration = 'Release',
    [string]$DotNetPath = 'dotnet',
    [string]$InnoCompilerPath = '',
    [switch]$NoRestore
)

$ErrorActionPreference = 'Stop'

$repositoryRoot = [IO.Path]::GetFullPath(
    (Join-Path $PSScriptRoot '..'))
$artifactRoot = [IO.Path]::GetFullPath(
    (Join-Path $repositoryRoot 'artifacts\windows'))
$publishDirectory = [IO.Path]::GetFullPath(
    (Join-Path $artifactRoot "publish\$Runtime"))
$installerDirectory = [IO.Path]::GetFullPath(
    (Join-Path $artifactRoot 'installer'))
$iconPath = [IO.Path]::GetFullPath(
    (Join-Path $artifactRoot 'HarmonyPcTouchpad.ico'))
$projectPath = Join-Path $repositoryRoot `
    'apps\windows-agent\src\HarmonyPcTouchpad.Agent.App\HarmonyPcTouchpad.Agent.App.csproj'
$installerScript = Join-Path $repositoryRoot `
    'packaging\windows\installer.iss'

function Reset-ArtifactDirectory([string]$Path) {
    $fullPath = [IO.Path]::GetFullPath($Path)
    $allowedPrefix = $artifactRoot.TrimEnd(
        [IO.Path]::DirectorySeparatorChar) +
        [IO.Path]::DirectorySeparatorChar
    if (-not $fullPath.StartsWith(
        $allowedPrefix,
        [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to reset a path outside $artifactRoot"
    }

    if (Test-Path -LiteralPath $fullPath) {
        Remove-Item -LiteralPath $fullPath -Recurse -Force
    }
    New-Item -ItemType Directory -Path $fullPath -Force | Out-Null
}

function New-TouchpadIcon([string]$Path) {
    Add-Type -AssemblyName System.Drawing
    $bitmap = [Drawing.Bitmap]::new(
        256,
        256,
        [Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.Clear([Drawing.Color]::Transparent)

    $background = [Drawing.Drawing2D.GraphicsPath]::new()
    $background.AddArc(0, 0, 112, 112, 180, 90)
    $background.AddArc(144, 0, 112, 112, 270, 90)
    $background.AddArc(144, 144, 112, 112, 0, 90)
    $background.AddArc(0, 144, 112, 112, 90, 90)
    $background.CloseFigure()
    $blue = [Drawing.SolidBrush]::new(
        [Drawing.Color]::FromArgb(37, 99, 235))
    $graphics.FillPath($blue, $background)

    $touchpad = [Drawing.Drawing2D.GraphicsPath]::new()
    $touchpad.AddArc(58, 48, 56, 56, 180, 90)
    $touchpad.AddArc(142, 48, 56, 56, 270, 90)
    $touchpad.AddArc(142, 152, 56, 56, 0, 90)
    $touchpad.AddArc(58, 152, 56, 56, 90, 90)
    $touchpad.CloseFigure()
    $whitePen = [Drawing.Pen]::new([Drawing.Color]::White, 16)
    $whitePen.StartCap = [Drawing.Drawing2D.LineCap]::Round
    $whitePen.EndCap = [Drawing.Drawing2D.LineCap]::Round
    $graphics.DrawPath($whitePen, $touchpad)
    $graphics.DrawLine($whitePen, 128, 50, 128, 100)
    $whiteBrush = [Drawing.SolidBrush]::new([Drawing.Color]::White)
    $graphics.FillEllipse($whiteBrush, 110, 122, 36, 36)

    $pngStream = [IO.MemoryStream]::new()
    $bitmap.Save($pngStream, [Drawing.Imaging.ImageFormat]::Png)
    $pngBytes = $pngStream.ToArray()
    $fileStream = [IO.File]::Create($Path)
    $writer = [IO.BinaryWriter]::new($fileStream)
    try {
        $writer.Write([UInt16]0)
        $writer.Write([UInt16]1)
        $writer.Write([UInt16]1)
        $writer.Write([byte]0)
        $writer.Write([byte]0)
        $writer.Write([byte]0)
        $writer.Write([byte]0)
        $writer.Write([UInt16]1)
        $writer.Write([UInt16]32)
        $writer.Write([UInt32]$pngBytes.Length)
        $writer.Write([UInt32]22)
        $writer.Write($pngBytes)
    }
    finally {
        $writer.Dispose()
        $pngStream.Dispose()
        $whiteBrush.Dispose()
        $whitePen.Dispose()
        $touchpad.Dispose()
        $blue.Dispose()
        $background.Dispose()
        $graphics.Dispose()
        $bitmap.Dispose()
    }
}

function Resolve-InnoCompiler([string]$RequestedPath) {
    if ($RequestedPath.Length -gt 0) {
        return (Resolve-Path -LiteralPath $RequestedPath).Path
    }

    $command = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($null -ne $command) {
        return $command.Source
    }

    $candidates = @(
        (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'),
        (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
        (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe')
    )
    foreach ($candidate in $candidates) {
        if (Test-Path -LiteralPath $candidate) {
            return $candidate
        }
    }

    throw 'Inno Setup 6 compiler was not found. Install JRSoftware.InnoSetup.'
}

Reset-ArtifactDirectory $publishDirectory
Reset-ArtifactDirectory $installerDirectory
New-Item -ItemType Directory -Path $artifactRoot -Force | Out-Null
New-TouchpadIcon $iconPath

$dotnet = (Get-Command $DotNetPath -ErrorAction Stop).Source
if (-not $NoRestore) {
    & $dotnet restore $projectPath `
        --runtime $Runtime `
        --disable-parallel
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet restore failed with exit code $LASTEXITCODE"
    }
}

& $dotnet publish $projectPath `
    --configuration $Configuration `
    --runtime $Runtime `
    --self-contained true `
    --no-restore `
    --output $publishDirectory `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:PublishTrimmed=false `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    "-p:ApplicationIcon=$iconPath" `
    "-p:Version=$Version"
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

$applicationPath = Join-Path $publishDirectory 'HarmonyPcTouchpad.exe'
if (-not (Test-Path -LiteralPath $applicationPath)) {
    throw "Published application was not found at $applicationPath"
}

$innoCompiler = Resolve-InnoCompiler $InnoCompilerPath
& $innoCompiler `
    "/DAppVersion=$Version" `
    "/DSourceDir=$publishDirectory" `
    "/DOutputDir=$installerDirectory" `
    "/DSetupIconFile=$iconPath" `
    $installerScript
if ($LASTEXITCODE -ne 0) {
    throw "Inno Setup failed with exit code $LASTEXITCODE"
}

$installerPath = Join-Path $installerDirectory `
    "HarmonyPcTouchpad-Setup-$Version.exe"
if (-not (Test-Path -LiteralPath $installerPath)) {
    throw "Installer was not found at $installerPath"
}

Write-Output $installerPath
