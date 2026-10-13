using System;

namespace Geuneda.Account.Server
{
	/// <summary>
	/// Google 로그인 검증기입니다. 자격은 구글 id 토큰이고, 대상(<c>aud</c>)은 그 토큰을 요청한 OAuth 클라이언트 ID 입니다
	/// (Android Credential Manager 는 서버용 웹 클라이언트 ID, iOS PKCE 는 iOS 클라이언트 ID). <see cref="AccountServerBuilder.AddGoogle"/> 로 등록합니다.
	/// </summary>
	public sealed class GoogleIdTokenVerifier : OpenIdConnectPlatformVerifier
	{
		public const string MetadataAddress = "https://accounts.google.com/.well-known/openid-configuration";

		/// <summary>구글 id 토큰의 <c>iss</c> 입니다. <see cref="LegacyIssuer"/> 형식도 함께 받습니다.</summary>
		public const string Issuer = "https://accounts.google.com";
		/// <summary>구글 id 토큰의 다른 <c>iss</c> 형식입니다.</summary>
		public const string LegacyIssuer = "accounts.google.com";

		/// <param name="options">클라이언트 ID 와 시계 오차입니다.</param>
		/// <param name="keys">구글 공개 키 캐시(<see cref="MetadataAddress"/>)입니다.</param>
		/// <param name="time">수명 판단에 쓸 시계입니다.</param>
		public GoogleIdTokenVerifier(GoogleSignInOptions options, OpenIdConnectKeyCache keys, TimeProvider time)
			: base(keys, new[] { Issuer, LegacyIssuer }, (options ?? throw new ArgumentNullException(nameof(options))).ClientIds, options.ClockSkew, time)
		{
		}

		public override string Platform => AccountPlatforms.Google;
	}
}
