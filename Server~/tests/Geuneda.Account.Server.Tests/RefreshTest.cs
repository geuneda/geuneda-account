using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Geuneda.Account.Server.Tests
{
	public class RefreshTest
	{
		[Fact]
		public async Task Refresh_RotatesTokens()
		{
			await using var server = await AccountTestServer.StartAsync();
			var (_, login) = await server.LoginGuestAsync(AccountTestServer.NewGuestSecret());

			var (status, refreshed) = await server.RefreshAsync(login.RefreshToken);

			Assert.Equal(200, status);
			Assert.Equal(login.AccountId, refreshed.AccountId);
			Assert.False(refreshed.IsNewAccount);
			Assert.NotEqual(login.RefreshToken, refreshed.RefreshToken);
			Assert.NotEqual(login.AccessToken, refreshed.AccessToken);
		}

		[Fact]
		public async Task ReuseAfterGrace_RevokesWholeLogin()
		{
			await using var server = await AccountTestServer.StartAsync();
			var (_, login) = await server.LoginGuestAsync(AccountTestServer.NewGuestSecret());
			var (_, refreshed) = await server.RefreshAsync(login.RefreshToken);
			server.Time.Advance(TimeSpan.FromSeconds(30));

			var (reuseStatus, reuse) = await server.PostAsync<ErrorResponse>("refresh", new RefreshRequest { RefreshToken = login.RefreshToken });
			var (laterStatus, later) = await server.PostAsync<ErrorResponse>("refresh", new RefreshRequest { RefreshToken = refreshed.RefreshToken });

			Assert.Equal(401, reuseStatus);
			Assert.Equal(AccountResultCodes.RefreshTokenReused, reuse.Code);
			Assert.Equal(401, laterStatus);
			Assert.Equal(AccountResultCodes.InvalidRefreshToken, later.Code);
		}

		[Fact]
		public async Task RetryWithinGrace_IssuesNewToken_ThatKeepsWorking()
		{
			await using var server = await AccountTestServer.StartAsync();
			var (_, login) = await server.LoginGuestAsync(AccountTestServer.NewGuestSecret());
			await server.RefreshAsync(login.RefreshToken);
			server.Time.Advance(TimeSpan.FromSeconds(5));

			var (retryStatus, retried) = await server.RefreshAsync(login.RefreshToken);
			var (nextStatus, _) = await server.RefreshAsync(retried.RefreshToken);

			Assert.Equal(200, retryStatus);
			Assert.Equal(200, nextStatus);
		}

		[Fact]
		public async Task LostTokenPresentedAfterRetry_IsReuse_AndRevokesLogin()
		{
			await using var server = await AccountTestServer.StartAsync();
			var (_, login) = await server.LoginGuestAsync(AccountTestServer.NewGuestSecret());
			var (_, lost) = await server.RefreshAsync(login.RefreshToken);
			var (_, retried) = await server.RefreshAsync(login.RefreshToken);

			var (lostStatus, lostError) = await server.PostAsync<ErrorResponse>("refresh", new RefreshRequest { RefreshToken = lost.RefreshToken });
			var (retriedStatus, _) = await server.PostAsync<ErrorResponse>("refresh", new RefreshRequest { RefreshToken = retried.RefreshToken });

			Assert.Equal(401, lostStatus);
			Assert.Equal(AccountResultCodes.RefreshTokenReused, lostError.Code);
			Assert.Equal(401, retriedStatus);
		}

		[Fact]
		public async Task OlderTokenAfterTwoRotations_IsReuse_EvenWithinGrace()
		{
			await using var server = await AccountTestServer.StartAsync();
			var (_, r0) = await server.LoginGuestAsync(AccountTestServer.NewGuestSecret());
			var (_, r1) = await server.RefreshAsync(r0.RefreshToken);
			server.Time.Advance(TimeSpan.FromSeconds(2));
			var (_, r2) = await server.RefreshAsync(r1.RefreshToken);
			server.Time.Advance(TimeSpan.FromSeconds(2));

			var (reuseStatus, reuse) = await server.PostAsync<ErrorResponse>("refresh", new RefreshRequest { RefreshToken = r0.RefreshToken });
			var (r2Status, _) = await server.PostAsync<ErrorResponse>("refresh", new RefreshRequest { RefreshToken = r2.RefreshToken });

			Assert.Equal(401, reuseStatus);
			Assert.Equal(AccountResultCodes.RefreshTokenReused, reuse.Code);
			Assert.Equal(401, r2Status);
		}

		[Fact]
		public async Task RetriesDoNotExtendGrace()
		{
			await using var server = await AccountTestServer.StartAsync();
			var (_, login) = await server.LoginGuestAsync(AccountTestServer.NewGuestSecret());
			await server.RefreshAsync(login.RefreshToken);
			server.Time.Advance(TimeSpan.FromSeconds(20));
			var (_, retried) = await server.RefreshAsync(login.RefreshToken);
			server.Time.Advance(TimeSpan.FromSeconds(11));

			var (status, error) = await server.PostAsync<ErrorResponse>("refresh", new RefreshRequest { RefreshToken = login.RefreshToken });
			var (retriedStatus, _) = await server.PostAsync<ErrorResponse>("refresh", new RefreshRequest { RefreshToken = retried.RefreshToken });

			Assert.Equal(AccountResultCodes.RefreshTokenReused, error.Code);
			Assert.Equal(401, status);
			Assert.Equal(401, retriedStatus);
		}

		[Fact]
		public async Task ZeroGrace_TreatsRetryAsReuse()
		{
			await using var server = await AccountTestServer.StartAsync(options => options.RefreshReuseGrace = TimeSpan.Zero);
			var (_, login) = await server.LoginGuestAsync(AccountTestServer.NewGuestSecret());
			await server.RefreshAsync(login.RefreshToken);

			var (status, error) = await server.PostAsync<ErrorResponse>("refresh", new RefreshRequest { RefreshToken = login.RefreshToken });

			Assert.Equal(401, status);
			Assert.Equal(AccountResultCodes.RefreshTokenReused, error.Code);
		}

		[Fact]
		public async Task Expired_IsInvalid_AndUseSlidesExpiry()
		{
			await using var server = await AccountTestServer.StartAsync();
			var (_, login) = await server.LoginGuestAsync(AccountTestServer.NewGuestSecret());
			server.Time.Advance(TimeSpan.FromDays(20));
			var (_, refreshed) = await server.RefreshAsync(login.RefreshToken);
			server.Time.Advance(TimeSpan.FromDays(25));

			var (slidStatus, slid) = await server.RefreshAsync(refreshed.RefreshToken);
			server.Time.Advance(TimeSpan.FromDays(30));
			var (expiredStatus, expired) = await server.PostAsync<ErrorResponse>("refresh", new RefreshRequest { RefreshToken = slid.RefreshToken });

			Assert.Equal(200, slidStatus);
			Assert.Equal(401, expiredStatus);
			Assert.Equal(AccountResultCodes.InvalidRefreshToken, expired.Code);
		}

		[Fact]
		public async Task UnknownToken_IsInvalid()
		{
			await using var server = await AccountTestServer.StartAsync();

			var (status, error) = await server.PostAsync<ErrorResponse>("refresh", new RefreshRequest { RefreshToken = "unknown" });

			Assert.Equal(401, status);
			Assert.Equal(AccountResultCodes.InvalidRefreshToken, error.Code);
		}

		[Fact]
		public async Task SessionId_KeptWhenNotSent_ReplacedWhenSent()
		{
			await using var server = await AccountTestServer.StartAsync();
			var validator = server.Services.GetRequiredService<IAccessTokenValidator>();
			var (_, login) = await server.LoginGuestAsync(AccountTestServer.NewGuestSecret(), "session-1");

			var (_, kept) = await server.RefreshAsync(login.RefreshToken);
			var (_, replaced) = await server.RefreshAsync(kept.RefreshToken, "session-2");
			var (_, keptAgain) = await server.RefreshAsync(replaced.RefreshToken);

			Assert.Equal("session-1", (await validator.ValidateAsync(kept.AccessToken)).SessionId);
			Assert.Equal("session-2", (await validator.ValidateAsync(replaced.AccessToken)).SessionId);
			Assert.Equal("session-2", (await validator.ValidateAsync(keptAgain.AccessToken)).SessionId);
		}

		[Fact]
		public async Task ConcurrentRefresh_LeavesExactlyOneLiveToken()
		{
			await using var server = await AccountTestServer.StartAsync();
			var (_, login) = await server.LoginGuestAsync(AccountTestServer.NewGuestSecret());

			var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => server.RefreshAsync(login.RefreshToken)));

			var store = server.Services.GetRequiredService<IRefreshTokenStore>();
			var issued = results.Where(result => result.Status == 200).Select(result => result.Body.RefreshToken).ToArray();
			var familyId = (await store.FindTokenAsync(RefreshTokens.Hash(login.RefreshToken), CancellationToken.None)).FamilyId;
			var family = await store.FindFamilyAsync(familyId, CancellationToken.None);

			Assert.NotEmpty(issued);
			Assert.Single(issued, token => RefreshTokens.Hash(token) == family.CurrentTokenHash);
			Assert.Null(family.RevokedAt);
		}

		[Fact]
		public async Task LosingRotationRaceRepeatedly_IsRetryLater_AndTokenStaysValid()
		{
			var store = new FlakyRotateStore();
			await using var server = await AccountTestServer.StartAsync(services: services => services.AddSingleton<IRefreshTokenStore>(store));
			var (_, login) = await server.LoginGuestAsync(AccountTestServer.NewGuestSecret());
			store.FailRotations = 3;

			var (busyStatus, busy) = await server.PostAsync<ErrorResponse>("refresh", new RefreshRequest { RefreshToken = login.RefreshToken });
			var (retryStatus, _) = await server.RefreshAsync(login.RefreshToken);

			Assert.Equal(503, busyStatus);
			Assert.Equal(AccountResultCodes.RetryLater, busy.Code);
			Assert.Equal(200, retryStatus);
		}

		[Fact]
		public async Task Logout_RevokesLogin_UnknownTokenStillNoContent()
		{
			await using var server = await AccountTestServer.StartAsync();
			var (_, login) = await server.LoginGuestAsync(AccountTestServer.NewGuestSecret());
			var (_, refreshed) = await server.RefreshAsync(login.RefreshToken);

			var (logoutStatus, _) = await server.PostAsync<ErrorResponse>("logout", new LogoutRequest { RefreshToken = refreshed.RefreshToken });
			var (unknownStatus, _) = await server.PostAsync<ErrorResponse>("logout", new LogoutRequest { RefreshToken = "unknown" });
			var (refreshStatus, error) = await server.PostAsync<ErrorResponse>("refresh", new RefreshRequest { RefreshToken = refreshed.RefreshToken });

			Assert.Equal(204, logoutStatus);
			Assert.Equal(204, unknownStatus);
			Assert.Equal(401, refreshStatus);
			Assert.Equal(AccountResultCodes.InvalidRefreshToken, error.Code);
		}

		[Fact]
		public async Task OtherLogins_AreNotRevokedByLogout()
		{
			await using var server = await AccountTestServer.StartAsync();
			var secret = AccountTestServer.NewGuestSecret();
			var (_, deviceA) = await server.LoginGuestAsync(secret);
			var (_, deviceB) = await server.LoginGuestAsync(secret);

			await server.PostAsync<ErrorResponse>("logout", new LogoutRequest { RefreshToken = deviceA.RefreshToken });
			var (status, _) = await server.RefreshAsync(deviceB.RefreshToken);

			Assert.Equal(200, status);
		}
	}
}
