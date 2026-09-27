using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Studio.Contracts;

namespace Studio.Host.Auth;

public sealed class AccountStore
{
    public const string DemoMode = "demo";
    public const string FieldMode = "field";
    public const string AccountModeEnvironmentVariable = "STUDIO_ACCOUNT_MODE";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private static readonly (string Username, string Role, string DemoPassword)[] Seeds =
    [
        ("admin", "admin", "admin"),
        ("engineer", "engineer", "engineer"),
        ("operator", "operator", "operator"),
        ("viewer", "viewer", "viewer")
    ];

    private readonly object _gate = new();
    private readonly string _accountsPath;
    private readonly string _signingKeyPath;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AccountStore> _logger;
    private bool _ready;
    private string _configuredMode = DemoMode;
    private string _fileMode = DemoMode;
    private string _signingKey = TokenService.DevSigningKey;
    private List<AccountRecord> _users = [];

    public AccountStore(string dataDirectory, IConfiguration configuration, ILogger<AccountStore> logger)
    {
        _configuration = configuration;
        _logger = logger;
        var authDirectory = Path.Combine(dataDirectory, "auth");
        Directory.CreateDirectory(authDirectory);
        _accountsPath = Path.Combine(authDirectory, "accounts.json");
        BootstrapPasswordPath = Path.Combine(authDirectory, "bootstrap-password.txt");
        _signingKeyPath = Path.Combine(authDirectory, "signing.key");
    }

    public string BootstrapPasswordPath { get; }

    /// <summary>
    /// Per host. A static callback is shared by every gateway in the process, so one
    /// host's login would run schema work on another host's database.
    /// </summary>
    public Action<string>? AccountsChanged { get; set; }

    public string Mode
    {
        get
        {
            EnsureInitialized();
            return _fileMode;
        }
    }

    public bool ModeMismatch { get; private set; }

    public string SigningKey
    {
        get
        {
            EnsureInitialized();
            return _signingKey;
        }
    }

    public void EnsureInitialized()
    {
        lock (_gate)
        {
            if (_ready)
            {
                return;
            }

            _configuredMode = ReadConfiguredMode(_configuration);
            if (File.Exists(_accountsPath))
            {
                Load();
            }
            else if (_configuredMode == FieldMode)
            {
                CreateFieldAccounts();
            }
            else
            {
                CreateDemoAccounts();
            }

            _signingKey = ResolveSigningKey();
            _ready = true;
            if (ModeMismatch)
            {
                _logger.LogWarning(
                    "Account file mode is {FileMode} but Studio:AccountMode / {Env} is {Configured}. Delete {Path} to recreate accounts.",
                    _fileMode,
                    AccountModeEnvironmentVariable,
                    _configuredMode,
                    _accountsPath);
            }
        }
    }

    public AuthPosture Posture()
    {
        lock (_gate)
        {
            EnsureInitialized();
            return new AuthPosture
            {
                Mode = _fileMode,
                Message = PostureMessage()
            };
        }
    }

    public AuthAttempt Authenticate(string? username, string? password)
    {
        if (string.IsNullOrWhiteSpace(username) || password is null || username.Contains('\n', StringComparison.Ordinal))
        {
            return AuthAttempt.Fail("invalid_credentials", "用户名或密码错误");
        }

        lock (_gate)
        {
            EnsureInitialized();
            var user = Find(username);
            if (user is null)
            {
                return AuthAttempt.Fail("invalid_credentials", "用户名或密码错误");
            }

            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            if (user.LockedUntilUnixMs > now)
            {
                return AuthAttempt.Fail("locked", "登录失败次数过多，账号已暂时锁定。请稍后再试。");
            }

            if (!PasswordHasher.Verify(password, user.PasswordHash))
            {
                user.FailedAttempts++;
                if (user.FailedAttempts >= LockoutThreshold)
                {
                    user.LockedUntilUnixMs = DateTimeOffset.UtcNow.AddMinutes(LockoutMinutes).ToUnixTimeMilliseconds();
                    user.FailedAttempts = 0;
                    Save();
                    return AuthAttempt.Fail("locked", "登录失败次数过多，账号已暂时锁定。请稍后再试。");
                }

                Save();
                return AuthAttempt.Fail("invalid_credentials", "用户名或密码错误");
            }

            user.FailedAttempts = 0;
            user.LockedUntilUnixMs = 0;
            Save();
            return AuthAttempt.Success(user.Role);
        }
    }

    public bool TryAuthenticate(string? username, string? password, out string role)
    {
        var attempt = Authenticate(username, password);
        role = attempt.Role;
        return attempt.Ok;
    }

    public bool UsesDemoPassword(string? username)
    {
        if (string.IsNullOrWhiteSpace(username))
        {
            return false;
        }

        lock (_gate)
        {
            EnsureInitialized();
            var user = Find(username);
            if (user is null)
            {
                return false;
            }

            return PasswordHasher.Verify("admin", user.PasswordHash)
                || PasswordHasher.Verify("engineer", user.PasswordHash)
                || PasswordHasher.Verify("operator", user.PasswordHash)
                || PasswordHasher.Verify("viewer", user.PasswordHash);
        }
    }

    public bool MustChangePassword(string? username)
    {
        if (string.IsNullOrWhiteSpace(username))
        {
            return false;
        }

        lock (_gate)
        {
            EnsureInitialized();
            return Find(username)?.MustChangePassword == true;
        }
    }

    public bool TryChangePassword(string? username, string? currentPassword, string? newPassword, out string error)
    {
        error = "";
        if (string.IsNullOrWhiteSpace(username))
        {
            error = "未登录，不能修改密码。";
            return false;
        }

        if (PasswordPolicy.Check(newPassword, username) is { } policy)
        {
            error = policy;
            return false;
        }

        lock (_gate)
        {
            EnsureInitialized();
            var user = Find(username);
            if (user is null || currentPassword is null || !PasswordHasher.Verify(currentPassword, user.PasswordHash))
            {
                error = "当前密码不正确。";
                return false;
            }

            if (PasswordHasher.Verify(newPassword!, user.PasswordHash))
            {
                error = "新密码不能与当前密码相同。";
                return false;
            }

            user.MustChangePassword = false;
            user.PasswordHash = PasswordHasher.Hash(newPassword!);
            Save();
            DropBootstrapLine(user.Username);
            _logger.LogInformation("Password changed for {Username}", user.Username);
            return true;
        }
    }

    public IReadOnlyList<UserInfo> ListUsers()
    {
        lock (_gate)
        {
            EnsureInitialized();
            return _users.Select(user => new UserInfo
            {
                Username = user.Username,
                Role = user.Role,
                MustChangePassword = user.MustChangePassword
            }).ToList();
        }
    }

    private void Load()
    {
        AccountFile? file;
        var migrated = false;
        try
        {
            file = JsonSerializer.Deserialize<AccountFile>(File.ReadAllText(_accountsPath), Json);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"账号文件无法读取：{_accountsPath}", ex);
        }

        if (file?.Users is null || file.Users.Count == 0)
        {
            throw new InvalidOperationException($"账号文件无效：{_accountsPath}");
        }

        foreach (var user in file.Users)
        {
            if (string.Equals(user.Username?.Trim(), "admin", StringComparison.OrdinalIgnoreCase)
                && !StudioRoles.IsKnown(user.Role))
            {
                user.Role = StudioRoles.Admin;
                migrated = true;
            }

            if (string.IsNullOrWhiteSpace(user.Username)
                || user.Username.Contains('\n', StringComparison.Ordinal)
                || !StudioRoles.IsKnown(user.Role)
                || string.IsNullOrWhiteSpace(user.PasswordHash))
            {
                throw new InvalidOperationException($"账号文件无效：{_accountsPath}");
            }
        }

        _fileMode = file.Mode == FieldMode ? FieldMode : DemoMode;
        _users = file.Users;
        ModeMismatch = !string.Equals(_fileMode, _configuredMode, StringComparison.Ordinal);
        if (migrated)
        {
            Save();
        }
    }

    private void CreateDemoAccounts()
    {
        var configured = _configuration.GetSection("Studio:Users").Get<List<Studio.Host.StudioUser>>() ?? [];
        List<(string Username, string Role, string Password)> seeds;
        if (configured.Count == 0)
        {
            seeds = Seeds.Select(seed => (seed.Username, seed.Role, seed.DemoPassword)).ToList();
        }
        else
        {
            seeds = configured
                .Where(user => StudioRoles.IsKnown(user.Role) && !string.IsNullOrWhiteSpace(user.Username))
                .Select(user => (user.Username.Trim(), user.Role, user.Password ?? ""))
                .ToList();
        }

        if (seeds.Count == 0)
        {
            seeds = Seeds.Select(seed => (seed.Username, seed.Role, seed.DemoPassword)).ToList();
        }

        _fileMode = DemoMode;
        _users = seeds.Select(seed => new AccountRecord
        {
            Username = seed.Username,
            Role = seed.Role,
            PasswordHash = PasswordHasher.Hash(seed.Password),
            MustChangePassword = false
        }).ToList();
        ModeMismatch = _configuredMode == FieldMode;
        Save();
        _logger.LogInformation("Created demo accounts in {Path}. These passwords are for localhost only.", _accountsPath);
    }

    private void CreateFieldAccounts()
    {
        var lines = new List<string>
        {
            "# 现场一次性登录口令。打开 http://127.0.0.1:5080 登录后必须修改。",
            "# 全部账号改密后，本文件会删除。不要使用 admin/admin 作为长期密码。",
            "# 角色仍是本机的 admin / engineer / operator / viewer。operator 与 viewer 只读。"
        };
        _users = [];
        foreach (var seed in Seeds)
        {
            var password = CreatePassword();
            _users.Add(new AccountRecord
            {
                Username = seed.Username,
                Role = seed.Role,
                PasswordHash = PasswordHasher.Hash(password),
                MustChangePassword = true
            });
            lines.Add($"{seed.Username}={password}");
        }

        _fileMode = FieldMode;
        ModeMismatch = false;
        Save();
        WriteText(BootstrapPasswordPath, string.Join(Environment.NewLine, lines) + Environment.NewLine);
        _logger.LogWarning(
            "Field accounts created. One-time passwords are in {Path}. Change them at first login. Default admin/admin is not accepted.",
            BootstrapPasswordPath);
    }

    private string ResolveSigningKey()
    {
        var configured = _configuration["Studio:SigningKey"];
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured.Trim();
        }

        if (_fileMode != FieldMode)
        {
            return TokenService.DevSigningKey;
        }

        if (File.Exists(_signingKeyPath))
        {
            var existing = File.ReadAllText(_signingKeyPath).Trim();
            if (existing.Length >= 16)
            {
                return existing;
            }
        }

        var key = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        WriteText(_signingKeyPath, key + Environment.NewLine);
        _logger.LogInformation("Generated a local signing key at {Path}", _signingKeyPath);
        return key;
    }

    private string PostureMessage()
    {
        if (ModeMismatch && _configuredMode == FieldMode && _fileMode == DemoMode)
        {
            return "数据目录里的账号文件是演示模式创建的，仍可能接受 admin/admin。现场交付请停止服务，删除 data\\auth 后重新启动，以生成一次性引导密码。";
        }

        if (_fileMode == FieldMode)
        {
            if (_users.Any(user => user.MustChangePassword))
            {
                return "现场模式。默认 admin/admin 不能登录。请打开 " + BootstrapPasswordPath
                    + " 查看一次性密码，登录后立即修改。角色是本机的 admin、engineer、operator、viewer。operator 与 viewer 只能查看。";
            }

            return "现场模式。一次性引导密码已失效。请使用修改后的本地账号登录。忘记密码时，停止服务并删除 data\\auth 后重启，会重新生成引导密码。";
        }

        return "本机演示模式。账号 admin / admin、engineer / engineer、operator / operator、viewer / viewer 只适合 localhost 开发。现场安装包不会使用这些默认口令；交到客户机器前必须修改密码。";
    }

    private void DropBootstrapLine(string username)
    {
        if (!File.Exists(BootstrapPasswordPath))
        {
            return;
        }

        if (_users.All(user => !user.MustChangePassword))
        {
            File.Delete(BootstrapPasswordPath);
            _logger.LogInformation("Removed one-time password file {Path}", BootstrapPasswordPath);
            return;
        }

        var prefix = username + "=";
        var kept = File.ReadAllLines(BootstrapPasswordPath)
            .Where(line => !line.Trim().StartsWith(prefix, StringComparison.Ordinal))
            .ToArray();
        WriteText(BootstrapPasswordPath, string.Join(Environment.NewLine, kept) + Environment.NewLine);
    }

    public bool TryCreate(string? username, string? role, string? password, out string error)
    {
        error = "";
        username = username?.Trim() ?? "";
        role = role?.Trim() ?? "";
        if (!IsUserName(username))
        {
            error = "用户名只能包含字母、数字、下划线和连字符，长度 1 到 32。";
            return false;
        }

        if (!StudioRoles.IsKnown(role))
        {
            error = "角色只能是 admin、engineer、operator 或 viewer。";
            return false;
        }

        if (PasswordPolicy.Check(password, username) is { } policy)
        {
            error = policy;
            return false;
        }

        lock (_gate)
        {
            EnsureInitialized();
            if (Find(username) is not null)
            {
                error = "用户名已存在。";
                return false;
            }

            _users.Add(new AccountRecord
            {
                Username = username,
                Role = role,
                PasswordHash = PasswordHasher.Hash(password!),
                MustChangePassword = false
            });
            Save();
            return true;
        }
    }

    public bool TryUpdate(string? username, string? role, string? password, out string error)
    {
        error = "";
        username = username?.Trim() ?? "";
        lock (_gate)
        {
            EnsureInitialized();
            var user = Find(username);
            if (user is null)
            {
                error = "用户不存在。";
                return false;
            }

            if (!string.IsNullOrWhiteSpace(role))
            {
                if (!StudioRoles.IsKnown(role))
                {
                    error = "角色只能是 admin、engineer、operator 或 viewer。";
                    return false;
                }

                if (user.Role == StudioRoles.Admin && role != StudioRoles.Admin && !HasAnotherAdmin(user.Username))
                {
                    error = "不能取消最后一个管理员。";
                    return false;
                }

                user.Role = role;
            }

            if (!string.IsNullOrEmpty(password))
            {
                if (PasswordPolicy.Check(password, user.Username) is { } policy)
                {
                    error = policy;
                    return false;
                }

                user.PasswordHash = PasswordHasher.Hash(password);
                user.MustChangePassword = false;
            }

            Save();
            return true;
        }
    }

    public bool TryDelete(string? username, out string error)
    {
        error = "";
        username = username?.Trim() ?? "";
        lock (_gate)
        {
            EnsureInitialized();
            var user = Find(username);
            if (user is null)
            {
                error = "用户不存在。";
                return false;
            }

            if (user.Role == StudioRoles.Admin && !HasAnotherAdmin(user.Username))
            {
                error = "不能删除最后一个管理员。";
                return false;
            }

            _users.Remove(user);
            Save();
            return true;
        }
    }

    private bool HasAnotherAdmin(string username) =>
        _users.Any(user => user.Role == StudioRoles.Admin && !string.Equals(user.Username, username, StringComparison.Ordinal));

    private int LockoutThreshold =>
        int.TryParse(_configuration["Studio:LockoutThreshold"], System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var value)
            ? Math.Clamp(value, 1, 50)
            : 5;

    private int LockoutMinutes =>
        int.TryParse(_configuration["Studio:LockoutMinutes"], System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var value)
            ? Math.Clamp(value, 1, 1440)
            : 15;

    private static bool IsUserName(string username) =>
        username.Length is >= 1 and <= 32 && username.All(ch => char.IsAsciiLetterOrDigit(ch) || ch is '_' or '-');

    private AccountRecord? Find(string username) =>
        _users.FirstOrDefault(user => string.Equals(user.Username, username, StringComparison.Ordinal));

    private void Save()
    {
        var file = new AccountFile
        {
            Mode = _fileMode,
            Users = _users
        };
        WriteText(_accountsPath, JsonSerializer.Serialize(file, Json) + Environment.NewLine);
        AccountsChanged?.Invoke(_accountsPath);
    }

    private static void WriteText(string path, string contents)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temporary = path + ".tmp";
        File.WriteAllText(temporary, contents, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        File.Move(temporary, path, overwrite: true);
    }

    private static string ReadConfiguredMode(IConfiguration configuration)
    {
        var raw = Environment.GetEnvironmentVariable(AccountModeEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(raw))
        {
            raw = configuration["Studio:AccountMode"];
        }

        return string.Equals(raw?.Trim(), FieldMode, StringComparison.OrdinalIgnoreCase) ? FieldMode : DemoMode;
    }

    private static bool IsDemoPassword(string password) => PasswordPolicy.IsDemo(password);

    private static string CreatePassword()
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789";
        var bytes = RandomNumberGenerator.GetBytes(20);
        var chars = new char[20];
        for (var i = 0; i < chars.Length; i++)
        {
            chars[i] = alphabet[bytes[i] % alphabet.Length];
        }

        return new string(chars);
    }

    private sealed class AccountFile
    {
        public string Mode { get; set; } = DemoMode;

        public List<AccountRecord> Users { get; set; } = [];
    }

    private sealed class AccountRecord
    {
        public string Username { get; set; } = "";

        public string Role { get; set; } = "";

        public string PasswordHash { get; set; } = "";

        public bool MustChangePassword { get; set; }

        public int FailedAttempts { get; set; }

        public long LockedUntilUnixMs { get; set; }
    }
}

public sealed class AuthAttempt
{
    public bool Ok { get; init; }

    public string Role { get; init; } = "";

    public string Code { get; init; } = "";

    public string Message { get; init; } = "";

    public static AuthAttempt Success(string role) => new() { Ok = true, Role = role };

    public static AuthAttempt Fail(string code, string message) => new() { Code = code, Message = message };
}

internal static class PasswordHasher
{
    private const int Iterations = 100_000;

    public static string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, 32);
        return $"pbkdf2-sha256${Iterations.ToString(System.Globalization.CultureInfo.InvariantCulture)}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    public static bool Verify(string password, string stored)
    {
        var parts = stored.Split('$');
        if (parts.Length != 4 || !string.Equals(parts[0], "pbkdf2-sha256", StringComparison.Ordinal))
        {
            return false;
        }

        if (!int.TryParse(parts[1], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var iterations)
            || iterations is < 1 or > 5_000_000)
        {
            return false;
        }

        byte[] salt;
        byte[] expected;
        try
        {
            salt = Convert.FromBase64String(parts[2]);
            expected = Convert.FromBase64String(parts[3]);
        }
        catch (FormatException)
        {
            return false;
        }

        var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }
}
