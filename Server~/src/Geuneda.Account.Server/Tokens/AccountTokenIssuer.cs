using System;
using System.Collections.Generic;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Geuneda.Account.Server
{
	/// <summary>access 토큰(JWT)과 refresh 토큰을 만듭니다. 저장은 하지 않습니다.</summary>
	internal sealed class AccountTokenIssuer
	{
		private readonly JsonWebTokenHandler _handler = new JsonWebTokenHandler { SetDefaultTimesOnTokenCreation = false };
		private readonly AccountServerOptions _options;

		public AccountTokenIssuer(IOptions<AccountServerOptions> options)
		{
			_options = options.Value;
		}

		public IssuedTokens Issue(string accountId, string sessionId, string familyId, DateTimeOffset now)
		{
			var claims = new Dictionary<string, object>
			{
				[JwtRegisteredClaimNames.Sub] = accountId,
				[JwtRegisteredClaimNames.Jti] = Guid.NewGuid().ToString("N")
			};

			if (!string.IsNullOrEmpty(sessionId))
			{
				claims[AccessTokenValidator.SessionIdClaim] = sessionId;
			}

			var accessToken = _handler.CreateToken(new SecurityTokenDescriptor
			{
				Issuer = _options.Issuer,
				Audience = _options.Audience,
				Claims = claims,
				IssuedAt = now.UtcDateTime,
				NotBefore = now.UtcDateTime,
				Expires = (now + _options.AccessTokenLifetime).UtcDateTime,
				SigningCredentials = _options.SigningCredentials
			});

			var refreshToken = RefreshTokens.NewToken();
			var record = new RefreshTokenRecord
			{
				TokenHash = RefreshTokens.Hash(refreshToken),
				FamilyId = familyId,
				IssuedAt = now,
				ExpiresAt = now + _options.RefreshTokenLifetime
			};

			return new IssuedTokens(accessToken, refreshToken, record);
		}

		public TokenResponse ToResponse(string accountId, IssuedTokens tokens, bool isNewAccount)
		{
			return new TokenResponse
			{
				AccountId = accountId,
				AccessToken = tokens.AccessToken,
				AccessTokenExpiresIn = (int) _options.AccessTokenLifetime.TotalSeconds,
				RefreshToken = tokens.RefreshToken,
				RefreshTokenExpiresIn = (int) _options.RefreshTokenLifetime.TotalSeconds,
				IsNewAccount = isNewAccount
			};
		}
	}
}
