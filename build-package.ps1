$ErrorActionPreference = "Stop"
$cscPath = "C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\Roslyn\csc.exe"
if (!(Test-Path $cscPath)) {
    Write-Error "Roslyn compiler csc.exe not found at $cscPath"
    exit 1
}

if (!(Test-Path "bin\Release")) {
    New-Item -ItemType Directory -Force -Path "bin\Release" | Out-Null
}

# Run Roslyn compilation to a temporary filename to bypass file locks on the running executable
$outputExe = "bin\Release\OlamaMessageAgent_build_new.exe"
if (Test-Path $outputExe) { Remove-Item $outputExe -Force }

& $cscPath /target:winexe /out:$outputExe /r:System.dll,System.Windows.Forms.dll,System.Drawing.dll,System.Net.Http.dll,System.Web.Extensions.dll,System.Security.dll *.cs

if (!(Test-Path $outputExe)) {
    Write-Error "Compilation failed"
    exit 1
}

# Create source package
$sourceZip = "..\olama-message-agent-windows-source.zip"
if (Test-Path $sourceZip) { Remove-Item $sourceZip -Force }
git archive --format=zip --output=$sourceZip HEAD

# Create release package
$releaseZip = "..\olama-message-agent-windows-release.zip"
if (Test-Path $releaseZip) { Remove-Item $releaseZip -Force }

$staging = "$env:TEMP\OlamaStaging"
if (Test-Path $staging) { Remove-Item $staging -Recurse -Force }
New-Item -ItemType Directory -Path $staging | Out-Null

# Copy and rename built binary to standard name in staging area
Copy-Item $outputExe -Destination "$staging\OlamaMessageAgent.exe"

if (Test-Path "App.config") {
    Copy-Item "App.config" -Destination "$staging\OlamaMessageAgent.exe.config"
}
if (Test-Path "README.md") {
    Copy-Item "README.md" -Destination "$staging\README.txt"
}

Compress-Archive -Path "$staging\*" -DestinationPath $releaseZip -Force
Remove-Item $staging -Recurse -Force

Write-Host "Build and packaging completed successfully!" -ForegroundColor Green
