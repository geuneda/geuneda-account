using System;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Xunit;

namespace Geuneda.Account.Server.Tests
{
	public class LinkTest
	{
		private static Task<(int Status, LinksResponse Body)> GetLinksAsync(AccountTestServer server, string accessToken)
		{
			return server.SendAsync<LinksResponse>(new HttpRequestMessage(HttpMethod.Get, "links"), accessToken);
		}

		private static Task<(int Status, T Body)> LinkAsync<T>(AccountTestServer server, string accessToken, string subject, string path = "links")
		{
			return server.PostAsync<T>(path, new LinkRequest { Platform = TestPlatformVerifier.Name, Credential = TestPlatformVerifier.Credential(subject) }, accessToken);
		}

		private static Task<(int Status, T Body)> UnlinkAsync<T>(AccountTestServer server, string accessToken, string platform)
		{
			return server.SendAsync<T>(new HttpRequestMessage(HttpMethod.Delete, "links/" + platform), accessToken);
		}

		[Fact]
		public async Task Links_ListLoginsInOrder()
		{
			await using var server = await AccountTestServer.StartAsync();
			var (_, login) = await server.LoginGuestAsync(AccountTestServer.NewGuestSecret());
			server.Time.Advance(TimeSpan.FromMinutes(1));

			var (linkStatus, linked) = await LinkAsync<LinksResponse>(server, login.AccessToken, "alice");
			var (listStatus, listed) = await GetLinksAsync(server, login.AccessToken);

			Assert.Equal(200, linkStatus);
			Assert.Equal(200, listStatus);
			Assert.Equal(new[] { AccountPlatforms.Guest, TestPlatformVerifier.Name }, listed.Links.Select(link => link.Platform));
			Assert.Equal(AccountTestServer.Start.AddMinutes(1).ToUnixTimeMilliseconds(), linked.Links[1].LinkedAt);
		}

		[Fact]
		public async Task Link_LoginOfAnotherAccount_IsConflictWithThatAccount()
		{
			await using var server = await AccountTestServer.StartAsync();
			var (_, other) = await server.LoginTestAsync("bob");
			var (_, mine) = await server.LoginGuestAsync(AccountTestServer.NewGuestSecret());

			var (status, error) = await LinkAsync<ErrorResponse>(server, mine.AccessToken, "bob");

			Assert.Equal(409, status);
			Assert.Equal(AccountResultCodes.LoginConflict, error.Code);
			Assert.Equal(other.AccountId, error.ConflictAccountId);
		}

		[Fact]
		public async Task Link_SecondLoginOfSamePlatform_IsAlreadyLinked_SameLoginIsIdempotent()
		{
			await using var server = await AccountTestServer.StartAsync();
			var (_, login) = await server.LoginGuestAsync(AccountTestServer.NewGuestSecret());
			await LinkAsync<LinksResponse>(server, login.AccessToken, "alice");

			var (againStatus, _) = await LinkAsync<LinksResponse>(server, login.AccessToken, "alice");
			var (otherStatus, error) = await LinkAsync<ErrorResponse>(server, login.AccessToken, "carol");

			Assert.Equal(200, againStatus);
			Assert.Equal(409, otherStatus);
			Assert.Equal(AccountResultCodes.PlatformAlreadyLinked, error.Code);
		}

		[Fact]
		public async Task Link_InvalidCredential_IsRejected()
		{
			await using var server = await AccountTestServer.StartAsync();
			var (_, login) = await server.LoginGuestAsync(AccountTestServer.NewGuestSecret());

			var (status, error) = await server.PostAsync<ErrorResponse>("links", new LinkRequest { Platform = TestPlatformVerifier.Name, Credential = "bad" }, login.AccessToken);

			Assert.Equal(401, status);
			Assert.Equal(AccountResultCodes.InvalidCredential, error.Code);
		}

		[Fact]
		public async Task Transfer_MovesLoginFromOtherAccount()
		{
			await using var server = await AccountTestServer.StartAsync();
			var (_, other) = await server.LoginTestAsync("bob");
			var (_, mine) = await server.LoginGuestAsync(AccountTestServer.NewGuestSecret());

			var (status, links) = await LinkAsync<LinksResponse>(server, mine.AccessToken, "bob", "links/transfer");
			var (_, relogin) = await server.LoginTestAsync("bob");
			var (otherLinksStatus, otherLinks) = await GetLinksAsync(server, other.AccessToken);

			Assert.Equal(200, status);
			Assert.Equal(2, links.Links.Count);
			Assert.Equal(mine.AccountId, relogin.AccountId);
			Assert.Equal(200, otherLinksStatus);
			Assert.Empty(otherLinks.Links);
		}

		[Fact]
		public async Task Unlink_LastLogin_IsRejected_OtherwiseRemoved()
		{
			await using var server = await AccountTestServer.StartAsync();
			var (_, login) = await server.LoginGuestAsync(AccountTestServer.NewGuestSecret());

			var (lastStatus, lastError) = await UnlinkAsync<ErrorResponse>(server, login.AccessToken, AccountPlatforms.Guest);
			await LinkAsync<LinksResponse>(server, login.AccessToken, "alice");
			var (unlinkStatus, links) = await UnlinkAsync<LinksResponse>(server, login.AccessToken, TestPlatformVerifier.Name);
			var (missingStatus, _) = await UnlinkAsync<LinksResponse>(server, login.AccessToken, TestPlatformVerifier.Name);
			var (_, fresh) = await server.LoginTestAsync("alice");

			Assert.Equal(409, lastStatus);
			Assert.Equal(AccountResultCodes.LastLogin, lastError.Code);
			Assert.Equal(200, unlinkStatus);
			Assert.Equal(new[] { AccountPlatforms.Guest }, links.Links.Select(link => link.Platform));
			Assert.Equal(200, missingStatus);
			Assert.True(fresh.IsNewAccount);
		}

		[Fact]
		public async Task ProtectedRoutes_NeedValidAccessToken()
		{
			await using var server = await AccountTestServer.StartAsync();
			var (_, login) = await server.LoginGuestAsync(AccountTestServer.NewGuestSecret());

			var (missingStatus, missing) = await server.SendAsync<ErrorResponse>(new HttpRequestMessage(HttpMethod.Get, "links"));
			var (garbageStatus, _) = await GetLinksAsync(server, "garbage");
			server.Time.Advance(TimeSpan.FromHours(1) + TimeSpan.FromMinutes(1));
			var (expiredStatus, expired) = await server.SendAsync<ErrorResponse>(new HttpRequestMessage(HttpMethod.Get, "links"), login.AccessToken);

			Assert.Equal(401, missingStatus);
			Assert.Equal(AccountResultCodes.InvalidAccessToken, missing.Code);
			Assert.Equal(401, garbageStatus);
			Assert.Equal(401, expiredStatus);
			Assert.Equal(AccountResultCodes.InvalidAccessToken, expired.Code);
		}

		[Fact]
		public async Task DeleteAccount_RemovesLoginsAndRevokesTokens()
		{
			await using var server = await AccountTestServer.StartAsync();
			var secret = AccountTestServer.NewGuestSecret();
			var (_, login) = await server.LoginGuestAsync(secret);

			var (deleteStatus, _) = await server.SendAsync<ErrorResponse>(new HttpRequestMessage(HttpMethod.Delete, "account"), login.AccessToken);
			var (againStatus, again) = await server.SendAsync<ErrorResponse>(new HttpRequestMessage(HttpMethod.Delete, "account"), login.AccessToken);
			var (linksStatus, _) = await server.SendAsync<ErrorResponse>(new HttpRequestMessage(HttpMethod.Get, "links"), login.AccessToken);
			var (refreshStatus, _) = await server.PostAsync<ErrorResponse>("refresh", new RefreshRequest { RefreshToken = login.RefreshToken });
			var (_, relogin) = await server.LoginGuestAsync(secret);

			Assert.Equal(204, deleteStatus);
			Assert.Equal(404, againStatus);
			Assert.Equal(AccountResultCodes.AccountNotFound, again.Code);
			Assert.Equal(404, linksStatus);
			Assert.Equal(401, refreshStatus);
			Assert.True(relogin.IsNewAccount);
			Assert.NotEqual(login.AccountId, relogin.AccountId);
		}
	}
}
