using FluxIndex.Core.Evaluation;
using AwesomeAssertions;
using Xunit;

namespace FluxIndex.Core.Tests.Evaluation;

public class KeywordOverlapEvaluatorTests
{
    private readonly KeywordOverlapEvaluator _sut = new();

    // ---------------------------------------------------------------
    // Faithfulness
    // ---------------------------------------------------------------

    [Fact]
    public async Task Faithfulness_AllAnswerWordsInContext_Returns1()
    {
        var contexts = new[] { "The quick brown fox jumps over the lazy dog" };
        var answer = "quick brown fox";

        var (faithfulness, _) = await _sut.EvaluateAsync("test query", contexts, answer, TestContext.Current.CancellationToken);

        faithfulness.Should().Be(1.0);
    }

    [Fact]
    public async Task Faithfulness_NoAnswerWordsInContext_Returns0()
    {
        var contexts = new[] { "completely unrelated text about astronomy" };
        var answer = "neural network training process";

        var (faithfulness, _) = await _sut.EvaluateAsync("test query", contexts, answer, TestContext.Current.CancellationToken);

        faithfulness.Should().Be(0.0);
    }

    [Fact]
    public async Task Faithfulness_PartialOverlap_ReturnsFraction()
    {
        var contexts = new[] { "machine learning algorithms are powerful" };
        // "machine", "learning" overlap; "quantum" does not (after stopword removal)
        var answer = "machine learning quantum";

        var (faithfulness, _) = await _sut.EvaluateAsync("test query", contexts, answer, TestContext.Current.CancellationToken);

        // 2 out of 3 content words appear in context
        faithfulness.Should().BeApproximately(2.0 / 3.0, 0.01);
    }

    [Fact]
    public async Task Faithfulness_CaseInsensitive()
    {
        var contexts = new[] { "Artificial Intelligence is evolving" };
        var answer = "artificial INTELLIGENCE evolving";

        var (faithfulness, _) = await _sut.EvaluateAsync("test query", contexts, answer, TestContext.Current.CancellationToken);

        faithfulness.Should().Be(1.0);
    }

    [Fact]
    public async Task Faithfulness_EmptyContexts_Returns0()
    {
        var contexts = Array.Empty<string>();
        var answer = "some meaningful answer";

        var (faithfulness, _) = await _sut.EvaluateAsync("test query", contexts, answer, TestContext.Current.CancellationToken);

        faithfulness.Should().Be(0.0);
    }

    [Fact]
    public async Task Faithfulness_MultipleContextsCombined()
    {
        var contexts = new[] { "alpha beta", "gamma delta" };
        var answer = "alpha gamma epsilon";

        var (faithfulness, _) = await _sut.EvaluateAsync("test query", contexts, answer, TestContext.Current.CancellationToken);

        // 2 of 3 content words found across combined contexts
        faithfulness.Should().BeApproximately(2.0 / 3.0, 0.01);
    }

    // ---------------------------------------------------------------
    // Relevancy
    // ---------------------------------------------------------------

    [Fact]
    public async Task Relevancy_AllQueryWordsInAnswer_Returns1()
    {
        var answer = "machine learning algorithms are powerful tools";

        var (_, relevancy) = await _sut.EvaluateAsync("machine learning algorithms", ["some context"], answer, TestContext.Current.CancellationToken);

        relevancy.Should().Be(1.0);
    }

    [Fact]
    public async Task Relevancy_NoQueryWordsInAnswer_Returns0()
    {
        var answer = "completely different topic here";

        var (_, relevancy) = await _sut.EvaluateAsync("quantum physics entanglement", ["some context"], answer, TestContext.Current.CancellationToken);

        relevancy.Should().Be(0.0);
    }

    [Fact]
    public async Task Relevancy_PartialQueryOverlap_ReturnsFraction()
    {
        // After stopword removal: "machine", "learning", "deep"
        var answer = "machine processing unit";

        var (_, relevancy) = await _sut.EvaluateAsync("machine learning deep", ["some context"], answer, TestContext.Current.CancellationToken);

        // Only "machine" appears in answer: 1/3
        relevancy.Should().BeApproximately(1.0 / 3.0, 0.01);
    }

    [Fact]
    public async Task Relevancy_StopwordsExcludedFromQueryWords()
    {
        // "the", "is", "of" are stopwords; only "cat" is a content word
        var answer = "the cat sat on the mat";

        var (_, relevancy) = await _sut.EvaluateAsync("the cat is of", ["some context"], answer, TestContext.Current.CancellationToken);

        // Only "cat" counted from query; "cat" present in answer => 1.0
        relevancy.Should().Be(1.0);
    }

    // ---------------------------------------------------------------
    // Stopword filtering
    // ---------------------------------------------------------------

    [Fact]
    public async Task Stopwords_EnglishStopwordsFiltered()
    {
        var contexts = new[] { "important data" };
        // "the" and "is" are stopwords; only "important" and "data" are content words
        var answer = "the important data is here";

        var (faithfulness, _) = await _sut.EvaluateAsync("test", contexts, answer, TestContext.Current.CancellationToken);

        // "important", "data", "here" are non-stopwords after tokenizer (words > 1 char)
        // "important" + "data" in context, "here" is not => 2/3
        faithfulness.Should().BeApproximately(2.0 / 3.0, 0.01);
    }

    [Fact]
    public async Task Stopwords_KoreanStopwordsFiltered()
    {
        // Korean particles should be filtered from query
        var answer = "인공지능 기술 발전";

        var (_, relevancy) = await _sut.EvaluateAsync("인공지능 은 기술 의", ["some context"], answer, TestContext.Current.CancellationToken);

        // After Korean stopword removal ("은", "의" removed), query content words: "인공지능", "기술"
        // Both present in answer => 1.0
        relevancy.Should().Be(1.0);
    }

    // ---------------------------------------------------------------
    // Edge cases
    // ---------------------------------------------------------------

    [Fact]
    public async Task EmptyAnswer_ReturnsBothZero()
    {
        var (faithfulness, relevancy) = await _sut.EvaluateAsync("some query", ["some context"], "", TestContext.Current.CancellationToken);

        faithfulness.Should().Be(0.0);
        relevancy.Should().Be(0.0);
    }

    [Fact]
    public async Task WhitespaceOnlyAnswer_ReturnsBothZero()
    {
        var (faithfulness, relevancy) = await _sut.EvaluateAsync("some query", ["some context"], "   \t  \n  ", TestContext.Current.CancellationToken);

        faithfulness.Should().Be(0.0);
        relevancy.Should().Be(0.0);
    }

    [Fact]
    public async Task StopwordOnlyAnswer_FaithfulnessIs1()
    {
        // Only stopwords in answer => vacuously faithful
        var (faithfulness, _) = await _sut.EvaluateAsync("test query", ["anything"], "the is a an", TestContext.Current.CancellationToken);

        faithfulness.Should().Be(1.0);
    }

    [Fact]
    public async Task StopwordOnlyQuery_RelevancyIs1()
    {
        // All query words are stopwords => vacuously relevant
        var (_, relevancy) = await _sut.EvaluateAsync("the is and or", ["anything"], "some answer text", TestContext.Current.CancellationToken);

        relevancy.Should().Be(1.0);
    }

    [Fact]
    public async Task EmptyQuery_RelevancyIs1()
    {
        var (_, relevancy) = await _sut.EvaluateAsync("", ["some context"], "some answer", TestContext.Current.CancellationToken);

        relevancy.Should().Be(1.0);
    }

    [Fact]
    public async Task ScoresClampedTo01Range()
    {
        // Normal usage should always produce [0..1], but verify clamp behavior
        var contexts = new[] { "alpha beta gamma delta epsilon" };
        var answer = "alpha beta gamma delta epsilon";

        var (faithfulness, relevancy) = await _sut.EvaluateAsync("alpha beta gamma", contexts, answer, TestContext.Current.CancellationToken);

        faithfulness.Should().BeGreaterThanOrEqualTo(0.0).And.BeLessThanOrEqualTo(1.0);
        relevancy.Should().BeGreaterThanOrEqualTo(0.0).And.BeLessThanOrEqualTo(1.0);
    }

    [Fact]
    public async Task CancellationToken_Respected()
    {
        using var cts = new CancellationTokenSource();
        // Should complete without throwing when not cancelled
        var (faithfulness, relevancy) = await _sut.EvaluateAsync(
            "test",
            ["context"],
            "answer",
            cts.Token);

        faithfulness.Should().BeGreaterThanOrEqualTo(0.0);
        relevancy.Should().BeGreaterThanOrEqualTo(0.0);
    }

    [Fact]
    public async Task PunctuationStrippedFromTokens()
    {
        var contexts = new[] { "hello world" };
        var answer = "hello, world!";

        var (faithfulness, _) = await _sut.EvaluateAsync("test", contexts, answer, TestContext.Current.CancellationToken);

        faithfulness.Should().Be(1.0);
    }

    [Fact]
    public async Task SingleCharTokensFiltered()
    {
        // Single-char tokens like "I" (after lowering to "i") should be filtered by length > 1
        var contexts = new[] { "important result" };
        var answer = "I got important result";

        var (faithfulness, _) = await _sut.EvaluateAsync("test", contexts, answer, TestContext.Current.CancellationToken);

        // "got" is not in context, "important" + "result" are => 2/3
        faithfulness.Should().BeApproximately(2.0 / 3.0, 0.01);
    }

    // ---------------------------------------------------------------
    // Interface contract
    // ---------------------------------------------------------------

    [Fact]
    public void ImplementsIResponseEvaluator()
    {
        _sut.Should().BeAssignableTo<IResponseEvaluator>();
    }

    // ---------------------------------------------------------------
    // EvalRunResult aggregation
    // ---------------------------------------------------------------

    [Fact]
    public void EvalRunResult_EmptyCases_AllZero()
    {
        var result = new EvalRunResult();

        result.AverageFaithfulness.Should().Be(0.0);
        result.AverageRelevancy.Should().Be(0.0);
        result.OverallScore.Should().Be(0.0);
        result.ErrorCount.Should().Be(0);
    }

    [Fact]
    public void EvalRunResult_AveragesCorrect()
    {
        var result = new EvalRunResult
        {
            Cases =
            [
                new EvalCaseResult { Question = "q1", GeneratedAnswer = "a1", FaithfulnessScore = 0.8, RelevancyScore = 0.6 },
                new EvalCaseResult { Question = "q2", GeneratedAnswer = "a2", FaithfulnessScore = 0.4, RelevancyScore = 1.0 },
            ]
        };

        result.AverageFaithfulness.Should().BeApproximately(0.6, 0.001);
        result.AverageRelevancy.Should().BeApproximately(0.8, 0.001);
        result.OverallScore.Should().BeApproximately(0.7, 0.001);
    }

    [Fact]
    public void EvalRunResult_ErrorCount()
    {
        var result = new EvalRunResult
        {
            Cases =
            [
                new EvalCaseResult { Question = "q1", GeneratedAnswer = "a1" },
                new EvalCaseResult { Question = "q2", GeneratedAnswer = "a2", Error = "timeout" },
                new EvalCaseResult { Question = "q3", GeneratedAnswer = "a3", Error = "failed" },
            ]
        };

        result.ErrorCount.Should().Be(2);
    }

    // ---------------------------------------------------------------
    // QATestCase record
    // ---------------------------------------------------------------

    [Fact]
    public void QATestCase_DefaultExpectedAnswerIsNull()
    {
        var tc = new QATestCase("What is AI?");
        tc.ExpectedAnswer.Should().BeNull();
    }

    [Fact]
    public void QATestCase_WithExpectedAnswer()
    {
        var tc = new QATestCase("What is AI?", "Artificial Intelligence");
        tc.ExpectedAnswer.Should().Be("Artificial Intelligence");
    }
}
