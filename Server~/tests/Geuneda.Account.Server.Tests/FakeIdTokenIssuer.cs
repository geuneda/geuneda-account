using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Tokens;

namespace Geuneda.Account.Server.Tests
{
	/// <summary>
	/// 애플·구글 대신 id 토큰을 서명하는 가짜 발급자입니다. 메타데이터와 공개 키(JWKS) 문서를 돌려주는 문서 retriever 이기도 해서,
	/// 운영과 같은 <see cref="OpenIdConnectKeyCache"/> 와 메타데이터 해석 경로로 검증합니다.
	/// </summary>
	internal sealed class FakeIdTokenIssuer : IDocumentRetriever
	{
		public const string MetadataAddress = "https://issuer.test/.well-known/openid-configuration";
		private const string JwksAddress = "https://issuer.test/jwks";

		private IReadOnlyList<RsaSecurityKey> _published;

		public FakeIdTokenIssuer()
		{
			SigningKey = NewKey();
			_published = new[] { SigningKey };
		}

		/// <summary>기본 서명 키입니다. 처음부터 공개되어 있습니다.</summary>
		public RsaSecurityKey SigningKey { get; }

		/// <summary>메타데이터를 받으려 한 횟수입니다(실패 포함).</summary>
		public int Attempts { get; private set; }

		/// <summary>true 면 문서를 돌려주지 않고 실패합니다(발급자 장애).</summary>
		public bool Failing { get; set; }

		/// <summary>설정하면 메타데이터 대신 이 문서를 돌려줍니다(잘못된 문서).</summary>
		public string MetadataOverride { get; set; }

		/// <summary>설정하면 메타데이터를 받을 때 이 예외를 던집니다(문서 retriever 의 구현 버그).</summary>
		public Exception Throws { get; set; }

		/// <summary>설정하면 공개 키 문서를 이 작업이 끝날 때까지 붙잡아 둡니다.</summary>
		public Task Hold { get; set; }

		public static RsaSecurityKey NewKey()
		{
			return new RsaSecurityKey(RSA.Create(2048)) { KeyId = Guid.NewGuid().ToString("N") };
		}

		/// <summary>공개 키 목록을 바꿉니다(키 교체).</summary>
		public void Publish(params RsaSecurityKey[] keys)
		{
			_published = keys.ToArray();
		}

		public OpenIdConnectKeyCache KeyCache(TimeProvider time)
		{
			return new OpenIdConnectKeyCache(MetadataAddress, this, time);
		}

		public string CreateToken(string issuer, string audience, string subject, DateTimeOffset issuedAt, TimeSpan lifetime, SecurityKey signingKey = null)
		{
			return new JsonWebTokenHandler { SetDefaultTimesOnTokenCreation = false }.CreateToken(new SecurityTokenDescriptor
			{
				Issuer = issuer,
				Audience = audience,
				Claims = new Dictionary<string, object> { [JwtRegisteredClaimNames.Sub] = subject },
				IssuedAt = issuedAt.UtcDateTime,
				NotBefore = issuedAt.UtcDateTime,
				Expires = (issuedAt + lifetime).UtcDateTime,
				SigningCredentials = new SigningCredentials(signingKey ?? SigningKey, SecurityAlgorithms.RsaSha256)
			});
		}

		public async Task<string> GetDocumentAsync(string address, CancellationToken cancel)
		{
			if (address == MetadataAddress)
			{
				Attempts++;

				if (Failing)
				{
					throw new IOException("issuer is down");
				}

				if (Throws != null)
				{
					throw Throws;
				}

				if (MetadataOverride != null)
				{
					return MetadataOverride;
				}

				return JsonSerializer.Serialize(new Dictionary<string, string> { ["issuer"] = "https://issuer.test", ["jwks_uri"] = JwksAddress });
			}

			if (Hold != null)
			{
				await Hold.WaitAsync(cancel);
			}

			var keys = _published.Select(key =>
			{
				var parameters = key.Rsa.ExportParameters(false);
				return new Dictionary<string, string>
				{
					["kty"] = "RSA",
					["use"] = "sig",
					["alg"] = "RS256",
					["kid"] = key.KeyId,
					["n"] = Base64UrlEncoder.Encode(parameters.Modulus),
					["e"] = Base64UrlEncoder.Encode(parameters.Exponent)
				};
			});

			return JsonSerializer.Serialize(new Dictionary<string, object> { ["keys"] = keys.ToArray() });
		}
	}
}
