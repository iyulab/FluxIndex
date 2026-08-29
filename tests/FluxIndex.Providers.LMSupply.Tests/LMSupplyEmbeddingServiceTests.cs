using AwesomeAssertions;
using FluxIndex.Providers.LMSupply.Services;
using LMSupply.Embedder;
using NSubstitute;
using Xunit;

namespace FluxIndex.Providers.LMSupply.Tests;

public class LMSupplyEmbeddingServiceTests : IAsyncDisposable
{
    private static readonly float[] s_singleEmbedding = [0.1f, 0.2f, 0.3f];
    private static readonly float[] s_embed1 = [0.1f, 0.2f];
    private static readonly float[] s_embed2 = [0.3f, 0.4f];
    private static readonly float[] s_embed3 = [0.5f, 0.6f];
    private static readonly float[][] s_batchEmbeddings = [s_embed1, s_embed2, s_embed3];
    private static readonly float[][] s_singleBatchResult = [[1.0f, 2.0f, 3.0f]];
    private static readonly string[] s_singleTextArg = ["single"];

    private readonly IEmbeddingModel _mockModel;
    private readonly LMSupplyEmbeddingService _service;

    public LMSupplyEmbeddingServiceTests()
    {
        _mockModel = Substitute.For<IEmbeddingModel>();
        _mockModel.Dimensions.Returns(384);
        _mockModel.ModelId.Returns("all-MiniLM-L6-v2");
        _service = new LMSupplyEmbeddingService(_mockModel);
    }

    public ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        return _service.DisposeAsync();
    }

    #region Constructor

    [Fact]
    public void Constructor_NullModel_ThrowsArgumentNullException()
    {
        var act = () => new LMSupplyEmbeddingService(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    #endregion

    #region GetEmbeddingDimension / GetModelName

    [Fact]
    public void GetEmbeddingDimension_ReturnsDimensionsFromModel()
    {
        _service.GetEmbeddingDimension().Should().Be(384);
    }

    [Fact]
    public void GetModelName_ReturnsModelIdFromModel()
    {
        _service.GetModelName().Should().Be("all-MiniLM-L6-v2");
    }

    [Fact]
    public void GetEmbeddingDimension_DifferentModel_ReturnsCorrectValue()
    {
        var model = Substitute.For<IEmbeddingModel>();
        model.Dimensions.Returns(768);
        var service = new LMSupplyEmbeddingService(model);

        service.GetEmbeddingDimension().Should().Be(768);
    }

    #endregion

    #region GenerateEmbeddingAsync

    [Fact]
    public async Task GenerateEmbeddingAsync_ValidText_DelegatesToModel()
    {
        _mockModel.EmbedAsync("test text", Arg.Any<CancellationToken>())
            .Returns(new ValueTask<float[]>(s_singleEmbedding));

        var result = await _service.GenerateEmbeddingAsync("test text", TestContext.Current.CancellationToken);

        result.Should().BeEquivalentTo(s_singleEmbedding);
        await _mockModel.Received(1).EmbedAsync("test text", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GenerateEmbeddingAsync_EmptyText_ReturnsEmptyArray()
    {
        var result = await _service.GenerateEmbeddingAsync("", TestContext.Current.CancellationToken);

        result.Should().BeEmpty();
        await _mockModel.DidNotReceive().EmbedAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GenerateEmbeddingAsync_WhitespaceText_ReturnsEmptyArray()
    {
        var result = await _service.GenerateEmbeddingAsync("   ", TestContext.Current.CancellationToken);

        result.Should().BeEmpty();
        await _mockModel.DidNotReceive().EmbedAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GenerateEmbeddingAsync_NullText_ReturnsEmptyArray()
    {
        var result = await _service.GenerateEmbeddingAsync(null!, TestContext.Current.CancellationToken);

        result.Should().BeEmpty();
    }

    #endregion

    #region GenerateEmbeddingsBatchAsync

    [Fact]
    public async Task GenerateEmbeddingsBatchAsync_MultipleTexts_DelegatesToModelBatch()
    {
        var texts = new List<string> { "text1", "text2", "text3" };
        _mockModel.EmbedAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<float[][]>(s_batchEmbeddings));

        var result = (await _service.GenerateEmbeddingsBatchAsync(texts, TestContext.Current.CancellationToken)).ToList();

        result.Should().HaveCount(3);
        result[0].Should().BeEquivalentTo(s_embed1);
        result[2].Should().BeEquivalentTo(s_embed3);
    }

    [Fact]
    public async Task GenerateEmbeddingsBatchAsync_EmptyList_DelegatesToModel()
    {
        _mockModel.EmbedAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<float[][]>(Array.Empty<float[]>()));

        var result = (await _service.GenerateEmbeddingsBatchAsync(Enumerable.Empty<string>(), TestContext.Current.CancellationToken)).ToList();

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GenerateEmbeddingsBatchAsync_SingleText_ReturnsOneResult()
    {
        _mockModel.EmbedAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<float[][]>(s_singleBatchResult));

        var result = (await _service.GenerateEmbeddingsBatchAsync(s_singleTextArg, TestContext.Current.CancellationToken)).ToList();

        result.Should().HaveCount(1);
    }

    #endregion

    #region Base class methods (GetMaxTokens, CountTokensAsync)

    [Fact]
    public void GetMaxTokens_ReturnsDefault512()
    {
        _service.GetMaxTokens().Should().Be(512);
    }

    [Fact]
    public async Task CountTokensAsync_EnglishText_ReturnsEstimate()
    {
        // "hello world" = 11 chars, ~11/4 + 2 = 4 tokens
        var count = await _service.CountTokensAsync("hello world", TestContext.Current.CancellationToken);

        count.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task CountTokensAsync_EmptyText_ReturnsZero()
    {
        var count = await _service.CountTokensAsync("", TestContext.Current.CancellationToken);

        count.Should().Be(0);
    }

    [Fact]
    public async Task CountTokensAsync_CjkText_CountsPerCharacter()
    {
        // Korean: 5 Hangul chars -> 5 CJK tokens + 2 special = 7
        var count = await _service.CountTokensAsync("안녕하세요", TestContext.Current.CancellationToken);

        count.Should().Be(7);
    }

    [Fact]
    public async Task CountTokensAsync_MixedText_HandlesBoth()
    {
        // "Hello 세계" = 6 non-CJK chars + 2 CJK chars
        var count = await _service.CountTokensAsync("Hello 세계", TestContext.Current.CancellationToken);

        count.Should().BeGreaterThan(0);
    }

    #endregion

    #region DisposeAsync

    [Fact]
    public async Task DisposeAsync_DisposesUnderlyingModel()
    {
        var model = Substitute.For<IEmbeddingModel>();
        var service = new LMSupplyEmbeddingService(model);

        await service.DisposeAsync();

        await model.Received(1).DisposeAsync();
    }

    #endregion
}
