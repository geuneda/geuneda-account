// ReSharper disable once CheckNamespace

namespace Geuneda.Account
{
	/// <summary>
	/// 인증 서버 경로입니다. 서버 접두사(예: <c>/auth</c>) 아래의 상대 경로이고, 클라이언트는 접두사까지 담은 기본 주소(예: <c>https://example.com/auth/</c>)에 붙입니다.
	/// </summary>
	internal static class AccountRoutes
	{
		/// <summary>POST <see cref="LoginRequest"/> → <see cref="TokenResponse"/></summary>
		public const string Login = "login";
		/// <summary>POST <see cref="RefreshRequest"/> → <see cref="TokenResponse"/></summary>
		public const string Refresh = "refresh";
		/// <summary>POST <see cref="LogoutRequest"/> → 204</summary>
		public const string Logout = "logout";
		/// <summary>GET → <see cref="LinksResponse"/>, POST <see cref="LinkRequest"/> → <see cref="LinksResponse"/>. access 토큰 필요</summary>
		public const string Links = "links";
		/// <summary>POST <see cref="LinkRequest"/> → <see cref="LinksResponse"/>. 다른 계정의 연동을 이 계정으로 옮김. access 토큰 필요</summary>
		public const string LinksTransfer = "links/transfer";
		/// <summary>DELETE → 204. 계정 삭제. access 토큰 필요</summary>
		public const string Account = "account";

		/// <summary>DELETE → <see cref="LinksResponse"/>. 연동 해제. access 토큰 필요</summary>
		public static string Link(string platform)
		{
			return Links + "/" + System.Uri.EscapeDataString(platform);
		}
	}
}
