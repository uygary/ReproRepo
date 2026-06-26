namespace BenchmarkSuite.MemoryPack.Models;

public interface INetworkMessage
{
    byte MessageTypeId { get; }
    Version? Version { get; }
    DateTime? TimeStamp { get; }
}