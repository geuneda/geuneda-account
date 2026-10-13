using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace Geuneda.Account.Server
{
	/// <summary><c>Authorization: Bearer</c> access 토큰을 검증하고 계정 ID 를 요청에 넣습니다.</summary>
	internal sealed class AccessTokenFilter : IEndpointFilter
	{
		private const string BearerPrefix = "Bearer ";
		private static readonly object AccountIdKey = new object();

		private readonly IAccessTokenValidator _validator;

		public AccessTokenFilter(IAccessTokenValidator validator)
		{
			_validator = validator;
		}

		public async ValueTask<object> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
		{
			var header = context.HttpContext.Request.Headers.Authorization.ToString();

			if (!header.StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase))
			{
				return AccountErrors.InvalidAccessToken("Missing bearer token");
			}

			var validation = await _validator.ValidateAsync(header.Substring(BearerPrefix.Length).Trim(), context.HttpContext.RequestAborted);

			if (!validation.IsValid)
			{
				return AccountErrors.InvalidAccessToken(validation.FailureReason);
			}

			context.HttpContext.Items[AccountIdKey] = validation.AccountId;
			return await next(context);
		}

		/// <summary>필터가 넣은 계정 ID</summary>
		public static string AccountId(HttpContext context)
		{
			return (string) context.Items[AccountIdKey];
		}
	}
}
