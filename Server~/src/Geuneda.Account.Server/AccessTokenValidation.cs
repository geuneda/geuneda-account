using System;

namespace Geuneda.Account.Server
{
	/// <summary>access 토큰 검증 결과입니다.</summary>
	public readonly struct AccessTokenValidation
	{
		public bool IsValid { get; }

		/// <summary>계정 ID(<c>sub</c>). 실패면 null 입니다.</summary>
		public string AccountId { get; }

		/// <summary>세션 ID(<c>sid</c>). 클라이언트가 보내지 않았으면 null 입니다.</summary>
		public string SessionId { get; }

		/// <summary>만료 시각(UTC)</summary>
		public DateTimeOffset ExpiresAt { get; }

		/// <summary>실패 이유(로그용). 성공이면 null 입니다.</summary>
		public string FailureReason { get; }

		private AccessTokenValidation(bool isValid, string accountId, string sessionId, DateTimeOffset expiresAt, string failureReason)
		{
			IsValid = isValid;
			AccountId = accountId;
			SessionId = sessionId;
			ExpiresAt = expiresAt;
			FailureReason = failureReason;
		}

		/// <summary>성공 결과입니다. 다른 검증기 구현이나 테스트 대역에서도 씁니다.</summary>
		/// <exception cref="ArgumentException"><paramref name="accountId"/> 가 비어 있는 경우</exception>
		public static AccessTokenValidation Valid(string accountId, string sessionId, DateTimeOffset expiresAt)
		{
			if (string.IsNullOrEmpty(accountId))
			{
				throw new ArgumentException("Account id must not be empty", nameof(accountId));
			}

			return new AccessTokenValidation(true, accountId, sessionId, expiresAt, null);
		}

		public static AccessTokenValidation Invalid(string reason)
		{
			return new AccessTokenValidation(false, null, null, default, reason ?? "invalid token");
		}
	}
}
