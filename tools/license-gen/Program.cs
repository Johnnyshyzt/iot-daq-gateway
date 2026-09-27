using System.Globalization;
using System.Security.Cryptography;
using IotDaq.Licensing;

var argsList = args.ToList();
if (argsList.Count == 0 || Has(argsList, "--help") || Has(argsList, "-h"))
{
    Console.WriteLine("""
        tools/license-gen — 签发离线许可证（ECDSA P-256 / ES256）

        私钥不要放进仓库。用环境变量 LICENSE_PRIVATE_KEY 或 --key 指向 PEM（PKCS#8）文件。

          --key <path>            私钥路径。也可用环境变量 LICENSE_PRIVATE_KEY
          --customer <name>       客户名称
          --edition community|commercial
          --devices <n>           设备上限。省略表示不限制
          --points <n>            点位上限。省略表示不限制
          --expires <utc>         到期时间，如 2027-12-31T00:00:00Z。省略表示不过期
          --feature <id>          可重复。例如 opcua、http-push、alarm-notifications、scheduled-reports、query-api、rules、oee、tool-life、nc-programs、central，或 *
          --fingerprint <hex>     可选机器指纹。省略表示不绑定
          --issued <utc>          签发时间，默认现在
          --out <file>            输出路径，默认标准输出
          --generate-keypair <dir>  在该目录生成测试用密钥（私钥文件不要提交）

        公钥默认内嵌在 Host 的 ProductKeys 中。换钥时只把公钥写进源码，私钥留在签发机上。
        """);
    return Has(argsList, "--help") || Has(argsList, "-h") ? 0 : 1;
}

if (Take(argsList, "--generate-keypair") is { } directory)
{
    var (publicSpki, privatePem) = LicenseCrypto.Generate();
    Directory.CreateDirectory(directory);
    var privatePath = Path.Combine(directory, "license-private.pem");
    var publicPath = Path.Combine(directory, "license-public.spki");
    File.WriteAllText(privatePath, privatePem);
    File.WriteAllText(publicPath, publicSpki + Environment.NewLine);
    Console.WriteLine("public " + publicSpki);
    Console.WriteLine("private " + privatePath);
    Console.WriteLine("不要把私钥提交进 git。");
    return 0;
}

var keyPath = Take(argsList, "--key")
    ?? Environment.GetEnvironmentVariable("LICENSE_PRIVATE_KEY");
if (string.IsNullOrWhiteSpace(keyPath) || !File.Exists(keyPath))
{
    Console.Error.WriteLine("需要 --key 或环境变量 LICENSE_PRIVATE_KEY 指向私钥文件。");
    return 2;
}

var customer = Take(argsList, "--customer");
var edition = (Take(argsList, "--edition") ?? "commercial").Trim().ToLowerInvariant();
if (string.IsNullOrWhiteSpace(customer))
{
    Console.Error.WriteLine("需要 --customer。");
    return 2;
}

int? devices = ParseOptional(Take(argsList, "--devices"));
int? points = ParseOptional(Take(argsList, "--points"));
var features = new List<string>();
while (Take(argsList, "--feature") is { } feature)
{
    features.Add(feature);
}

var payload = new LicensePayload
{
    V = 1,
    Customer = customer.Trim(),
    Edition = edition,
    DeviceLimit = devices,
    PointLimit = points,
    ExpiresAt = Take(argsList, "--expires"),
    Features = features,
    MachineFingerprint = Take(argsList, "--fingerprint"),
    IssuedAt = Take(argsList, "--issued")
        ?? DateTimeOffset.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture)
};

var fieldError = LicenseCodec.Validate(payload);
if (fieldError is not null)
{
    Console.Error.WriteLine(fieldError);
    return 2;
}

string document;
using (var key = LicenseCrypto.CreatePrivate(File.ReadAllText(keyPath)))
{
    document = LicenseCodec.Sign(payload, key);
}

var outPath = Take(argsList, "--out");
if (string.IsNullOrWhiteSpace(outPath))
{
    Console.WriteLine(document);
}
else
{
    File.WriteAllText(outPath, document);
    Console.WriteLine("wrote " + outPath);
}

return 0;

static bool Has(List<string> list, string name) =>
    list.Any(item => string.Equals(item, name, StringComparison.Ordinal));

static string? Take(List<string> list, string name)
{
    var index = list.FindIndex(item => string.Equals(item, name, StringComparison.Ordinal));
    if (index < 0 || index + 1 >= list.Count)
    {
        return null;
    }

    var value = list[index + 1];
    list.RemoveAt(index + 1);
    list.RemoveAt(index);
    return value;
}

static int? ParseOptional(string? text)
{
    if (string.IsNullOrWhiteSpace(text))
    {
        return null;
    }

    if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) || number < 0)
    {
        throw new InvalidOperationException("数量必须是非负整数：" + text);
    }

    return number;
}
