using Microsoft.AspNetCore.Http;

namespace Geuneda.Account.Server
{
	/// <summary>오류 응답(<see cref="ErrorResponse"/>)입니다.</summary>
	internal static class AccountErrors
	{
		public static IResult BadRequest(string message)
		{
			return Error(StatusCodes.Status400BadRequest, AccountResultCodes.BadRequest, message);
		}

		public static IResult UnsupportedPlatform(string platform)
		{
			return Error(StatusCodes.Status400BadRequest, AccountResultCodes.UnsupportedPlatform, $"Platform '{platform}' is not supported");
		}

		public static IResult InvalidCredential(string reason)
		{
			return Error(StatusCodes.Status401Unauthorized, AccountResultCodes.InvalidCredential, reason);
		}

		public static IResult InvalidRefreshToken()
		{
			return Error(StatusCodes.Status401Unauthorized, AccountResultCodes.InvalidRefreshToken, "Refresh token is invalid, expired or revoked");
		}

		public static IResult RefreshTokenReused()
		{
			return Error(StatusCodes.Status401Unauthorized, AccountResultCodes.RefreshTokenReused, "Refresh token was already used. The login has been revoked");
		}

		public static IResult InvalidAccessToken(string reason)
		{
			return Error(StatusCodes.Status401Unauthorized, AccountResultCodes.InvalidAccessToken, reason);
		}

		public static IResult AccountNotFound()
		{
			return Error(StatusCodes.Status404NotFound, AccountResultCodes.AccountNotFound, "Account not found");
		}

		public static IResult LoginConflict(string otherAccountId)
		{
			return Results.Json(new ErrorResponse
			{
				Code = AccountResultCodes.LoginConflict,
				Message = "The platform login is linked to another account",
				ConflictAccountId = otherAccountId
			}, AccountJson.Options, statusCode: StatusCodes.Status409Conflict);
		}

		public static IResult PlatformAlreadyLinked(string platform)
		{
			return Error(StatusCodes.Status409Conflict, AccountResultCodes.PlatformAlreadyLinked, $"The account already has a '{platform}' login");
		}

		public static IResult RetryLater()
		{
			return Error(StatusCodes.Status503ServiceUnavailable, AccountResultCodes.RetryLater, "Concurrent refresh of the same login. Retry shortly");
		}

		public static IResult LastLogin()
		{
			return Error(StatusCodes.Status409Conflict, AccountResultCodes.LastLogin, "The last login cannot be unlinked");
		}

		private static IResult Error(int status, int code, string message)
		{
			return Results.Json(new ErrorResponse { Code = code, Message = message }, AccountJson.Options, statusCode: status);
		}
	}
}
