using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Geuneda.Account.Server.Tests
{
	/// <summary>연동 충돌 티켓: 409 응답에 나가고, 그 플랫폼 계정으로의 로그인이나 티켓을 받은 계정의 옮겨오기에 id 토큰 대신 한 번만 씁니다.</summary>
	public class ConflictTicketTest
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

		private static Task<(int Status, T Body)> SignInWithTicketAsync<T>(AccountTestServer server, string ticket, string platform = NonceTestPlatformVerifier.Name)
		{
			return server.PostAsync<T>("login", new LoginRequest { Platform = platform, ConflictTicket = ticket });
		}

		private static Task<(int Status, T Body)> TransferWithTicketAsync<T>(AccountTestServer server, string accessToken, string ticket)
		{
			return server.PostAsync<T>("links/transfer", new LinkRequest { Platform = NonceTestPlatformVerifier.Name, ConflictTicket = ticket }, accessToken);
		}

		/// <summary>bob 계정(nonce 시험 플랫폼)과 게스트 계정을 만들고, 게스트에서 bob 을 연동해 충돌 응답을 받습니다.</summary>
		private static async Task<(TokenResponse Other, TokenResponse Mine, ErrorResponse Conflict, string Nonce)> ConflictAsync(AccountTestServer server)
		{
			var (_, other) = await server.LoginNonceTestAsync<TokenResponse>("bob", await server.IssueNonceAsync());
			var (_, mine) = await server.LoginGuestAsync(AccountTestServer.NewGuestSecret());
			var nonce = await server.IssueNonceAsync();
			var (status, conflict) = await LinkAsync<ErrorResponse>(server, mine.AccessToken, "bob", nonce);
			Assert.Equal(409, status);
			return (other, mine, conflict, nonce);
		}

		[Fact]
		public async Task Conflict_GivesTicket_ThatSignsInToTheOtherAccountOnce_WhileTheIdTokenStaysUsed()
		{
			await using var server = await AccountTestServer.StartAsync();
			var (other, _, conflict, nonce) = await ConflictAsync(server);

			var (replayStatus, replay) = await server.LoginNonceTestAsync<ErrorResponse>("bob", nonce);
			var (status, login) = await SignInWithTicketAsync<TokenResponse>(server, conflict.ConflictTicket);
			var (againStatus, again) = await SignInWithTicketAsync<ErrorResponse>(server, conflict.ConflictTicket);

			Assert.Equal(AccountResultCodes.LoginConflict, conflict.Code);
			Assert.Equal(other.AccountId, conflict.ConflictAccountId);
			Assert.True(RequestFields.IsCredentialOrTicket(null, null, conflict.ConflictTicket));
			// 충돌한 id 토큰은 연동 요청에서 이미 썼으므로 다시 통과하지 않는다
			Assert.Equal(401, replayStatus);
			Assert.Equal(AccountResultCodes.InvalidNonce, replay.Code);
			Assert.Equal(200, status);
			Assert.Equal(other.AccountId, login.AccountId);
			Assert.False(login.IsNewAccount);
			Assert.Equal(401, againStatus);
			Assert.Equal(AccountResultCodes.InvalidConflictTicket, again.Code);
		}

		[Fact]
		public async Task Ticket_TransfersOnce_ForTheAccountThatGotIt()
		{
			await using var server = await AccountTestServer.StartAsync();
			var (_, mine, conflict, _) = await ConflictAsync(server);

			var (status, links) = await TransferWithTicketAsync<LinksResponse>(server, mine.AccessToken, conflict.ConflictTicket);
			var (againStatus, again) = await TransferWithTicketAsync<ErrorResponse>(server, mine.AccessToken, conflict.ConflictTicket);
			var (_, relogin) = await server.LoginNonceTestAsync<TokenResponse>("bob", await server.IssueNonceAsync());

			Assert.Equal(200, status);
			Assert.Equal(2, links.Links.Count);
			Assert.Equal(401, againStatus);
			Assert.Equal(AccountResultCodes.InvalidConflictTicket, again.Code);
			Assert.Equal(mine.AccountId, relogin.AccountId);
		}

		[Fact]
		public async Task Ticket_FromAnotherAccount_IsRejected_AndBurned()
		{
			await using var server = await AccountTestServer.StartAsync();
			var (other, mine, conflict, _) = await ConflictAsync(server);
			var (_, stranger) = await server.LoginGuestAsync(AccountTestServer.NewGuestSecret());

			var (strangerStatus, strangerError) = await TransferWithTicketAsync<ErrorResponse>(server, stranger.AccessToken, conflict.ConflictTicket);
			var (mineStatus, _) = await TransferWithTicketAsync<ErrorResponse>(server, mine.AccessToken, conflict.ConflictTicket);
			var (_, relogin) = await server.LoginNonceTestAsync<TokenResponse>("bob", await server.IssueNonceAsync());

			Assert.Equal(401, strangerStatus);
			Assert.Equal(AccountResultCodes.InvalidConflictTicket, strangerError.Code);
			// 새어 나간 티켓은 받은 계정도 더 쓰지 못한다(연동부터 다시)
			Assert.Equal(401, mineStatus);
			Assert.Equal(other.AccountId, relogin.AccountId);
		}

		[Fact]
		public async Task Ticket_ExpiresAfterNonceLifetime()
		{
			await using var server = await AccountTestServer.StartAsync();
			var (_, mine, conflict, _) = await ConflictAsync(server);
			var (_, _, secondConflict, _) = await ConflictAsync(server);

			server.Time.Advance(TimeSpan.FromMinutes(5) - TimeSpan.FromSeconds(1));
			var (beforeStatus, _) = await TransferWithTicketAsync<LinksResponse>(server, mine.AccessToken, conflict.ConflictTicket);
			server.Time.Advance(TimeSpan.FromSeconds(1));
			var (afterStatus, after) = await SignInWithTicketAsync<ErrorResponse>(server, secondConflict.ConflictTicket);

			Assert.Equal(200, beforeStatus);
			Assert.Equal(401, afterStatus);
			Assert.Equal(AccountResultCodes.InvalidConflictTicket, after.Code);
		}

		[Fact]
		public async Task TicketWithIdTokenOrNonce_OrTicketForLink_IsBadRequest_AndKeepsTheTicket()
		{
			await using var server = await AccountTestServer.StartAsync();
			var (_, mine, conflict, nonce) = await ConflictAsync(server);
			var credential = NonceTestPlatformVerifier.Credential("bob", nonce);

			var (loginStatus, loginError) = await server.PostAsync<ErrorResponse>("login",
				new LoginRequest { Platform = NonceTestPlatformVerifier.Name, Credential = credential, ConflictTicket = conflict.ConflictTicket });
			var (nonceStatus, _) = await server.PostAsync<ErrorResponse>("login",
				new LoginRequest { Platform = NonceTestPlatformVerifier.Name, Nonce = nonce, ConflictTicket = conflict.ConflictTicket });
			var (transferStatus, _) = await server.PostAsync<ErrorResponse>("links/transfer",
				new LinkRequest { Platform = NonceTestPlatformVerifier.Name, Credential = credential, Nonce = nonce, ConflictTicket = conflict.ConflictTicket },
				mine.AccessToken);
			var (linkStatus, _) = await server.PostAsync<ErrorResponse>("links",
				new LinkRequest { Platform = NonceTestPlatformVerifier.Name, ConflictTicket = conflict.ConflictTicket }, mine.AccessToken);
			var (status, _) = await TransferWithTicketAsync<LinksResponse>(server, mine.AccessToken, conflict.ConflictTicket);

			Assert.Equal(400, loginStatus);
			Assert.Equal(AccountResultCodes.BadRequest, loginError.Code);
			Assert.Equal(400, nonceStatus);
			Assert.Equal(400, transferStatus);
			Assert.Equal(400, linkStatus);
			// 형식 오류는 티켓을 쓰지 않는다
			Assert.Equal(200, status);
		}

		[Fact]
		public async Task Ticket_ForAnotherPlatform_IsInvalid_AndTicketsAndNoncesDoNotMix()
		{
			await using var server = await AccountTestServer.StartAsync();
			var (_, _, conflict, _) = await ConflictAsync(server);
			var (_, _, secondConflict, _) = await ConflictAsync(server);
			var nonce = await server.IssueNonceAsync();

			var (platformStatus, platformError) = await SignInWithTicketAsync<ErrorResponse>(server, conflict.ConflictTicket, TestPlatformVerifier.Name);
			var (ticketAsNonceStatus, ticketAsNonce) = await server.LoginNonceTestAsync<ErrorResponse>("bob", secondConflict.ConflictTicket);
			var (nonceAsTicketStatus, nonceAsTicket) = await SignInWithTicketAsync<ErrorResponse>(server, nonce);

			Assert.Equal(401, platformStatus);
			Assert.Equal(AccountResultCodes.InvalidConflictTicket, platformError.Code);
			Assert.Equal(401, ticketAsNonceStatus);
			Assert.Equal(AccountResultCodes.InvalidNonce, ticketAsNonce.Code);
			Assert.Equal(401, nonceAsTicketStatus);
			Assert.Equal(AccountResultCodes.InvalidConflictTicket, nonceAsTicket.Code);
		}

		[Fact]
		public async Task PlatformWithoutNonce_AlsoGetsATicket()
		{
			await using var server = await AccountTestServer.StartAsync();
			var (_, other) = await server.LoginTestAsync("carol");
			var (_, mine) = await server.LoginGuestAsync(AccountTestServer.NewGuestSecret());

			var (status, conflict) = await server.PostAsync<ErrorResponse>("links",
				new LinkRequest { Platform = TestPlatformVerifier.Name, Credential = TestPlatformVerifier.Credential("carol") }, mine.AccessToken);
			var (loginStatus, login) = await SignInWithTicketAsync<TokenResponse>(server, conflict.ConflictTicket, TestPlatformVerifier.Name);

			Assert.Equal(409, status);
			Assert.Equal(200, loginStatus);
			Assert.Equal(other.AccountId, login.AccountId);
		}

		[Fact]
		public async Task FullStore_AtConflict_IsRetryLater()
		{
			await using var server = await AccountTestServer.StartAsync(services: services =>
				services.AddSingleton<IOneTimeValueStore>(new InMemoryOneTimeValueStore(1)));
			await server.LoginTestAsync("carol");
			var (_, mine) = await server.LoginGuestAsync(AccountTestServer.NewGuestSecret());
			await server.IssueNonceAsync();

			var (status, error) = await server.PostAsync<ErrorResponse>("links",
				new LinkRequest { Platform = TestPlatformVerifier.Name, Credential = TestPlatformVerifier.Credential("carol") }, mine.AccessToken);

			Assert.Equal(503, status);
			Assert.Equal(AccountResultCodes.RetryLater, error.Code);
		}
	}
}
