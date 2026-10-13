using System;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Geuneda.Account.Server.Tests
{
	public class LoginTest
	{
		[Fact]
		public async Task Guest_NewSecret_CreatesAccount_ThenSameSecretReturnsSameAccount()
		{
			await using var server = await AccountTestServer.StartAsync();
			var secret = AccountTestServer.NewGuestSecret();

			var (status, first) = await server.LoginGuestAsync(secret);
			var (_, second) = await server.LoginGuestAsync(secret);

			Assert.Equal(200, status);
			Assert.True(first.IsNewAccount);
			Assert.False(second.IsNewAccount);
			Assert.Equal(first.AccountId, second.AccountId);
			Assert.False(string.IsNullOrEmpty(first.AccessToken));
			Assert.False(string.IsNullOrEmpty(first.RefreshToken));
			Assert.NotEqual(first.RefreshToken, second.RefreshToken);
			Assert.Equal(3600, first.AccessTokenExpiresIn);
			Assert.Equal((int) TimeSpan.FromDays(30).TotalSeconds, first.RefreshTokenExpiresIn);
		}

		[Fact]
		public async Task DifferentSecrets_CreateDifferentAccounts()
		{
			await using var server = await AccountTestServer.StartAsync();

			var (_, first) = await server.LoginGuestAsync(AccountTestServer.NewGuestSecret());
			var (_, second) = await server.LoginGuestAsync(AccountTestServer.NewGuestSecret());

			Assert.NotEqual(first.AccountId, second.AccountId);
		}

		[Fact]
		public async Task AccessToken_CarriesAccountAndSession()
		{
			await using var server = await AccountTestServer.StartAsync();
			var (_, tokens) = await server.LoginGuestAsync(AccountTestServer.NewGuestSecret(), "0001a2b3c4d5e6f7");

			var validation = await server.Services.GetRequiredService<IAccessTokenValidator>().ValidateAsync(tokens.AccessToken);

			Assert.True(validation.IsValid, validation.FailureReason);
			Assert.Equal(tokens.AccountId, validation.AccountId);
			Assert.Equal("0001a2b3c4d5e6f7", validation.SessionId);
			Assert.Equal(AccountTestServer.Start.AddHours(1), validation.ExpiresAt);
		}

		[Theory]
		[InlineData("short")]
		[InlineData("not base64 !!")]
		[InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
		public async Task Guest_InvalidSecret_IsInvalidCredential(string secret)
		{
			await using var server = await AccountTestServer.StartAsync();

			var (status, error) = await server.PostAsync<ErrorResponse>("login", new LoginRequest { Platform = AccountPlatforms.Guest, Credential = secret });

			Assert.Equal(401, status);
			Assert.Equal(AccountResultCodes.InvalidCredential, error.Code);
		}

		[Fact]
		public async Task UnknownPlatform_IsUnsupported()
		{
			await using var server = await AccountTestServer.StartAsync();

			var (status, error) = await server.PostAsync<ErrorResponse>("login", new LoginRequest { Platform = "facebook", Credential = "x" });

			Assert.Equal(400, status);
			Assert.Equal(AccountResultCodes.UnsupportedPlatform, error.Code);
		}

		[Theory]
		[InlineData("{")]
		[InlineData("null")]
		[InlineData("{\"platform\":\"guest\"}")]
		[InlineData("{\"platform\":\"guest\",\"credential\":\"x\",\"sessionId\":\"has space\"}")]
		[InlineData("{\"platform\":\"gu/est\",\"credential\":\"x\"}")]
		public async Task MalformedRequest_IsBadRequest(string json)
		{
			await using var server = await AccountTestServer.StartAsync();
			using var request = new HttpRequestMessage(HttpMethod.Post, "login") { Content = new StringContent(json, Encoding.UTF8, "application/json") };

			var (status, error) = await server.SendAsync<ErrorResponse>(request);

			Assert.Equal(400, status);
			Assert.Equal(AccountResultCodes.BadRequest, error.Code);
		}

		[Fact]
		public async Task OversizedBody_IsBadRequest()
		{
			await using var server = await AccountTestServer.StartAsync();
			var credential = new string('a', RequestBody.MaxBytes);

			var (status, error) = await server.PostAsync<ErrorResponse>("login", new LoginRequest { Platform = AccountPlatforms.Guest, Credential = credential });

			Assert.Equal(400, status);
			Assert.Equal(AccountResultCodes.BadRequest, error.Code);
		}

		[Fact]
		public async Task ConcurrentFirstLogins_WithSameSecret_CreateOneAccount()
		{
			await using var server = await AccountTestServer.StartAsync();
			var secret = AccountTestServer.NewGuestSecret();

			var results = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => server.LoginGuestAsync(secret)));

			Assert.All(results, result => Assert.Equal(200, result.Status));
			Assert.Single(results.Select(result => result.Body.AccountId).Distinct());
			Assert.Single(results, result => result.Body.IsNewAccount);
		}
	}
}
