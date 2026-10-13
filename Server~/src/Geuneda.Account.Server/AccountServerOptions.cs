using System;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Geuneda.Account.Server
{
	/// <summary>인증 서버 설정입니다. <see cref="AccountServiceCollectionExtensions.AddGeunedaAccount"/> 에서 채웁니다.</summary>
	public sealed class AccountServerOptions
	{
		/// <summary>access 토큰의 <c>iss</c>. 토큰을 검증하는 게임 서버의 설정과 같아야 합니다. 필수입니다.</summary>
		public string Issuer { get; set; }

		/// <summary>access 토큰의 <c>aud</c>. 필수입니다.</summary>
		public string Audience { get; set; }

		/// <summary>
		/// access 토큰 서명 키입니다. 필수입니다. HMAC(HS256)이면 32바이트 이상 무작위 키, RSA·ECDSA 면 개인 키를 넣습니다.
		/// 운영에서는 코드나 저장소가 아니라 비밀 저장소에서 읽습니다.
		/// </summary>
		public SigningCredentials SigningCredentials { get; set; }

		/// <summary>access 토큰 수명입니다. 기본 1시간입니다.</summary>
		public TimeSpan AccessTokenLifetime { get; set; } = TimeSpan.FromHours(1);

		/// <summary>refresh 토큰 수명입니다. 재발급할 때마다 새 토큰이 이 수명으로 나갑니다(마지막 사용부터 셈). 기본 30일입니다.</summary>
		public TimeSpan RefreshTokenLifetime { get; set; } = TimeSpan.FromDays(30);

		/// <summary>
		/// 이미 쓴 refresh 토큰이 다시 와도 응답을 잃은 클라이언트의 재시도로 보는 시간입니다. 기본 30초입니다.
		/// 이 안의 재시도는 새 토큰을 주고 직전에 준 토큰을 폐기합니다. 이 시간이 지난 재사용은 탈취로 보고 그 로그인의 토큰을 모두 폐기합니다.
		/// 0 이면 재시도도 재사용으로 봅니다.
		/// </summary>
		public TimeSpan RefreshReuseGrace { get; set; } = TimeSpan.FromSeconds(30);

		/// <summary>access 토큰을 검증할 때 허용하는 시계 오차입니다. 기본 1분입니다.</summary>
		public TimeSpan ClockSkew { get; set; } = TimeSpan.FromMinutes(1);

		/// <exception cref="InvalidOperationException">필수 값이 없거나 범위를 벗어났거나, 키로 토큰에 서명할 수 없는 경우</exception>
		internal void Validate()
		{
			if (string.IsNullOrWhiteSpace(Issuer) || string.IsNullOrWhiteSpace(Audience))
			{
				throw new InvalidOperationException("AccountServerOptions.Issuer and Audience are required");
			}

			if (SigningCredentials == null)
			{
				throw new InvalidOperationException("AccountServerOptions.SigningCredentials is required");
			}

			if (AccessTokenLifetime <= TimeSpan.Zero || AccessTokenLifetime.TotalSeconds > int.MaxValue)
			{
				throw new InvalidOperationException("AccountServerOptions.AccessTokenLifetime is out of range");
			}

			if (RefreshTokenLifetime <= TimeSpan.Zero || RefreshTokenLifetime.TotalSeconds > int.MaxValue)
			{
				throw new InvalidOperationException("AccountServerOptions.RefreshTokenLifetime is out of range");
			}

			if (RefreshReuseGrace < TimeSpan.Zero || RefreshReuseGrace >= RefreshTokenLifetime)
			{
				throw new InvalidOperationException("AccountServerOptions.RefreshReuseGrace must be between zero and RefreshTokenLifetime");
			}

			if (ClockSkew < TimeSpan.Zero)
			{
				throw new InvalidOperationException("AccountServerOptions.ClockSkew must not be negative");
			}

			try
			{
				// 키 길이·알고리즘이 맞지 않으면 첫 로그인이 아니라 시작할 때 실패하게 한 번 서명해 본다
				new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
				{
					Issuer = Issuer,
					Audience = Audience,
					SigningCredentials = SigningCredentials
				});
			}
			catch (Exception exception) when (exception is ArgumentException || exception is SecurityTokenException || exception is NotSupportedException)
			{
				throw new InvalidOperationException("AccountServerOptions.SigningCredentials cannot sign tokens: " + exception.Message, exception);
			}
		}
	}
}
