using System.Threading;
using System.Threading.Tasks;
using Geuneda.Account.Server;

namespace Geuneda.Account.SampleServer
{
	/// <summary>
	/// 연동·충돌·옮겨오기와 nonce 흐름을 시험하는 가짜 플랫폼입니다. 자격 <c>sample:&lt;id&gt;:&lt;nonce&gt;</c> 를 그대로 믿고, 끝의 nonce 가 요청의 nonce 와 같아야 합니다
	/// (애플·구글 id 토큰의 <c>nonce</c> 클레임 역할). 운영 서버에 등록하지 마세요.
	/// </summary>
	public sealed class SamplePlatformVerifier : IPlatformVerifier
	{
		public const string Name = "sample";
		private const string Prefix = "sample:";

		public string Platform => Name;

		public bool RequiresNonce => true;

		public Task<PlatformVerification> VerifyAsync(string credential, string nonce, CancellationToken cancellationToken)
		{
			var separator = credential.LastIndexOf(':');

			if (!credential.StartsWith(Prefix) || separator <= Prefix.Length)
			{
				return Task.FromResult(PlatformVerification.Invalid("sample credential must be 'sample:<id>:<nonce>'"));
			}

			return Task.FromResult(credential.Substring(separator + 1) == nonce
				? PlatformVerification.Valid(credential.Substring(Prefix.Length, separator - Prefix.Length))
				: PlatformVerification.NonceMismatch("sample credential is not bound to the nonce"));
		}
	}
}
