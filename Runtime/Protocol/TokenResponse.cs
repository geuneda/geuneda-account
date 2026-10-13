// ReSharper disable once CheckNamespace

namespace Geuneda.Account
{
	/// <summary>로그인과 재발급의 응답입니다.</summary>
	[Preserve]
	internal sealed class TokenResponse
	{
		[Preserve]
		public string AccountId { get; set; }

		/// <summary>JWT. 게임 서버 요청의 <c>Authorization: Bearer</c> 와 실시간 RPC 로그인에 씁니다.</summary>
		[Preserve]
		public string AccessToken { get; set; }

		/// <summary>access 토큰의 남은 수명(초). 기기 시계와 무관하게 받은 시점부터 셉니다.</summary>
		[Preserve]
		public int AccessTokenExpiresIn { get; set; }

		/// <summary>불투명한 무작위 문자열. 재발급과 로그아웃에만 씁니다.</summary>
		[Preserve]
		public string RefreshToken { get; set; }

		/// <summary>refresh 토큰의 남은 수명(초)</summary>
		[Preserve]
		public int RefreshTokenExpiresIn { get; set; }

		/// <summary>이 로그인으로 계정을 새로 만들었으면 true 입니다. 재발급에서는 항상 false 입니다.</summary>
		[Preserve]
		public bool IsNewAccount { get; set; }
	}
}
