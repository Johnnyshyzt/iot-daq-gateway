namespace Studio.Host.Commissioning;

public static class ProtocolHints
{
    public static string For(string? protocol, string? brandId, IReadOnlyDictionary<string, string?>? parameters, string? sdkStatus)
    {
        var kind = (protocol ?? "").Trim().ToLowerInvariant();
        var brand = (brandId ?? "").Trim().ToLowerInvariant();
        var rack = Has(parameters, "rack") || Has(parameters, "slot") || kind is "s7" or "s7comm" || brand.Contains("siemens", StringComparison.Ordinal) || brand.Contains("s7", StringComparison.Ordinal);
        return kind switch
        {
            "focas" => sdkStatus == "missing"
                ? "缺少 FOCAS 库（Windows 上的 Fwlib64.dll）。放到 data/sdk/fanuc 或安装目录，不要在 Linux 容器里找这份库。网关不会编造 SDK 调用。"
                : "FOCAS 常见端口 8193。库已在目录里也不等于已经调用成功；没有公开签名的入口不会被调用。",
            "modbus" => "核对 Modbus 站号 unitId（常见是 1）以及功能码 03 的保持寄存器。默认地址不是台达出厂表，要按现场 PLC 改点位地址。",
            "opcua" => rack
                ? "OPC UA 要核对端点 URL 和证书信任：未受信任的客户端证书不会被自动接受。若实际走的是 S7 而不是 OPC UA，还要核对机架 rack（常见 0）和槽位 slot（S7-300 常见 2，S7-1200/1500 常见 1）。本网关没有单独的 S7 驱动。"
                : "OPC UA 要核对端点 URL、端口和安全策略。把网关客户端证书放进对端信任列表；未受信任的证书不会自动接受。",
            "s7" or "s7comm" => "S7 要核对机架 rack（常见 0）和槽位 slot（S7-300 常见 2，S7-1200/1500 常见 1）。本产品的西门子采集走 OPC UA，不另写 S7 驱动。",
            "mtconnect" => "MTConnect Agent 的地址应能在浏览器打开，常见是 http://主机:端口/current 或 /sample。路径写在连接的 path 里。",
            "lsv2" => "LSV2 先确认海德汉端口和口令。登录失败时核对 INSPECT / DNC 角色，不要把口令写进诊断包。",
            "haas-q" => "哈斯 Q 指令需要机床打开 Setting 143，并允许以太网查询。端口不通时先看网线和该设置。",
            "ftp" => "FTP 只读取状态文件。核对主机、端口、路径和账号。口令不会出现在报文查看里。",
            "sim" or "fake" => "这是模拟器，不连接真实机床，用来确认页面和采集链路。",
            _ when DriverNeedsSdk(kind) => sdkStatus == "missing"
                ? "data/sdk 里没有该品牌的库。找到文件也只做存在性检查，没有核对过的函数不会被调用。"
                : "库文件在目录里。没有可核对的公开函数签名，网关不会调用未知入口。",
            _ => "核对 IP、端口和 VLAN。机床网不要直接接到办公网。"
        };
    }

    private static bool DriverNeedsSdk(string protocol) =>
        protocol is "ezsocket" or "syntec" or "gsk" or "knd" or "hnc" or "baoyuan" or "kede" or "jdsoft" or "mitsubishi" or "brother";

    private static bool Has(IReadOnlyDictionary<string, string?>? parameters, string key) =>
        parameters is not null && parameters.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value);
}
