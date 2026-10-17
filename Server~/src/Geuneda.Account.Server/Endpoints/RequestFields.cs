namespace Geuneda.Account.Server
{
	/// <summary>요청 필드 형식 검사입니다.</summary>
	internal static class RequestFields
	{
		private const int MaxPlatformLength = 32;
		private const int MaxSessionIdLength = 64;
		private const int MaxRefreshTokenLength = 256;
		private const int MaxOneTimeValueLength = 128;

		public static bool IsPlatform(string platform)
		{
			return !string.IsNullOrEmpty(platform) && platform.Length <= MaxPlatformLength && IsToken(platform);
		}

		/// <summary>본문 크기 제한 안에서 비어 있지 않으면 됩니다. 내용은 검증기가 봅니다.</summary>
		public static bool IsCredential(string credential)
		{
			return !string.IsNullOrEmpty(credential);
		}

		/// <summary>없거나, 64자 이하의 영문·숫자·-·_ 입니다.</summary>
		public static bool IsOptionalSessionId(string sessionId)
		{
			return sessionId == null || (sessionId.Length > 0 && sessionId.Length <= MaxSessionIdLength && IsToken(sessionId));
		}

		/// <summary>
		/// 자격(과 선택 nonce) 또는 연동 충돌 티켓 중 하나만 있습니다. 둘 다 오면(티켓과 id 토큰) 형식 오류입니다.
		/// nonce·티켓은 128자 이하의 영문·숫자·-·_·. 입니다(발급한 값은 Base64url 43자).
		/// </summary>
		public static bool IsCredentialOrTicket(string credential, string nonce, string conflictTicket)
		{
			return conflictTicket == null
				? IsCredential(credential) && (nonce == null || IsOneTimeValue(nonce))
				: credential == null && nonce == null && IsOneTimeValue(conflictTicket);
		}

		public static bool IsRefreshToken(string refreshToken)
		{
			return !string.IsNullOrEmpty(refreshToken) && refreshToken.Length <= MaxRefreshTokenLength;
		}

		private static bool IsOneTimeValue(string value)
		{
			return value.Length > 0 && value.Length <= MaxOneTimeValueLength && IsToken(value);
		}

		private static bool IsToken(string value)
		{
			foreach (var c in value)
			{
				var valid = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '-' || c == '_' || c == '.';

				if (!valid)
				{
					return false;
				}
			}

			return true;
		}
	}
}
