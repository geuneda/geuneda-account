using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Geuneda.Account.Server.Tests
{
	public class IdTokenVerifierTest
	{
		private const string BundleId = "com.example.game";
		private const string GoogleClientId = "1234-abc.apps.googleusercontent.com";

		private static readonly DateTimeOffset Now = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

		// 서버가 발급한 nonce 원문 역할. 애플 토큰에는 그 SHA-256 16진 소문자, 구글 토큰에는 그대로 들어간다
		private const string RawNonce = "raw-nonce-value";

		private static AppleIdTokenVerifier Apple(FakeIdTokenIssuer issuer, FakeTimeProvider time)
		{
			var options = new AppleSignInOptions();
			options.Audiences.Add(BundleId);
			return new AppleIdTokenVerifier(options, issuer.KeyCache(time), time);
		}

		private static GoogleIdTokenVerifier Google(FakeIdTokenIssuer issuer)
		{
			var options = new GoogleSignInOptions();
			options.ClientIds.Add(GoogleClientId);
			var time = new FakeTimeProvider(Now);
			return new GoogleIdTokenVerifier(options, issuer.KeyCache(time), time);
		}

		private static string AppleToken(FakeIdTokenIssuer issuer, FakeTimeProvider time, Microsoft.IdentityModel.Tokens.SecurityKey key = null)
		{
			return issuer.CreateToken(AppleIdTokenVerifier.Issuer, BundleId, "apple-user", time.GetUtcNow(), TimeSpan.FromMinutes(10), key, OneTimeValues.Hash(RawNonce));
		}

		[Fact]
		public async Task Apple_ValidToken_ReturnsSubject()
		{
			var issuer = new FakeIdTokenIssuer();
			var time = new FakeTimeProvider(Now);
			var verifier = Apple(issuer, time);

			var result = await verifier.VerifyAsync(AppleToken(issuer, time), RawNonce, CancellationToken.None);

			Assert.True(result.IsValid, result.FailureReason);
			Assert.Equal("apple-user", result.Subject);
			Assert.Equal(AccountPlatforms.Apple, verifier.Platform);
		}

		[Theory]
		[InlineData("https://appleid.apple.com", "com.other.game")]
		[InlineData("https://evil.example.com", BundleId)]
		public async Task Apple_WrongAudienceOrIssuer_IsInvalid(string tokenIssuer, string audience)
		{
			var issuer = new FakeIdTokenIssuer();
			var token = issuer.CreateToken(tokenIssuer, audience, "apple-user", Now, TimeSpan.FromMinutes(10));

			Assert.False((await Apple(issuer, new FakeTimeProvider(Now)).VerifyAsync(token, RawNonce, CancellationToken.None)).IsValid);
		}

		[Fact]
		public async Task Apple_Expired_IsInvalid()
		{
			var issuer = new FakeIdTokenIssuer();
			var time = new FakeTimeProvider(Now);
			var token = AppleToken(issuer, time);
			time.Advance(TimeSpan.FromMinutes(16));

			Assert.False((await Apple(issuer, time).VerifyAsync(token, RawNonce, CancellationToken.None)).IsValid);
		}

		[Fact]
		public async Task RotatedKey_IsAcceptedOnTheFirstRequest()
		{
			var issuer = new FakeIdTokenIssuer();
			var time = new FakeTimeProvider(Now);
			var verifier = Apple(issuer, time);
			Assert.True((await verifier.VerifyAsync(AppleToken(issuer, time), RawNonce, CancellationToken.None)).IsValid);

			// 키를 받아 둔 뒤 발급자가 새 키로 바꿨다
			time.Advance(OpenIdConnectKeyCache.RefreshInterval);
			var rotated = FakeIdTokenIssuer.NewKey();
			issuer.Publish(issuer.SigningKey, rotated);

			var result = await verifier.VerifyAsync(AppleToken(issuer, time, rotated), RawNonce, CancellationToken.None);

			Assert.True(result.IsValid, result.FailureReason);
			Assert.Equal(2, issuer.Attempts);
		}

		[Fact]
		public async Task UnknownKey_IsRejected_AndRefetchedAtMostOncePerInterval()
		{
			var issuer = new FakeIdTokenIssuer();
			var time = new FakeTimeProvider(Now);
			var verifier = Apple(issuer, time);
			var forged = FakeIdTokenIssuer.NewKey();

			// 방금 받았으므로 다시 받지 않는다
			Assert.False((await verifier.VerifyAsync(AppleToken(issuer, time, forged), RawNonce, CancellationToken.None)).IsValid);
			Assert.Equal(1, issuer.Attempts);

			time.Advance(OpenIdConnectKeyCache.RefreshInterval);
			Assert.False((await verifier.VerifyAsync(AppleToken(issuer, time, forged), RawNonce, CancellationToken.None)).IsValid);
			Assert.False((await verifier.VerifyAsync(AppleToken(issuer, time, forged), RawNonce, CancellationToken.None)).IsValid);
			Assert.Equal(2, issuer.Attempts);
		}

		[Fact]
		public async Task RefreshFailure_IsAServerError_NotACredentialError()
		{
			var issuer = new FakeIdTokenIssuer();
			var time = new FakeTimeProvider(Now);
			var verifier = Apple(issuer, time);
			Assert.True((await verifier.VerifyAsync(AppleToken(issuer, time), RawNonce, CancellationToken.None)).IsValid);

			time.Advance(OpenIdConnectKeyCache.RefreshInterval);
			issuer.Failing = true;
			var rotated = AppleToken(issuer, time, FakeIdTokenIssuer.NewKey());

			await Assert.ThrowsAsync<InvalidOperationException>(() => verifier.VerifyAsync(rotated, RawNonce, CancellationToken.None));
			// 최근 시도가 실패했으면 다시 두드리지 않고 같은 서버 오류다
			await Assert.ThrowsAsync<InvalidOperationException>(() => verifier.VerifyAsync(rotated, RawNonce, CancellationToken.None));
			Assert.Equal(2, issuer.Attempts);
			// 알고 있는 키로 서명한 토큰은 계속 받는다
			Assert.True((await verifier.VerifyAsync(AppleToken(issuer, time), RawNonce, CancellationToken.None)).IsValid);
		}

		[Fact]
		public async Task ConcurrentRefresh_RetrievesOnce_AndBothWait()
		{
			var issuer = new FakeIdTokenIssuer();
			var time = new FakeTimeProvider(Now);
			var verifier = Apple(issuer, time);
			Assert.True((await verifier.VerifyAsync(AppleToken(issuer, time), RawNonce, CancellationToken.None)).IsValid);

			time.Advance(OpenIdConnectKeyCache.RefreshInterval);
			var rotated = FakeIdTokenIssuer.NewKey();
			issuer.Publish(issuer.SigningKey, rotated);
			var hold = new TaskCompletionSource();
			issuer.Hold = hold.Task;

			var first = verifier.VerifyAsync(AppleToken(issuer, time, rotated), RawNonce, CancellationToken.None);
			var second = verifier.VerifyAsync(AppleToken(issuer, time, rotated), RawNonce, CancellationToken.None);
			await Task.Delay(50);
			Assert.False(first.IsCompleted || second.IsCompleted);

			hold.SetResult();

			Assert.True((await first).IsValid);
			Assert.True((await second).IsValid);
			Assert.Equal(2, issuer.Attempts);
		}

		[Fact]
		public async Task AutomaticRefreshFailure_KeepsKnownKeys_AndBacksOff()
		{
			var issuer = new FakeIdTokenIssuer();
			var time = new FakeTimeProvider(Now);
			var verifier = Apple(issuer, time);
			Assert.True((await verifier.VerifyAsync(AppleToken(issuer, time), RawNonce, CancellationToken.None)).IsValid);

			time.Advance(OpenIdConnectKeyCache.AutomaticRefreshInterval);
			issuer.Failing = true;

			Assert.True((await verifier.VerifyAsync(AppleToken(issuer, time), RawNonce, CancellationToken.None)).IsValid);
			Assert.True((await verifier.VerifyAsync(AppleToken(issuer, time), RawNonce, CancellationToken.None)).IsValid);
			Assert.Equal(2, issuer.Attempts);

			time.Advance(OpenIdConnectKeyCache.RefreshInterval);
			issuer.Failing = false;
			Assert.True((await verifier.VerifyAsync(AppleToken(issuer, time), RawNonce, CancellationToken.None)).IsValid);
			Assert.Equal(3, issuer.Attempts);
		}

		[Fact]
		public async Task FirstRetrievalFailure_IsAServerError_AndBacksOff()
		{
			var issuer = new FakeIdTokenIssuer { Failing = true };
			var time = new FakeTimeProvider(Now);
			var verifier = Apple(issuer, time);

			await Assert.ThrowsAsync<InvalidOperationException>(() => verifier.VerifyAsync(AppleToken(issuer, time), RawNonce, CancellationToken.None));
			// 키가 없는 동안은 간격 안에 다시 받지 않고 바로 실패한다
			issuer.Failing = false;
			await Assert.ThrowsAsync<InvalidOperationException>(() => verifier.VerifyAsync(AppleToken(issuer, time), RawNonce, CancellationToken.None));
			Assert.Equal(1, issuer.Attempts);

			time.Advance(OpenIdConnectKeyCache.RetryIntervalWithoutKeys);
			Assert.True((await verifier.VerifyAsync(AppleToken(issuer, time), RawNonce, CancellationToken.None)).IsValid);
			Assert.Equal(2, issuer.Attempts);
		}

		[Theory]
		[InlineData("not json")]
		[InlineData("[]")]
		[InlineData("{\"issuer\":\"https://issuer.test\"}")]
		public async Task MalformedMetadata_IsARetrievalFailure(string metadata)
		{
			var issuer = new FakeIdTokenIssuer { MetadataOverride = metadata };
			var time = new FakeTimeProvider(Now);

			var exception = await Record.ExceptionAsync(() => Apple(issuer, time).VerifyAsync(AppleToken(issuer, time), RawNonce, CancellationToken.None));

			Assert.IsType<InvalidOperationException>(exception);
			Assert.Contains("unavailable", exception.Message);
		}

		[Fact]
		public async Task RetrieverBugOnRefresh_IsNotSwallowed()
		{
			var issuer = new FakeIdTokenIssuer();
			var time = new FakeTimeProvider(Now);
			var verifier = Apple(issuer, time);
			Assert.True((await verifier.VerifyAsync(AppleToken(issuer, time), RawNonce, CancellationToken.None)).IsValid);

			time.Advance(OpenIdConnectKeyCache.AutomaticRefreshInterval);
			issuer.Throws = new NullReferenceException("bug in the retriever");

			await Assert.ThrowsAsync<NullReferenceException>(() => verifier.VerifyAsync(AppleToken(issuer, time), RawNonce, CancellationToken.None));
		}

		[Fact]
		public async Task EmptyKeySet_IsARetrievalFailure()
		{
			var issuer = new FakeIdTokenIssuer();
			issuer.Publish();
			var time = new FakeTimeProvider(Now);

			await Assert.ThrowsAsync<InvalidOperationException>(() => Apple(issuer, time).VerifyAsync(AppleToken(issuer, time), RawNonce, CancellationToken.None));
		}

		[Fact]
		public async Task EmptyKeySetOnRefresh_KeepsKnownKeys()
		{
			var issuer = new FakeIdTokenIssuer();
			var time = new FakeTimeProvider(Now);
			var verifier = Apple(issuer, time);
			Assert.True((await verifier.VerifyAsync(AppleToken(issuer, time), RawNonce, CancellationToken.None)).IsValid);

			time.Advance(OpenIdConnectKeyCache.AutomaticRefreshInterval);
			issuer.Publish();

			Assert.True((await verifier.VerifyAsync(AppleToken(issuer, time), RawNonce, CancellationToken.None)).IsValid);
			Assert.Equal(2, issuer.Attempts);
		}

		[Theory]
		[InlineData(GoogleIdTokenVerifier.Issuer)]
		[InlineData(GoogleIdTokenVerifier.LegacyIssuer)]
		public async Task Google_AcceptsBothIssuerForms(string tokenIssuer)
		{
			var issuer = new FakeIdTokenIssuer();
			var token = issuer.CreateToken(tokenIssuer, GoogleClientId, "google-user", Now, TimeSpan.FromHours(1), nonce: RawNonce);

			var result = await Google(issuer).VerifyAsync(token, RawNonce, CancellationToken.None);

			Assert.True(result.IsValid, result.FailureReason);
			Assert.Equal("google-user", result.Subject);
		}

		[Theory]
		[InlineData("")]
		[InlineData("not-a-jwt")]
		public async Task Garbage_IsInvalid(string credential)
		{
			Assert.False((await Google(new FakeIdTokenIssuer()).VerifyAsync(credential, RawNonce, CancellationToken.None)).IsValid);
		}

		[Fact]
		public void AddAppleOrGoogle_WithoutAudience_OrWithNegativeSkew_Throws()
		{
			var builder = new ServiceCollection().AddGeunedaAccount(_ => { });

			Assert.Throws<InvalidOperationException>(() => builder.AddApple(_ => { }));
			Assert.Throws<InvalidOperationException>(() => builder.AddGoogle(_ => { }));
			Assert.Throws<InvalidOperationException>(() => builder.AddApple(options =>
			{
				options.Audiences.Add(BundleId);
				options.ClockSkew = TimeSpan.FromSeconds(-1);
			}));
			Assert.Throws<InvalidOperationException>(() => builder.AddGoogle(options =>
			{
				options.ClientIds.Add(GoogleClientId);
				options.ClockSkew = TimeSpan.FromSeconds(-1);
			}));
			builder.AddApple(options =>
			{
				options.Audiences.Add(BundleId);
				options.ClockSkew = TimeSpan.Zero;
			});
		}

		[Fact]
		public void AddGuestAppleAndGoogle_RegistersAllThree()
		{
			var services = new ServiceCollection();
			services.AddSingleton(TimeProvider.System);
			services.AddGeunedaAccount(_ => { })
				.AddGuest()
				.AddApple(options => options.Audiences.Add(BundleId))
				.AddGoogle(options => options.ClientIds.Add(GoogleClientId));

			using var provider = services.BuildServiceProvider();
			var platforms = provider.GetServices<IPlatformVerifier>().Select(verifier => verifier.Platform).OrderBy(platform => platform).ToArray();

			Assert.Equal(new[] { AccountPlatforms.Apple, AccountPlatforms.Google, AccountPlatforms.Guest }, platforms);
		}

		[Fact]
		public void Verifier_WithNegativeSkew_Throws()
		{
			var options = new AppleSignInOptions { ClockSkew = TimeSpan.FromSeconds(-1) };
			options.Audiences.Add(BundleId);
			var time = new FakeTimeProvider(Now);

			Assert.Throws<ArgumentOutOfRangeException>(() => new AppleIdTokenVerifier(options, new FakeIdTokenIssuer().KeyCache(time), time));
		}

		[Fact]
		public async Task Login_WithAppleIdToken_CreatesAccount()
		{
			var issuer = new FakeIdTokenIssuer();
			await using var server = await AccountTestServer.StartAsync(services: services =>
			{
				var options = new AppleSignInOptions();
				options.Audiences.Add(BundleId);
				services.AddSingleton<IPlatformVerifier>(provider =>
				{
					var time = provider.GetRequiredService<TimeProvider>();
					return new AppleIdTokenVerifier(options, issuer.KeyCache(time), time);
				});
			});
			var nonce = await server.IssueNonceAsync();
			var other = await server.IssueNonceAsync();
			// 클라이언트는 원문의 SHA-256 16진 소문자를 애플에 넣고 원문을 서버에 보낸다
			var token = issuer.CreateToken(AppleIdTokenVerifier.Issuer, BundleId, "apple-user", AccountTestServer.Start, TimeSpan.FromMinutes(10),
				nonce: OneTimeValues.Hash(nonce));
			var unhashed = issuer.CreateToken(AppleIdTokenVerifier.Issuer, BundleId, "apple-user", AccountTestServer.Start, TimeSpan.FromMinutes(10),
				nonce: other);

			var (status, login) = await server.PostAsync<TokenResponse>("login", new LoginRequest { Platform = AccountPlatforms.Apple, Credential = token, Nonce = nonce });
			var (replayStatus, replay) = await server.PostAsync<ErrorResponse>("login",
				new LoginRequest { Platform = AccountPlatforms.Apple, Credential = token, Nonce = nonce });
			var (unhashedStatus, unhashedError) = await server.PostAsync<ErrorResponse>("login",
				new LoginRequest { Platform = AccountPlatforms.Apple, Credential = unhashed, Nonce = other });
			var (badStatus, error) = await server.PostAsync<ErrorResponse>("login",
				new LoginRequest { Platform = AccountPlatforms.Apple, Credential = "nope", Nonce = other });

			Assert.Equal(200, status);
			Assert.True(login.IsNewAccount);
			Assert.Equal(401, replayStatus);
			Assert.Equal(AccountResultCodes.InvalidNonce, replay.Code);
			Assert.Equal(401, unhashedStatus);
			Assert.Equal(AccountResultCodes.NonceMismatch, unhashedError.Code);
			Assert.Equal(401, badStatus);
			Assert.Equal(AccountResultCodes.InvalidCredential, error.Code);
		}

		[Fact]
		public async Task Apple_NonceClaimIsTheSha256OfTheRawNonce()
		{
			var issuer = new FakeIdTokenIssuer();
			var time = new FakeTimeProvider(Now);
			var verifier = Apple(issuer, time);
			string Make(string nonce) => issuer.CreateToken(AppleIdTokenVerifier.Issuer, BundleId, "apple-user", Now, TimeSpan.FromMinutes(10), nonce: nonce);

			var hashed = await verifier.VerifyAsync(Make(OneTimeValues.Hash(RawNonce)), RawNonce, CancellationToken.None);
			var raw = await verifier.VerifyAsync(Make(RawNonce), RawNonce, CancellationToken.None);
			var missing = await verifier.VerifyAsync(Make(null), RawNonce, CancellationToken.None);
			var other = await verifier.VerifyAsync(Make(OneTimeValues.Hash(RawNonce)), "other-nonce", CancellationToken.None);

			Assert.True(hashed.IsValid, hashed.FailureReason);
			Assert.True(verifier.RequiresNonce);
			// SHA-256("raw-nonce-value") 16진 소문자(shasum -a 256). 클라이언트(AppleNonce.Hash)도 같은 값을 애플에 넣는다
			Assert.Equal("90d1bd536f17b7ddb159e27bb5716ae0db8a41b69dc20ffe276681196112b496", OneTimeValues.Hash(RawNonce));
			Assert.True(raw.IsNonceMismatch);
			Assert.True(missing.IsNonceMismatch);
			Assert.True(other.IsNonceMismatch);
			Assert.False(raw.IsValid || missing.IsValid || other.IsValid);
		}

		[Fact]
		public async Task Google_NonceClaimIsTheRawNonce()
		{
			var issuer = new FakeIdTokenIssuer();
			var verifier = Google(issuer);
			string Make(string nonce) => issuer.CreateToken(GoogleIdTokenVerifier.Issuer, GoogleClientId, "google-user", Now, TimeSpan.FromHours(1), nonce: nonce);

			var raw = await verifier.VerifyAsync(Make(RawNonce), RawNonce, CancellationToken.None);
			var hashed = await verifier.VerifyAsync(Make(OneTimeValues.Hash(RawNonce)), RawNonce, CancellationToken.None);
			var missing = await verifier.VerifyAsync(Make(null), RawNonce, CancellationToken.None);

			Assert.True(raw.IsValid, raw.FailureReason);
			Assert.True(hashed.IsNonceMismatch);
			Assert.True(missing.IsNonceMismatch);
		}
	}
}
