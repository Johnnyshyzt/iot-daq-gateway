; Inno Setup script for the Windows x64 field package.
; Build on a Windows runner after scripts/pack-win-x64.ps1:
;   ISCC.exe packaging\windows\iot-daq-gateway.iss
; The compiler is not required to build or test the product on Linux.

#define AppVersion "0.8.0"
#define Payload "..\..\artifacts\win-x64\payload"

[Setup]
AppId={{8F3A1C2E-6B47-4D19-9C5A-7E2D4B91A0F6}
AppName=采集网关
AppVersion={#AppVersion}
AppPublisher=iot-daq-gateway
DefaultDirName={autopf}\IoT DAQ Gateway
DefaultGroupName=采集网关
DisableProgramGroupPage=yes
OutputDir=..\..\artifacts\win-x64
OutputBaseFilename=iot-daq-gateway-{#AppVersion}-win-x64-setup
Compression=lzma2
SolidCompression=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
WizardStyle=modern

[Languages]
Name: "chinesesimplified"; MessagesFile: "compiler:Languages\ChineseSimplified.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Files]
Source: "{#Payload}\*"; DestDir: "{app}"; Flags: recursesubdirs ignoreversion

[Icons]
Name: "{group}\采集网关"; Filename: "http://127.0.0.1:5080/setup"
Name: "{commondesktop}\采集网关"; Filename: "http://127.0.0.1:5080/setup"

[Run]
Filename: "{sys}\sc.exe"; Parameters: "stop IotDaqGateway"; Flags: runhidden; StatusMsg: "停止旧服务…"
Filename: "{sys}\sc.exe"; Parameters: "delete IotDaqGateway"; Flags: runhidden
Filename: "{sys}\sc.exe"; Parameters: "create IotDaqGateway binPath= ""{app}\Host.exe"" start= auto DisplayName= ""IoT DAQ Gateway"""; Flags: runhidden; StatusMsg: "安装 Windows 服务…"
Filename: "{sys}\sc.exe"; Parameters: "description IotDaqGateway ""内网 CNC 采集网关。浏览器打开 http://127.0.0.1:5080 。"""; Flags: runhidden
Filename: "{sys}\sc.exe"; Parameters: "failure IotDaqGateway reset= 86400 actions= restart/5000/restart/10000/restart/30000"; Flags: runhidden
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall add rule name=""IotDaqGateway"" dir=in action=allow protocol=TCP localport=5080"; Flags: runhidden; StatusMsg: "放行 TCP 5080…"
Filename: "{sys}\sc.exe"; Parameters: "start IotDaqGateway"; Flags: runhidden waituntilterminated; StatusMsg: "启动服务…"
Filename: "http://127.0.0.1:5080/setup"; Description: "打开上手引导"; Flags: shellexec nowait postinstall skipifsilent
