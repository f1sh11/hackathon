$ErrorActionPreference = 'Stop'
Push-Location (Join-Path $PSScriptRoot '..')
try { python server/app.py } finally { Pop-Location }
