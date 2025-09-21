using FluxIndex.RealApiTest;

namespace FluxIndex.RealApiTest;

class Program
{
    static async Task Main(string[] args)
    {
        await StandaloneTest.RunAsync();
        Console.WriteLine("\n테스트 완료.");
    }
}