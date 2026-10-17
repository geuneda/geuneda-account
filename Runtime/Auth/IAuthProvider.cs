using System.Threading;
using Cysharp.Threading.Tasks;
using Geuneda.Core;

// ReSharper disable once CheckNamespace

namespace Geuneda.Account
{
	/// <summary>
	/// 플랫폼 로그인 제공자입니다(게스트, 애플, 구글). 플랫폼 SDK 로 로그인해 서버에 보낼 자격을 만듭니다. <see cref="AuthServiceOptions.Providers"/> 에 등록합니다.
	/// </summary>
	public interface IAuthProvider
	{
		/// <summary>플랫폼 이름(<see cref="AccountPlatforms"/>). 서버 검증기의 이름과 같아야 합니다.</summary>
		string Platform { get; }

		/// <summary>
		/// true 면 <see cref="AuthService"/> 가 플랫폼 로그인 전에 인증 서버에서 일회용 nonce 를 받아 <see cref="SignInAsync"/> 에 넘깁니다(애플·구글 id 토큰 replay 방어).
		/// 서버 검증기의 <c>RequiresNonce</c> 와 같아야 합니다. 게스트는 false 입니다.
		/// </summary>
		bool RequiresNonce { get; }

		/// <summary>
		/// 플랫폼 로그인을 해서 자격을 받습니다. 사용자가 취소하면 <c>ResultCodes.Cancelled</c>, SDK 실패는 <see cref="AccountResultCodes.PlatformSignInFailed"/> 입니다.
		/// </summary>
		/// <param name="nonce">
		/// <see cref="RequiresNonce"/> 가 true 면 서버가 발급한 nonce 원문이고, 제공자는 받을 id 토큰을 이 nonce 에 묶습니다(애플은 SHA-256 16진 소문자를 요청에,
		/// 구글은 그대로). 서버에는 <see cref="AuthService"/> 가 원문을 함께 보냅니다. false 면 null 입니다.
		/// </param>
		UniTask<Result<PlatformCredential>> SignInAsync(string nonce, CancellationToken cancellationToken);

		/// <summary>플랫폼 SDK 에서 로그아웃합니다(다음 로그인에서 계정을 다시 고르게). 실패해도 예외를 던지지 않습니다.</summary>
		UniTask SignOutAsync(CancellationToken cancellationToken);
	}
}
