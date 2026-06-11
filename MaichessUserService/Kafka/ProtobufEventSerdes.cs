using System.Diagnostics.CodeAnalysis;
using Confluent.Kafka;
using Google.Protobuf;

namespace MaichessUserService.Kafka;

// Raw-Protobuf Kafka serdes for the maichess.events.v1 generated messages
// (UserEvent producer, MatchEvent consumer). Kafka task 09 removed the Confluent
// Schema Registry: the wire format is the bare Protobuf bytes
// (msg.ToByteArray() / Parser.ParseFrom(bytes)), with schemas owned solely by
// maichess-api-contracts.
[ExcludeFromCodeCoverage]
internal static class ProtobufEventSerdes
{
    public static ISerializer<T> Serializer<T>()
        where T : IMessage<T>
        => new RawSerializer<T>();

    public static IDeserializer<T> Deserializer<T>()
        where T : IMessage<T>, new()
        => new RawDeserializer<T>();

    private sealed class RawSerializer<T> : ISerializer<T>
        where T : IMessage<T>
    {
        public byte[] Serialize(T data, SerializationContext context) => data.ToByteArray();
    }

    private sealed class RawDeserializer<T> : IDeserializer<T>
        where T : IMessage<T>, new()
    {
        private static readonly MessageParser<T> Parser = new(() => new T());

        public T Deserialize(ReadOnlySpan<byte> data, bool isNull, SerializationContext context) =>
            isNull ? new T() : Parser.ParseFrom(data);
    }
}
