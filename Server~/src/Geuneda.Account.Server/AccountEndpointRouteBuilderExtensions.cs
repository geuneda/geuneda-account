using System;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Geuneda.Account.Server
{
	public static class AccountEndpointRouteBuilderExtensions
	{
		/// <summary>
		/// 인증 엔드포인트를 <paramref name="prefix"/> 아래에 연결합니다(클라이언트의 기본 주소는 이 접두사까지).
		/// 설정, 저장소 등록, 검증기 중복을 여기서 검사해 잘못되면 시작할 때 예외를 던집니다.
		/// </summary>
		/// <remarks>로그인·nonce 요청 횟수 제한은 하지 않습니다(nonce 는 인증 없이 발급). 운영에서는 ASP.NET Core 의 rate limiter 를 이 그룹에 붙이세요.</remarks>
		/// <exception cref="InvalidOperationException">AddGeunedaAccount 를 부르지 않았거나 설정·저장소·검증기 등록이 잘못된 경우</exception>
		public static RouteGroupBuilder MapGeunedaAccount(this IEndpointRouteBuilder endpoints, string prefix = "/auth")
		{
			ArgumentNullException.ThrowIfNull(endpoints);

			var services = endpoints.ServiceProvider;

			if (services.GetService<AccountTokenIssuer>() == null)
			{
				throw new InvalidOperationException("Call services.AddGeunedaAccount(...) before MapGeunedaAccount");
			}

			services.GetRequiredService<IOptions<AccountServerOptions>>().Value.Validate();

			var registrations = services.GetRequiredService<IServiceProviderIsService>();

			if (!registrations.IsService(typeof(IAccountStore)) || !registrations.IsService(typeof(IRefreshTokenStore)) ||
				!registrations.IsService(typeof(IOneTimeValueStore)))
			{
				throw new InvalidOperationException("Register IAccountStore, IRefreshTokenStore and IOneTimeValueStore (AddInMemoryStores() for samples and tests)");
			}

			// 검증기 이름 검사를 시작할 때 한다
			services.GetRequiredService<PlatformVerifierRegistry>();

			// 처리기는 요청 DI 범위에서 받는다(저장소가 scoped 일 수 있음)
			var group = endpoints.MapGroup(prefix);
			group.MapPost("/" + AccountRoutes.Nonce, (HttpContext context, NonceEndpoints nonces) => nonces.IssueAsync(context));
			group.MapPost("/" + AccountRoutes.Login, (HttpContext context, TokenEndpoints tokens) => tokens.LoginAsync(context));
			group.MapPost("/" + AccountRoutes.Refresh, (HttpContext context, TokenEndpoints tokens) => tokens.RefreshAsync(context));
			group.MapPost("/" + AccountRoutes.Logout, (HttpContext context, TokenEndpoints tokens) => tokens.LogoutAsync(context));

			var authorized = group.MapGroup(string.Empty).AddEndpointFilter<AccessTokenFilter>();
			authorized.MapGet("/" + AccountRoutes.Links, (HttpContext context, LinkEndpoints links) => links.GetLinksAsync(context));
			authorized.MapPost("/" + AccountRoutes.Links, (HttpContext context, LinkEndpoints links) => links.LinkAsync(context));
			authorized.MapPost("/" + AccountRoutes.LinksTransfer, (HttpContext context, LinkEndpoints links) => links.TransferAsync(context));
			authorized.MapDelete("/" + AccountRoutes.Links + "/{platform}",
				(HttpContext context, string platform, LinkEndpoints links) => links.UnlinkAsync(context, platform));
			authorized.MapDelete("/" + AccountRoutes.Account, (HttpContext context, LinkEndpoints links) => links.DeleteAccountAsync(context));
			return group;
		}
	}
}
