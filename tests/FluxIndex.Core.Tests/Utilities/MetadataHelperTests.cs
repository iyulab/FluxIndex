using FluxIndex.Core.Application.Utilities;
using FluxIndex.Core.Domain.Entities;
using Xunit;

namespace FluxIndex.Core.Tests.Utilities;

public class MetadataHelperTests
{
    #region EnsureInitialized Tests

    [Fact]
    public void EnsureInitialized_NullMetadata_ReturnsNewDictionary()
    {
        // Act
        var result = MetadataHelper.EnsureInitialized(null);

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public void EnsureInitialized_ExistingMetadata_ReturnsSameInstance()
    {
        // Arrange
        var existing = new Dictionary<string, object> { ["key"] = "value" };

        // Act
        var result = MetadataHelper.EnsureInitialized(existing);

        // Assert
        Assert.Same(existing, result);
    }

    #endregion

    #region AddStandardFields Tests

    [Fact]
    public void AddStandardFields_AddsDocumentIdAndChunkIndex()
    {
        // Arrange
        var metadata = new Dictionary<string, object>();
        var chunk = new DocumentChunk
        {
            DocumentId = "doc-123",
            ChunkIndex = 5
        };

        // Act
        MetadataHelper.AddStandardFields(metadata, chunk);

        // Assert
        Assert.Equal("doc-123", metadata[MetadataHelper.StandardKeys.DocumentId]);
        Assert.Equal(5, metadata[MetadataHelper.StandardKeys.ChunkIndex]);
        Assert.True(metadata.ContainsKey(MetadataHelper.StandardKeys.StoredAt));
    }

    [Fact]
    public void AddStandardFields_DoesNotOverwriteExisting()
    {
        // Arrange
        var metadata = new Dictionary<string, object>
        {
            [MetadataHelper.StandardKeys.DocumentId] = "existing-doc"
        };
        var chunk = new DocumentChunk
        {
            DocumentId = "new-doc",
            ChunkIndex = 0
        };

        // Act
        MetadataHelper.AddStandardFields(metadata, chunk);

        // Assert
        Assert.Equal("existing-doc", metadata[MetadataHelper.StandardKeys.DocumentId]);
    }

    #endregion

    #region SetUpdatedTimestamp Tests

    [Fact]
    public void SetUpdatedTimestamp_AddsOrUpdatesTimestamp()
    {
        // Arrange
        var metadata = new Dictionary<string, object>();

        // Act
        MetadataHelper.SetUpdatedTimestamp(metadata);

        // Assert
        Assert.True(metadata.ContainsKey(MetadataHelper.StandardKeys.UpdatedAt));
        var timestamp = metadata[MetadataHelper.StandardKeys.UpdatedAt] as string;
        Assert.NotNull(timestamp);
        Assert.True(DateTime.TryParse(timestamp, out _));
    }

    #endregion

    #region MergeMetadata Tests

    [Fact]
    public void MergeMetadata_MergesSourceIntoTarget()
    {
        // Arrange
        var target = new Dictionary<string, object> { ["a"] = 1 };
        var source = new Dictionary<string, object> { ["b"] = 2 };

        // Act
        MetadataHelper.MergeMetadata(target, source);

        // Assert
        Assert.Equal(1, target["a"]);
        Assert.Equal(2, target["b"]);
    }

    [Fact]
    public void MergeMetadata_DoesNotOverwriteExistingKeys()
    {
        // Arrange
        var target = new Dictionary<string, object> { ["key"] = "original" };
        var source = new Dictionary<string, object> { ["key"] = "new" };

        // Act
        MetadataHelper.MergeMetadata(target, source);

        // Assert
        Assert.Equal("original", target["key"]);
    }

    [Fact]
    public void MergeMetadata_NullSource_DoesNothing()
    {
        // Arrange
        var target = new Dictionary<string, object> { ["key"] = "value" };

        // Act
        MetadataHelper.MergeMetadata(target, null);

        // Assert
        Assert.Single(target);
    }

    #endregion

    #region GetValue Tests

    [Fact]
    public void GetValue_ExistingKey_ReturnsTypedValue()
    {
        // Arrange
        var metadata = new Dictionary<string, object> { ["count"] = 42 };

        // Act
        var result = MetadataHelper.GetValue<int>(metadata, "count");

        // Assert
        Assert.Equal(42, result);
    }

    [Fact]
    public void GetValue_MissingKey_ReturnsDefault()
    {
        // Arrange
        var metadata = new Dictionary<string, object>();

        // Act
        var result = MetadataHelper.GetValue<int>(metadata, "missing", -1);

        // Assert
        Assert.Equal(-1, result);
    }

    [Fact]
    public void GetValue_NullMetadata_ReturnsDefault()
    {
        // Act
        var result = MetadataHelper.GetValue<string>(null, "key", "default");

        // Assert
        Assert.Equal("default", result);
    }

    [Fact]
    public void GetValue_TypeConversion_ConvertsStringToInt()
    {
        // Arrange
        var metadata = new Dictionary<string, object> { ["number"] = "123" };

        // Act
        var result = MetadataHelper.GetValue<int>(metadata, "number");

        // Assert
        Assert.Equal(123, result);
    }

    #endregion

    #region CloneMetadata Tests

    [Fact]
    public void CloneMetadata_CreatesShallowCopy()
    {
        // Arrange
        var original = new Dictionary<string, object> { ["key"] = "value" };

        // Act
        var clone = MetadataHelper.CloneMetadata(original);

        // Assert
        Assert.NotSame(original, clone);
        Assert.Equal(original["key"], clone["key"]);
    }

    [Fact]
    public void CloneMetadata_NullMetadata_ReturnsEmptyDictionary()
    {
        // Act
        var result = MetadataHelper.CloneMetadata(null);

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result);
    }

    #endregion

    #region FilterKeys Tests

    [Fact]
    public void FilterKeys_ReturnsOnlySpecifiedKeys()
    {
        // Arrange
        var metadata = new Dictionary<string, object>
        {
            ["a"] = 1,
            ["b"] = 2,
            ["c"] = 3
        };

        // Act
        var filtered = MetadataHelper.FilterKeys(metadata, new[] { "a", "c" });

        // Assert
        Assert.Equal(2, filtered.Count);
        Assert.True(filtered.ContainsKey("a"));
        Assert.True(filtered.ContainsKey("c"));
        Assert.False(filtered.ContainsKey("b"));
    }

    #endregion

    #region RemoveEmptyValues Tests

    [Fact]
    public void RemoveEmptyValues_RemovesNullAndEmptyStrings()
    {
        // Arrange
        var metadata = new Dictionary<string, object>
        {
            ["null"] = null!,
            ["empty"] = "",
            ["valid"] = "value"
        };

        // Act
        MetadataHelper.RemoveEmptyValues(metadata);

        // Assert
        Assert.Single(metadata);
        Assert.True(metadata.ContainsKey("valid"));
    }

    #endregion
}
