using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Maichess.Events.V1;

namespace MaichessUserService.Kafka;

// Pure transform: a Debezium Postgres change event for public.users -> zero or more
// user.events.v1 envelope records. This curated mapping is the *only* producer of
// user.events.v1 (the User-service dual-write emitter was never implemented; see
// CONTRACT_NOTES.md and change-data-capture.md). Stateless and deterministic, so
// reprocessing the same WAL position yields the same event_id (idempotent on replay).
//
// Mapping (faithful to what an in-process emitter would have produced per operation):
//   op c/r (insert / snapshot) -> UserRegistered{user_id, username}
//   op u   (update):
//     username or dev_mode changed -> ProfileUpdated{user_id, username, dev_mode}
//     rating / rating_deviation / volatility / elo / wins / losses / draws changed -> RatingUpdated{...}
//   op d   (delete) / unknown      -> nothing
// Per-operation fidelity needs the full before-image, which requires the users table to
// run with REPLICA IDENTITY FULL (set in the user-db migration). If a before-image is
// absent (default replica identity), the mapper degrades safely to emitting both the
// profile and rating events for the current state.
internal static class CdcUserEventMapper
{
    private const string Producer = "user-cdc-relay";

    public static IReadOnlyList<UserEvent> Map(string cdcValueJson)
    {
        using var doc = JsonDocument.Parse(cdcValueJson);
        JsonElement change = Unwrap(doc.RootElement);

        if (change.ValueKind != JsonValueKind.Object
            || !change.TryGetProperty("op", out JsonElement opEl)
            || opEl.ValueKind != JsonValueKind.String)
        {
            return [];
        }

        JsonElement? after = GetObject(change, "after");
        JsonElement? before = GetObject(change, "before");
        long sequence = ReadSequence(change);
        long occurredAt = ReadOccurredAt(change);

        return opEl.GetString() switch
        {
            "c" or "r" => after is { } a ? [BuildUserRegistered(a, sequence, occurredAt)] : [],
            "u" => after is { } a ? MapUpdate(before, a, sequence, occurredAt) : [],
            _ => [],
        };
    }

    private static bool RatingDiffers(JsonElement before, JsonElement after) =>
        GetDouble(before, "rating") != GetDouble(after, "rating")
        || GetDouble(before, "rating_deviation") != GetDouble(after, "rating_deviation")
        || GetDouble(before, "volatility") != GetDouble(after, "volatility")
        || GetInt(before, "elo") != GetInt(after, "elo")
        || GetInt(before, "wins") != GetInt(after, "wins")
        || GetInt(before, "losses") != GetInt(after, "losses")
        || GetInt(before, "draws") != GetInt(after, "draws");

    // Stable id derived from (aggregate, WAL position, event type) so replaying the same
    // change yields the same event_id — the idempotency key downstream consumers dedupe on.
#pragma warning disable CA5351 // MD5 derives a deterministic id here, not a security digest.
    private static string DeterministicEventId(string aggregateId, long seq, string eventType)
    {
        byte[] hash = MD5.HashData(Encoding.UTF8.GetBytes($"{aggregateId}:{seq}:{eventType}"));
        return new Guid(hash).ToString();
    }
#pragma warning restore CA5351

    // Debezium with the JSON converter and schemas enabled wraps the change event as
    // { "schema": {...}, "payload": {...} }; with schemas disabled the root *is* the
    // change event. Accept both shapes.
    private static JsonElement Unwrap(JsonElement root) =>
        root.ValueKind == JsonValueKind.Object
        && root.TryGetProperty("schema", out _)
        && root.TryGetProperty("payload", out JsonElement payload)
        && payload.ValueKind == JsonValueKind.Object
            ? payload
            : root;

    private static long ReadSequence(JsonElement change) =>
        change.TryGetProperty("source", out JsonElement source)
        && source.ValueKind == JsonValueKind.Object
        && source.TryGetProperty("lsn", out JsonElement lsn)
        && lsn.ValueKind == JsonValueKind.Number
            ? lsn.GetInt64()
            : 0L;

    private static long ReadOccurredAt(JsonElement change) =>
        change.TryGetProperty("ts_ms", out JsonElement ts) && ts.ValueKind == JsonValueKind.Number
            ? ts.GetInt64()
            : change.TryGetProperty("source", out JsonElement source)
                && source.ValueKind == JsonValueKind.Object
                && source.TryGetProperty("ts_ms", out JsonElement sourceTs)
                && sourceTs.ValueKind == JsonValueKind.Number
                    ? sourceTs.GetInt64()
                    : 0L;

    private static JsonElement? GetObject(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out JsonElement el) && el.ValueKind == JsonValueKind.Object
            ? el
            : null;

    private static string GetString(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out JsonElement el) && el.ValueKind == JsonValueKind.String
            ? el.GetString()!
            : string.Empty;

    private static bool GetBool(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out JsonElement el) && el.ValueKind == JsonValueKind.True;

    private static double GetDouble(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out JsonElement el) && el.ValueKind == JsonValueKind.Number
            ? el.GetDouble()
            : 0d;

    private static int GetInt(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out JsonElement el) && el.ValueKind == JsonValueKind.Number
            ? el.GetInt32()
            : 0;

    private static List<UserEvent> MapUpdate(JsonElement? before, JsonElement after, long seq, long ts)
    {
        bool profileChanged = before is not { } b
            || GetString(b, "username") != GetString(after, "username")
            || GetBool(b, "dev_mode") != GetBool(after, "dev_mode");

        bool ratingChanged = before is not { } b2 || RatingDiffers(b2, after);

        var events = new List<UserEvent>(2);
        if (profileChanged)
        {
            events.Add(BuildProfileUpdated(after, seq, ts));
        }

        if (ratingChanged)
        {
            events.Add(BuildRatingUpdated(after, seq, ts));
        }

        return events;
    }

    private static UserEvent BuildUserRegistered(JsonElement after, long seq, long ts)
    {
        string userId = GetString(after, "id");
        UserEvent envelope = NewEnvelope("user.UserRegistered", userId, seq, ts);
        envelope.UserRegistered = new UserRegistered
        {
            UserId = userId,
            Username = GetString(after, "username"),
        };
        return envelope;
    }

    private static UserEvent BuildProfileUpdated(JsonElement after, long seq, long ts)
    {
        string userId = GetString(after, "id");
        UserEvent envelope = NewEnvelope("user.ProfileUpdated", userId, seq, ts);
        envelope.ProfileUpdated = new ProfileUpdated
        {
            UserId = userId,
            Username = GetString(after, "username"),
            DevMode = GetBool(after, "dev_mode"),
        };
        return envelope;
    }

    private static UserEvent BuildRatingUpdated(JsonElement after, long seq, long ts)
    {
        string userId = GetString(after, "id");
        UserEvent envelope = NewEnvelope("user.RatingUpdated", userId, seq, ts);
        envelope.RatingUpdated = new RatingUpdated
        {
            UserId = userId,
            Rating = GetDouble(after, "rating"),
            RatingDeviation = GetDouble(after, "rating_deviation"),
            Volatility = GetDouble(after, "volatility"),
            Elo = GetInt(after, "elo"),
            Wins = GetInt(after, "wins"),
            Losses = GetInt(after, "losses"),
            Draws = GetInt(after, "draws"),
        };
        return envelope;
    }

    private static UserEvent NewEnvelope(string eventType, string aggregateId, long seq, long ts) => new()
    {
        EventId = DeterministicEventId(aggregateId, seq, eventType),
        EventType = eventType,
        AggregateId = aggregateId,
        Sequence = seq,
        OccurredAt = ts,
        CorrelationId = string.Empty,
        CausationId = string.Empty,
        Producer = Producer,
    };
}
