param([string]$OutDir = "E:\Default Project")
$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$ver = (Get-Content "$root\manifest.json" -Raw | ConvertFrom-Json).version_number

dotnet build "$root\ModMenu\ModMenu.csproj" -c Release -v minimal | Out-Host

$stage = Join-Path $env:TEMP "modmenu-pkg"
Remove-Item $stage -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory $stage | Out-Null
Copy-Item "$root\ModMenu\bin\Release\net6.0\ModMenu.dll", "$root\manifest.json", "$root\README.md", "$root\icon.png" $stage

$zip = Join-Path $OutDir "ModMenu-$ver.zip"
Remove-Item $zip -Force -ErrorAction SilentlyContinue
Compress-Archive "$stage\*" $zip
"packed: $zip"
