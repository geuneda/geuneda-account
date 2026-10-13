using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

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

		/// <summary>메모리 저장소를 등록합니다. 샘플과 테스트용이고, 이미 등록한 저장소가 있으면 그것을 씁니다.</summary>
		public AccountServerBuilder AddInMemoryStores()
		{
			Services.TryAddSingleton<IAccountStore, InMemoryAccountStore>();
			Services.TryAddSingleton<IRefreshTokenStore, InMemoryRefreshTokenStore>();
			return this;
		}
	}
}
