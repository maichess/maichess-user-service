using System.Diagnostics.CodeAnalysis;
using Confluent.Kafka;
using Maichess.Events.V1;

namespace MaichessUserService.Kafka;

// Consumes the raw Debezium change stream user.cdc.v1 and produces the curated,
// envelope-wrapped user.events.v1 via CdcUserEventMapper. This relay replaces the
// User-service's (never-built) event-emit dual write: the write path now touches
// Postgres only, and the WAL is the single source of the change event.
//
// Excluded from coverage: pure Kafka consume/produce plumbing requiring a live broker.
// The mapping it delegates to (CdcUserEventMapper) is fully tested. The curated
// user.events.v1 envelopes are written as raw Protobuf bytes (Kafka task 09 removed the
// Schema Registry).
[ExcludeFromCodeCoverage]
internal sealed class UserCdcRelay : BackgroundService
{
    private const string CdcTopic = "user.cdc.v1";
    private const string EventsTopic = "user.events.v1";
    private const string ConsumerGroup = "user-cdc-relay";

    private readonly ILogger<UserCdcRelay> logger;
    private readonly string bootstrap;

    public UserCdcRelay(ILogger<UserCdcRelay> logger)
    {
        this.logger = logger;
        bootstrap = Environment.GetEnvironmentVariable("KAFKA_BOOTSTRAP") ?? "kafka:9092";
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using IConsumer<string, string> consumer = new ConsumerBuilder<string, string>(
                new ConsumerConfig
                {
                    BootstrapServers = bootstrap,
                    GroupId = ConsumerGroup,
                    AutoOffsetReset = AutoOffsetReset.Earliest,
                    EnableAutoCommit = false,
                })
            .Build();

        using IProducer<string, UserEvent> producer = new ProducerBuilder<string, UserEvent>(
                new ProducerConfig { BootstrapServers = bootstrap })
            .SetValueSerializer(ProtobufEventSerdes.Serializer<UserEvent>())
            .Build();

        consumer.Subscribe(CdcTopic);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                ConsumeResult<string, string> result = consumer.Consume(stoppingToken);
                if (result?.Message?.Value is null)
                {
                    continue;
                }

                foreach (UserEvent envelope in CdcUserEventMapper.Map(result.Message.Value))
                {
                    await producer.ProduceAsync(
                        EventsTopic,
                        new Message<string, UserEvent> { Key = envelope.AggregateId, Value = envelope },
                        stoppingToken);
                }

                consumer.Commit(result);
            }
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation("User CDC relay is shutting down.");
        }
        catch (ProduceException<string, UserEvent> ex)
        {
            logger.LogError(ex, "Failed to produce a curated user event.");
            throw;
        }
        finally
        {
            producer.Flush(TimeSpan.FromSeconds(5));
            consumer.Close();
        }
    }
}
