using System.Globalization;
using System.Text;
using Gateway.Abstractions.Security;
using Opc.Ua;
using Opc.Ua.Configuration;
using Opc.Ua.Server;

namespace Sinks.OpcUa;

public sealed class OpcUaServerOptions
{
    public int Port { get; set; } = 48400;

    public bool AllowAnonymous { get; set; } = true;

    public bool AllowNone { get; set; } = true;

    public bool AllowSignAndEncrypt { get; set; } = true;

    public string Username { get; set; } = "";

    public string PasswordHash { get; set; } = "";

    public string PkiDirectory { get; set; } = "";

    public string ApplicationUri { get; set; } = "urn:iot-daq-gateway:opcua";
}

public sealed class OpcUaPointSnapshot
{
    public string Id { get; set; } = "";

    public string? Value { get; set; }

    public double? Numeric { get; set; }

    public string Quality { get; set; } = "good";

    public long TimestampUnixMs { get; set; }

    public string DataType { get; set; } = "";

    public bool Computed { get; set; }
}

public sealed class OpcUaDeviceSnapshot
{
    public string Id { get; set; } = "";

    public string DisplayName { get; set; } = "";

    public string Workshop { get; set; } = "";

    public string Line { get; set; } = "";

    public string Status { get; set; } = "";

    public string StatusMessage { get; set; } = "";

    public bool AlarmActive { get; set; }

    public List<OpcUaPointSnapshot> Points { get; set; } = [];
}

public sealed class OpcUaSnapshot
{
    public List<OpcUaDeviceSnapshot> Devices { get; set; } = [];
}

/// <summary>
/// In-process OPC UA server. Address space is Objects / workshop / line / device.
/// </summary>
public sealed class OpcUaNorthbound : IAsyncDisposable
{
    public const string NamespaceUri = "urn:iot-daq-gateway:nodes";

    private static readonly ITelemetryContext Telemetry = DefaultTelemetry.Create(static _ => { });

    private readonly OpcUaServerOptions _options;
    private readonly DaqServer _server;
    private readonly ApplicationInstance _application;

    private OpcUaNorthbound(OpcUaServerOptions options, DaqServer server, ApplicationInstance application)
    {
        _options = options;
        _server = server;
        _application = application;
    }

    public string EndpointUrl => "opc.tcp://127.0.0.1:" + _options.Port.ToString(CultureInfo.InvariantCulture) + "/iot-daq-gateway";

    public static async Task<OpcUaNorthbound> StartAsync(OpcUaServerOptions options, CancellationToken cancellationToken)
    {
        if (options.Port is < 1 or > 65535)
        {
            throw new InvalidOperationException("OPC UA 端口无效");
        }

        if (!options.AllowNone && !options.AllowSignAndEncrypt)
        {
            throw new InvalidOperationException("至少启用一种 OPC UA 安全策略");
        }

        var pki = string.IsNullOrWhiteSpace(options.PkiDirectory)
            ? Path.Combine(Path.GetTempPath(), "iot-daq-opcua", Guid.NewGuid().ToString("N"))
            : options.PkiDirectory;
        Directory.CreateDirectory(pki);
        var configuration = BuildConfiguration(options, pki);
        await configuration.ValidateAsync(ApplicationType.Server, cancellationToken).ConfigureAwait(false);
        var application = new ApplicationInstance(Telemetry)
        {
            ApplicationName = "IoT DAQ Gateway",
            ApplicationType = ApplicationType.Server,
            ApplicationConfiguration = configuration
        };
        Directory.CreateDirectory(Path.Combine(pki, "own"));
        var haveCert = await application.CheckApplicationInstanceCertificatesAsync(false, null, cancellationToken).ConfigureAwait(false);
        if (!haveCert)
        {
            throw new InvalidOperationException("OPC UA 应用证书没有生成。请检查数据目录是否可写。");
        }

        var server = new DaqServer(options);
        await application.StartAsync(server).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return new OpcUaNorthbound(options, server, application);
    }

    public void Update(OpcUaSnapshot snapshot)
    {
        _server.Nodes?.Apply(snapshot);
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await _server.StopAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Stopping an already stopped server is not fatal.
        }

        await _application.StopAsync().ConfigureAwait(false);
    }

    private static ApplicationConfiguration BuildConfiguration(OpcUaServerOptions options, string pki)
    {
        var policies = new ServerSecurityPolicyCollection();
        if (options.AllowNone)
        {
            policies.Add(new ServerSecurityPolicy
            {
                SecurityMode = MessageSecurityMode.None,
                SecurityPolicyUri = SecurityPolicies.None
            });
        }

        if (options.AllowSignAndEncrypt)
        {
            policies.Add(new ServerSecurityPolicy
            {
                SecurityMode = MessageSecurityMode.SignAndEncrypt,
                SecurityPolicyUri = SecurityPolicies.Basic256Sha256
            });
        }

        var tokens = new UserTokenPolicyCollection();
        if (options.AllowAnonymous)
        {
            tokens.Add(new UserTokenPolicy(UserTokenType.Anonymous));
        }

        if (!string.IsNullOrEmpty(options.Username))
        {
            tokens.Add(new UserTokenPolicy(UserTokenType.UserName));
        }

        if (tokens.Count == 0)
        {
            tokens.Add(new UserTokenPolicy(UserTokenType.Anonymous));
        }

        return new ApplicationConfiguration
        {
            ApplicationName = "IoT DAQ Gateway",
            ApplicationUri = string.IsNullOrWhiteSpace(options.ApplicationUri) ? "urn:iot-daq-gateway:opcua" : options.ApplicationUri,
            ApplicationType = ApplicationType.Server,
            SecurityConfiguration = new SecurityConfiguration
            {
                ApplicationCertificate = new CertificateIdentifier
                {
                    StoreType = CertificateStoreType.Directory,
                    StorePath = Path.Combine(pki, "own"),
                    SubjectName = "CN=IoT DAQ Gateway, O=iot-daq-gateway, DC=localhost"
                },
                TrustedIssuerCertificates = new CertificateTrustList
                {
                    StoreType = CertificateStoreType.Directory,
                    StorePath = Path.Combine(pki, "issuer")
                },
                TrustedPeerCertificates = new CertificateTrustList
                {
                    StoreType = CertificateStoreType.Directory,
                    StorePath = Path.Combine(pki, "trusted")
                },
                RejectedCertificateStore = new CertificateTrustList
                {
                    StoreType = CertificateStoreType.Directory,
                    StorePath = Path.Combine(pki, "rejected")
                },
                AutoAcceptUntrustedCertificates = false,
                AddAppCertToTrustedStore = true,
                RejectSHA1SignedCertificates = false,
                MinimumCertificateKeySize = 2048
            },
            TransportQuotas = new TransportQuotas
            {
                OperationTimeout = 15000,
                MaxStringLength = 1_048_576,
                MaxByteStringLength = 1_048_576,
                MaxArrayLength = 65_535,
                MaxMessageSize = 4_194_304,
                MaxBufferSize = 65_535,
                ChannelLifetime = 300_000,
                SecurityTokenLifetime = 3_600_000
            },
            ServerConfiguration = new ServerConfiguration
            {
                BaseAddresses = { "opc.tcp://0.0.0.0:" + options.Port.ToString(CultureInfo.InvariantCulture) + "/iot-daq-gateway" },
                MinRequestThreadCount = 2,
                MaxRequestThreadCount = 8,
                MaxQueuedRequestCount = 32,
                SecurityPolicies = policies,
                UserTokenPolicies = tokens
            },
            DisableHiResClock = true
        };
    }

    private sealed class DaqServer : StandardServer
    {
        private readonly OpcUaServerOptions _options;

        public DaqServer(OpcUaServerOptions options)
        {
            _options = options;
        }

        public DaqNodeManager? Nodes { get; private set; }

        /// <summary>
        /// The product runs with invariant globalization, so the stack's en-US status text cannot be loaded.
        /// </summary>
        protected override ResourceManager CreateResourceManager(IServerInternal server, ApplicationConfiguration configuration) =>
            new(configuration);

        protected override MasterNodeManager CreateMasterNodeManager(IServerInternal server, ApplicationConfiguration configuration)
        {
            Nodes = new DaqNodeManager(server, configuration);
            return new MasterNodeManager(server, configuration, null, Nodes);
        }

        protected override void OnServerStarted(IServerInternal server)
        {
            base.OnServerStarted(server);
            server.SessionManager.ImpersonateUser += (_, args) =>
            {
                if (args.NewIdentity is UserNameIdentityToken username)
                {
                    var name = username.UserName ?? "";
                    var password = username.DecryptedPassword is { Length: > 0 } bytes
                        ? Encoding.UTF8.GetString(bytes)
                        : "";
                    if (!string.Equals(name, _options.Username, StringComparison.Ordinal)
                        || string.IsNullOrEmpty(_options.PasswordHash)
                        || !SecretHash.VerifyPbkdf2(password, _options.PasswordHash))
                    {
                        throw new ServiceResultException(StatusCodes.BadUserAccessDenied);
                    }

                    args.Identity = new UserIdentity(username);
                    args.Identity.GrantedRoleIds.Add(ObjectIds.WellKnownRole_AuthenticatedUser);
                    return;
                }

                if (!_options.AllowAnonymous)
                {
                    throw new ServiceResultException(StatusCodes.BadIdentityTokenRejected);
                }

                args.Identity = new UserIdentity();
                args.Identity.GrantedRoleIds.Add(ObjectIds.WellKnownRole_Anonymous);
            };
        }
    }
}

internal sealed class DaqNodeManager : CustomNodeManager2
{
    private FolderState? _root;
    private readonly Dictionary<string, BaseDataVariableState> _variables = new(StringComparer.Ordinal);
    private string _structure = "";

    public DaqNodeManager(IServerInternal server, ApplicationConfiguration configuration)
        : base(server, configuration, OpcUaNorthbound.NamespaceUri)
    {
    }

    public override void CreateAddressSpace(IDictionary<NodeId, IList<IReference>> externalReferences)
    {
        lock (Lock)
        {
            _root = new FolderState(null)
            {
                SymbolicName = "Daq",
                ReferenceTypeId = ReferenceTypeIds.Organizes,
                TypeDefinitionId = ObjectTypeIds.FolderType,
                NodeId = new NodeId("daq", NamespaceIndexes[0]),
                BrowseName = new QualifiedName("daq", NamespaceIndexes[0]),
                DisplayName = new LocalizedText("采集网关"),
                EventNotifier = EventNotifiers.None
            };
            if (!externalReferences.TryGetValue(ObjectIds.ObjectsFolder, out var references))
            {
                references = new List<IReference>();
                externalReferences[ObjectIds.ObjectsFolder] = references;
            }

            references.Add(new NodeStateReference(ReferenceTypes.Organizes, false, _root.NodeId));
            _root.AddReference(ReferenceTypes.Organizes, true, ObjectIds.ObjectsFolder);
            AddPredefinedNode(SystemContext, _root);
        }
    }

    public void Apply(OpcUaSnapshot snapshot)
    {
        lock (Lock)
        {
            if (_root is null)
            {
                return;
            }

            var signature = string.Join("|", snapshot.Devices
                .OrderBy(device => device.Id, StringComparer.Ordinal)
                .Select(device => device.Workshop + "/" + device.Line + "/" + device.Id + ":" + string.Join(",", device.Points.Select(point => point.Id + point.DataType))));
            if (!string.Equals(signature, _structure, StringComparison.Ordinal))
            {
                EnsureStructure(snapshot);
                _structure = signature;
            }

            foreach (var device in snapshot.Devices)
            {
                var prefix = PathOf(device);
                Set(_variables.GetValueOrDefault(prefix + "/status"), device.Status, "string", "good", DateTime.UtcNow);
                Set(_variables.GetValueOrDefault(prefix + "/alarmActive"), device.AlarmActive, "bool", "good", DateTime.UtcNow);
                foreach (var point in device.Points)
                {
                    var when = point.TimestampUnixMs <= 0
                        ? DateTime.UtcNow
                        : DateTimeOffset.FromUnixTimeMilliseconds(point.TimestampUnixMs).UtcDateTime;
                    Set(_variables.GetValueOrDefault(prefix + "/points/" + Sanitize(point.Id)), Coerce(point), point.DataType, point.Quality, when);
                }
            }
        }
    }

    private void EnsureStructure(OpcUaSnapshot snapshot)
    {
        foreach (var device in snapshot.Devices)
        {
            var workshopName = string.IsNullOrWhiteSpace(device.Workshop) ? "未分组" : device.Workshop.Trim();
            var lineName = string.IsNullOrWhiteSpace(device.Line) ? "未分线" : device.Line.Trim();
            var workshop = EnsureFolder(_root!, "w/" + Sanitize(workshopName), workshopName);
            var line = EnsureFolder(workshop, "l/" + Sanitize(workshopName) + "/" + Sanitize(lineName), lineName);
            var folder = EnsureFolder(line, "d/" + Sanitize(device.Id), string.IsNullOrWhiteSpace(device.DisplayName) ? device.Id : device.DisplayName);
            var prefix = PathOf(device);
            EnsureVariable(folder, prefix + "/status", "状态", DataTypeIds.String);
            EnsureVariable(folder, prefix + "/alarmActive", "报警中", DataTypeIds.Boolean);
            var points = EnsureFolder(folder, prefix + "/points", "点位");
            foreach (var point in device.Points)
            {
                var id = Sanitize(point.Id);
                var (type, _) = Typed(point);
                EnsureVariable(points, prefix + "/points/" + id, point.Computed ? point.Id + "（计算）" : point.Id, type);
            }
        }
    }

    private static string PathOf(OpcUaDeviceSnapshot device)
    {
        var workshop = string.IsNullOrWhiteSpace(device.Workshop) ? "未分组" : device.Workshop.Trim();
        var line = string.IsNullOrWhiteSpace(device.Line) ? "未分线" : device.Line.Trim();
        return Sanitize(workshop) + "/" + Sanitize(line) + "/" + Sanitize(device.Id);
    }

    private FolderState EnsureFolder(NodeState parent, string path, string display)
    {
        var existing = parent.FindChildBySymbolicName(SystemContext, path) as FolderState;
        if (existing is not null)
        {
            return existing;
        }

        var folder = new FolderState(parent)
        {
            SymbolicName = path,
            ReferenceTypeId = ReferenceTypeIds.Organizes,
            TypeDefinitionId = ObjectTypeIds.FolderType,
            NodeId = new NodeId(path, NamespaceIndexes[0]),
            BrowseName = new QualifiedName(display, NamespaceIndexes[0]),
            DisplayName = new LocalizedText(display),
            EventNotifier = EventNotifiers.None
        };
        parent.AddChild(folder);
        AddPredefinedNode(SystemContext, folder);
        return folder;
    }

    private void EnsureVariable(NodeState parent, string path, string display, NodeId dataType)
    {
        if (_variables.ContainsKey(path))
        {
            return;
        }

        _variables[path] = AddVariable(parent, path, display, dataType);
    }

    private BaseDataVariableState AddVariable(NodeState parent, string path, string display, NodeId dataType)
    {
        var variable = new BaseDataVariableState(parent)
        {
            SymbolicName = path,
            ReferenceTypeId = ReferenceTypeIds.Organizes,
            TypeDefinitionId = VariableTypeIds.BaseDataVariableType,
            NodeId = new NodeId(path, NamespaceIndexes[0]),
            BrowseName = new QualifiedName(display, NamespaceIndexes[0]),
            DisplayName = new LocalizedText(display),
            AccessLevel = AccessLevels.CurrentRead,
            UserAccessLevel = AccessLevels.CurrentRead,
            DataType = dataType,
            ValueRank = ValueRanks.Scalar,
            Historizing = false
        };
        parent.AddChild(variable);
        AddPredefinedNode(SystemContext, variable);
        return variable;
    }

    private void Set(BaseDataVariableState? variable, object? raw, string dataType, string quality, DateTime timestamp)
    {
        if (variable is null)
        {
            return;
        }

        variable.Value = raw;
        variable.StatusCode = Quality(quality);
        variable.Timestamp = DateTime.SpecifyKind(timestamp, DateTimeKind.Utc);
        variable.ClearChangeMasks(SystemContext, false);
    }

    private static object? Coerce(OpcUaPointSnapshot point)
    {
        var (_, value) = Typed(point);
        return value;
    }

    private static (NodeId Type, object? Value) Typed(OpcUaPointSnapshot point)
    {
        var kind = (point.DataType ?? "").Trim().ToLowerInvariant();
        if (kind is "bool" or "boolean")
        {
            var text = point.Value ?? "";
            var flag = text.Equals("true", StringComparison.OrdinalIgnoreCase) || text == "1";
            return (DataTypeIds.Boolean, flag);
        }

        if (point.Numeric is double number)
        {
            if (kind is "int" or "int16" or "int32" or "int64" or "uint" or "uint16" or "uint32" or "uint64")
            {
                return (DataTypeIds.Int64, (long)number);
            }

            if (kind is "float" or "double" or "number")
            {
                return (DataTypeIds.Double, number);
            }

            if (number == Math.Truncate(number) && kind is not "float" and not "double")
            {
                return (DataTypeIds.Int64, (long)number);
            }

            return (DataTypeIds.Double, number);
        }

        return (DataTypeIds.String, point.Value ?? "");
    }

    private static StatusCode Quality(string quality) => (quality ?? "").Trim().ToLowerInvariant() switch
    {
        "good" => StatusCodes.Good,
        "bad" => StatusCodes.Bad,
        _ => StatusCodes.Uncertain
    };

    private static string Sanitize(string value)
    {
        var text = value.Trim();
        if (text.Length == 0)
        {
            return "_";
        }

        return text.Replace('/', '_').Replace('\\', '_');
    }
}
