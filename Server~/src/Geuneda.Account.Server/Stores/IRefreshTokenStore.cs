using System;
using System.Threading;
using System.Threading.Tasks;

namespace Geuneda.Account.Server
{
	/// <summary>
	/// refresh 토큰 저장소입니다. 운영에서는 데이터베이스로 구현해 DI 에 등록하고(만료된 기록은 TTL 인덱스 등으로 정리),
	/// 샘플과 테스트는 <see cref="InMemoryRefreshTokenStore"/> 를 씁니다.
	/// </summary>
	public interface IRefreshTokenStore
	{
		/// <summary>새 로그인과 첫 토큰 기록을 함께 저장합니다.</summary>
		Task CreateAsync(RefreshTokenFamily family, RefreshTokenRecord token, CancellationToken cancellationToken);

		/// <summary>토큰 해시로 기록을 찾습니다. 없으면 null 입니다.</summary>
		Task<RefreshTokenRecord> FindTokenAsync(string tokenHash, CancellationToken cancellationToken);

		/// <summary>로그인을 찾습니다. 없으면 null 입니다.</summary>
		Task<RefreshTokenFamily> FindFamilyAsync(string familyId, CancellationToken cancellationToken);

		/// <summary>
		/// 로그인의 현재 토큰이 <paramref name="expectedCurrentTokenHash"/> 이고 폐기되지 않았을 때만 <paramref name="updated"/> 로 바꾸고
		/// <paramref name="newToken"/> 기록을 추가한 뒤 true 를 돌려줍니다. 두 변경은 원자적이어야 합니다(데이터베이스라면 한 트랜잭션의 조건부 갱신).
		/// 같은 로그인을 동시에 재발급해도 한 요청만 성공하게 합니다.
		/// </summary>
		Task<bool> TryRotateAsync(string familyId, string expectedCurrentTokenHash, RefreshTokenFamily updated, RefreshTokenRecord newToken,
			CancellationToken cancellationToken);

		/// <summary>로그인을 폐기합니다. 없으면 아무것도 하지 않습니다.</summary>
		Task RevokeFamilyAsync(string familyId, DateTimeOffset now, CancellationToken cancellationToken);

		/// <summary>계정의 로그인을 모두 폐기합니다.</summary>
		Task RevokeAccountAsync(string accountId, DateTimeOffset now, CancellationToken cancellationToken);
	}
}
