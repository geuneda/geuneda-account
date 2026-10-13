using System;
using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace Geuneda.Account.Server.Tests
{
	public class AccessTokenTest
	{
		private static IAccessTokenValidator GameServerValidator(Action<AccessTokenValidationOptions> configure, TimeProvider time)
		{
			var services = new ServiceCollection();
			services.AddSingleton(time);
			services.AddGeunedaAccessTokenValidation(configure);
			return services.BuildServiceProvider().GetRequiredService<IAccessTokenValidator>();
		}

		[Fact]
		public async Task Expired_AfterLifetimeAndSkew()
		{
			await using var server = await AccountTestServer.StartAsync();
			var validator = server.Services.GetRequiredService<IAccessTokenValidator>();
			var (_, login) = await server.LoginGuestAsync(AccountTestServer.NewGuestSecret());

			server.Time.Advance(TimeSpan.FromHours(1) + TimeSpan.FromSeconds(59));
			var withinSkew = await validator.ValidateAsync(login.AccessToken);
			server.Time.Advance(TimeSpan.FromSeconds(1));
			var expired = await validator.ValidateAsync(login.AccessToken);

			Assert.True(withinSkew.IsValid);
			Assert.False(expired.IsValid);
		}

		[Fact]
		public async Task GameServerValidator_AcceptsTokensFromAuthServer_RejectsWrongKeyOrAudience()
		{
			await using var server = await AccountTestServer.StartAsync();
			var (_, login) = await server.LoginGuestAsync(AccountTestServer.NewGuestSecret());

			var matching = GameServerValidator(options =>
			{
				options.Issuer = AccountTestServer.Issuer;
				options.Audience = AccountTestServer.Audience;
				options.SigningKeys.Add(server.Key);
			}, server.Time);
			var wrongKey = GameServerValidator(options =>
			{
				options.Issuer = AccountTestServer.Issuer;
				options.Audience = AccountTestServer.Audience;
				options.SigningKeys.Add(new SymmetricSecurityKey(RandomNumberGenerator.GetBytes(32)));
			}, server.Time);
			var wrongAudience = GameServerValidator(options =>
			{
				options.Issuer = AccountTestServer.Issuer;
				options.Audience = "other-game";
				options.SigningKeys.Add(server.Key);
			}, server.Time);

			var valid = await matching.ValidateAsync(login.AccessToken);

			Assert.True(valid.IsValid, valid.FailureReason);
			Assert.Equal(login.AccountId, valid.AccountId);
			Assert.False((await wrongKey.ValidateAsync(login.AccessToken)).IsValid);
			Assert.False((await wrongAudience.ValidateAsync(login.AccessToken)).IsValid);
		}

		[Fact]
		public async Task TamperedOrUnsignedTokens_AreRejected()
		{
			await using var server = await AccountTestServer.StartAsync();
			var validator = server.Services.GetRequiredService<IAccessTokenValidator>();
			var (_, login) = await server.LoginGuestAsync(AccountTestServer.NewGuestSecret());
			var parts = login.AccessToken.Split('.');
			var payload = Encoding.UTF8.GetString(Base64Url.DecodeFromChars(parts[1])).Replace(login.AccountId, "someone-else");
			var tampered = parts[0] + "." + Base64Url.EncodeToString(Encoding.UTF8.GetBytes(payload)) + "." + parts[2];
			var unsigned = Base64Url.EncodeToString(Encoding.UTF8.GetBytes("{\"alg\":\"none\",\"typ\":\"JWT\"}")) + "." + parts[1] + ".";

			Assert.False((await validator.ValidateAsync(tampered)).IsValid);
			Assert.False((await validator.ValidateAsync(unsigned)).IsValid);
			Assert.False((await validator.ValidateAsync(null)).IsValid);
		}

		[Fact]
		public void GameServerValidator_WithoutKeys_Throws()
		{
			Assert.Throws<InvalidOperationException>(() => GameServerValidator(options =>
			{
				options.Issuer = "a";
				options.Audience = "b";
			}, new FakeTimeProvider()));
		}
	}
}
