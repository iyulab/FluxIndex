using FluxIndex.Core.Application.Services.Base;
using FluxIndex.Core.Domain.ValueObjects;
using Xunit;

namespace FluxIndex.Core.Tests.Domain;

/// <summary>
/// <see cref="EmbeddingIdentity"/>가 지키는 불변식을 고정한다:
/// «같은 Identity = 같은 벡터 공간 = 비교 가능한 벡터».
/// 특히 <see cref="EmbeddingIdentity.Revision"/> 도입이 기존 소비자의 Fingerprint를
/// 바꾸지 않는다는 것 — 그것이 바뀌면 이미 저장된 컬렉션/테이블이 조용히 개명된다.
/// </summary>
public class EmbeddingIdentityTests
{
    private static EmbeddingIdentity Identity(string? revision = null) => new()
    {
        Provider = "LMSupply",
        Model = "multilingual-e5-base",
        Dimension = 768,
        Revision = revision
    };

    [Fact]
    public void Fingerprint_IsEightLowercaseHexCharacters()
    {
        var fingerprint = Identity().Fingerprint;

        Assert.Equal(8, fingerprint.Length);
        Assert.All(fingerprint, c => Assert.True(
            (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'),
            $"unexpected character '{c}' in fingerprint"));
    }

    /// <summary>
    /// 회귀 가드: Revision을 설정하지 않은 소비자의 Fingerprint는 Revision 도입 «이전»과
    /// 바이트 단위로 같아야 한다. 이 값은 SHA256("lmsupply:multilingual-e5-base")의 앞 8자로,
    /// Revision 필드가 존재하지 않던 시점의 구현이 내던 값이다.
    /// </summary>
    [Fact]
    public void Fingerprint_WithoutRevision_MatchesThePreRevisionHash()
    {
        Assert.Equal("9d492bc4", Identity().Fingerprint);
    }

    [Fact]
    public void Fingerprint_ChangesWhenRevisionIsSet()
    {
        Assert.NotEqual(Identity().Fingerprint, Identity("r2").Fingerprint);
    }

    [Fact]
    public void Fingerprint_DiffersBetweenRevisions()
    {
        Assert.NotEqual(Identity("r2").Fingerprint, Identity("r3").Fingerprint);
    }

    /// <summary>
    /// 설정에서 바인딩된 빈 문자열은 «미설정»으로 취급한다. 그러지 않으면 빈 값을 바인딩한
    /// 소비자의 컬렉션이 업그레이드만으로 개명된다 — 이 기능이 막으려는 바로 그 종류의 무음 파손.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Fingerprint_TreatsBlankRevisionAsUnset(string? revision)
    {
        Assert.Equal(Identity().Fingerprint, Identity(revision).Fingerprint);
    }

    /// <summary>
    /// Revision은 불투명한 토큰이라 대소문자를 보존한다 — Provider/Model과 달리 관례적
    /// 표기 흔들림이 아니라 «구분» 자체가 존재 이유이므로, 접어버리면 호환 불가한 두 공간이
    /// 한 Fingerprint를 공유하게 된다.
    /// </summary>
    [Fact]
    public void Fingerprint_PreservesRevisionCase()
    {
        Assert.NotEqual(Identity("R2").Fingerprint, Identity("r2").Fingerprint);
    }

    [Fact]
    public void Fingerprint_IgnoresProviderAndModelCase()
    {
        var upper = new EmbeddingIdentity { Provider = "LMSUPPLY", Model = "MULTILINGUAL-E5-BASE", Dimension = 768 };

        Assert.Equal(Identity().Fingerprint, upper.Fingerprint);
    }

    [Fact]
    public void Equality_DistinguishesRevisions()
    {
        Assert.NotEqual(Identity("r2"), Identity("r3"));
        Assert.Equal(Identity("r2"), Identity("r2"));
    }

    [Fact]
    public void ToString_IncludesRevisionOnlyWhenSet()
    {
        Assert.DoesNotContain("@", Identity().ToString(), StringComparison.Ordinal);
        Assert.Contains("@r2", Identity("r2").ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void GetIdentity_CarriesTheRevisionDeclaredByTheService()
    {
        Assert.Equal("r2", new RevisionedEmbeddingService().GetIdentity().Revision);
    }

    /// <summary>
    /// Revision을 선언하지 않은 기존 하위 클래스는 seam 도입 후에도 Revision이 없어야 한다.
    /// </summary>
    [Fact]
    public void GetIdentity_HasNoRevisionByDefault()
    {
        Assert.Null(new PlainEmbeddingService().GetIdentity().Revision);
    }

    private class PlainEmbeddingService : EmbeddingServiceBase
    {
        public override int GetEmbeddingDimension() => 768;
        public override string GetModelName() => "multilingual-e5-base";
        protected override string GetProviderName() => "LMSupply";

        protected override Task<float[]> EmbedCoreAsync(string text, CancellationToken cancellationToken)
            => Task.FromResult(new float[768]);
    }

    private sealed class RevisionedEmbeddingService : PlainEmbeddingService
    {
        protected override string? GetRevision() => "r2";
    }
}
