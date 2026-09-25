#Requires -Version 5.1
# Versioned Windows x64 self-contained zip. No Fwlib64.dll is packed.
$ErrorActionPreference = "Stop"

$Root = Resolve-Path (Join-Path $PSScriptRoot "..")
Set-Location $Root

$Configuration = if ($env:CONFIGURATION) { $env:CONFIGURATION } else { "Release" }
$Rid = "win-x64"

if (-not (Test-Path "src/Web/dist/index.html")) {
    Write-Host "Building src/Web ..."
    Push-Location src/Web
    npm ci
    if ($LASTEXITCODE -ne 0) { throw "npm ci failed" }
    npm run build
    if ($LASTEXITCODE -ne 0) { throw "npm run build failed" }
    Pop-Location
}

$Version = (dotnet msbuild src/Host/Host.csproj -nologo -getProperty:Version).Trim()
if (-not $Version) {
    throw "Could not read Version from Host.csproj"
}

$Sha = ""
try { $Sha = (git rev-parse --short HEAD).Trim() } catch { $Sha = "" }
$Informational = if ($Sha) { "$Version+$Sha" } else { $Version }

$Stage = Join-Path $Root "artifacts/$Rid/payload"
$Dist = Join-Path $Root "artifacts/$Rid"
$FolderName = "iot-daq-gateway-$Version-$Rid"
$Zip = Join-Path $Dist "$FolderName.zip"

if (Test-Path $Stage) { Remove-Item -Recurse -Force $Stage }
New-Item -ItemType Directory -Force -Path $Stage, $Dist | Out-Null

Write-Host "Publishing Host $Informational ($Rid self-contained)..."
dotnet publish src/Host/Host.csproj `
  -c $Configuration `
  -r $Rid `
  --self-contained true `
  --nologo `
  -p:PublishSingleFile=false `
  -p:PublishTrimmed=false `
  -p:DebugType=None `
  -p:DebugSymbols=false `
  -p:InformationalVersion=$Informational `
  -o $Stage
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

Copy-Item -Force (Join-Path $Root "configs/examples/gateway.windows.yaml") (Join-Path $Stage "gateway.yaml")
Copy-Item -Force (Join-Path $Root "configs/examples/gateway.yaml") (Join-Path $Stage "gateway.fake.yaml")
Copy-Item -Force (Join-Path $Root "configs/examples/gateway.focas.yaml") (Join-Path $Stage "gateway.focas.yaml")
Remove-Item -Force -ErrorAction SilentlyContinue (Join-Path $Stage "appsettings.Development.json")
Set-Content -Path (Join-Path $Stage "VERSION.txt") -Value $Informational -Encoding ascii

Copy-Item -Force (Join-Path $Root "packaging/windows/install-service.bat") $Stage
Copy-Item -Force (Join-Path $Root "packaging/windows/uninstall-service.bat") $Stage
Copy-Item -Force (Join-Path $Root "packaging/windows/run-console.bat") $Stage
Copy-Item -Force (Join-Path $Root "packaging/windows/service-env.ps1") $Stage
Copy-Item -Force (Join-Path $Root "packaging/windows/service.env.example") $Stage
Copy-Item -Force (Join-Path $Root "packaging/windows/Fwlib64.dll.PLACE_HERE.txt") $Stage
Copy-Item -Force (Join-Path $Root "packaging/windows/安装说明.txt") $Stage
Copy-Item -Force (Join-Path $Root "packaging/windows/appsettings.Field.json") (Join-Path $Stage "appsettings.json")

foreach ($dll in @("Fwlib64.dll", "fwlib64.dll")) {
    if (Test-Path (Join-Path $Stage $dll)) {
        throw "Refusing to pack vendor FOCAS binaries."
    }
}

$exe = Join-Path $Stage "Host.exe"
if (-not (Test-Path $exe)) {
    throw "Publish did not produce Host.exe"
}
if (-not (Test-Path (Join-Path $Stage "wwwroot/index.html"))) {
    throw "Publish did not include wwwroot/index.html. Build src/Web first."
}
if (-not (Test-Path (Join-Path $Stage "data/seed/gateway.yaml"))) {
    throw "Publish did not include data/seed/gateway.yaml."
}
if (-not (Test-Path (Join-Path $Stage "service.env.example"))) {
    throw "Stage is missing service.env.example."
}
$fieldSettings = Get-Content -LiteralPath (Join-Path $Stage "appsettings.json") -Raw
if ($fieldSettings -notmatch '"AccountMode"\s*:\s*"field"') {
    throw "Field appsettings.json is missing AccountMode=field."
}
if ($fieldSettings -match '"Password"') {
    throw "Field appsettings.json still contains demo passwords."
}

$Folder = Join-Path $Dist $FolderName
if (Test-Path $Folder) { Remove-Item -Recurse -Force $Folder }
New-Item -ItemType Directory -Force -Path $Folder | Out-Null
Copy-Item -Recurse -Force (Join-Path $Stage "*") $Folder

if (Test-Path $Zip) { Remove-Item -Force $Zip }
Compress-Archive -Path $Folder -DestinationPath $Zip -Force
Remove-Item -Recurse -Force $Folder

Write-Host "Wrote $Zip"
