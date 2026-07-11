$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$publish = Join-Path $root "publish"
$source = Join-Path $root "Program.cs"
$exe = Join-Path $publish "AnnotationViewer.exe"
$version = "1.6.0"
$versionedExe = Join-Path $publish "AnnotationViewerV$version.exe"
$icon = Join-Path $root "AnnotationViewer.ico"
$compiler = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"

if (-not (Test-Path $compiler)) {
    $compiler = "$env:WINDIR\Microsoft.NET\Framework\v4.0.30319\csc.exe"
}

if (-not (Test-Path $compiler)) {
    throw "Could not find .NET Framework C# compiler csc.exe"
}

if (-not (Test-Path $icon)) {
    throw "Could not find application icon: $icon"
}

New-Item -ItemType Directory -Force -Path $publish | Out-Null

& $compiler `
    /nologo `
    /codepage:65001 `
    /target:winexe `
    /out:$exe `
    /win32icon:$icon `
    /reference:System.dll `
    /reference:System.Core.dll `
    /reference:System.Drawing.dll `
    /reference:System.Windows.Forms.dll `
    /reference:System.Web.Extensions.dll `
    $source

if ($LASTEXITCODE -ne 0) {
    throw "Build failed with exit code: $LASTEXITCODE"
}

Copy-Item -LiteralPath $icon -Destination (Join-Path $publish "AnnotationViewer.ico") -Force
Copy-Item -LiteralPath $exe -Destination $versionedExe -Force
Write-Host "Built: $exe"
Write-Host "Built: $versionedExe"
