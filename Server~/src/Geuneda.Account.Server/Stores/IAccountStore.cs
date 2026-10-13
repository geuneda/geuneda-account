using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Geuneda.Account.Server
{
	/// <summary>
	/// 계정과 로그인 수단(플랫폼, 고유 ID) 저장소입니다. 운영에서는 데이터베이스로 구현해 DI 에 등록하고,
	/// 샘플과 테스트는 <see cref="InMemoryAccountStore"/>(<see cref="AccountServerBuilder.AddInMemoryStores"/>)를 씁니다.
	/// </summary>
	/// <remarks>
	/// 구현은 다음을 원자적으로 지켜야 합니다(데이터베이스라면 고유 인덱스와 트랜잭션). 같은 로그인 수단(플랫폼, 고유 ID)은 한 계정에만 있고,
	/// 한 계정에는 플랫폼마다 로그인 수단이 하나이며, 계정에는 로그인 수단이 하나 이상 남습니다(옮겨오기로 빼앗긴 계정은 예외).
	/// </remarks>
	public interface IAccountStore
	{
		/// <summary>그 로그인 수단의 계정을 찾고, 없으면 새 계정과 함께 만듭니다. 같은 수단으로 동시에 처음 로그인해도 계정은 하나입니다.</summary>
		Task<AccountLookup> FindOrCreateAsync(string platform, string subject, DateTimeOffset now, CancellationToken cancellationToken);

		Task<bool> ExistsAsync(string accountId, CancellationToken cancellationToken);

		/// <summary>연동된 로그인 수단 목록입니다. 계정이 없으면 null 입니다.</summary>
		Task<IReadOnlyList<AccountLink>> GetLinksAsync(string accountId, CancellationToken cancellationToken);

		/// <summary>로그인 수단을 이 계정에 연동합니다. 다른 계정에 있으면 연동하지 않고 그 계정 ID 를 돌려줍니다.</summary>
		Task<LinkOutcome> LinkAsync(string accountId, string platform, string subject, DateTimeOffset now, CancellationToken cancellationToken);

		/// <summary>
		/// 로그인 수단을 이 계정으로 옮깁니다. 다른 계정에 있으면 그 계정에서 빼서 옮기고, 어디에도 없으면 연동합니다.
		/// 빼앗긴 계정에 로그인 수단이 남지 않으면 그 계정은 더 로그인할 수 없습니다(삭제하지 않음).
		/// </summary>
		Task<LinkOutcome> TransferAsync(string accountId, string platform, string subject, DateTimeOffset now, CancellationToken cancellationToken);

		/// <summary>그 플랫폼의 로그인 수단을 해제합니다. 마지막 로그인 수단이면 해제하지 않습니다.</summary>
		Task<UnlinkOutcome> UnlinkAsync(string accountId, string platform, CancellationToken cancellationToken);

		/// <summary>계정과 로그인 수단을 지웁니다. 계정이 없었으면 false 입니다.</summary>
		Task<bool> DeleteAsync(string accountId, CancellationToken cancellationToken);
	}
}
