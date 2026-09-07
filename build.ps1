param(
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '../Afloat'),
    [switch]$SkipPreview
)
$ErrorActionPreference = 'Stop'
$buildScratch = Join-Path $PSScriptRoot '../../work/build-tools'
New-Item -ItemType Directory -Force -Path $buildScratch, $OutputDirectory | Out-Null
$env:DOTNET_CLI_HOME = Join-Path $buildScratch 'dotnet-home'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
$env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH = 'false'
$env:NUGET_PACKAGES = Join-Path $buildScratch 'nuget'
$env:APPDATA = Join-Path $buildScratch 'appdata'
New-Item -ItemType Directory -Force -Path $env:APPDATA | Out-Null
dotnet restore (Join-Path $PSScriptRoot 'Afloat.csproj') --configfile (Join-Path $PSScriptRoot 'NuGet.Config')
if ($LASTEXITCODE -ne 0) { throw 'Restore failed.' }
dotnet build (Join-Path $PSScriptRoot 'Afloat.csproj') -c Release --no-restore -o $OutputDirectory
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
if (-not $SkipPreview) {
    dotnet (Join-Path $OutputDirectory 'Afloat.dll') --render-preview (Join-Path $buildScratch 'preview.png')
    if ($LASTEXITCODE -ne 0) { throw 'Preview failed.' }
} else {
    dotnet (Join-Path $OutputDirectory 'Afloat.dll') --render-icon (Join-Path $buildScratch 'Afloat.ico')
    if ($LASTEXITCODE -ne 0) { throw 'Icon generation failed.' }
}
$compiler = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
& $compiler /nologo /target:winexe /optimize+ "/out:$OutputDirectory/Afloat.exe" "/win32icon:$buildScratch/Afloat.ico" /reference:System.Windows.Forms.dll (Join-Path $PSScriptRoot 'launcher/Launcher.cs')
if ($LASTEXITCODE -ne 0) { throw 'Launcher build failed.' }
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'README.md') -Destination (Join-Path $OutputDirectory '사용 안내.md') -Force
if (Test-Path -LiteralPath (Join-Path $OutputDirectory 'Afloat.pdb')) { Remove-Item -LiteralPath (Join-Path $OutputDirectory 'Afloat.pdb') -Force }
$setupPath = Join-Path (Split-Path $OutputDirectory -Parent) 'Afloat-Setup-1.1.0.exe'
$installerSource = Join-Path $PSScriptRoot 'installer/Installer.cs'
& $compiler /nologo /target:winexe /optimize+ "/out:$setupPath" "/win32icon:$buildScratch/Afloat.ico" /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll "/resource:$buildScratch/Afloat.ico,installer.Afloat.ico" "/resource:$PSScriptRoot/assets/Afloat-logo.png,installer.Afloat-logo.png" "/resource:$OutputDirectory/Afloat.exe,payload.Afloat.exe" "/resource:$OutputDirectory/Afloat.dll,payload.Afloat.dll" "/resource:$OutputDirectory/Afloat.deps.json,payload.Afloat.deps.json" "/resource:$OutputDirectory/Afloat.runtimeconfig.json,payload.Afloat.runtimeconfig.json" "/resource:$OutputDirectory/사용 안내.md,payload.guide.md" $installerSource
if ($LASTEXITCODE -ne 0) { throw 'Installer build failed.' }
Write-Output "Ready: $OutputDirectory/Afloat.exe"
Write-Output "Setup: $setupPath"

