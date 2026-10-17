using System;

// ReSharper disable once CheckNamespace

namespace Geuneda.Account
{
	/// <summary>
	/// 플랫폼 로그인으로 받은 자격입니다(게스트 비밀 값, id 토큰). 서버가 검증합니다. 로그에 남기지 마세요(<see cref="ToString"/> 은 자격을 빼고 보여 줌).
	/// <see cref="AuthService"/> 가 받은 애플·구글 자격에는 id 토큰을 묶은 nonce 가, 연동 충돌로 받은 자격(<see cref="LinkResult.Credential"/>)에는 충돌 티켓이 함께 있습니다.
	/// 서버는 둘 다 한 번만 받습니다.
	/// </summary>
	public sealed class PlatformCredential
	{
		/// <summary><see cref="AccountPlatforms"/> 의 이름</summary>
		public string Platform { get; }

		public string Credential { get; }

		/// <summary>자격을 묶은 서버 발급 nonce 원문. nonce 를 쓰지 않는 플랫폼(게스트)이나 직접 만든 자격은 null 입니다.</summary>
		internal string Nonce { get; }

		/// <summary>연동 충돌 응답의 1회용 티켓. 있으면 로그인·옮겨오기 요청에 id 토큰 대신 이것을 보냅니다.</summary>
		internal string ConflictTicket { get; }

		/// <exception cref="ArgumentException">둘 중 하나가 비어 있는 경우</exception>
		public PlatformCredential(string platform, string credential) : this(platform, credential, null, null)
		{
		}

		internal PlatformCredential(string platform, string credential, string nonce, string conflictTicket)
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
			Nonce = string.IsNullOrEmpty(nonce) ? null : nonce;
			ConflictTicket = string.IsNullOrEmpty(conflictTicket) ? null : conflictTicket;
		}

		internal PlatformCredential WithConflictTicket(string conflictTicket)
		{
			return new PlatformCredential(Platform, Credential, null, conflictTicket);
		}

		public override string ToString()
		{
			return $"PlatformCredential({Platform})";
		}
	}
}
