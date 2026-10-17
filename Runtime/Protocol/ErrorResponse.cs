// ReSharper disable once CheckNamespace

namespace Geuneda.Account
{
	/// <summary>인증 서버의 오류 응답 본문입니다. HTTP 4xx 와 함께 옵니다.</summary>
	[Preserve]
	internal sealed class ErrorResponse
	{
		/// <summary><see cref="AccountResultCodes"/> 의 서버 코드(3001~3099)</summary>
		[Preserve]
		public int Code { get; set; }

		/// <summary>개발자용 설명. 사용자에게 그대로 보여 주지 않습니다.</summary>
		[Preserve]
		public string Message { get; set; }

		/// <summary><see cref="AccountResultCodes.LoginConflict"/> 일 때 그 플랫폼 계정이 연동된 계정의 ID 입니다.</summary>
		[Preserve]
		public string ConflictAccountId { get; set; }

		/// <summary>
		/// <see cref="AccountResultCodes.LoginConflict"/> 일 때 서버가 만든 1회용 티켓입니다(그 플랫폼 계정과 연동을 요청한 계정에 묶임, 기본 5분).
		/// 플랫폼 로그인을 다시 하지 않고 그 계정으로 로그인하거나 요청한 계정으로 옮겨올 때 id 토큰 대신 보냅니다.
		/// </summary>
		[Preserve]
		public string ConflictTicket { get; set; }
	}
}
