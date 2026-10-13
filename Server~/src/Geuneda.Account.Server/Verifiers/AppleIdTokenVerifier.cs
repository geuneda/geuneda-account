using System;

namespace Geuneda.Account.Server
{
	/// <summary>
	/// Sign in with Apple 검증기입니다. 자격은 애플 id 토큰(identityToken)이고, 대상(<c>aud</c>)은 앱 번들 ID(웹 로그인이면 Services ID)입니다.
	/// <see cref="AccountServerBuilder.AddApple"/> 로 등록합니다.
	/// </summary>
	public sealed class AppleIdTokenVerifier : OpenIdConnectPlatformVerifier
	{
		public const string Issuer = "https://appleid.apple.com";
		public const string MetadataAddress = "https://appleid.apple.com/.well-known/openid-configuration";

		/// <param name="options">대상과 시계 오차입니다.</param>
		/// <param name="keys">애플 공개 키 캐시(<see cref="MetadataAddress"/>)입니다.</param>
		/// <param name="time">수명 판단에 쓸 시계입니다.</param>
		public AppleIdTokenVerifier(AppleSignInOptions options, OpenIdConnectKeyCache keys, TimeProvider time)
			: base(keys, new[] { Issuer }, (options ?? throw new ArgumentNullException(nameof(options))).Audiences, options.ClockSkew, time)
		{
		}

		public override string Platform => AccountPlatforms.Apple;
	}
}
