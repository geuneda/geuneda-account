// ReSharper disable once CheckNamespace

namespace Geuneda.Account
{
	/// <summary>
	/// 기본 제공 로그인 플랫폼 이름입니다. 서버 검증기와 클라이언트 제공자가 같은 이름을 씁니다. 대소문자를 구분합니다.
	/// </summary>
	public static class AccountPlatforms
	{
		/// <summary>게스트. 자격은 앱이 만든 무작위 비밀 값이고 서버는 해시만 저장합니다.</summary>
		public const string Guest = "guest";
		/// <summary>Sign in with Apple. 자격은 애플 id 토큰입니다.</summary>
		public const string Apple = "apple";
		/// <summary>Google 로그인. 자격은 구글 id 토큰입니다.</summary>
		public const string Google = "google";
	}
}
