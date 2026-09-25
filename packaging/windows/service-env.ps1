# Injects service.env into the current process (Mode run) or the Windows service
# Environment registry value (Mode service). Prints variable names only, never values.
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('service', 'run')]
    [string]$Mode,
    [string]$ServiceName = 'IotDaqGateway'
)

$ErrorActionPreference = 'Stop'
$InstallDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$EnvFile = Join-Path $InstallDir 'service.env'

function Read-ServiceEnvPairs {
    param([string]$Path)
    $pairs = New-Object System.Collections.Generic.List[string]
    if (-not (Test-Path -LiteralPath $Path)) {
        return ,$pairs
    }

    $utf8 = New-Object System.Text.UTF8Encoding $false
    foreach ($line in [System.IO.File]::ReadAllLines($Path, $utf8)) {
        $trim = $line.Trim()
        if ($trim.Length -eq 0 -or $trim.StartsWith('#')) {
            continue
        }

        $eq = $trim.IndexOf('=')
        if ($eq -lt 1) {
            throw "service.env 行格式应为 NAME=VALUE: $trim"
        }

        $name = $trim.Substring(0, $eq).Trim()
        $value = $trim.Substring($eq + 1)
        if ($name -notmatch '^[A-Za-z_][A-Za-z0-9_]*$') {
            throw "非法环境变量名: $name"
        }

        if ($value.Length -eq 0) {
            continue
        }

        $pairs.Add("$name=$value") | Out-Null
    }

    return ,$pairs
}

function Get-PairNames {
    param($Pairs)
    $names = New-Object System.Collections.Generic.List[string]
    foreach ($pair in $Pairs) {
        $names.Add($pair.Substring(0, $pair.IndexOf('='))) | Out-Null
    }

    return ,$names
}

if (-not (Test-Path -LiteralPath $EnvFile)) {
    Write-Host "[警告] 未找到 service.env。MQTT 账号不会注入。"
    Write-Host "        请复制 service.env.example 为 service.env，填写 MQTT_USER 和 MQTT_PASSWORD 后重新运行。"
}

$pairs = Read-ServiceEnvPairs -Path $EnvFile
$names = Get-PairNames -Pairs $pairs
$hasAccountMode = $false
foreach ($name in $names) {
    if ($name -eq 'STUDIO_ACCOUNT_MODE') {
        $hasAccountMode = $true
    }
}

if (-not $hasAccountMode) {
    $pairs.Add('STUDIO_ACCOUNT_MODE=field') | Out-Null
    $names.Add('STUDIO_ACCOUNT_MODE') | Out-Null
}

$hasUser = $false
$hasPassword = $false
foreach ($name in $names) {
    if ($name -eq 'MQTT_USER') { $hasUser = $true }
    if ($name -eq 'MQTT_PASSWORD') { $hasPassword = $true }
}

if (-not $hasUser -or -not $hasPassword) {
    Write-Host "[警告] 未同时注入 MQTT_USER 与 MQTT_PASSWORD。需要认证的 Broker 将无法登录。密码不要写进 YAML。"
}

if ($Mode -eq 'run') {
    Set-Location -LiteralPath $InstallDir
    foreach ($pair in $pairs) {
        $eq = $pair.IndexOf('=')
        Set-Item -LiteralPath ("Env:" + $pair.Substring(0, $eq)) -Value $pair.Substring($eq + 1)
    }

    $exe = Join-Path $InstallDir 'Host.exe'
    if (-not (Test-Path -LiteralPath $exe)) {
        Write-Host "[错误] 未找到 Host.exe"
        exit 1
    }

    Write-Host "前台运行 Host。页面 http://127.0.0.1:5080  日志写入 logs\ 。按 Ctrl+C 结束。"
    Write-Host ("已加载环境变量（只显示名称）: " + ($names -join ', '))
    & $exe
    if ($null -ne $LASTEXITCODE) {
        exit $LASTEXITCODE
    }

    exit 0
}

$path = "HKLM:\SYSTEM\CurrentControlSet\Services\$ServiceName"
if (-not (Test-Path -LiteralPath $path)) {
    throw "服务尚未注册: $ServiceName。请先 sc create，再写入环境变量。"
}

New-ItemProperty -Path $path -Name Environment -PropertyType MultiString -Value $pairs.ToArray() -Force | Out-Null
Write-Host ("[完成] 已写入服务环境变量（只显示名称）: " + ($names -join ', '))
Write-Host "        修改 service.env 后请重新以管理员运行 install-service.bat。"
exit 0
