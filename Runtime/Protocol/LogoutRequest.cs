// ReSharper disable once CheckNamespace

namespace Geuneda.Account
{
	/// <summary>로그아웃 요청입니다. 이 refresh 토큰과 같은 로그인에서 이어진 토큰을 모두 폐기합니다.</summary>
	[Preserve]
	internal sealed class LogoutRequest
	{
		[Preserve]
		public string RefreshToken { get; set; }
	}
}
