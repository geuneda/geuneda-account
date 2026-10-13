using System.Threading;
using System.Threading.Tasks;

namespace Geuneda.Account.Server
{
	/// <summary>
	/// access 토큰을 검증합니다. 게임 서버의 HTTP API 와 실시간 RPC 로그인에서 씁니다. 서명, 발급자, 대상, 만료를 확인합니다.
	/// </summary>
	/// <remarks>
	/// 토큰 자체만 검증합니다. 탈퇴한 계정의 토큰도 만료 전까지는 통과하므로, 필요하면 호출하는 쪽이 계정을 확인합니다.
	/// </remarks>
	public interface IAccessTokenValidator
	{
		/// <summary>검증 결과를 돌려줍니다. 잘못된 토큰도 예외 없이 <see cref="AccessTokenValidation.IsValid"/> false 입니다.</summary>
		Task<AccessTokenValidation> ValidateAsync(string accessToken, CancellationToken cancellationToken = default);
	}
}
