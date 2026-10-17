using System;
using System.Threading;
using System.Threading.Tasks;

namespace Geuneda.Account.Server
{
	/// <summary>
	/// 1회용 값 저장소입니다. 애플·구글 id 토큰 replay 방어용 nonce 와 연동 충돌 티켓을 함께 둡니다(<see cref="OneTimeValueKind"/>).
	/// 서버를 여러 대 두면 모든 서버가 같은 저장소를 봐야 합니다(데이터베이스, Redis 등. 만료된 기록은 TTL 로 정리).
	/// 샘플·테스트·서버 한 대는 <see cref="InMemoryOneTimeValueStore"/> 를 씁니다.
	/// </summary>
	public interface IOneTimeValueStore
	{
		/// <summary>
		/// 기록을 넣습니다(같은 용도·해시가 있으면 덮어씀). 더 받을 수 없으면 false 이고 서버는 503 <c>RetryLater</c> 로 답합니다.
		/// nonce 는 인증 없이 발급되므로 저장소는 기록 수가 끝없이 늘지 않게 합니다.
		/// </summary>
		Task<bool> TryAddAsync(OneTimeValue value, DateTimeOffset now, CancellationToken cancellationToken);

		/// <summary>
		/// 그 용도·해시의 기록을 지우고, 지운 기록이 만료 전이었으면 돌려줍니다. 없거나 만료되었으면 null 입니다. 원자적이어야 합니다(같은 값으로 동시에 와도
		/// 한 요청만 기록을 받음. 데이터베이스라면 조건부 삭제, Redis 라면 <c>GETDEL</c>).
		/// </summary>
		Task<OneTimeValue> TryConsumeAsync(OneTimeValueKind kind, string hash, DateTimeOffset now, CancellationToken cancellationToken);
	}
}
