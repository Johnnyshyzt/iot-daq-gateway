# Apply a staged upgrade after Host.exe has exited.
# Called as: powershell -File apply-upgrade.ps1 -WaitPid 123 -Install C:\app -Data C:\app\data
param(
  [int]$WaitPid = 0,
  [string]$Install = "",
  [string]$Data = ""
)
$ErrorActionPreference = "Stop"
if (-not $Install -or -not $Data) { throw "Install and Data are required." }
$statePath = Join-Path $Data "upgrade\state.json"
$package = Join-Path $Data "upgrade\package.zip"
if (-not (Test-Path $statePath) -or -not (Test-Path $package)) { exit 0 }
if ($WaitPid -gt 0) {
  $deadline = (Get-Date).AddMinutes(2)
  while ((Get-Process -Id $WaitPid -ErrorAction SilentlyContinue) -and (Get-Date) -lt $deadline) {
    Start-Sleep -Seconds 1
  }
  if (Get-Process -Id $WaitPid -ErrorAction SilentlyContinue) {
    throw "Host is still running. Refusing to replace files."
  }
}
$previous = Join-Path $Data "upgrade\previous"
if (Test-Path $previous) { Remove-Item -Recurse -Force $previous }
New-Item -ItemType Directory -Force -Path $previous | Out-Null
Copy-Item -Recurse -Force (Join-Path $Install "*") $previous
if (Test-Path (Join-Path $previous "data")) { Remove-Item -Recurse -Force (Join-Path $previous "data") }
$state = Get-Content -Raw -Path $statePath | ConvertFrom-Json
$state.phase = "applying"
$state.message = "脚本正在替换程序文件。"
$state | ConvertTo-Json | Set-Content -Path $statePath -Encoding utf8
try {
  Expand-Archive -Path $package -DestinationPath $Install -Force
} catch {
  Copy-Item -Recurse -Force (Join-Path $previous "*") $Install
  throw
}
sc.exe start IotDaqGateway | Out-Null
