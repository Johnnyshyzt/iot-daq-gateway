using System.Net;
using System.Net.Sockets;
using Opc.Ua;
using Opc.Ua.Client;
using Sinks.OpcUa;
using Xunit;

namespace Gateway.Tests;

public sealed class OpcUaServerTests
{
    private static readonly ITelemetryContext Telemetry = DefaultTelemetry.Create(static _ => { });

    [Fact]
    public async Task Client_reads_typed_values_and_server_offers_sign_and_encrypt()
    {
        var port = FreePort();
        var pki = Directory.CreateTempSubdirectory("opcua-server").FullName;
        var when = new DateTimeOffset(2026, 9, 27, 8, 0, 0, TimeSpan.Zero);
        await using var server = await OpcUaNorthbound.StartAsync(new OpcUaServerOptions
        {
            Port = port,
            AllowAnonymous = true,
            AllowNone = true,
            AllowSignAndEncrypt = true,
            Username = "mes",
            PasswordHash = Gateway.Abstractions.Security.SecretHash.Pbkdf2("mes-secret"),
            PkiDirectory = pki
        }, CancellationToken.None);
        server.Update(new OpcUaSnapshot
        {
            Devices =
            [
                new OpcUaDeviceSnapshot
                {
                    Id = "cnc-01",
                    DisplayName = "加工中心 01",
                    Workshop = "机加",
                    Line = "一线",
                    Status = "online",
                    AlarmActive = true,
                    Points =
                    [
                        new OpcUaPointSnapshot
                        {
                            Id = "spindleSpeed",
                            Numeric = 1200,
                            Value = "1200",
                            Quality = "good",
                            DataType = "double",
                            TimestampUnixMs = when.ToUnixTimeMilliseconds()
                        }
                    ]
                }
            ]
        });

        var url = server.EndpointUrl;
        var configuration = await ClientConfigurationAsync(pki);
        var secure = await CoreClientUtils.SelectEndpointAsync(configuration, url, useSecurity: true, discoverTimeout: 8000, Telemetry, CancellationToken.None);
        Assert.NotNull(secure);
        Assert.Equal(SecurityPolicies.Basic256Sha256, secure!.SecurityPolicyUri);
        Assert.Equal(MessageSecurityMode.SignAndEncrypt, secure.SecurityMode);

        var selected = await CoreClientUtils.SelectEndpointAsync(configuration, url, useSecurity: false, discoverTimeout: 8000, Telemetry, CancellationToken.None);
        Assert.NotNull(selected);
        var endpoint = new ConfiguredEndpoint(null, selected, EndpointConfiguration.Create(configuration));
        var factory = new DefaultSessionFactory(Telemetry);
        var session = await factory.CreateAsync(
            configuration,
            endpoint,
            updateBeforeConnect: false,
            sessionName: "phase6-test",
            sessionTimeout: 8000,
            new UserIdentity(),
            preferredLocales: null,
            ct: CancellationToken.None);
        try
        {
            var ns = session.NamespaceUris.GetIndex(OpcUaNorthbound.NamespaceUri);
            Assert.True(ns > 0);
            var speedId = new NodeId("机加/一线/cnc-01/points/spindleSpeed", (ushort)ns);
            var speed = await session.ReadValueAsync(speedId, CancellationToken.None);
            Assert.Equal(StatusCodes.Good, speed.StatusCode);
            Assert.Equal(1200d, Convert.ToDouble(speed.Value, System.Globalization.CultureInfo.InvariantCulture));
            Assert.Equal(when.UtcDateTime, speed.SourceTimestamp);

            var alarmId = new NodeId("机加/一线/cnc-01/alarmActive", (ushort)ns);
            var alarm = await session.ReadValueAsync(alarmId, CancellationToken.None);
            Assert.Equal(true, alarm.Value);

            var statusId = new NodeId("机加/一线/cnc-01/status", (ushort)ns);
            var status = await session.ReadValueAsync(statusId, CancellationToken.None);
            Assert.Equal("online", status.Value);
        }
        finally
        {
            await session.CloseAsync(CancellationToken.None);
            session.Dispose();
        }

        var signed = await CoreClientUtils.SelectEndpointAsync(configuration, url, useSecurity: false, discoverTimeout: 8000, Telemetry, CancellationToken.None);
        var signedEndpoint = new ConfiguredEndpoint(null, signed, EndpointConfiguration.Create(configuration));
        var authed = await factory.CreateAsync(
            configuration,
            signedEndpoint,
            updateBeforeConnect: false,
            sessionName: "phase6-user",
            sessionTimeout: 8000,
            new UserIdentity("mes", System.Text.Encoding.UTF8.GetBytes("mes-secret")),
            preferredLocales: null,
            ct: CancellationToken.None);
        await authed.CloseAsync(CancellationToken.None);
        authed.Dispose();
        Directory.Delete(pki, recursive: true);
    }

    private static async Task<ApplicationConfiguration> ClientConfigurationAsync(string pki)
    {
        var root = Path.Combine(pki, "client");
        Directory.CreateDirectory(root);
        var configuration = new ApplicationConfiguration
        {
            ApplicationName = "IoT DAQ Gateway Test",
            ApplicationUri = "urn:iot-daq-gateway:opcua-test",
            ApplicationType = ApplicationType.Client,
            SecurityConfiguration = new SecurityConfiguration
            {
                ApplicationCertificate = new CertificateIdentifier
                {
                    StoreType = CertificateStoreType.Directory,
                    StorePath = Path.Combine(root, "own"),
                    SubjectName = "CN=IoT DAQ Gateway Test, O=IoT"
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
            TransportQuotas = new TransportQuotas { OperationTimeout = 8000 },
            ClientConfiguration = new ClientConfiguration { DefaultSessionTimeout = 8000 },
            DisableHiResClock = true
        };
        await configuration.ValidateAsync(ApplicationType.Client, CancellationToken.None);
        configuration.CertificateValidator.CertificateValidation += (_, args) => { args.Accept = true; };
        return configuration;
    }

    private static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
