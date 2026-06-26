using BenchmarkDotNet.Attributes;
using MemoryPack;
using System.Buffers;
using System.Numerics;
using BenchmarkSuite.MemoryPack.Models;

namespace BenchmarkSuite.MemoryPack
{
    [MemoryDiagnoser]
    public class DeserializationComparisonBenchmark
    {
        private PlayerUpdate _message;

        // MemoryPack buffers
        private ArrayBufferWriter<byte> _memoryPackBufferWriter;
        private byte[] _memoryPackSerializedBytes;

        // BinaryWriter buffers
        private MemoryStream _memoryStream;
        private BinaryWriter _binaryWriter;
        private BinaryReader _binaryReader;
        private byte[] _binaryWriterSerializedBytes;

        [GlobalSetup]
        public void Setup()
        {
            _message = new PlayerUpdate
            {
                MessageType = MessageType.PlayerUpdate,
                NetworkPlayerId = new NetworkPlayerId { Value = 123456789 },
                EntityId = Guid.NewGuid(),
                Position = new Vector2(100.5f, 200.5f),
                Velocity = new Vector2(1.0f, -1.0f),
                Direction = new Vector2(0.68f, 0.32f),
                IsJumping = true,
                IsFiring = false,
                Lives = 3,
                IsAlive = true,
                IsActive = true,
                CurrentAnimation = AnimationType.Run,
                ParachuteState = 1,
                IsPullingParachuteCord = false,
                TimeStamp = DateTime.UtcNow,
                Version = null,
            };

            // Setup MemoryPack
            _memoryPackBufferWriter = new ArrayBufferWriter<byte>(1024);
            MemoryPackSerializer.Serialize(_memoryPackBufferWriter, _message);
            _memoryPackSerializedBytes = _memoryPackBufferWriter.WrittenSpan.ToArray();

            // Setup BinaryWriter
            _memoryStream = new MemoryStream(1024);
            _binaryWriter = new BinaryWriter(_memoryStream);
            _binaryReader = new BinaryReader(_memoryStream);

            SerializeWithBinaryWriter(_binaryWriter, _message);
            _binaryWriterSerializedBytes = _memoryStream.ToArray();
        }

        [Benchmark(Baseline = true)]
        public PlayerUpdate BinaryReader_Deserialize()
        {
            _memoryStream.Position = 0;
            return DeserializeWithBinaryReader(_binaryReader);
        }

        [Benchmark]
        public PlayerUpdate MemoryPack_Deserialize()
        {
            return MemoryPackSerializer.Deserialize<PlayerUpdate>(_memoryPackSerializedBytes);
        }

        private void SerializeWithBinaryWriter(BinaryWriter writer, in PlayerUpdate message)
        {
            writer.Write((byte)message.MessageType);

            // Version (Nullable)
            if (message.Version != null)
            {
                writer.Write(true);
                writer.Write(message.Version.Major);
                writer.Write(message.Version.Minor);
                writer.Write(message.Version.Build);
                writer.Write(message.Version.Revision);
            }
            else
            {
                writer.Write(false);
            }

            writer.Write(message.NetworkPlayerId.Value); // Assuming it has a ulong/long Value property
            writer.Write(message.EntityId.ToByteArray());

            writer.Write(message.Position.X);
            writer.Write(message.Position.Y);

            writer.Write(message.Velocity.X);
            writer.Write(message.Velocity.Y);

            writer.Write(message.Direction.X);
            writer.Write(message.Direction.Y);

            writer.Write(message.IsJumping);
            writer.Write(message.IsFiring);
            writer.Write(message.Lives);
            writer.Write(message.IsAlive);
            writer.Write(message.IsActive);
            writer.Write((byte)message.CurrentAnimation);
            writer.Write(message.ParachuteState);
            writer.Write(message.IsPullingParachuteCord);

            if (message.TimeStamp.HasValue)
            {
                writer.Write(true);
                writer.Write(message.TimeStamp.Value.ToBinary());
            }
            else
            {
                writer.Write(false);
            }
        }

        private PlayerUpdate DeserializeWithBinaryReader(BinaryReader reader)
        {
            var update = new PlayerUpdate();

            update.MessageType = (MessageType)reader.ReadByte();

            bool hasVersion = reader.ReadBoolean();
            if (hasVersion)
            {
                update.Version = new Version(reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32());
            }

            update.NetworkPlayerId = new NetworkPlayerId { Value = reader.ReadUInt64() }; // Assuming ulong
            update.EntityId = new Guid(reader.ReadBytes(16));

            update.Position = new Vector2(reader.ReadSingle(), reader.ReadSingle());
            update.Velocity = new Vector2(reader.ReadSingle(), reader.ReadSingle());
            update.Direction = new Vector2(reader.ReadSingle(), reader.ReadSingle());
            
            update.IsJumping = reader.ReadBoolean();
            update.IsFiring = reader.ReadBoolean();
            update.Lives = reader.ReadUInt16();
            update.IsAlive = reader.ReadBoolean();
            update.IsActive = reader.ReadBoolean();
            update.CurrentAnimation = (AnimationType)reader.ReadByte();
            update.ParachuteState = reader.ReadByte();
            update.IsPullingParachuteCord = reader.ReadBoolean();

            bool hasTimestamp = reader.ReadBoolean();
            if (hasTimestamp)
            {
                update.TimeStamp = DateTime.FromBinary(reader.ReadInt64());
            }

            return update;
        }
    }
}
