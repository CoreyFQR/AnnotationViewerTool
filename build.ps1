param([switch]$Test)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
if (-not (Test-Path -LiteralPath $compiler)) { throw 'Install .NET Framework 4.8 before building.' }
$publish = Join-Path $root 'publish'
New-Item -ItemType Directory -Force -Path $publish | Out-Null
$versionSource = Get-Content -LiteralPath (Join-Path $root 'src\AppInfo.cs') -Raw
$version = [regex]::Match($versionSource, 'const string Version = "([^"]+)"').Groups[1].Value
if (-not $version) { throw 'Missing application version.' }
$icon = Join-Path $root 'assets\AnnotationViewer.ico'
if (-not (Test-Path -LiteralPath $icon)) { & (Join-Path $root 'tools\build-icon.ps1') }
$sources = @(Get-ChildItem -LiteralPath (Join-Path $root 'src') -Recurse -Filter '*.cs' | Sort-Object FullName | ForEach-Object FullName)
$common = @('/nologo', '/codepage:65001', '/optimize+', '/warn:4', '/warnaserror+',
    '/reference:System.dll', '/reference:System.Core.dll', '/reference:System.Drawing.dll',
    '/reference:System.Windows.Forms.dll', '/reference:System.Web.Extensions.dll',
    ('/win32icon:' + $icon), ('/win32manifest:' + (Join-Path $root 'app.manifest')),
    ('/resource:' + (Join-Path $root 'assets\logo.png') + ',MedVision.logo.png'))
$exe = Join-Path $publish 'AnnotationViewer.exe'
& $compiler @common /target:winexe "/out:$exe" @sources
if ($LASTEXITCODE -ne 0) { throw 'Application compilation failed.' }
Copy-Item -LiteralPath (Join-Path $root 'App.config') -Destination ($exe + '.config') -Force
$versioned = Join-Path $publish "AnnotationViewerV$version.exe"
Copy-Item -LiteralPath $exe -Destination $versioned -Force
Copy-Item -LiteralPath ($exe + '.config') -Destination ($versioned + '.config') -Force
if ($Test) {
    $testExe = Join-Path $publish 'AnnotationViewer.Tests.exe'
    $tests = @(Get-ChildItem -LiteralPath (Join-Path $root 'tests') -Filter '*.cs' | ForEach-Object FullName)
    & $compiler @common /target:exe /main:MedVision.AnnotationViewer.Tests "/out:$testExe" @sources @tests
    if ($LASTEXITCODE -ne 0) { throw 'Test compilation failed.' }
    & $testExe (Join-Path $root 'qa')
    if ($LASTEXITCODE -ne 0) { throw 'Regression tests failed.' }
}
Write-Host "Built $versioned"
