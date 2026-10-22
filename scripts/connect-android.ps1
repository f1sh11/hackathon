param([string]$Serial)
$ErrorActionPreference = 'Stop'
$taskAdb = Get-Command adb -ErrorAction SilentlyContinue
if ($taskAdb) { $taskAdbPath = $taskAdb.Source }
elseif ($env:ANDROID_HOME) { $taskAdbPath = Join-Path $env:ANDROID_HOME 'platform-tools/adb.exe' }
else { $taskAdbPath = Join-Path $env:LOCALAPPDATA 'Android/Sdk/platform-tools/adb.exe' }
if (-not (Test-Path -LiteralPath $taskAdbPath)) { throw 'ADB not found. Install Android SDK Platform-Tools or set ANDROID_HOME.' }
$taskArgs = @()
if ($Serial) { $taskArgs += @('-s', $Serial) }
& $taskAdbPath @taskArgs reverse tcp:8787 tcp:8787
if ($LASTEXITCODE -ne 0) { throw 'Port forwarding failed. Start one emulator, or provide -Serial.' }
Write-Host 'Sydney Buddy can now connect to the computer backend at http://127.0.0.1:8787.'
