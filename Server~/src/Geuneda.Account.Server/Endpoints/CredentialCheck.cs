using Microsoft.AspNetCore.Http;

namespace Geuneda.Account.Server
{
	/// <summary><see cref="PlatformCredentials"/> 의 확인 결과입니다. 실패면 <see cref="Error"/> 가 응답입니다.</summary>
	internal readonly struct CredentialCheck
	{
		/// <summary>실패 응답. 성공이면 null 입니다.</summary>
		public IResult Error { get; }

		/// <summary>실패 이유(로그용). 지원하지 않는 플랫폼이면 null 입니다.</summary>
		public string FailureReason { get; }

		/// <summary>플랫폼 안의 고유 ID</summary>
		public string Subject { get; }

		private CredentialCheck(IResult error, string failureReason, string subject)
		{
			Error = error;
			FailureReason = failureReason;
			Subject = subject;
		}

		public static CredentialCheck Ok(string subject)
		{
			return new CredentialCheck(null, null, subject);
		}

		public static CredentialCheck Fail(IResult error, string failureReason)
		{
			return new CredentialCheck(error, failureReason, null);
		}
	}
}
