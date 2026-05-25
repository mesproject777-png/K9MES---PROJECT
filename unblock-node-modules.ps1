$ErrorActionPreference = "SilentlyContinue"

Set-Location -LiteralPath $PSScriptRoot

# If the project was downloaded as a zip, Windows may block executables inside node_modules
# (Mark-of-the-Web). Angular uses esbuild which needs to execute a native binary.
Get-ChildItem -LiteralPath ".\\node_modules" -Recurse -File -Include *.exe,*.dll,*.node |
  ForEach-Object { Unblock-File -LiteralPath $_.FullName }

Write-Host "Unblocked executables under node_modules."

