using Opc.Ua;
using Opc.Ua.Client;

namespace Adapters.Cnc.Drivers;

public sealed class OpcUaNetSession : IOpcUaSession
{
    private static readonly ITelemetryContext Telemetry = DefaultTelemetry.Create(static _ => { });

    private ISession? _session;

    public async Task ConnectAsync(OpcConnectInfo info, CancellationToken cancellationToken)
    {
        var timeout = Math.Clamp(info.TimeoutMs, 200, 120_000);
        var configuration = await ConfigurationAsync(timeout, cancellationToken).ConfigureAwait(false);
        var selected = await CoreClientUtils.SelectEndpointAsync(
            configuration,
            info.Url,
            useSecurity: false,
            discoverTimeout: timeout,
            Telemetry,
            cancellationToken).ConfigureAwait(false);
        if (selected is null)
        {
            throw new InvalidOperationException("OPC UA 发现不到 SecurityPolicy None 的端点");
        }

        var endpoint = new ConfiguredEndpoint(null, selected, EndpointConfiguration.Create(configuration));
        IUserIdentity identity = string.IsNullOrEmpty(info.Username)
            ? new UserIdentity()
            : new UserIdentity(info.Username, System.Text.Encoding.UTF8.GetBytes(info.Password));
        var factory = new DefaultSessionFactory(Telemetry);
        _session = await factory.CreateAsync(
            configuration,
            endpoint,
            updateBeforeConnect: false,
            sessionName: "iot-daq-gateway",
            sessionTimeout: (uint)timeout,
            identity,
            preferredLocales: null,
            ct: cancellationToken).ConfigureAwait(false);
    }

    public int ResolveNamespace(string? uriHint, string fallbackContains)
    {
        var table = _session?.NamespaceUris ?? throw new InvalidOperationException("OPC UA 会话未建立");
        if (!string.IsNullOrWhiteSpace(uriHint))
        {
            var exact = table.GetIndex(uriHint);
            if (exact >= 0)
            {
                return exact;
            }
        }

        var needle = string.IsNullOrWhiteSpace(uriHint) ? fallbackContains : uriHint;
        if (!string.IsNullOrWhiteSpace(needle))
        {
            for (var i = 0; i < table.Count; i++)
            {
                var uri = table.GetString((uint)i) ?? "";
                if (uri.Contains(needle, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }
        }

        return table.Count > 2 ? 2 : Math.Max(0, table.Count - 1);
    }

    public async Task<IReadOnlyDictionary<string, OpcReading>> ReadAsync(IReadOnlyList<string> nodeIds, CancellationToken cancellationToken)
    {
        var session = _session ?? throw new InvalidOperationException("OPC UA 会话未建立");
        var map = new Dictionary<string, OpcReading>(StringComparer.Ordinal);
        foreach (var nodeId in nodeIds.Distinct(StringComparer.Ordinal))
        {
            try
            {
                var value = await session.ReadValueAsync(NodeId.Parse(nodeId), cancellationToken).ConfigureAwait(false);
                if (StatusCode.IsGood(value.StatusCode))
                {
                    map[nodeId] = new OpcReading(value.Value, null);
                }
                else
                {
                    map[nodeId] = new OpcReading(null, value.StatusCode.ToString());
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                map[nodeId] = new OpcReading(null, ex.Message);
            }
        }

        return map;
    }

    public async Task<IReadOnlyDictionary<string, string>> BrowseNamesAsync(int limit, CancellationToken cancellationToken)
    {
        var session = _session ?? throw new InvalidOperationException("OPC UA 会话未建立");
        var found = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var pending = new Queue<NodeId>();
        pending.Enqueue(ObjectIds.ObjectsFolder);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        while (pending.Count > 0 && found.Count < limit)
        {
            var node = pending.Dequeue();
            var key = node.ToString();
            if (!seen.Add(key))
            {
                continue;
            }

            ReferenceDescriptionCollection references;
            try
            {
                var browsed = await session.BrowseAsync(
                    requestHeader: null,
                    view: null,
                    node,
                    maxResultsToReturn: 20,
                    BrowseDirection.Forward,
                    ReferenceTypeIds.HierarchicalReferences,
                    includeSubtypes: true,
                    nodeClassMask: (uint)(NodeClass.Object | NodeClass.Variable),
                    cancellationToken).ConfigureAwait(false);
                references = browsed.Item3;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                break;
            }

            foreach (var reference in references)
            {
                var name = reference.BrowseName?.Name;
                if (reference.NodeId is null)
                {
                    continue;
                }

                var expanded = ExpandedNodeId.ToNodeId(reference.NodeId, session.NamespaceUris);
                if (expanded is null)
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(name) && !found.ContainsKey(name))
                {
                    found[name] = expanded.ToString();
                }

                if (reference.NodeClass == NodeClass.Object && pending.Count + found.Count < limit)
                {
                    pending.Enqueue(expanded);
                }
            }
        }

        return found;
    }

    public async ValueTask DisposeAsync()
    {
        if (_session is null)
        {
            return;
        }

        try
        {
            await _session.CloseAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Closing a failed session should not hide the original probe error.
        }

        _session.Dispose();
        _session = null;
    }

    private static async Task<ApplicationConfiguration> ConfigurationAsync(int timeout, CancellationToken cancellationToken)
    {
        var root = Path.Combine(SdkLocator.Root(), "opcua-pki");
        Directory.CreateDirectory(root);
        var configuration = new ApplicationConfiguration
        {
            ApplicationName = "IoT DAQ Gateway",
            ApplicationUri = Utils.Format("urn:{0}:iot-daq-gateway", Utils.GetHostName()),
            ApplicationType = ApplicationType.Client,
            SecurityConfiguration = new SecurityConfiguration
            {
                ApplicationCertificate = new CertificateIdentifier
                {
                    StoreType = CertificateStoreType.Directory,
                    StorePath = Path.Combine(root, "own"),
                    SubjectName = "CN=IoT DAQ Gateway, O=IoT"
                },
                TrustedIssuerCertificates = new CertificateTrustList
                {
                    StoreType = CertificateStoreType.Directory,
                    StorePath = Path.Combine(root, "issuer")
                },
                TrustedPeerCertificates = new CertificateTrustList
                {
                    StoreType = CertificateStoreType.Directory,
                    StorePath = Path.Combine(root, "trusted")
                },
                RejectedCertificateStore = new CertificateTrustList
                {
                    StoreType = CertificateStoreType.Directory,
                    StorePath = Path.Combine(root, "rejected")
                },
                AutoAcceptUntrustedCertificates = true,
                AddAppCertToTrustedStore = true,
                RejectSHA1SignedCertificates = false
            },
            TransportQuotas = new TransportQuotas { OperationTimeout = timeout },
            ClientConfiguration = new ClientConfiguration { DefaultSessionTimeout = timeout },
            DisableHiResClock = true
        };
        await configuration.ValidateAsync(ApplicationType.Client, cancellationToken).ConfigureAwait(false);
        if (configuration.SecurityConfiguration.AutoAcceptUntrustedCertificates)
        {
            configuration.CertificateValidator.CertificateValidation += (_, eventArgs) =>
            {
                eventArgs.Accept = true;
            };
        }

        return configuration;
    }
}
