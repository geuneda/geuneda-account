using System;
using System.Buffers.Text;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace Geuneda.Account.Server
{
	/// <summary>
	/// 게스트 검증기입니다. 자격은 클라이언트가 만든 무작위 비밀 값(Base64url, 32~64바이트)이고, 고유 ID 는 그 SHA-256 해시라 서버에는 원문이 남지 않습니다.
	/// </summary>
	/// <remarks>
	/// 비밀 값을 잃으면(앱 데이터 삭제 등) 그 게스트 계정에는 다시 로그인할 수 없습니다. 게임은 다른 플랫폼 연동을 권합니다.
	/// </remarks>
	public sealed class GuestPlatformVerifier : IPlatformVerifier
	{
		public const int MinSecretBytes = 32;
		public const int MaxSecretBytes = 64;

		public string Platform => AccountPlatforms.Guest;

		/// <summary>비밀 값은 기기 밖으로 나가지 않는 값이라 nonce 를 쓰지 않습니다.</summary>
		public bool RequiresNonce => false;

		public Task<PlatformVerification> VerifyAsync(string credential, string nonce, CancellationToken cancellationToken)
		{
			if (string.IsNullOrEmpty(credential) || !Base64Url.IsValid(credential, out var length) || length < MinSecretBytes || length > MaxSecretBytes)
			{
				return Task.FromResult(PlatformVerification.Invalid($"guest secret must be base64url of {MinSecretBytes}-{MaxSecretBytes} bytes"));
			}

			var secret = Base64Url.DecodeFromChars(credential);
			var subject = Convert.ToHexStringLower(SHA256.HashData(secret));
			return Task.FromResult(PlatformVerification.Valid(subject));
		}
	}
}
