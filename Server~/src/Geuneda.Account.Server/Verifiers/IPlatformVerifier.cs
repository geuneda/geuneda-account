using System.Threading;
using System.Threading.Tasks;

namespace Geuneda.Account.Server
{
	/// <summary>
	/// 클라이언트가 보낸 플랫폼 자격(게스트 비밀 값, id 토큰)을 검증해 플랫폼 안의 고유 ID 를 돌려줍니다.
	/// <see cref="AccountServerBuilder.AddPlatformVerifier{TVerifier}"/> 로 등록하고, 플랫폼 이름마다 하나만 둡니다.
	/// </summary>
	public interface IPlatformVerifier
	{
		/// <summary>플랫폼 이름(<see cref="AccountPlatforms"/>). 클라이언트 제공자의 이름과 같아야 합니다.</summary>
		string Platform { get; }

		/// <summary>자격을 검증합니다. 잘못된 자격은 예외가 아니라 <see cref="PlatformVerification.Invalid"/> 입니다.</summary>
		Task<PlatformVerification> VerifyAsync(string credential, CancellationToken cancellationToken);
	}
}
