using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Geuneda.Core;
using Geuneda.Http;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

// ReSharper disable once CheckNamespace

namespace Geuneda.Account
{
	/// <summary>
	/// 인증 서버 HTTP 호출입니다. 5xx 는 본문과 관계없이 <c>HttpResultCodes.HttpError</c>, 그 밖의 오류 응답 본문의 서버 코드(3001~3099)는 그대로 결과 코드,
	/// 해석할 수 없는 응답은 <see cref="AccountResultCodes.InvalidResponse"/> 입니다. 연결 실패·타임아웃·취소는 http 결과 그대로입니다.
	/// 모든 요청에 제한 시간을 붙입니다(요청이 끝나지 않아 재발급이 영원히 기다리지 않게).
	/// </summary>
	internal sealed class AccountApi
	{
		private const string Authorization = "Authorization";
		private const int ServerCodeEnd = AccountResultCodes.RangeStart + 100;

		/// <summary>서버(System.Text.Json Web 기본값)와 같은 camelCase 입니다.</summary>
		public static readonly JsonSerializerSettings JsonSettings = new JsonSerializerSettings
		{
			ContractResolver = new CamelCasePropertyNamesContractResolver(),
			NullValueHandling = NullValueHandling.Ignore
		};

		private readonly IHttpClient _http;
		private readonly TimeSpan _timeout;

		public AccountApi(IHttpClient http, TimeSpan timeout)
		{
			_http = http;
			_timeout = timeout;
		}

		public UniTask<AccountApiResult<NonceResponse>> IssueNonceAsync(CancellationToken cancellationToken)
		{
			return SendAsync<NonceResponse>(HttpRequest.Post(AccountRoutes.Nonce), true, cancellationToken);
		}

		public UniTask<AccountApiResult<TokenResponse>> LoginAsync(LoginRequest request, CancellationToken cancellationToken)
		{
			return SendAsync<TokenResponse>(HttpRequest.Post(AccountRoutes.Login).WithJsonBody(request, JsonSettings), true, cancellationToken);
		}

		public UniTask<AccountApiResult<TokenResponse>> RefreshAsync(RefreshRequest request, CancellationToken cancellationToken)
		{
			return SendAsync<TokenResponse>(HttpRequest.Post(AccountRoutes.Refresh).WithJsonBody(request, JsonSettings), true, cancellationToken);
		}

		public UniTask<AccountApiResult<object>> LogoutAsync(LogoutRequest request, CancellationToken cancellationToken)
		{
			return SendAsync<object>(HttpRequest.Post(AccountRoutes.Logout).WithJsonBody(request, JsonSettings), false, cancellationToken);
		}

		public UniTask<AccountApiResult<LinksResponse>> GetLinksAsync(string accessToken, CancellationToken cancellationToken)
		{
			return SendAsync<LinksResponse>(Authorized(HttpRequest.Get(AccountRoutes.Links), accessToken), true, cancellationToken);
		}

		public UniTask<AccountApiResult<LinksResponse>> LinkAsync(string accessToken, LinkRequest request, bool transfer, CancellationToken cancellationToken)
		{
			var route = transfer ? AccountRoutes.LinksTransfer : AccountRoutes.Links;
			return SendAsync<LinksResponse>(Authorized(HttpRequest.Post(route).WithJsonBody(request, JsonSettings), accessToken), true, cancellationToken);
		}

		public UniTask<AccountApiResult<LinksResponse>> UnlinkAsync(string accessToken, string platform, CancellationToken cancellationToken)
		{
			return SendAsync<LinksResponse>(Authorized(HttpRequest.Delete(AccountRoutes.Link(platform)), accessToken), true, cancellationToken);
		}

		public UniTask<AccountApiResult<object>> DeleteAccountAsync(string accessToken, CancellationToken cancellationToken)
		{
			return SendAsync<object>(Authorized(HttpRequest.Delete(AccountRoutes.Account), accessToken), false, cancellationToken);
		}

		private static HttpRequest Authorized(HttpRequest request, string accessToken)
		{
			return request.WithHeader(Authorization, "Bearer " + accessToken);
		}

		private async UniTask<AccountApiResult<T>> SendAsync<T>(HttpRequest request, bool readBody, CancellationToken cancellationToken) where T : class
		{
			var sent = await _http.SendAsync(request.WithTimeout(_timeout), cancellationToken);

			if (!sent.TryGetValue(out var response))
			{
				return AccountApiResult<T>.Fail(sent.Code, sent.Message);
			}

			if (response.IsSuccessStatusCode)
			{
				if (!readBody)
				{
					return AccountApiResult<T>.Ok(null);
				}

				var body = response.ReadJson<T>(JsonSettings);
				return body.TryGetValue(out var value) && value != null
					? AccountApiResult<T>.Ok(value)
					: AccountApiResult<T>.Fail(AccountResultCodes.InvalidResponse, $"Invalid response body from {request}");
			}

			if (response.StatusCode >= 500)
			{
				// 서버 장애는 본문에 계정 코드가 있어도 일시적 실패로 본다(저장된 로그인을 지우지 않게)
				return AccountApiResult<T>.Fail(HttpResultCodes.HttpError, $"HTTP {response.StatusCode} from {request}");
			}

			var error = response.ReadJson<ErrorResponse>(JsonSettings);

			if (error.TryGetValue(out var errorBody) && errorBody != null && errorBody.Code > AccountResultCodes.RangeStart && errorBody.Code < ServerCodeEnd)
			{
				return AccountApiResult<T>.Fail(errorBody.Code, errorBody.Message, errorBody);
			}

			return AccountApiResult<T>.Fail(AccountResultCodes.InvalidResponse, $"Unexpected HTTP {response.StatusCode} from {request}");
		}
	}
}
