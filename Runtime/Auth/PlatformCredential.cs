using System;

// ReSharper disable once CheckNamespace

namespace Geuneda.Account
{
	/// <summary>
	/// 플랫폼 로그인으로 받은 자격입니다(게스트 비밀 값, id 토큰). 서버가 검증합니다. 로그에 남기지 마세요(<see cref="ToString"/> 은 자격을 빼고 보여 줌).
	/// </summary>
	public sealed class PlatformCredential
	{
		/// <summary><see cref="AccountPlatforms"/> 의 이름</summary>
		public string Platform { get; }

		public string Credential { get; }

		/// <exception cref="ArgumentException">둘 중 하나가 비어 있는 경우</exception>
		public PlatformCredential(string platform, string credential)
		{
			if (string.IsNullOrEmpty(platform))
			{
				throw new ArgumentException("Platform must not be empty", nameof(platform));
			}

			if (string.IsNullOrEmpty(credential))
			{
				throw new ArgumentException("Credential must not be empty", nameof(credential));
			}

			Platform = platform;
			Credential = credential;
		}

		public override string ToString()
		{
			return $"PlatformCredential({Platform})";
		}
	}
}
