using FluxIndex.Core.Application.Models;

namespace FluxIndex.Core.Application.Interfaces;

/// <summary>
/// 벡터 스토어의 컬렉션 관리 인터페이스.
/// <see cref="IVectorStore"/>가 단일 컬렉션 CRUD를 담당한다면,
/// 이 인터페이스는 cross-collection 관리(열거, 조회, 삭제)를 담당한다.
/// </summary>
/// <remarks>
/// 구현체는 일반적으로 <see cref="IVectorStore"/>와 동일 클래스가 구현한다.
/// Default method는 <see cref="NotSupportedException"/>을 throw하여,
/// 미구현 스토어도 컴파일 가능하게 한다.
/// </remarks>
public interface IVectorStoreManager
{
    /// <summary>
    /// 스토어가 관리하는 모든 벡터 컬렉션을 열거한다.
    /// fingerprint 없는 레거시 테이블도 포함된다.
    /// </summary>
    Task<IReadOnlyList<CollectionInfo>> ListCollectionsAsync(
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    /// <summary>
    /// 특정 컬렉션의 정보를 조회한다.
    /// </summary>
    /// <returns>컬렉션이 존재하면 정보, 없으면 null.</returns>
    Task<CollectionInfo?> GetCollectionInfoAsync(
        string collectionName,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    /// <summary>
    /// 특정 컬렉션과 그 벡터 데이터를 영구 삭제한다.
    /// </summary>
    /// <returns>삭제 성공 시 true, 컬렉션이 존재하지 않으면 false.</returns>
    Task<bool> DeleteCollectionAsync(
        string collectionName,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException();
}
