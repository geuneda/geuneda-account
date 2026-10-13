using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Geuneda.Core;
using Geuneda.Http;

// ReSharper disable once CheckNamespace

namespace Geuneda.Account
{
	/// <summary>
	/// 게임 서버 HTTP 요청에 <c>Authorization: Bearer &lt;access 토큰&gt;</c> 를 넣습니다. 응답이 401 이면 토큰을 재발급해 한 번만 다시 보냅니다.
	/// </summary>
	/// <remarks>
	/// 게임 서버 클라이언트에 넣습니다(인증 서버 클라이언트에는 넣지 않음). 재시도 미들웨어보다 바깥(order 가 작음)에 둡니다.
	/// 그러면 재시도는 처음 받은 토큰으로 같은 계정의 요청을 다시 보내고, 401 이면 이 미들웨어가 재발급해 한 번만 다시 보냅니다.
	/// 안쪽에 두면 재시도 사이에 다른 계정으로 로그인했을 때 이전 요청이 새 계정의 토큰으로 나갈 수 있습니다.
	/// 로그인 상태가 아니면 요청을 보내지 않고 <see cref="AccountResultCodes.NotSignedIn"/> 입니다. 401 뒤 재발급이 실패하면 그 결과 코드이고,
	/// 그사이 다른 계정으로 바뀌었으면 다시 보내지 않고 401 응답을 그대로 돌려줍니다(이전 계정의 요청이 새 계정에 적용되지 않게).
	/// <code>
	/// var gameClient = new HttpClientBuilder()
	///     .WithBaseAddress("https://game.example.com/")
	///     .AddMiddleware(new AuthTokenMiddleware(auth), order: -100)
	///     .AddMiddleware(new RetryMiddleware(), order: 0)
	///     .Build();
	/// </code>
	/// </remarks>
	public sealed class AuthTokenMiddleware : IHttpMiddleware
	{
		private const string Authorization = "Authorization";
		private const int Unauthorized = 401;

		private readonly IAuthService _auth;

		/// <exception cref="ArgumentNullException"><paramref name="auth"/> 가 null 인 경우</exception>
		public AuthTokenMiddleware(IAuthService auth)
		{
			_auth = auth ?? throw new ArgumentNullException(nameof(auth));
		}

		public async UniTask<Result<HttpResponse>> SendAsync(HttpRequest request, HttpHandler next, CancellationToken cancellationToken)
		{
			var token = await _auth.GetAccessTokenAsync(cancellationToken);

			if (!token.TryGetValue(out var accessToken))
			{
				return Result<HttpResponse>.Fail(token.Code, token.Message);
			}

			var accountId = _auth.AccountId;
			var result = await next(request.WithHeader(Authorization, "Bearer " + accessToken), cancellationToken);

			if (!result.TryGetValue(out var response) || response.StatusCode != Unauthorized)
			{
				return result;
			}

			// 만료 직전이나 서명 키 교체로 거절된 토큰. 재발급해 한 번만 다시 보낸다(다른 요청이 이미 재발급했으면 그 토큰을 씀)
			_auth.InvalidateAccessToken(accessToken);
			Result<string> renewed;

			try
			{
				renewed = await _auth.GetAccessTokenAsync(cancellationToken);
			}
			catch (ObjectDisposedException)
			{
				// 요청을 보낸 뒤 인증 서비스가 Dispose 됐다(처음부터 Dispose 된 서비스는 첫 토큰에서 예외로 드러남)
				return Result<HttpResponse>.Fail(ResultCodes.Cancelled, "The auth service was disposed during the request");
			}

			if (!renewed.TryGetValue(out var renewedToken))
			{
				return Result<HttpResponse>.Fail(renewed.Code, renewed.Message);
			}

			if (renewedToken == accessToken || _auth.AccountId != accountId)
			{
				return result;
			}

			return await next(request.WithHeader(Authorization, "Bearer " + renewedToken), cancellationToken);
		}
	}
}
