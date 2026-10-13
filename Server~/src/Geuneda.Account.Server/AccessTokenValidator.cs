using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Geuneda.Account.Server
{
	internal sealed class AccessTokenValidator : IAccessTokenValidator
	{
		internal const string SessionIdClaim = "sid";

		private readonly JsonWebTokenHandler _handler = new JsonWebTokenHandler();
		private readonly TokenValidationParameters _parameters;
		private readonly TimeProvider _time;

		public AccessTokenValidator(string issuer, string audience, IEnumerable<SecurityKey> signingKeys, TimeSpan clockSkew, TimeProvider time)
		{
			_time = time;
			_parameters = new TokenValidationParameters
			{
				ValidIssuer = issuer,
				ValidAudience = audience,
				IssuerSigningKeys = signingKeys.ToArray(),
				RequireSignedTokens = true,
				RequireExpirationTime = true,
				ValidateLifetime = true,
				ClockSkew = clockSkew,
				// 테스트에서 시간을 바꿀 수 있게 TimeProvider 로 수명을 판단한다
				LifetimeValidator = ValidateLifetime
			};
		}

		public async Task<AccessTokenValidation> ValidateAsync(string accessToken, CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrEmpty(accessToken))
			{
				return AccessTokenValidation.Invalid("missing token");
			}

			cancellationToken.ThrowIfCancellationRequested();
			var result = await _handler.ValidateTokenAsync(accessToken, _parameters);

			if (!result.IsValid || !(result.SecurityToken is JsonWebToken token))
			{
				return AccessTokenValidation.Invalid(result.Exception?.GetType().Name ?? "invalid token");
			}

			if (string.IsNullOrEmpty(token.Subject))
			{
				return AccessTokenValidation.Invalid("missing sub");
			}

			token.TryGetPayloadValue<string>(SessionIdClaim, out var sessionId);
			return AccessTokenValidation.Valid(token.Subject, sessionId, new DateTimeOffset(token.ValidTo, TimeSpan.Zero));
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
