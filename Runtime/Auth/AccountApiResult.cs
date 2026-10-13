using Geuneda.Core;

// ReSharper disable once CheckNamespace

namespace Geuneda.Account
{
	/// <summary>인증 서버 호출 결과와, 서버가 보낸 오류 본문(있으면)입니다.</summary>
	internal readonly struct AccountApiResult<T>
	{
		public Result<T> Result { get; }

		/// <summary>서버 오류 응답 본문. 응답이 없었거나 해석하지 못했으면 null 입니다.</summary>
		public ErrorResponse Error { get; }

		private AccountApiResult(Result<T> result, ErrorResponse error)
		{
			Result = result;
			Error = error;
		}

		public static AccountApiResult<T> Ok(T value)
		{
			return new AccountApiResult<T>(Result<T>.Ok(value), null);
		}

		public static AccountApiResult<T> Fail(int code, string message, ErrorResponse error = null)
		{
			return new AccountApiResult<T>(Result<T>.Fail(code, message), error);
		}
	}
}
