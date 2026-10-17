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

		/// <summary>
		/// true 면 자격이 서버가 발급한 nonce 에 묶여 있어야 합니다(애플·구글 id 토큰의 <c>nonce</c> 클레임, replay 방어). 서버는 요청에 nonce 가 없으면
		/// <see cref="AccountResultCodes.NonceMissing"/> 로 거절하고, 검증을 통과하면 그 nonce 가 발급한 적 있고 만료되지 않았고 아직 쓰지 않은 것인지 확인해 소비합니다.
		/// </summary>
		bool RequiresNonce { get; }

		/// <summary>
		/// 자격을 검증합니다. 잘못된 자격은 예외가 아니라 <see cref="PlatformVerification.Invalid"/>, 자격이 <paramref name="nonce"/> 에 묶이지 않았으면
		/// <see cref="PlatformVerification.NonceMismatch"/> 입니다.
		/// </summary>
		/// <param name="nonce">요청의 nonce 원문. <see cref="RequiresNonce"/> 가 true 면 비어 있지 않고, false 면 null 입니다.</param>
		Task<PlatformVerification> VerifyAsync(string credential, string nonce, CancellationToken cancellationToken);
	}
}
