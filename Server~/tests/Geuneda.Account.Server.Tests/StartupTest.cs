using System;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace Geuneda.Account.Server.Tests
{
	public class StartupTest
	{
		private sealed class DuplicateGuestVerifier : IPlatformVerifier
		{
			public string Platform => AccountPlatforms.Guest;

			public Task<PlatformVerification> VerifyAsync(string credential, System.Threading.CancellationToken cancellationToken)
			{
				return Task.FromResult(PlatformVerification.Invalid("unused"));
			}
		}

		private static void ValidOptions(AccountServerOptions options)
		{
			options.Issuer = "a";
			options.Audience = "b";
			options.SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(RandomNumberGenerator.GetBytes(32)), SecurityAlgorithms.HmacSha256);
		}

		private static void Map(Action<AccountServerOptions> configure, Action<AccountServerBuilder> build)
		{
			var app = AccountTestServer.Build(new FakeTimeProvider(), configure, build);
			app.MapGeunedaAccount();
		}

		[Fact]
		public void MissingStores_Throws()
		{
			var exception = Assert.Throws<InvalidOperationException>(() => Map(ValidOptions, builder => builder.AddGuest()));
			Assert.Contains("IAccountStore", exception.Message);
		}

		[Fact]
		public void NoVerifier_Throws()
		{
			Assert.Throws<InvalidOperationException>(() => Map(ValidOptions, builder => builder.AddInMemoryStores()));
		}

		[Fact]
		public void DuplicatePlatform_Throws()
		{
			var exception = Assert.Throws<InvalidOperationException>(() =>
				Map(ValidOptions, builder => builder.AddGuest().AddPlatformVerifier<DuplicateGuestVerifier>().AddInMemoryStores()));
			Assert.Contains("guest", exception.Message);
		}

		[Fact]
		public void ShortHmacKey_Throws()
		{
			Assert.Throws<InvalidOperationException>(() => Map(options =>
			{
				ValidOptions(options);
				options.SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(new byte[8]), SecurityAlgorithms.HmacSha256);
			}, builder => builder.AddGuest().AddInMemoryStores()));
		}

		[Fact]
		public void GraceNotShorterThanRefreshLifetime_Throws()
		{
			Assert.Throws<InvalidOperationException>(() => Map(options =>
			{
				ValidOptions(options);
				options.RefreshReuseGrace = options.RefreshTokenLifetime;
			}, builder => builder.AddGuest().AddInMemoryStores()));
		}

		[Fact]
		public void MissingIssuer_Throws()
		{
			Assert.Throws<InvalidOperationException>(() => Map(options =>
			{
				ValidOptions(options);
				options.Issuer = null;
			}, builder => builder.AddGuest().AddInMemoryStores()));
		}

		[Fact]
		public async Task ScopedStores_AreResolvedPerRequest()
		{
			await using var server = await AccountTestServer.StartAsync(services: services =>
			{
				services.AddSingleton<InMemoryAccountStore>();
				services.AddScoped<IAccountStore>(provider => provider.GetRequiredService<InMemoryAccountStore>());
			});

			var (status, login) = await server.LoginGuestAsync(AccountTestServer.NewGuestSecret());

			Assert.Equal(200, status);
			Assert.True(login.IsNewAccount);
		}
	}
}
