using BenchmarkDotNet.Running;
using BenchmarkSuite.MemoryPack;

namespace BenchmarkSuite
{
    internal class Program
    {
        static void Main(string[] args)
        {
            var result = BenchmarkRunner.Run(typeof(SerializationComparisonBenchmark).Assembly);
        }
    }
}
