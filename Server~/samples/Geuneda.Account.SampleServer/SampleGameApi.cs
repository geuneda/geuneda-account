using System.Threading.Tasks;
using Geuneda.Account.Server;
using Microsoft.AspNetCore.Http;

namespace Geuneda.Account.SampleServer
{
	/// <summary>access 토큰으로 보호한 게임 API 예시입니다. 게임 서버도 같은 방식으로 <see cref="IAccessTokenValidator"/> 를 씁니다.</summary>
	public static class SampleGameApi
	{
		private const string BearerPrefix = "Bearer ";

		public static async Task<IResult> GetMeAsync(HttpContext context, IAccessTokenValidator validator)
		{
			var header = context.Request.Headers.Authorization.ToString();
			var token = header.StartsWith(BearerPrefix) ? header.Substring(BearerPrefix.Length) : null;
			var validation = await validator.ValidateAsync(token, context.RequestAborted);

			if (!validation.IsValid)
			{
				return Results.Json(new { code = AccountResultCodes.InvalidAccessToken, message = validation.FailureReason },
					statusCode: StatusCodes.Status401Unauthorized);
			}

			return Results.Json(new { accountId = validation.AccountId, sessionId = validation.SessionId });
		}
	}
}
