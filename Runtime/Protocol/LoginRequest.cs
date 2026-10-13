// ReSharper disable once CheckNamespace

namespace Geuneda.Account
{
	/// <summary>로그인 요청입니다. 그 플랫폼 계정이 처음이면 새 계정을 만듭니다.</summary>
	[Preserve]
	internal sealed class LoginRequest
	{
		/// <summary><see cref="AccountPlatforms"/> 의 이름</summary>
		[Preserve]
		public string Platform { get; set; }

		/// <summary>플랫폼 자격(게스트 비밀 값, id 토큰). 서버가 검증합니다.</summary>
		[Preserve]
		public string Credential { get; set; }

		/// <summary>로그인에 성공하면 시작할 세션 ID. access 토큰의 <c>sid</c> 로 들어갑니다. 없어도 됩니다.</summary>
		[Preserve]
		public string SessionId { get; set; }
	}
}
