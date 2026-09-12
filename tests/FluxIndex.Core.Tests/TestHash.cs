namespace FluxIndex.Core.Tests;

/// <summary>
/// A string hash that is the same in every process. Test doubles that derive an embedding from a
/// text must not seed it with <see cref="string.GetHashCode()"/>: .NET randomises string hash codes
/// per process, so such a double is deterministic only within one <c>dotnet test</c> invocation and
/// the same text embeds differently in every run — a similarity that clears a threshold in one
/// process fails it in the next, and the failure looks like a flake tied to load or test order.
/// Shared by the storage test projects through their reference to this project.
/// </summary>
public static class TestHash
{
    /// <summary>FNV-1a, 32-bit, over the string's code units. Stable across processes, runtimes and machines.</summary>
    public static int Fnv1a(string text)
    {
        unchecked
        {
            uint hash = 2166136261;
            foreach (var c in text)
            {
                hash ^= c;
                hash *= 16777619;
            }
            return (int)hash;
        }
    }
}
