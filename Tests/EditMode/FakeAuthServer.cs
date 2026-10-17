using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using Geuneda.Core;
using Geuneda.Http;
using Newtonsoft.Json;
using NUnit.Framework;

// ReSharper disable once CheckNamespace

namespace Geuneda.Account.Tests
{
	/// <summary>
	/// 인증 서버 프로토콜을 흉내 내는 HTTP 전송입니다. 자격 <c>bad</c> 는 검증 실패이고, 그 밖의 자격은 그대로 고유 ID 로 씁니다.
	/// 요청에 nonce 가 있으면 발급한 nonce 인지 보고 소비합니다. 연동 충돌이면 1회용 티켓을 주고, 로그인·옮겨오기에서 티켓을 한 번 받습니다(옮겨오기는 받은 계정만).
	/// 게이트가 없으면 모든 요청이 동기적으로 끝납니다.
	/// </summary>
	internal sealed class FakeAuthServer : IHttpTransport
	{
		public const string BaseAddress = "https://auth.test/auth/";

		public readonly List<HttpRequest> Requests = new List<HttpRequest>();
		public readonly Dictionary<string, string> Owners = new Dictionary<string, string>();
		public readonly Dictionary<string, List<string>> Links = new Dictionary<string, List<string>>();
		public readonly Dictionary<string, string> RefreshTokens = new Dictionary<string, string>();
		public readonly Dictionary<string, string> AccessTokens = new Dictionary<string, string>();
		public readonly List<string> LoggedOut = new List<string>();
		public readonly List<string> SessionIds = new List<string>();
		public readonly HashSet<string> IssuedNonces = new HashSet<string>();
		/// <summary>로그인·연동·옮겨오기 요청에 실려 온 nonce(없으면 null)</summary>
		public readonly List<string> ReceivedNonces = new List<string>();
		/// <summary>쓰지 않은 충돌 티켓 → (로그인 수단 키, 티켓을 받은 계정)</summary>
		public readonly Dictionary<string, (string Key, string AccountId)> Tickets = new Dictionary<string, (string, string)>();

		public int AccessTokenExpiresIn = 3600;

		/// <summary>경로("login", "refresh", "links" 등)별로 두면 그 요청이 게이트가 끝날 때까지 기다립니다.</summary>
		public readonly Dictionary<string, UniTaskCompletionSource> Gates = new Dictionary<string, UniTaskCompletionSource>();

		/// <summary>있으면 응답하기 전에 기다립니다(요청마다 다른 게이트를 줄 때).</summary>
		public Func<HttpRequest, UniTask> BeforeRespond;
		public Func<HttpRequest, Result<HttpResponse>?> Intercept;

		private int _next;

		public int Count(string route)
		{
			return Requests.Count(request => Route(request) == route);
		}

		/// <summary>서버가 지금까지 준 access 토큰을 모두 거절하게 합니다(서명 키 교체 등).</summary>
		public void RejectIssuedAccessTokens()
		{
			AccessTokens.Clear();
		}

		public async UniTask<Result<HttpResponse>> SendAsync(HttpRequest request, CancellationToken cancellationToken)
		{
			Requests.Add(request);

			var intercepted = Intercept?.Invoke(request);

			if (intercepted.HasValue)
			{
				return intercepted.Value;
			}

			var route = Route(request);

			if (Gates.TryGetValue(route, out var gate))
			{
				await gate.Task;
			}

			if (BeforeRespond != null)
			{
				await BeforeRespond(request);
			}

			switch (route)
			{
				case "nonce":
					var nonce = "nonce-" + ++_next;
					IssuedNonces.Add(nonce);
					return Respond(request, 200, new NonceResponse { Nonce = nonce, ExpiresIn = 300 });
				case "login":
					return Login(request, Body<LoginRequest>(request));
				case "refresh":
					return Refresh(request, Body<RefreshRequest>(request));
				case "logout":
					var logout = Body<LogoutRequest>(request);
					LoggedOut.Add(logout.RefreshToken);
					RefreshTokens.Remove(logout.RefreshToken);
					return Respond(request, 204, null);
			}

			if (!request.Headers.TryGetValue("Authorization", out var header) || !AccessTokens.TryGetValue(header.Replace("Bearer ", string.Empty), out var accountId))
			{
				return Error(request, 401, AccountResultCodes.InvalidAccessToken);
			}

			if (!Links.TryGetValue(accountId, out var links))
			{
				return Error(request, 404, AccountResultCodes.AccountNotFound);
			}

			if (route == "links" && request.Method == HttpMethod.Get)
			{
				return LinksResponse(request, links);
			}

			if (route == "links" || route == "links/transfer")
			{
				return Link(request, accountId, links, Body<LinkRequest>(request), route == "links/transfer");
			}

			if (route.StartsWith("links/") && request.Method == HttpMethod.Delete)
			{
				var platform = Uri.UnescapeDataString(route.Substring("links/".Length));
				var key = links.FirstOrDefault(link => link.StartsWith(platform + "|"));

				if (key != null && links.Count == 1)
				{
					return Error(request, 409, AccountResultCodes.LastLogin);
				}

				if (key != null)
				{
					links.Remove(key);
					Owners.Remove(key);
				}

				return LinksResponse(request, links);
			}

			if (route == "account" && request.Method == HttpMethod.Delete)
			{
				foreach (var key in links)
				{
					Owners.Remove(key);
				}

				Links.Remove(accountId);

				foreach (var token in RefreshTokens.Where(pair => pair.Value == accountId).Select(pair => pair.Key).ToArray())
				{
					RefreshTokens.Remove(token);
				}

				return Respond(request, 204, null);
			}

			Assert.Fail($"Unexpected request {request}");
			return default;
		}

		private Result<HttpResponse> Login(HttpRequest request, LoginRequest body)
		{
			if (body.Credential == "bad")
			{
				return Error(request, 401, AccountResultCodes.InvalidCredential);
			}

			if (!ConsumeNonce(body.Nonce))
			{
				return Error(request, 401, AccountResultCodes.InvalidNonce);
			}

			var key = body.Platform + "|" + body.Credential;

			if (body.ConflictTicket != null)
			{
				if (body.Credential != null || !Tickets.Remove(body.ConflictTicket, out var ticket))
				{
					return Error(request, 401, AccountResultCodes.InvalidConflictTicket);
				}

				key = ticket.Key;
			}

			SessionIds.Add(body.SessionId);
			var created = !Owners.TryGetValue(key, out var accountId);

			if (created)
			{
				accountId = "acc-" + ++_next;
				Owners[key] = accountId;
				Links[accountId] = new List<string> { key };
			}

			return Respond(request, 200, Issue(accountId, created));
		}

		private Result<HttpResponse> Refresh(HttpRequest request, RefreshRequest body)
		{
			if (!RefreshTokens.TryGetValue(body.RefreshToken, out var accountId) || !Links.ContainsKey(accountId))
			{
				return Error(request, 401, AccountResultCodes.InvalidRefreshToken);
			}

			SessionIds.Add(body.SessionId);
			RefreshTokens.Remove(body.RefreshToken);
			return Respond(request, 200, Issue(accountId, false));
		}

		private Result<HttpResponse> Link(HttpRequest request, string accountId, List<string> links, LinkRequest body, bool transfer)
		{
			if (body.Credential == "bad")
			{
				return Error(request, 401, AccountResultCodes.InvalidCredential);
			}

			if (!ConsumeNonce(body.Nonce))
			{
				return Error(request, 401, AccountResultCodes.InvalidNonce);
			}

			var key = body.Platform + "|" + body.Credential;

			if (body.ConflictTicket != null)
			{
				if (!transfer || body.Credential != null || !Tickets.Remove(body.ConflictTicket, out var ticket) || ticket.AccountId != accountId)
				{
					return Error(request, 401, AccountResultCodes.InvalidConflictTicket);
				}

				key = ticket.Key;
			}

			if (Owners.TryGetValue(key, out var owner) && owner != accountId && !transfer)
			{
				var conflictTicket = "ticket-" + ++_next;
				Tickets[conflictTicket] = (key, accountId);
				return Respond(request, 409, new ErrorResponse
				{
					Code = AccountResultCodes.LoginConflict, Message = "conflict", ConflictAccountId = owner, ConflictTicket = conflictTicket
				});
			}

			if (owner != accountId && links.Any(link => link.StartsWith(body.Platform + "|")))
			{
				return Error(request, 409, AccountResultCodes.PlatformAlreadyLinked);
			}

			if (owner != null && owner != accountId)
			{
				Links[owner].Remove(key);
			}

			if (owner != accountId)
			{
				links.Add(key);
				Owners[key] = accountId;
			}

			return LinksResponse(request, links);
		}

		private bool ConsumeNonce(string nonce)
		{
			ReceivedNonces.Add(nonce);
			return nonce == null || IssuedNonces.Remove(nonce);
		}

		private TokenResponse Issue(string accountId, bool created)
		{
			var accessToken = "at-" + ++_next;
			var refreshToken = "rt-" + ++_next;
			AccessTokens[accessToken] = accountId;
			RefreshTokens[refreshToken] = accountId;
			return new TokenResponse
			{
				AccountId = accountId,
				AccessToken = accessToken,
				AccessTokenExpiresIn = AccessTokenExpiresIn,
				RefreshToken = refreshToken,
				RefreshTokenExpiresIn = 86400,
				IsNewAccount = created
			};
		}

		private static Result<HttpResponse> LinksResponse(HttpRequest request, List<string> links)
		{
			return Respond(request, 200, new LinksResponse
			{
				Links = links.Select(link => new LinkedPlatformDto { Platform = link.Split('|')[0], LinkedAt = 1000 }).ToList()
			});
		}

		private static string Route(HttpRequest request)
		{
			return request.Uri.AbsolutePath.Substring("/auth/".Length);
		}

		private static T Body<T>(HttpRequest request)
		{
			return JsonConvert.DeserializeObject<T>(Encoding.UTF8.GetString(request.Body), AccountApi.JsonSettings);
		}

		public static Result<HttpResponse> Error(HttpRequest request, int status, int code)
		{
			return Respond(request, status, new ErrorResponse { Code = code, Message = "error " + code });
		}

		public static Result<HttpResponse> Respond(HttpRequest request, int status, object body)
		{
			var json = body == null ? string.Empty : JsonConvert.SerializeObject(body, AccountApi.JsonSettings);
			return Result<HttpResponse>.Ok(new HttpResponse(request.Uri, status, Encoding.UTF8.GetBytes(json), null));
		}

		/// <summary>이미 끝난 UniTask 의 결과를 꺼냅니다. 끝나지 않았으면 테스트 실패입니다.</summary>
		public static T Run<T>(UniTask<T> task)
		{
			Assert.IsTrue(task.Status.IsCompleted(), "Expected the task to complete synchronously");
			return task.GetAwaiter().GetResult();
		}
	}
}
