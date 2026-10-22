$ErrorActionPreference = 'Stop'
Push-Location (Join-Path $PSScriptRoot '..')
try {
    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
        throw 'Please install the .NET 10 SDK and reopen this terminal.'
    }
    dotnet run --project server-dotnet/SydneyBuddy.Api
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
} finally { Pop-Location }
