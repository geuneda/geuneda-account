using System;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Geuneda.Account.Server.Tests
{
	public class NonceTest
	{
		private static Task<(int Status, T Body)> LinkAsync<T>(AccountTestServer server, string accessToken, string subject, string nonce, string path = "links")
		{
			return server.PostAsync<T>(path, new LinkRequest
			{
				Platform = NonceTestPlatformVerifier.Name,
				Credential = NonceTestPlatformVerifier.Credential(subject, nonce),
				Nonce = nonce
			}, accessToken);
		}

		[Fact]
		public async Task Nonce_IsUrlSafe_AndWorksOnce()
		{
			await using var server = await AccountTestServer.StartAsync();

			var (issueStatus, issued) = await server.PostAsync<NonceResponse>("nonce", null);
			var (status, login) = await server.LoginNonceTestAsync<TokenResponse>("alice", issued.Nonce);
			var (reuseStatus, reuse) = await server.LoginNonceTestAsync<ErrorResponse>("alice", issued.Nonce);

			Assert.Equal(200, issueStatus);
			Assert.Equal(43, issued.Nonce.Length);
			Assert.True(RequestFields.IsCredentialOrTicket(null, null, issued.Nonce));
			Assert.Equal(300, issued.ExpiresIn);
			Assert.Equal(200, status);
			Assert.True(login.IsNewAccount);
			Assert.Equal(401, reuseStatus);
			Assert.Equal(AccountResultCodes.InvalidNonce, reuse.Code);
		}

		[Fact]
		public async Task Nonce_ExpiresAfterLifetime()
		{
			await using var server = await AccountTestServer.StartAsync();
			var early = await server.IssueNonceAsync();
			var late = await server.IssueNonceAsync();

			server.Time.Advance(TimeSpan.FromMinutes(5) - TimeSpan.FromSeconds(1));
			var (earlyStatus, _) = await server.LoginNonceTestAsync<TokenResponse>("alice", early);
			server.Time.Advance(TimeSpan.FromSeconds(1));
			var (lateStatus, error) = await server.LoginNonceTestAsync<ErrorResponse>("alice", late);

			Assert.Equal(200, earlyStatus);
			Assert.Equal(401, lateStatus);
			Assert.Equal(AccountResultCodes.InvalidNonce, error.Code);
		}

		[Fact]
		public async Task CredentialBoundToAnotherNonce_IsMismatch_AndConsumesNothing()
		{
			await using var server = await AccountTestServer.StartAsync();
			var bound = await server.IssueNonceAsync();
			var sent = await server.IssueNonceAsync();

			var (status, error) = await server.LoginNonceTestAsync<ErrorResponse>("alice", sent, boundNonce: bound);
			var (boundStatus, _) = await server.LoginNonceTestAsync<TokenResponse>("alice", bound);
			var (sentStatus, _) = await server.LoginNonceTestAsync<TokenResponse>("bob", sent);

			Assert.Equal(401, status);
			Assert.Equal(AccountResultCodes.NonceMismatch, error.Code);
			Assert.Equal(200, boundStatus);
			Assert.Equal(200, sentStatus);
		}

		[Fact]
		public async Task MissingNonce_IsNonceMissing_UnissuedNonce_IsInvalid_MalformedNonce_IsBadRequest()
		{
			await using var server = await AccountTestServer.StartAsync();

			var (missingStatus, missing) = await server.PostAsync<ErrorResponse>("login",
				new LoginRequest { Platform = NonceTestPlatformVerifier.Name, Credential = NonceTestPlatformVerifier.Credential("alice", "x") });
			var (unissuedStatus, unissued) = await server.LoginNonceTestAsync<ErrorResponse>("alice", OneTimeValues.NewValue());
			var (malformedStatus, malformed) = await server.LoginNonceTestAsync<ErrorResponse>("alice", "not a nonce");

			Assert.Equal(400, missingStatus);
			Assert.Equal(AccountResultCodes.NonceMissing, missing.Code);
			Assert.Equal(401, unissuedStatus);
			Assert.Equal(AccountResultCodes.InvalidNonce, unissued.Code);
			Assert.Equal(400, malformedStatus);
			Assert.Equal(AccountResultCodes.BadRequest, malformed.Code);
		}

		[Fact]
		public async Task Guest_NeedsNoNonce()
		{
			await using var server = await AccountTestServer.StartAsync();

			var (status, login) = await server.LoginGuestAsync(AccountTestServer.NewGuestSecret());

			Assert.Equal(200, status);
			Assert.True(login.IsNewAccount);
		}

		[Fact]
		public async Task ConcurrentLogins_WithOneNonce_OnlyOneSucceeds()
		{
			await using var server = await AccountTestServer.StartAsync();
			var nonce = await server.IssueNonceAsync();

			var results = await Task.WhenAll(Enumerable.Range(0, 16).Select(_ => server.LoginNonceTestAsync<ErrorResponse>("alice", nonce)));

			Assert.Single(results, result => result.Status == 200);
			Assert.All(results.Where(result => result.Status != 200), result => Assert.Equal(AccountResultCodes.InvalidNonce, result.Body.Code));
		}

		[Fact]
		public async Task Store_ConcurrentConsume_GivesTheValueToOneCaller()
		{
			var store = new InMemoryOneTimeValueStore();
			var now = AccountTestServer.Start;
			await store.TryAddAsync(new OneTimeValue { Kind = OneTimeValueKind.Nonce, Hash = "h", ExpiresAt = now.AddMinutes(5) }, now, CancellationToken.None);
			using var start = new ManualResetEventSlim();

			var consumers = Enumerable.Range(0, 8).Select(_ => Task.Run(() =>
			{
				start.Wait();
				return store.TryConsumeAsync(OneTimeValueKind.Nonce, "h", now, CancellationToken.None);
			})).ToArray();
			start.Set();
			var values = await Task.WhenAll(consumers);

			Assert.Single(values, value => value != null);
		}

		[Fact]
		public async Task Store_IsBounded_DropsExpiredValues_AndSeparatesKinds()
		{
			var store = new InMemoryOneTimeValueStore(2);
			var now = AccountTestServer.Start;
			OneTimeValue Value(OneTimeValueKind kind, string hash, int minutes) => new OneTimeValue { Kind = kind, Hash = hash, ExpiresAt = now.AddMinutes(minutes) };

			Assert.True(await store.TryAddAsync(Value(OneTimeValueKind.Nonce, "a", 5), now, CancellationToken.None));
			Assert.True(await store.TryAddAsync(Value(OneTimeValueKind.ConflictTicket, "b", 5), now, CancellationToken.None));
			Assert.False(await store.TryAddAsync(Value(OneTimeValueKind.Nonce, "c", 5), now, CancellationToken.None));
			// 같은 해시라도 용도가 다르면 다른 값이다
			Assert.Null(await store.TryConsumeAsync(OneTimeValueKind.ConflictTicket, "a", now, CancellationToken.None));
			Assert.Null(await store.TryConsumeAsync(OneTimeValueKind.Nonce, "a", now.AddMinutes(5), CancellationToken.None));
			Assert.True(await store.TryAddAsync(Value(OneTimeValueKind.Nonce, "d", 10), now.AddMinutes(5), CancellationToken.None));
			Assert.True(await store.TryAddAsync(Value(OneTimeValueKind.Nonce, "e", 10), now.AddMinutes(5), CancellationToken.None));
			Assert.Null(await store.TryConsumeAsync(OneTimeValueKind.ConflictTicket, "b", now.AddMinutes(5), CancellationToken.None));
		}

		[Fact]
		public async Task Store_ConsumedOrOverwrittenValues_DoNotPileUp()
		{
			var store = new InMemoryOneTimeValueStore(2);
			var now = AccountTestServer.Start;
			var retained = 0;

			for (var i = 0; i < 100; i++)
			{
				var value = new OneTimeValue { Kind = OneTimeValueKind.Nonce, Hash = "v" + i, ExpiresAt = now.AddMinutes(5) };
				Assert.True(await store.TryAddAsync(value, now, CancellationToken.None));
				Assert.NotNull(await store.TryConsumeAsync(OneTimeValueKind.Nonce, value.Hash, now, CancellationToken.None));
				// 같은 키를 덮어쓰는 것도 쌓이지 않는다
				Assert.True(await store.TryAddAsync(value with { Hash = "same" }, now, CancellationToken.None));
				retained = Math.Max(retained, store.RetainedCount);
			}

			Assert.InRange(retained, 1, 2 * store.Capacity);
			Assert.NotNull(await store.TryConsumeAsync(OneTimeValueKind.Nonce, "same", now, CancellationToken.None));
		}

		[Fact]
		public async Task FullStore_IsRetryLater()
		{
			await using var server = await AccountTestServer.StartAsync(services: services => services.AddSingleton<IOneTimeValueStore>(new InMemoryOneTimeValueStore(2)));
			await server.IssueNonceAsync();
			await server.IssueNonceAsync();

			var (fullStatus, full) = await server.PostAsync<ErrorResponse>("nonce", null);
			server.Time.Advance(TimeSpan.FromMinutes(5));
			var (laterStatus, _) = await server.PostAsync<NonceResponse>("nonce", null);

			Assert.Equal(503, fullStatus);
			Assert.Equal(AccountResultCodes.RetryLater, full.Code);
			Assert.Equal(200, laterStatus);
		}

		[Fact]
		public async Task Link_ConsumesTheNonce()
		{
			await using var server = await AccountTestServer.StartAsync();
			var (_, mine) = await server.LoginGuestAsync(AccountTestServer.NewGuestSecret());
			var nonce = await server.IssueNonceAsync();

			var (linkStatus, _) = await LinkAsync<LinksResponse>(server, mine.AccessToken, "alice", nonce);
			var (againStatus, again) = await server.LoginNonceTestAsync<ErrorResponse>("alice", nonce);

			Assert.Equal(200, linkStatus);
			Assert.Equal(401, againStatus);
			Assert.Equal(AccountResultCodes.InvalidNonce, again.Code);
		}

		[Fact]
		public async Task NonceRoute_NeedsNoAccessToken_AndTakesNoBody()
		{
			await using var server = await AccountTestServer.StartAsync();
			using var request = new HttpRequestMessage(HttpMethod.Post, "nonce");

			var (status, body) = await server.SendAsync<NonceResponse>(request);

			Assert.Equal(200, status);
			Assert.False(string.IsNullOrEmpty(body.Nonce));
		}
	}
}
