using MemoryPack;
using System.Numerics;

namespace BenchmarkSuite.MemoryPack.Models;

[MemoryPackable(SerializeLayout.Explicit)]
public partial record struct PlayerUpdate() : INetworkMessage
{
    [MemoryPackOrder(0)]
    public MessageType MessageType { get; set; } = MessageType.PlayerUpdate;

    [MemoryPackIgnore]
    public byte MessageTypeId => (byte)MessageType;

    [MemoryPackOrder(1)]
    public Version? Version { get; set; }

    [MemoryPackOrder(2)]
    public NetworkPlayerId NetworkPlayerId { get; set; }

    [MemoryPackOrder(3)]
    public Guid EntityId { get; set; }

    [MemoryPackOrder(4)]
    public Vector2 Position { get; set; }

    [MemoryPackOrder(5)]
    public Vector2 Velocity { get; set; }

    [MemoryPackOrder(6)]
    public Vector2 Direction { get; set; }

    [MemoryPackOrder(7)]
    public bool IsJumping { get; set; }

    [MemoryPackOrder(8)]
    public bool IsFiring { get; set; }

    [MemoryPackOrder(9)]
    public ushort Lives { get; set; }

    [MemoryPackOrder(10)]
    public bool IsAlive { get; set; }

    [MemoryPackOrder(11)]
    public bool IsActive { get; set; }

    [MemoryPackOrder(12)]
    public AnimationType CurrentAnimation { get; set; }

    [MemoryPackOrder(13)]
    public byte ParachuteState { get; set; }

    [MemoryPackOrder(14)]
    public bool IsPullingParachuteCord { get; set; }

    [MemoryPackOrder(15)]
    public DateTime? TimeStamp { get; set; }
}