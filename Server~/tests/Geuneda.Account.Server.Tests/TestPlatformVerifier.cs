using System.Threading;
using System.Threading.Tasks;

namespace Geuneda.Account.Server.Tests
{
	/// <summary>자격 <c>ok:&lt;id&gt;</c> 를 믿는 시험용 플랫폼입니다.</summary>
	internal sealed class TestPlatformVerifier : IPlatformVerifier
	{
		public const string Name = "test";

		public string Platform => Name;

		public static string Credential(string subject)
		{
			return "ok:" + subject;
		}

		public Task<PlatformVerification> VerifyAsync(string credential, CancellationToken cancellationToken)
		{
			return Task.FromResult(credential.StartsWith("ok:")
				? PlatformVerification.Valid(credential.Substring(3))
				: PlatformVerification.Invalid("not ok"));
		}
	}
}
