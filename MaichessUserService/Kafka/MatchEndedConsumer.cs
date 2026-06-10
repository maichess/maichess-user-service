using System.Diagnostics.CodeAnalysis;
using Confluent.Kafka;
using Confluent.Kafka.SyncOverAsync;
using Confluent.SchemaRegistry.Serdes;
using Maichess.Events.V1;
using MaichessUserService.Rating;

namespace MaichessUserService.Kafka;

// Consumes match.events.v1 and drives the rating side: every MatchEnded is mapped
// (MatchEndedEventMapper) to the fact UsersService.ApplyMatchEndedAsync applies —
// the event-driven successor to the retired RecordMatchResult fan-out (kafka 08).
// The resulting rating change flows out on user.events.v1 via the CDC relay; this
// consumer never produces.
//
// Offsets are committed only after a successful apply, so a crash redelivers the
// event; the per-row rated_matches marker makes the redelivery a no-op. A decode
// failure is WARN-logged and skipped (the platform's fire-and-forget consume rule);
// a processing failure propagates so the host restarts onto the committed offset.
//
// Excluded from coverage: pure Kafka consume plumbing requiring a live broker. The
// mapping and the trigger it delegates to are fully tested.
[ExcludeFromCodeCoverage]
internal sealed class MatchEndedConsumer : BackgroundService
{
    private const string Topic = "match.events.v1";
    private const string ConsumerGroup = "user-service-rating";

    private readonly UsersService service;
    private readonly ILogger<MatchEndedConsumer> logger;
    private readonly string bootstrap;

    public MatchEndedConsumer(UsersService service, ILogger<MatchEndedConsumer> logger)
    {
        this.service = service;
        this.logger = logger;
        bootstrap = Environment.GetEnvironmentVariable("KAFKA_BOOTSTRAP") ?? "kafka:9092";
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        Task.Run(() => RunAsync(stoppingToken), stoppingToken);

    private async Task RunAsync(CancellationToken stoppingToken)
    {
        using IConsumer<string, MatchEvent> consumer = new ConsumerBuilder<string, MatchEvent>(
                new ConsumerConfig
                {
                    BootstrapServers = bootstrap,
                    GroupId = ConsumerGroup,
                    AutoOffsetReset = AutoOffsetReset.Earliest,
                    EnableAutoCommit = false,
                })
            .SetValueDeserializer(new ProtobufDeserializer<MatchEvent>().AsSyncOverAsync())
            .Build();

        consumer.Subscribe(Topic);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                ConsumeResult<string, MatchEvent>? result;
                try
                {
                    result = consumer.Consume(stoppingToken);
                }
                catch (ConsumeException ex)
                {
                    logger.LogWarning(
                        ex,
                        "Skipping an undecodable record on {Topic} at {Offset}.",
                        Topic,
                        ex.ConsumerRecord?.TopicPartitionOffset);
                    continue;
                }

                if (result?.Message?.Value is null)
                {
                    continue;
                }

                if (MatchEndedEventMapper.Map(result.Message.Value) is { } fact)
                {
                    IReadOnlyList<string> applied = await service.ApplyMatchEndedAsync(fact, stoppingToken);
                    if (applied.Count > 0 && logger.IsEnabled(LogLevel.Information))
                    {
                        logger.LogInformation(
                            "Applied match {MatchId} results to {Count} player(s).",
                            fact.MatchId,
                            applied.Count);
                    }
                }

                consumer.Commit(result);
            }
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation("MatchEnded consumer is shutting down.");
        }
        finally
        {
            consumer.Close();
        }
    }
}
