using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Protocols;

namespace Geuneda.Account.Server
{
	/// <summary><see cref="AccountServiceCollectionExtensions.AddGeunedaAccount"/> 다음에 검증기와 저장소를 더합니다.</summary>
	public sealed class AccountServerBuilder
	{
		public IServiceCollection Services { get; }

		internal AccountServerBuilder(IServiceCollection services)
		{
			Services = services;
		}

		/// <summary>플랫폼 검증기를 싱글턴으로 등록합니다. 같은 플랫폼 이름이 둘이면 MapGeunedaAccount 에서 예외입니다.</summary>
		public AccountServerBuilder AddPlatformVerifier<TVerifier>() where TVerifier : class, IPlatformVerifier
		{
			Services.TryAddEnumerable(ServiceDescriptor.Singleton<IPlatformVerifier, TVerifier>());
			return this;
		}

		/// <summary>게스트 로그인(<see cref="GuestPlatformVerifier"/>)을 켭니다.</summary>
		public AccountServerBuilder AddGuest()
		{
			return AddPlatformVerifier<GuestPlatformVerifier>();
		}

		/// <summary>
		/// Sign in with Apple(<see cref="AppleIdTokenVerifier"/>)을 켭니다. 애플 공개 키는 처음 검증할 때 받고 주기적으로 갱신합니다(<see cref="OpenIdConnectKeyCache"/>).
		/// </summary>
		/// <exception cref="ArgumentNullException"><paramref name="configure"/> 가 null 인 경우</exception>
		/// <exception cref="InvalidOperationException">대상을 하나도 넣지 않았거나 시계 오차가 음수인 경우</exception>
		public AccountServerBuilder AddApple(Action<AppleSignInOptions> configure)
		{
			ArgumentNullException.ThrowIfNull(configure);
			var options = new AppleSignInOptions();
			configure(options);

			if (options.Audiences.Count == 0)
			{
				throw new InvalidOperationException("AddApple needs at least one audience (the app bundle id)");
			}

			ThrowIfNegative(options.ClockSkew, nameof(AddApple));
			Services.TryAddEnumerable(ServiceDescriptor.Singleton<IPlatformVerifier, AppleIdTokenVerifier>(provider =>
			{
				var time = provider.GetRequiredService<TimeProvider>();
				return new AppleIdTokenVerifier(options, new OpenIdConnectKeyCache(AppleIdTokenVerifier.MetadataAddress, new HttpDocumentRetriever(), time), time);
			}));
			return this;
		}

		/// <summary>
		/// Google 로그인(<see cref="GoogleIdTokenVerifier"/>)을 켭니다. 구글 공개 키는 처음 검증할 때 받고 주기적으로 갱신합니다(<see cref="OpenIdConnectKeyCache"/>).
		/// </summary>
		/// <exception cref="ArgumentNullException"><paramref name="configure"/> 가 null 인 경우</exception>
		/// <exception cref="InvalidOperationException">클라이언트 ID 를 하나도 넣지 않았거나 시계 오차가 음수인 경우</exception>
		public AccountServerBuilder AddGoogle(Action<GoogleSignInOptions> configure)
		{
			ArgumentNullException.ThrowIfNull(configure);
			var options = new GoogleSignInOptions();
			configure(options);

			if (options.ClientIds.Count == 0)
			{
				throw new InvalidOperationException("AddGoogle needs at least one OAuth client id");
			}

			ThrowIfNegative(options.ClockSkew, nameof(AddGoogle));
			Services.TryAddEnumerable(ServiceDescriptor.Singleton<IPlatformVerifier, GoogleIdTokenVerifier>(provider =>
			{
				var time = provider.GetRequiredService<TimeProvider>();
				return new GoogleIdTokenVerifier(options, new OpenIdConnectKeyCache(GoogleIdTokenVerifier.MetadataAddress, new HttpDocumentRetriever(), time), time);
			}));
			return this;
		}

		/// <summary>메모리 저장소를 등록합니다. 샘플과 테스트용이고, 이미 등록한 저장소가 있으면 그것을 씁니다.</summary>
		public AccountServerBuilder AddInMemoryStores()
		{
			Services.TryAddSingleton<IAccountStore, InMemoryAccountStore>();
			Services.TryAddSingleton<IRefreshTokenStore, InMemoryRefreshTokenStore>();
			return this;
		}

		private static void ThrowIfNegative(TimeSpan clockSkew, string method)
		{
			if (clockSkew < TimeSpan.Zero)
			{
				throw new InvalidOperationException($"{method} needs a clock skew of zero or more");
			}
		}
	}
}
