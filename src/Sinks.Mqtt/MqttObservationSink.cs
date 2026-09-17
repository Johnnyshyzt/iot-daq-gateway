using System.Text;
using System.Text.Json;
using Gateway.Abstractions.Configuration;
using Gateway.Abstractions.Contracts;
using Gateway.Abstractions.Models;
using Gateway.Abstractions.Topics;
using Microsoft.Extensions.Logging;
using MQTTnet;
using MQTTnet.Protocol;

namespace Sinks.Mqtt;

public sealed class MqttObservationSink : INorthboundSink
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly GatewayConfiguration _config;
    private readonly ILogger<MqttObservationSink> _logger;
    private readonly IMqttClient _client;
    private readonly MqttClientOptions _options;
    private readonly MqttQualityOfServiceLevel _qos;
    private readonly SemaphoreSlim _connectLock = new(1, 1);

    public MqttObservationSink(GatewayConfiguration config, ILogger<MqttObservationSink> logger)
    {
        _config = config;
        _logger = logger;
        _qos = MapQos(config.Mqtt.Qos);

        var builder = new MqttClientOptionsBuilder()
            .WithTcpServer(config.Mqtt.Host, config.Mqtt.Port)
            .WithClientId(config.Mqtt.ClientId)
            .WithProtocolVersion(MQTTnet.Formatter.MqttProtocolVersion.V311)
            .WithKeepAlivePeriod(TimeSpan.FromSeconds(30));

        if (!string.IsNullOrWhiteSpace(config.Mqtt.Username))
        {
            builder.WithCredentials(config.Mqtt.Username, config.Mqtt.Password);
        }

        if (config.Mqtt.Tls)
        {
            builder.WithTlsOptions(o => o.UseTls());
        }

        _options = builder.Build();
        _client = new MqttClientFactory().CreateMqttClient();
        _client.DisconnectedAsync += OnDisconnectedAsync;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await EnsureConnectedAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task PublishObservationAsync(Observation observation, CancellationToken cancellationToken)
    {
        var topic = DaqTopics.Point(_config.Gateway.Site, observation.DeviceId, observation.Point);
        var payload = new ObservationEnvelope
        {
            GatewayId = _config.Gateway.Id,
            Site = _config.Gateway.Site,
            DeviceId = observation.DeviceId,
            Point = observation.Point,
            Value = observation.Value,
            Quality = observation.Quality,
            Unit = observation.Unit,
            Ts = observation.Timestamp
        };

        await PublishAsync(topic, payload, cancellationToken).ConfigureAwait(false);
    }

    public async Task PublishStatusAsync(DeviceHealth health, CancellationToken cancellationToken)
    {
        var topic = DaqTopics.Status(_config.Gateway.Site, health.DeviceId);
        var payload = new StatusEnvelope
        {
            GatewayId = _config.Gateway.Id,
            Site = _config.Gateway.Site,
            DeviceId = health.DeviceId,
            Status = health.Status.ToString().ToLowerInvariant(),
            Message = health.Message,
            Ts = health.Timestamp
        };

        await PublishAsync(topic, payload, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        _client.DisconnectedAsync -= OnDisconnectedAsync;
        if (_client.IsConnected)
        {
            await _client.DisconnectAsync().ConfigureAwait(false);
        }

        _client.Dispose();
        _connectLock.Dispose();
    }

    private async Task PublishAsync<T>(string topic, T payload, CancellationToken cancellationToken)
    {
        await EnsureConnectedAsync(cancellationToken).ConfigureAwait(false);
        if (!_client.IsConnected)
        {
            _logger.LogWarning("MQTT not connected; drop publish to {Topic}", topic);
            return;
        }

        var json = JsonSerializer.Serialize(payload, JsonOptions);
        var message = new MqttApplicationMessageBuilder()
            .WithTopic(topic)
            .WithPayload(Encoding.UTF8.GetBytes(json))
            .WithQualityOfServiceLevel(_qos)
            .Build();

        await _client.PublishAsync(message, cancellationToken).ConfigureAwait(false);
        _logger.LogDebug("Published {Topic}: {Payload}", topic, json);
    }

    private async Task EnsureConnectedAsync(CancellationToken cancellationToken)
    {
        if (_client.IsConnected)
        {
            return;
        }

        await _connectLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_client.IsConnected)
            {
                return;
            }

            await _client.ConnectAsync(_options, cancellationToken).ConfigureAwait(false);
            _logger.LogInformation(
                "MQTT connected to {Host}:{Port} as {ClientId}",
                _config.Mqtt.Host,
                _config.Mqtt.Port,
                _config.Mqtt.ClientId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(
                ex,
                "MQTT connect failed ({Host}:{Port})",
                _config.Mqtt.Host,
                _config.Mqtt.Port);
        }
        finally
        {
            _connectLock.Release();
        }
    }

    private Task OnDisconnectedAsync(MqttClientDisconnectedEventArgs args)
    {
        _logger.LogWarning("MQTT disconnected: {Reason}", args.Reason);
        return Task.CompletedTask;
    }

    private static MqttQualityOfServiceLevel MapQos(int qos) => qos switch
    {
        0 => MqttQualityOfServiceLevel.AtMostOnce,
        2 => MqttQualityOfServiceLevel.ExactlyOnce,
        _ => MqttQualityOfServiceLevel.AtLeastOnce
    };

    private sealed class ObservationEnvelope
    {
        public required string GatewayId { get; init; }

        public required string Site { get; init; }

        public required string DeviceId { get; init; }

        public required string Point { get; init; }

        public object? Value { get; init; }

        public required string Quality { get; init; }

        public string? Unit { get; init; }

        public DateTimeOffset Ts { get; init; }
    }

    private sealed class StatusEnvelope
    {
        public required string GatewayId { get; init; }

        public required string Site { get; init; }

        public required string DeviceId { get; init; }

        public required string Status { get; init; }

        public string? Message { get; init; }

        public DateTimeOffset Ts { get; init; }
    }
}
