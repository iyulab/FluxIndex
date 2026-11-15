using FluxIndex.Integrations.FileFlux.Processing;
using Xunit;

namespace FluxIndex.SDK.Tests.Processing;

/// <summary>
/// A document is most often hashed while the program that edits it still has it open, and on Windows an editor holds
/// its file open for writing; <c>File.OpenRead</c> denies writers and was refused with «being used by another process».
/// </summary>
public sealed class FileHashOfAHeldOpenFileTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"fluxindex_held_open_{Guid.NewGuid():N}");

    public FileHashOfAHeldOpenFileTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public void ComputeFileHash_OfAFileAnEditorHoldsOpenForWriting_IsTheHashOfItsContent()
    {
        var path = Path.Combine(_dir, "plan.md");
        File.WriteAllText(path, "# Plan\n\nThe budget is approved.\n");
        var closed = ExtractionResult.ComputeFileHash(path);

        using var editor = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);
        var held = ExtractionResult.ComputeFileHash(path);

        Assert.Equal(closed, held);
    }
}
