using System;

namespace Geuneda.Account.Server
{
	/// <summary>플랫폼 자격 검증 결과입니다.</summary>
	public readonly struct PlatformVerification
	{
		public bool IsValid { get; }

		/// <summary>플랫폼 안에서 사용자를 가리키는 고유 ID. 성공이면 비어 있지 않습니다.</summary>
		public string Subject { get; }

		/// <summary>실패 이유(로그용, 클라이언트에도 메시지로 감). 자격 원문을 넣지 않습니다.</summary>
		public string FailureReason { get; }

		private PlatformVerification(bool isValid, string subject, string failureReason)
		{
			IsValid = isValid;
			Subject = subject;
			FailureReason = failureReason;
		}

		/// <exception cref="ArgumentException"><paramref name="subject"/> 가 비어 있는 경우</exception>
		public static PlatformVerification Valid(string subject)
		{
			if (string.IsNullOrEmpty(subject))
			{
				throw new ArgumentException("Subject must not be empty", nameof(subject));
			}

			return new PlatformVerification(true, subject, null);
		}

		public static PlatformVerification Invalid(string reason)
		{
			return new PlatformVerification(false, null, reason ?? "invalid credential");
		}
	}
}
