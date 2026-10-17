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

		/// <summary>플랫폼 자격(게스트 비밀 값, id 토큰). 서버가 검증합니다. <see cref="ConflictTicket"/> 을 보내면 비웁니다.</summary>
		[Preserve]
		public string Credential { get; set; }

		/// <summary>
		/// 서버가 발급한 nonce 원문(<see cref="AccountRoutes.Nonce"/>). id 토큰이 이 nonce 에 묶여 있어야 합니다(애플은 SHA-256 16진 소문자, 구글은 그대로).
		/// 게스트처럼 nonce 를 쓰지 않는 플랫폼은 비웁니다.
		/// </summary>
		[Preserve]
		public string Nonce { get; set; }

		/// <summary>
		/// 연동 충돌 응답의 티켓(<see cref="ErrorResponse.ConflictTicket"/>). 보내면 <c>Credential</c> 과 <c>Nonce</c> 는 비웁니다. 서버가 한 번만 받습니다.
		/// </summary>
		[Preserve]
		public string ConflictTicket { get; set; }

		/// <summary>로그인에 성공하면 시작할 세션 ID. access 토큰의 <c>sid</c> 로 들어갑니다. 없어도 됩니다.</summary>
		[Preserve]
		public string SessionId { get; set; }
	}
}
