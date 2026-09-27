using System.Globalization;
using IotDaq.Licensing;
using IotDaq.Persistence;
using Studio.Host.Config;

namespace Studio.Host.Licensing;

public sealed class LicensingOptions
{
    public int GraceDays { get; set; } = 14;

    public int? CommunityDeviceLimit { get; set; } = 64;

    public int? CommunityPointLimit { get; set; } = 8000;

    public List<string> CommunityFeatures { get; set; } = [];

    public List<string> GatedFeatures { get; set; } = [.. LicenseFeatures.DefaultGated];

    public int ClockDriftToleranceSeconds { get; set; } = 300;

    public string? PublicKeySpki { get; set; }
}

public sealed class LicenseService
{
    private readonly GatewayPersistence _database;
    private readonly LicensingOptions _options;
    private readonly Func<DateTimeOffset> _clock;
    private readonly LicenseGuard _guard;

    public LicenseService(GatewayPersistence database, IConfiguration configuration, string dataDirectory, Func<DateTimeOffset>? clock = null)
    {
        _database = database;
        _options = Read(configuration);
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _guard = new LicenseGuard(database, dataDirectory, _options.ClockDriftToleranceSeconds, _clock);
    }

    public LicensingOptions Options => _options;

    public LicenseView Describe(ConfigStore store)
    {
        var evaluation = Evaluate();
        var (draftDevices, draftPoints) = store.CountUsage("draft");
        var (publishedDevices, publishedPoints) = store.CountUsage("published");
        return ToView(evaluation, draftDevices, draftPoints, publishedDevices, publishedPoints, store.Database.GetSetting(DemoMode.SettingKey) == "1");
    }

    public bool Allows(string feature)
    {
        var token = feature.Trim().ToLowerInvariant();
        if (!_options.GatedFeatures.Any(item => string.Equals(item, token, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        var evaluation = Evaluate();
        return HasFeature(evaluation.CommunityFeatures, token)
            || (evaluation.EntitlementsActive && HasFeature(evaluation.LicenseFeatures, token));
    }

    public string Denial(string feature)
    {
        var evaluation = Evaluate();
        var title = LicenseFeatures.Title(feature);
        return evaluation.State switch
        {
            "grace" => $"{title}在宽限期内仍可使用，但当前许可证没有包含它。采集不会停止。",
            "expired" => $"许可证已过宽限期，{title}已停用。采集仍在继续，配置里已有的设备不会被关掉。请导入新的许可证。",
            "invalid" => $"许可证无效（{evaluation.Detail}），{title}按社区版处理。采集不会停止。",
            _ => $"当前是社区版，未授权{title}。采集不会停止。可在「授权许可」页导入许可证，或调整配置里的功能清单。"
        };
    }

    public string? RejectIncrease(int beforeDevices, int afterDevices, int beforePoints, int afterPoints)
    {
        var evaluation = Evaluate();
        var deviceLimit = evaluation.DeviceLimit;
        var pointLimit = evaluation.PointLimit;
        if (deviceLimit is int devices && afterDevices > devices && afterDevices > beforeDevices)
        {
            return $"设备数将变成 {afterDevices.ToString(CultureInfo.InvariantCulture)}，超过当前授权上限 {devices.ToString(CultureInfo.InvariantCulture)}。采集仍会继续，但不能再增加设备。";
        }

        if (pointLimit is int points && afterPoints > points && afterPoints > beforePoints)
        {
            return $"启用点位将变成 {afterPoints.ToString(CultureInfo.InvariantCulture)}，超过当前授权上限 {points.ToString(CultureInfo.InvariantCulture)}。采集仍会继续，但不能再增加点位。";
        }

        return null;
    }

    public (bool Ok, string Code, string Message) Import(string document, string importedBy)
    {
        using var key = LicenseCrypto.CreatePublic(_options.PublicKeySpki);
        var check = LicenseCodec.Verify(document, key);
        if (!check.Ok || check.Payload is null)
        {
            return (false, check.Code, check.Message);
        }

        var payload = check.Payload;
        _database.SaveInstalledLicense(
            document,
            payload.Customer.Trim(),
            payload.Edition.Trim().ToLowerInvariant(),
            importedBy,
            _clock().ToUnixTimeMilliseconds());
        _guard.Observe(document, allowDocumentChange: true);
        var evaluation = Evaluate();
        return (true, "license_imported", evaluation.Message);
    }

    public void Remove()
    {
        _database.ClearInstalledLicense();
        _guard.Observe(null, allowDocumentChange: true);
    }

    public LicenseGuardResult AcknowledgeTamper()
    {
        var installed = _database.ReadInstalledLicense();
        var result = _guard.Observe(installed?.DocumentText, allowDocumentChange: true, clearTamper: true);
        if (result.Code.Length == 0)
        {
            _database.AppendAudit("system", "admin", "security.ack", "license", "管理员确认并清除了授权状态异常标记。时钟回拨不会因此清除。");
        }

        return result;
    }

    public string? ConfigBlockMessage()
    {
        var evaluation = Evaluate();
        return evaluation.BlocksConfig ? evaluation.TamperMessage : null;
    }

    public LicenseEvaluation Evaluate()
    {
        var now = _clock();
        var installed = _database.ReadInstalledLicense();
        var guard = _guard.Observe(installed?.DocumentText, allowDocumentChange: false);
        if (installed is null || string.IsNullOrWhiteSpace(installed.DocumentText))
        {
            return Community("community", "未导入许可证，按社区版运行。采集不受影响。", null, guard);
        }

        using var key = LicenseCrypto.CreatePublic(_options.PublicKeySpki);
        var check = LicenseCodec.Verify(installed.DocumentText, key);
        if (!check.Ok || check.Payload is null)
        {
            return Community("invalid", "已保存的许可证无法通过校验，已按社区版运行。采集不受影响。", check.Message, guard);
        }

        var payload = check.Payload;
        if (!string.IsNullOrWhiteSpace(payload.MachineFingerprint)
            && !MachineFingerprint.Matches(payload.MachineFingerprint))
        {
            return Community("invalid", "许可证绑定的机器指纹与本机不一致，已按社区版运行。采集不受影响。", "指纹不匹配", guard);
        }

        DateTimeOffset? expires = null;
        if (!string.IsNullOrWhiteSpace(payload.ExpiresAt) && LicenseCodec.TryTime(payload.ExpiresAt, out var expiry))
        {
            expires = expiry;
        }

        DateTimeOffset? issued = null;
        if (LicenseCodec.TryTime(payload.IssuedAt, out var issuedAt))
        {
            issued = issuedAt;
        }

        var graceDays = Math.Clamp(_options.GraceDays, 0, 3650);
        var graceUntil = expires?.AddDays(graceDays);
        string state;
        string message;
        var entitlements = true;
        int? deviceLimit = payload.DeviceLimit;
        int? pointLimit = payload.PointLimit;
        if (expires is null || now <= expires.Value)
        {
            state = "valid";
            message = expires is null
                ? $"许可证有效，客户 {payload.Customer}，无到期日。"
                : $"许可证有效，客户 {payload.Customer}，到期 {Format(expires.Value)}。";
        }
        else if (graceUntil is not null && now <= graceUntil.Value)
        {
            state = "grace";
            message = $"许可证已于 {Format(expires!.Value)} 到期，宽限期至 {Format(graceUntil.Value)}。此期间功能仍可用，请尽快更换。采集不会停止。";
        }
        else
        {
            state = "expired";
            entitlements = false;
            deviceLimit = _options.CommunityDeviceLimit;
            pointLimit = _options.CommunityPointLimit;
            message = $"许可证已过宽限期（到期 {Format(expires!.Value)}）。采集继续，已有配置保持运行；不能再把设备或点位加到社区版上限之上，需授权的功能已停用。";
        }

        return new LicenseEvaluation
        {
            State = state,
            Edition = payload.Edition,
            Customer = payload.Customer,
            Message = message,
            Detail = "",
            DeviceLimit = deviceLimit,
            PointLimit = pointLimit,
            ExpiresAt = expires,
            GraceUntil = graceUntil,
            IssuedAt = issued,
            LicenseFeatures = payload.Features ?? [],
            CommunityFeatures = _options.CommunityFeatures,
            EntitlementsActive = entitlements,
            BoundFingerprint = string.IsNullOrWhiteSpace(payload.MachineFingerprint) ? null : payload.MachineFingerprint.Trim().ToLowerInvariant(),
            TamperCode = guard.Code,
            TamperMessage = guard.Message,
            BlocksConfig = guard.BlocksConfig
        };
    }

    private LicenseEvaluation Community(string state, string message, string? detail, LicenseGuardResult guard) => new()
    {
        State = state,
        Edition = "community",
        Customer = "",
        Message = message,
        Detail = detail ?? "",
        DeviceLimit = _options.CommunityDeviceLimit,
        PointLimit = _options.CommunityPointLimit,
        LicenseFeatures = [],
        CommunityFeatures = _options.CommunityFeatures,
        EntitlementsActive = false,
        TamperCode = guard.Code,
        TamperMessage = guard.Message,
        BlocksConfig = guard.BlocksConfig
    };

    private LicenseView ToView(
        LicenseEvaluation evaluation,
        int draftDevices,
        int draftPoints,
        int publishedDevices,
        int publishedPoints,
        bool demo)
    {
        var entitlements = LicenseFeatures.DefaultGated.ToDictionary(
            feature => feature,
            feature => Allows(feature),
            StringComparer.Ordinal);
        string? banner = guardBanner(evaluation) ?? evaluation.State switch
        {
            "grace" => evaluation.Message,
            "expired" => evaluation.Message,
            "invalid" => evaluation.Message,
            _ => null
        };
        return new LicenseView
        {
            Enforced = true,
            Status = evaluation.State,
            Edition = evaluation.Edition,
            Message = evaluation.Message,
            Customer = evaluation.Customer,
            DeviceLimit = evaluation.DeviceLimit,
            PointLimit = evaluation.PointLimit,
            DraftDevices = draftDevices,
            DraftPoints = draftPoints,
            PublishedDevices = publishedDevices,
            PublishedPoints = publishedPoints,
            ExpiresAt = evaluation.ExpiresAt,
            GraceUntil = evaluation.GraceUntil,
            IssuedAt = evaluation.IssuedAt,
            GraceDays = _options.GraceDays,
            Features = evaluation.EntitlementsActive ? evaluation.LicenseFeatures : evaluation.CommunityFeatures,
            GatedFeatures = _options.GatedFeatures,
            Entitlements = entitlements,
            MachineFingerprint = MachineFingerprint.Current(),
            BoundFingerprint = evaluation.BoundFingerprint,
            Banner = banner,
            CollectionContinues = true,
            DemoMode = demo,
            TamperCode = evaluation.TamperCode,
            TamperMessage = evaluation.TamperMessage,
            BlocksConfig = evaluation.BlocksConfig,
            FingerprintNote = "指纹由系统标识、主机名、主板信息和网卡组成。更换一块网卡仍然有效；换主板或重装系统后需要重新签发。"
        };
    }

    private static string? guardBanner(LicenseEvaluation evaluation) =>
        evaluation.BlocksConfig ? evaluation.TamperMessage : null;

    private static bool HasFeature(IEnumerable<string> features, string token) =>
        features.Any(feature =>
            string.Equals(feature, token, StringComparison.OrdinalIgnoreCase)
            || string.Equals(feature, LicenseFeatures.All, StringComparison.Ordinal));

    private static string Format(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture);

    private static LicensingOptions Read(IConfiguration configuration)
    {
        var section = configuration.GetSection("Licensing");
        var options = new LicensingOptions();
        if (int.TryParse(section["GraceDays"], NumberStyles.Integer, CultureInfo.InvariantCulture, out var grace))
        {
            options.GraceDays = Math.Clamp(grace, 0, 3650);
        }

        options.CommunityDeviceLimit = OptionalInt(section["Community:DeviceLimit"], 64);
        options.CommunityPointLimit = OptionalInt(section["Community:PointLimit"], 8000);
        var communityFeatures = section.GetSection("Community:Features").GetChildren().Select(item => item.Value ?? "").Where(item => item.Length > 0).ToList();
        if (communityFeatures.Count > 0)
        {
            options.CommunityFeatures = communityFeatures;
        }

        var gated = section.GetSection("GatedFeatures").GetChildren().Select(item => item.Value ?? "").Where(item => item.Length > 0).ToList();
        if (gated.Count > 0)
        {
            options.GatedFeatures = gated;
        }

        var spki = section["PublicKeySpki"];
        options.PublicKeySpki = string.IsNullOrWhiteSpace(spki) ? null : spki.Trim();
        if (int.TryParse(section["ClockDriftToleranceSeconds"], NumberStyles.Integer, CultureInfo.InvariantCulture, out var drift))
        {
            options.ClockDriftToleranceSeconds = Math.Clamp(drift, 0, 86_400);
        }

        return options;
    }

    private static int? OptionalInt(string? text, int fallback)
    {
        if (text is null)
        {
            return fallback;
        }

        if (string.Equals(text, "unlimited", StringComparison.OrdinalIgnoreCase) || text.Length == 0)
        {
            return null;
        }

        return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number)
            ? Math.Max(0, number)
            : fallback;
    }
}

public sealed class LicenseEvaluation
{
    public string State { get; init; } = "community";

    public string Edition { get; init; } = "community";

    public string Customer { get; init; } = "";

    public string Message { get; init; } = "";

    public string Detail { get; init; } = "";

    public int? DeviceLimit { get; init; }

    public int? PointLimit { get; init; }

    public DateTimeOffset? ExpiresAt { get; init; }

    public DateTimeOffset? GraceUntil { get; init; }

    public DateTimeOffset? IssuedAt { get; init; }

    public List<string> LicenseFeatures { get; init; } = [];

    public List<string> CommunityFeatures { get; init; } = [];

    public bool EntitlementsActive { get; init; }

    public string? BoundFingerprint { get; init; }

    public string TamperCode { get; init; } = "";

    public string TamperMessage { get; init; } = "";

    public bool BlocksConfig { get; init; }
}

public sealed class LicenseView
{
    public bool Enforced { get; set; }

    public string Status { get; set; } = "community";

    public string Edition { get; set; } = "community";

    public string Message { get; set; } = "";

    public string Customer { get; set; } = "";

    public int? DeviceLimit { get; set; }

    public int? PointLimit { get; set; }

    public int DraftDevices { get; set; }

    public int DraftPoints { get; set; }

    public int PublishedDevices { get; set; }

    public int PublishedPoints { get; set; }

    public DateTimeOffset? ExpiresAt { get; set; }

    public DateTimeOffset? GraceUntil { get; set; }

    public DateTimeOffset? IssuedAt { get; set; }

    public int GraceDays { get; set; }

    public List<string> Features { get; set; } = [];

    public List<string> GatedFeatures { get; set; } = [];

    public Dictionary<string, bool> Entitlements { get; set; } = [];

    public string MachineFingerprint { get; set; } = "";

    public string? BoundFingerprint { get; set; }

    public string? Banner { get; set; }

    public bool CollectionContinues { get; set; } = true;

    public bool DemoMode { get; set; }

    public string TamperCode { get; set; } = "";

    public string TamperMessage { get; set; } = "";

    public bool BlocksConfig { get; set; }

    public string FingerprintNote { get; set; } = "";
}
