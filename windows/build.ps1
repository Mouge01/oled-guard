param([switch]$Test, [switch]$HeadlessTest)
$ErrorActionPreference = 'Stop'
$oledCompiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (!(Test-Path -LiteralPath $oledCompiler)) { throw '.NET Framework 4.x C# derleyicisi bulunamadı.' }
$oledSources = @(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'src') -Filter '*.cs' | ForEach-Object FullName)
$oledExe = Join-Path $PSScriptRoot 'OLEDGuard.exe'
& $oledCompiler /nologo /target:winexe /platform:anycpu /optimize+ /utf8output /warn:4 "/out:$oledExe" "/win32manifest:$PSScriptRoot\app.manifest" "/resource:$PSScriptRoot\..\common\translations.json,translations.json" /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Management.dll /reference:System.Runtime.Serialization.dll @oledSources
if ($LASTEXITCODE -ne 0) { throw 'Derleme başarısız.' }
Write-Output "Hazır: $oledExe"
if ($Test) {
    $oledTests = Join-Path $PSScriptRoot 'OLEDGuard.Tests.exe'
    & $oledCompiler /nologo /target:exe /platform:anycpu /utf8output "/out:$oledTests" "/reference:$oledExe" /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Runtime.Serialization.dll (Join-Path $PSScriptRoot 'tests\Tests.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Testler derlenemedi.' }
    if ($HeadlessTest) { & $oledTests --headless } else { & $oledTests }
    if ($LASTEXITCODE -ne 0) { throw 'Test başarısız.' }
}
