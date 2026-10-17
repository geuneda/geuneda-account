using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Geuneda.Account.Server
{
	public static class AccountServiceCollectionExtensions
	{
		/// <summary>
		/// 인증 서버를 등록합니다. 검증기와 저장소는 돌려받은 빌더로 더하고, 엔드포인트는 <c>app.MapGeunedaAccount()</c> 로 연결합니다.
		/// <see cref="IAccessTokenValidator"/> 도 같은 키로 등록합니다.
		/// </summary>
		/// <example>
		/// <code>
		/// builder.Services.AddGeunedaAccount(options =>
		/// {
		///     options.Issuer = "https://auth.example.com";
		///     options.Audience = "my-game";
		///     options.SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
		/// }).AddGuest().AddInMemoryStores();
		/// app.MapGeunedaAccount("/auth");
		/// </code>
		/// </example>
		public static AccountServerBuilder AddGeunedaAccount(this IServiceCollection services, Action<AccountServerOptions> configure)
		{
			ArgumentNullException.ThrowIfNull(services);
			ArgumentNullException.ThrowIfNull(configure);

			services.Configure(configure);
			services.TryAddSingleton(TimeProvider.System);
			services.TryAddSingleton<AccountTokenIssuer>();
			services.TryAddSingleton<PlatformVerifierRegistry>();
			services.TryAddScoped<PlatformCredentials>();
			services.TryAddScoped<NonceEndpoints>();
			services.TryAddScoped<TokenEndpoints>();
			services.TryAddScoped<LinkEndpoints>();
			services.TryAddSingleton<IAccessTokenValidator>(provider =>
			{
				var options = provider.GetRequiredService<IOptions<AccountServerOptions>>().Value;
				return new AccessTokenValidator(options.Issuer, options.Audience, new[] { options.SigningCredentials.Key }, options.ClockSkew,
					provider.GetRequiredService<TimeProvider>());
			});
			return new AccountServerBuilder(services);
		}

		/// <summary>
		/// 인증 서버가 아닌 서버(게임 서버, 실시간 RPC 서버)에서 access 토큰 검증기(<see cref="IAccessTokenValidator"/>)만 등록합니다.
		/// </summary>
		public static IServiceCollection AddGeunedaAccessTokenValidation(this IServiceCollection services, Action<AccessTokenValidationOptions> configure)
		{
			ArgumentNullException.ThrowIfNull(services);
			ArgumentNullException.ThrowIfNull(configure);

			services.Configure(configure);
			services.TryAddSingleton(TimeProvider.System);
			services.TryAddSingleton<IAccessTokenValidator>(provider =>
			{
				var options = provider.GetRequiredService<IOptions<AccessTokenValidationOptions>>().Value;
				options.Validate();
				return new AccessTokenValidator(options.Issuer, options.Audience, options.SigningKeys, options.ClockSkew,
					provider.GetRequiredService<TimeProvider>());
			});
			return services;
		}
	}
}
