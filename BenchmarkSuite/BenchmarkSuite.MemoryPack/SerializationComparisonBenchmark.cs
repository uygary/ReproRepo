using BenchmarkDotNet.Attributes;
using MemoryPack;
using System.Buffers;
using System.Numerics;
using BenchmarkSuite.MemoryPack.Models;

namespace BenchmarkSuite.MemoryPack
{
    [MemoryDiagnoser]
    public class SerializationComparisonBenchmark
    {
        private PlayerUpdate _message;

        // MemoryPack buffers
        private ArrayBufferWriter<byte> _memoryPackBufferWriter;

        // BinaryWriter buffers
        private MemoryStream _memoryStream;
        private BinaryWriter _binaryWriter;

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

            // Setup BinaryWriter
            _memoryStream = new MemoryStream(1024);
            _binaryWriter = new BinaryWriter(_memoryStream);
        }

        [Benchmark(Baseline = true)]
        public void BinaryWriter_Serialize()
        {
            _memoryStream.Position = 0;
            SerializeWithBinaryWriter(_binaryWriter, _message);
        }

        [Benchmark]
        public void MemoryPack_Serialize()
        {
            _memoryPackBufferWriter.Clear();
            MemoryPackSerializer.Serialize(_memoryPackBufferWriter, _message);
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
    }
}
