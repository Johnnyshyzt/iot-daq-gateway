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
    private readonly TimeSpan _reconnectDelay = TimeSpan.FromSeconds(5);
    private readonly MqttSpool? _spool;
    private readonly MqttBufferStatus? _buffer;
    private DateTimeOffset _nextConnectAttempt = DateTimeOffset.MinValue;
    private bool _wasConnected;

    public MqttObservationSink(GatewayConfiguration config, ILogger<MqttObservationSink> logger)
        : this(config, logger, spool: null, buffer: null)
    {
    }

    public MqttObservationSink(
        GatewayConfiguration config,
        ILogger<MqttObservationSink> logger,
        MqttSpool? spool,
        MqttBufferStatus? buffer)
    {
        _config = config;
        _logger = logger;
        _spool = spool;
        _buffer = buffer;
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
        var topic = DaqTopics.PointTopic(
            _config.Mqtt.TopicTemplate,
            _config.Gateway.Site,
            observation.DeviceId,
            observation.Point);
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
        var topic = DaqTopics.StatusTopic(
            _config.Mqtt.StatusTopic,
            _config.Gateway.Site,
            health.DeviceId);
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
        TouchBuffer();
    }

    private async Task PublishAsync<T>(string topic, T payload, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(payload, JsonOptions);
        await EnsureConnectedAsync(cancellationToken).ConfigureAwait(false);
        if (_client.IsConnected)
        {
            await DrainAsync(cancellationToken).ConfigureAwait(false);
        }

        if (!_client.IsConnected)
        {
            Hold(topic, json);
            return;
        }

        try
        {
            await SendAsync(topic, json, cancellationToken).ConfigureAwait(false);
            _logger.LogDebug("Published {Topic}: {Payload}", topic, json);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "MQTT publish failed; spool {Topic}", topic);
            Hold(topic, json);
        }
    }

    private async Task DrainAsync(CancellationToken cancellationToken)
    {
        if (_spool is null)
        {
            return;
        }

        while (_client.IsConnected)
        {
            var item = _spool.PeekOldest(DateTimeOffset.UtcNow);
            if (item is null)
            {
                TouchBuffer();
                return;
            }

            try
            {
                await SendAsync(item.Topic, item.Payload, cancellationToken).ConfigureAwait(false);
                _spool.Acknowledge(item.Seq);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning("MQTT replay stopped at seq {Seq}: {Message}", item.Seq, ex.Message);
                TouchBuffer();
                return;
            }
        }

        TouchBuffer();
    }

    private void Hold(string topic, string json)
    {
        if (_spool is null)
        {
            _logger.LogWarning("MQTT not connected; drop publish to {Topic}", topic);
            TouchBuffer();
            return;
        }

        _spool.Enqueue(topic, json, (int)_qos, _config.Mqtt.Retain, DateTimeOffset.UtcNow);
        _logger.LogWarning("MQTT not connected; spooled {Topic} (depth {Depth})", topic, _spool.Depth);
        TouchBuffer();
    }

    private void TouchBuffer()
    {
        _buffer?.Publish(_spool?.Depth ?? 0, _spool?.Dropped ?? 0, _client.IsConnected);
    }

    private Task SendAsync(string topic, string json, CancellationToken cancellationToken)
    {
        var message = new MqttApplicationMessageBuilder()
            .WithTopic(topic)
            .WithPayload(Encoding.UTF8.GetBytes(json))
            .WithQualityOfServiceLevel(_qos)
            .WithRetainFlag(_config.Mqtt.Retain)
            .Build();
        return _client.PublishAsync(message, cancellationToken);
    }

    private async Task EnsureConnectedAsync(CancellationToken cancellationToken)
    {
        if (_client.IsConnected)
        {
            return;
        }

        if (DateTimeOffset.UtcNow < _nextConnectAttempt)
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

            if (DateTimeOffset.UtcNow < _nextConnectAttempt)
            {
                return;
            }

            await _client.ConnectAsync(_options, cancellationToken).ConfigureAwait(false);
            _wasConnected = true;
            _nextConnectAttempt = DateTimeOffset.MinValue;
            TouchBuffer();
            _logger.LogInformation(
                "MQTT connected to {Host}:{Port} as {ClientId}",
                _config.Mqtt.Host,
                _config.Mqtt.Port,
                _config.Mqtt.ClientId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _nextConnectAttempt = DateTimeOffset.UtcNow + _reconnectDelay;
            TouchBuffer();
            _logger.LogWarning(
                "MQTT connect failed ({Host}:{Port}): {Message}. Retry in {Delay}s",
                _config.Mqtt.Host,
                _config.Mqtt.Port,
                ex.Message,
                _reconnectDelay.TotalSeconds);
        }
        finally
        {
            _connectLock.Release();
        }
    }

    private Task OnDisconnectedAsync(MqttClientDisconnectedEventArgs args)
    {
        if (_wasConnected)
        {
            _logger.LogWarning("MQTT disconnected: {Reason}", args.Reason);
        }

        _wasConnected = false;
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
