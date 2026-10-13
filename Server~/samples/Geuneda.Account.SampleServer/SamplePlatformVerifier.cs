using System.Threading;
using System.Threading.Tasks;
using Geuneda.Account.Server;

namespace Geuneda.Account.SampleServer
{
	/// <summary>
	/// 연동·충돌·옮겨오기를 시험하는 가짜 플랫폼입니다. 자격 <c>sample:&lt;id&gt;</c> 를 그대로 믿습니다. 운영 서버에 등록하지 마세요.
	/// </summary>
	public sealed class SamplePlatformVerifier : IPlatformVerifier
	{
		public const string Name = "sample";
		private const string Prefix = "sample:";

		public string Platform => Name;

		public Task<PlatformVerification> VerifyAsync(string credential, CancellationToken cancellationToken)
		{
			return Task.FromResult(credential.StartsWith(Prefix) && credential.Length > Prefix.Length
				? PlatformVerification.Valid(credential.Substring(Prefix.Length))
				: PlatformVerification.Invalid("sample credential must be 'sample:<id>'"));
		}
	}
}
