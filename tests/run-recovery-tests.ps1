$ErrorActionPreference = 'Stop'
$compilerPath = 'C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\Roslyn\csc.exe'
$testBuildDir = Join-Path $env:TEMP ('olama-agent-tests-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $testBuildDir | Out-Null
& $compilerPath /nologo /target:exe "/out:$testBuildDir\FakeKdeCli.exe" tests/FakeKdeCli.cs
if ($LASTEXITCODE -ne 0) { throw 'Fake CLI build failed' }
& $compilerPath /nologo /target:exe /main:RecoveryTests "/out:$testBuildDir\RecoveryTests.exe" /r:System.dll,System.Core.dll,System.Windows.Forms.dll,System.Drawing.dll,System.Net.Http.dll,System.Web.Extensions.dll,System.Security.dll *.cs tests/RecoveryTests.cs
if ($LASTEXITCODE -ne 0) { throw 'Recovery test build failed' }
& "$testBuildDir\RecoveryTests.exe" "$testBuildDir\FakeKdeCli.exe"
if ($LASTEXITCODE -ne 0) { throw 'Recovery tests failed' }
