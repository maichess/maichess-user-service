using System.Diagnostics.CodeAnalysis;
using Avro.Generic;
using Confluent.Kafka;
using Confluent.SchemaRegistry;
using Confluent.SchemaRegistry.Serdes;

namespace MaichessUserService.Kafka;

// Consumes the raw Debezium change stream user.cdc.v1 and produces the curated,
// envelope-wrapped user.events.v1 via CdcUserEventMapper. This relay replaces the
// User-service's (never-built) event-emit dual write: the write path now touches
// Postgres only, and the WAL is the single source of the change event.
//
// Excluded from coverage: pure Kafka consume/produce plumbing requiring a live broker
// and Schema Registry. The mapping it delegates to (CdcUserEventMapper) is fully tested.
[ExcludeFromCodeCoverage]
internal sealed class UserCdcRelay : BackgroundService
{
    private const string CdcTopic = "user.cdc.v1";
    private const string EventsTopic = "user.events.v1";
    private const string ConsumerGroup = "user-cdc-relay";

    private readonly CdcUserEventMapper mapper;
    private readonly ILogger<UserCdcRelay> logger;
    private readonly string bootstrap;
    private readonly string registryUrl;

    public UserCdcRelay(ILogger<UserCdcRelay> logger)
    {
        this.logger = logger;
        mapper = CdcUserEventMapper.FromEmbeddedSchema();
        bootstrap = Environment.GetEnvironmentVariable("KAFKA_BOOTSTRAP") ?? "kafka:9092";
        registryUrl = Environment.GetEnvironmentVariable("SCHEMA_REGISTRY_URL")
            ?? "http://schema-registry:8081";
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

        using CachedSchemaRegistryClient registry = new(new SchemaRegistryConfig { Url = registryUrl });
        using IProducer<string, GenericRecord> producer = new ProducerBuilder<string, GenericRecord>(
                new ProducerConfig { BootstrapServers = bootstrap })
            .SetValueSerializer(new AvroSerializer<GenericRecord>(registry))
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

                foreach (GenericRecord envelope in mapper.Map(result.Message.Value))
                {
                    string key = (string)envelope["aggregate_id"];
                    await producer.ProduceAsync(
                        EventsTopic,
                        new Message<string, GenericRecord> { Key = key, Value = envelope },
                        stoppingToken);
                }

                consumer.Commit(result);
            }
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation("User CDC relay is shutting down.");
        }
        catch (ProduceException<string, GenericRecord> ex)
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
