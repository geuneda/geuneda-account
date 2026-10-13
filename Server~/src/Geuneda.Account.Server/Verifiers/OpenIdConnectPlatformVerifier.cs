using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace Geuneda.Account.Server
{
	/// <summary>
	/// OpenID Connect id 토큰(JWT)을 자격으로 받는 플랫폼 검증기의 공통 구현입니다. 서명은 발급자의 공개 키(JWKS)로, 발급자·대상·만료를 확인하고
	/// <c>sub</c> 를 고유 ID 로 씁니다. 공개 키는 <see cref="OpenIdConnectKeyCache"/> 가 받아 두고 주기적으로 갱신하며,
	/// 모르는 키로 서명된 토큰이 오면(발급자의 키 교체) 다시 받기를 기다려 한 번 더 확인합니다.
	/// </summary>
	/// <remarks>공개 키를 확인하지 못하면(발급자 장애, 네트워크) 예외가 나 요청은 500 입니다. 자격 오류가 아니므로 클라이언트가 로그인을 지우지 않습니다.</remarks>
	public abstract class OpenIdConnectPlatformVerifier : IPlatformVerifier
	{
		private readonly JsonWebTokenHandler _handler = new JsonWebTokenHandler();
		private readonly OpenIdConnectKeyCache _keys;
		private readonly string[] _issuers;
		private readonly string[] _audiences;
		private readonly TimeProvider _time;
		private readonly TimeSpan _clockSkew;

		/// <exception cref="ArgumentException">발급자나 대상이 없는 경우</exception>
		/// <exception cref="ArgumentOutOfRangeException">시계 오차가 음수인 경우</exception>
		protected OpenIdConnectPlatformVerifier(OpenIdConnectKeyCache keys, IEnumerable<string> issuers, IEnumerable<string> audiences, TimeSpan clockSkew,
			TimeProvider time)
		{
			_keys = keys ?? throw new ArgumentNullException(nameof(keys));
			_issuers = issuers.Where(issuer => !string.IsNullOrWhiteSpace(issuer)).ToArray();
			_audiences = audiences.Where(audience => !string.IsNullOrWhiteSpace(audience)).ToArray();
			_time = time ?? throw new ArgumentNullException(nameof(time));
			_clockSkew = clockSkew >= TimeSpan.Zero ? clockSkew : throw new ArgumentOutOfRangeException(nameof(clockSkew), clockSkew, "Clock skew must not be negative");

			if (_issuers.Length == 0 || _audiences.Length == 0)
			{
				throw new ArgumentException($"{GetType().Name} needs at least one issuer and one audience");
			}
		}

		public abstract string Platform { get; }

		public async Task<PlatformVerification> VerifyAsync(string credential, CancellationToken cancellationToken)
		{
			if (string.IsNullOrEmpty(credential))
			{
				return PlatformVerification.Invalid("missing id token");
			}

			var configuration = await _keys.GetAsync(cancellationToken);
			var result = await ValidateAsync(credential, configuration);

			if (!result.IsValid && result.Exception is SecurityTokenSignatureKeyNotFoundException)
			{
				// 발급자가 키를 바꿨을 수 있다. 공개 키를 다시 받아(최근에 받았으면 그대로) 새로 받았을 때만 한 번 더 확인한다
				var refreshed = await _keys.RefreshAsync(configuration, cancellationToken);

				if (!ReferenceEquals(refreshed, configuration))
				{
					result = await ValidateAsync(credential, refreshed);
				}
			}

			if (!result.IsValid || !(result.SecurityToken is JsonWebToken token) || string.IsNullOrEmpty(token.Subject))
			{
				return PlatformVerification.Invalid($"{Platform} id token rejected ({result.Exception?.GetType().Name ?? "no subject"})");
			}

			return PlatformVerification.Valid(token.Subject);
		}

		private Task<TokenValidationResult> ValidateAsync(string credential, OpenIdConnectConfiguration configuration)
		{
			return _handler.ValidateTokenAsync(credential, new TokenValidationParameters
			{
				ValidIssuers = _issuers,
				ValidAudiences = _audiences,
				IssuerSigningKeys = configuration.SigningKeys,
				RequireSignedTokens = true,
				RequireExpirationTime = true,
				ValidateLifetime = true,
				ClockSkew = _clockSkew,
				LifetimeValidator = ValidateLifetime
			});
		}

		private bool ValidateLifetime(DateTime? notBefore, DateTime? expires, SecurityToken token, TokenValidationParameters parameters)
		{
			if (expires == null)
			{
				return false;
			}

			var now = _time.GetUtcNow().UtcDateTime;
			return (notBefore == null || now + parameters.ClockSkew >= notBefore.Value) && now - parameters.ClockSkew < expires.Value;
		}
	}
}
