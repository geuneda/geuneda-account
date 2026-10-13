using System;
using System.Collections.Generic;
using Microsoft.IdentityModel.Tokens;

namespace Geuneda.Account.Server
{
	/// <summary>
	/// 인증 서버가 아닌 서버(게임 서버, 실시간 RPC 서버)에서 access 토큰을 검증하는 설정입니다.
	/// <see cref="AccountServiceCollectionExtensions.AddGeunedaAccessTokenValidation"/> 에서 채웁니다.
	/// </summary>
	public sealed class AccessTokenValidationOptions
	{
		/// <summary>인증 서버의 <see cref="AccountServerOptions.Issuer"/> 와 같은 값. 필수입니다.</summary>
		public string Issuer { get; set; }

		/// <summary>인증 서버의 <see cref="AccountServerOptions.Audience"/> 와 같은 값. 필수입니다.</summary>
		public string Audience { get; set; }

		/// <summary>서명 검증 키(HMAC 키 또는 공개 키)입니다. 하나 이상 필요하고, 키를 바꾸는 동안에는 이전 키도 넣습니다.</summary>
		public IList<SecurityKey> SigningKeys { get; } = new List<SecurityKey>();

		/// <summary>허용하는 시계 오차입니다. 기본 1분입니다.</summary>
		public TimeSpan ClockSkew { get; set; } = TimeSpan.FromMinutes(1);

		/// <exception cref="InvalidOperationException">필수 값이 없거나 범위를 벗어난 경우</exception>
		internal void Validate()
		{
			if (string.IsNullOrWhiteSpace(Issuer) || string.IsNullOrWhiteSpace(Audience))
			{
				throw new InvalidOperationException("AccessTokenValidationOptions.Issuer and Audience are required");
			}

			if (SigningKeys.Count == 0 || SigningKeys.Contains(null))
			{
				throw new InvalidOperationException("AccessTokenValidationOptions.SigningKeys needs at least one key and no nulls");
			}

			if (ClockSkew < TimeSpan.Zero)
			{
				throw new InvalidOperationException("AccessTokenValidationOptions.ClockSkew must not be negative");
			}
		}
	}
}
