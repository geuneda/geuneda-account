using System.Threading;
using System.Threading.Tasks;

namespace Geuneda.Account.Server.Tests
{
	/// <summary>자격 <c>ok:&lt;id&gt;:&lt;nonce&gt;</c> 를 믿는 nonce 시험용 플랫폼입니다. 끝의 nonce 가 id 토큰의 <c>nonce</c> 클레임 역할입니다.</summary>
	internal sealed class NonceTestPlatformVerifier : IPlatformVerifier
	{
		public const string Name = "test-nonce";

		public string Platform => Name;

		public bool RequiresNonce => true;

		public static string Credential(string subject, string nonce)
		{
			return "ok:" + subject + ":" + nonce;
		}

		public Task<PlatformVerification> VerifyAsync(string credential, string nonce, CancellationToken cancellationToken)
		{
			var parts = credential.Split(':');

			if (parts.Length != 3 || parts[0] != "ok")
			{
				return Task.FromResult(PlatformVerification.Invalid("not ok"));
			}

			return Task.FromResult(parts[2] == nonce ? PlatformVerification.Valid(parts[1]) : PlatformVerification.NonceMismatch("not bound"));
		}
	}
}
