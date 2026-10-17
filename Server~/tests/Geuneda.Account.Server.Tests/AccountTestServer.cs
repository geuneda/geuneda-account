using System;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Microsoft.IdentityModel.Tokens;

namespace Geuneda.Account.Server.Tests
{
	/// <summary>127.0.0.1 빈 포트에 띄운 인증 서버입니다. 시간은 <see cref="Time"/> 으로 움직입니다.</summary>
	internal sealed class AccountTestServer : IAsyncDisposable
	{
		public const string Issuer = "test-auth";
		public const string Audience = "test-game";
		public static readonly DateTimeOffset Start = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

		private readonly WebApplication _app;

		private AccountTestServer(WebApplication app, FakeTimeProvider time, SymmetricSecurityKey key, Uri baseAddress)
		{
			_app = app;
			Time = time;
			Key = key;
			Http = new HttpClient { BaseAddress = baseAddress };
		}

		public FakeTimeProvider Time { get; }
		public SymmetricSecurityKey Key { get; }
		public HttpClient Http { get; }
		public IServiceProvider Services => _app.Services;

		public static async Task<AccountTestServer> StartAsync(Action<AccountServerOptions> configure = null, Action<IServiceCollection> services = null)
		{
			var time = new FakeTimeProvider(Start);
			var key = new SymmetricSecurityKey(RandomNumberGenerator.GetBytes(32));
			var app = Build(time, options =>
			{
				options.Issuer = Issuer;
				options.Audience = Audience;
				options.SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
				configure?.Invoke(options);
			}, builder =>
			{
				services?.Invoke(builder.Services);
				builder.AddGuest().AddPlatformVerifier<TestPlatformVerifier>().AddPlatformVerifier<NonceTestPlatformVerifier>().AddInMemoryStores();
			});

			app.MapGeunedaAccount("/auth");
			await app.StartAsync();

			var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>().Addresses.First();
			return new AccountTestServer(app, time, key, new Uri(address + "/auth/"));
		}

		public static WebApplication Build(TimeProvider time, Action<AccountServerOptions> configure, Action<AccountServerBuilder> build)
		{
			var builder = WebApplication.CreateSlimBuilder();
			builder.WebHost.UseUrls("http://127.0.0.1:0");
			builder.Logging.ClearProviders();
			builder.Services.AddSingleton(time);
			build(builder.Services.AddGeunedaAccount(configure));
			return builder.Build();
		}

		public static string NewGuestSecret()
		{
			return System.Buffers.Text.Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));
		}

		public async Task<(int Status, T Body)> PostAsync<T>(string path, object body, string accessToken = null)
		{
			using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = body == null ? null : JsonContent.Create(body, options: AccountJson.Options) };
			return await SendAsync<T>(request, accessToken);
		}

		public async Task<(int Status, T Body)> SendAsync<T>(HttpRequestMessage request, string accessToken = null)
		{
			if (accessToken != null)
			{
				request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
			}

			using var response = await Http.SendAsync(request);
			var text = await response.Content.ReadAsStringAsync();
			var parsed = string.IsNullOrEmpty(text) ? default : JsonSerializer.Deserialize<T>(text, AccountJson.Options);
			return ((int) response.StatusCode, parsed);
		}

		public Task<(int Status, TokenResponse Body)> LoginGuestAsync(string secret, string sessionId = null)
		{
			return PostAsync<TokenResponse>("login", new LoginRequest { Platform = AccountPlatforms.Guest, Credential = secret, SessionId = sessionId });
		}

		public Task<(int Status, TokenResponse Body)> LoginTestAsync(string subject)
		{
			return PostAsync<TokenResponse>("login", new LoginRequest { Platform = TestPlatformVerifier.Name, Credential = TestPlatformVerifier.Credential(subject) });
		}

		/// <summary><c>POST nonce</c> 로 nonce 를 받습니다.</summary>
		public async Task<string> IssueNonceAsync()
		{
			var (status, body) = await PostAsync<NonceResponse>("nonce", null);

			if (status != 200 || string.IsNullOrEmpty(body?.Nonce))
			{
				throw new InvalidOperationException($"POST nonce failed with HTTP {status}");
			}

			return body.Nonce;
		}

		/// <summary>nonce 를 쓰는 시험용 플랫폼(<see cref="NonceTestPlatformVerifier"/>)으로 로그인합니다.</summary>
		public Task<(int Status, T Body)> LoginNonceTestAsync<T>(string subject, string nonce, string boundNonce = null)
		{
			return PostAsync<T>("login", new LoginRequest
			{
				Platform = NonceTestPlatformVerifier.Name,
				Credential = NonceTestPlatformVerifier.Credential(subject, boundNonce ?? nonce),
				Nonce = nonce
			});
		}

		public Task<(int Status, TokenResponse Body)> RefreshAsync(string refreshToken, string sessionId = null)
		{
			return PostAsync<TokenResponse>("refresh", new RefreshRequest { RefreshToken = refreshToken, SessionId = sessionId });
		}

		public async ValueTask DisposeAsync()
		{
			Http.Dispose();
			await _app.StopAsync();
			await _app.DisposeAsync();
		}
	}
}
