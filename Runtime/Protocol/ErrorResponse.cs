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
	}
}
