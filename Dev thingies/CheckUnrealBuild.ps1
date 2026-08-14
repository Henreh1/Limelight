$exe = "D:\SteamLibrary\steamapps\common\Dead as Disco\Pagoda.exe"

if (-not (Test-Path -LiteralPath $exe)) {
    Write-Host "Executable not found:" -ForegroundColor Red
    Write-Host $exe
    Read-Host "Press Enter to close"
    exit
}

$file = Get-Item -LiteralPath $exe

Write-Host ""
Write-Host "Executable information" -ForegroundColor Cyan
Write-Host "----------------------"
Write-Host "I am the best! L was here!" -ForegroundColor Cyan

$file.VersionInfo |
    Format-List FileVersion, ProductVersion, FileDescription, ProductName

Read-Host "Press Enter to close"