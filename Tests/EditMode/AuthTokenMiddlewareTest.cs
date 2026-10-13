using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using Geuneda.Core;
using Geuneda.Http;
using NUnit.Framework;
using UnityEngine.TestTools;

// ReSharper disable once CheckNamespace

namespace Geuneda.Account.Tests
{
	[TestFixture]
	public class AuthTokenMiddlewareTest
	{
		private sealed class FakeAuth : IAuthService
		{
			public readonly List<string> Invalidated = new List<string>();
			public Queue<Result<string>> Tokens = new Queue<Result<string>>();

			public string Account = "acc";

			public bool IsSignedIn => true;
			public string AccountId => Account;
			public bool HasSavedLogin => true;

			public event Action<SignInResult> SignedIn
			{
				add { }
				remove { }
			}

			public event Action<SignOutReason> SignedOut
			{
				add { }
				remove { }
			}

			public UniTask<Result<string>> GetAccessTokenAsync(CancellationToken cancellationToken = default)
			{
				return UniTask.FromResult(Tokens.Count > 1 ? Tokens.Dequeue() : Tokens.Peek());
			}

			public void InvalidateAccessToken(string accessToken)
			{
				Invalidated.Add(accessToken);
			}

			public UniTask<Result<SignInResult>> SignInAsync(string platform, CancellationToken cancellationToken = default) => throw new NotSupportedException();
			public UniTask<Result<SignInResult>> SignInAsync(PlatformCredential credential, CancellationToken cancellationToken = default) => throw new NotSupportedException();
			public UniTask<Result<SignInResult>> SignInSilentlyAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
			public UniTask<Result> SignOutAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
			public UniTask<Result<IReadOnlyList<LinkedPlatform>>> GetLinksAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
			public UniTask<Result<LinkResult>> LinkAsync(string platform, CancellationToken cancellationToken = default) => throw new NotSupportedException();
			public UniTask<Result<IReadOnlyList<LinkedPlatform>>> TransferAsync(PlatformCredential credential, CancellationToken cancellationToken = default) => throw new NotSupportedException();
			public UniTask<Result<IReadOnlyList<LinkedPlatform>>> UnlinkAsync(string platform, CancellationToken cancellationToken = default) => throw new NotSupportedException();
			public UniTask<Result> DeleteAccountAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
		}

		private sealed class RecordingTransport : IHttpTransport
		{
			public readonly List<HttpRequest> Requests = new List<HttpRequest>();
			public Func<HttpRequest, int> Status = request => 200;
			public Func<HttpRequest, Result<HttpResponse>?> Fail;

			public UniTask<Result<HttpResponse>> SendAsync(HttpRequest request, CancellationToken cancellationToken)
			{
				Requests.Add(request);
				var failed = Fail?.Invoke(request);
				return UniTask.FromResult(failed ?? Result<HttpResponse>.Ok(new HttpResponse(request.Uri, Status(request), Array.Empty<byte>(), null)));
			}
		}

		private static (HttpClient Client, RecordingTransport Transport) Create(FakeAuth auth)
		{
			var transport = new RecordingTransport();
			var client = new HttpClientBuilder()
				.WithBaseAddress("https://game.test/")
				.AddMiddleware(new AuthTokenMiddleware(auth))
				.WithTransport(transport)
				.Build();
			return (client, transport);
		}

		[Test]
		public void AddsBearerToken()
		{
			var auth = new FakeAuth();
			auth.Tokens.Enqueue(Result<string>.Ok("token-1"));
			var (client, transport) = Create(auth);

			var result = FakeAuthServer.Run(client.GetAsync("me"));

			Assert.AreEqual(200, result.Value.StatusCode);
			Assert.AreEqual("Bearer token-1", transport.Requests.Single().Headers["Authorization"]);
		}

		[Test]
		public void Unauthorized_RefreshesAndRetriesOnce()
		{
			var auth = new FakeAuth();
			auth.Tokens.Enqueue(Result<string>.Ok("old"));
			auth.Tokens.Enqueue(Result<string>.Ok("new"));
			var (client, transport) = Create(auth);
			transport.Status = request => 401;

			var result = FakeAuthServer.Run(client.GetAsync("me"));

			Assert.AreEqual(401, result.Value.StatusCode);
			Assert.AreEqual(new[] { "Bearer old", "Bearer new" }, transport.Requests.Select(request => request.Headers["Authorization"]));
			Assert.AreEqual(new[] { "old" }, auth.Invalidated);
		}

		[Test]
		public void Unauthorized_SameTokenAfterRefresh_DoesNotRetry()
		{
			var auth = new FakeAuth();
			auth.Tokens.Enqueue(Result<string>.Ok("same"));
			var (client, transport) = Create(auth);
			transport.Status = request => 401;

			FakeAuthServer.Run(client.GetAsync("me"));

			Assert.AreEqual(1, transport.Requests.Count);
		}

		[Test]
		public void Unauthorized_RenewFailure_ReturnsThatCode()
		{
			var auth = new FakeAuth();
			auth.Tokens.Enqueue(Result<string>.Ok("old"));
			auth.Tokens.Enqueue(Result<string>.Fail(ResultCodes.Cancelled));
			var (client, transport) = Create(auth);
			transport.Status = request => 401;

			var result = FakeAuthServer.Run(client.GetAsync("me"));

			Assert.AreEqual(ResultCodes.Cancelled, result.Code);
			Assert.AreEqual(1, transport.Requests.Count);
		}

		[Test]
		public void Unauthorized_AfterAccountSwitch_DoesNotResendWithNewAccount()
		{
			var auth = new FakeAuth();
			auth.Tokens.Enqueue(Result<string>.Ok("token-a"));
			auth.Tokens.Enqueue(Result<string>.Ok("token-b"));
			var (client, transport) = Create(auth);
			transport.Status = request =>
			{
				auth.Account = "other";
				return 401;
			};

			var result = FakeAuthServer.Run(client.GetAsync("me"));

			Assert.AreEqual(401, result.Value.StatusCode);
			Assert.AreEqual(1, transport.Requests.Count);
		}

		[UnityTest]
		public IEnumerator RetryInsideAuth_ResendsWithOriginalToken_AfterAccountSwitch() => UniTask.ToCoroutine(async () =>
		{
			var auth = new FakeAuth();
			auth.Tokens.Enqueue(Result<string>.Ok("token-a"));
			auth.Tokens.Enqueue(Result<string>.Ok("token-b"));
			var transport = new RecordingTransport();
			transport.Fail = request =>
			{
				if (transport.Requests.Count > 1)
				{
					return null;
				}

				// 첫 시도가 실패한 뒤 재시도를 기다리는 동안 다른 계정으로 로그인했다
				auth.Account = "other";
				return Result<HttpResponse>.Fail(ResultCodes.NetworkError);
			};
			var client = new HttpClientBuilder()
				.WithBaseAddress("https://game.test/")
				.AddMiddleware(new AuthTokenMiddleware(auth), -100)
				.AddMiddleware(new RetryMiddleware(1, TimeSpan.Zero, TimeSpan.Zero), 0)
				.WithTransport(transport)
				.Build();

			var result = await client.SendAsync(HttpRequest.Put("settings"));

			Assert.IsTrue(result.IsSuccess, result.ToString());
			Assert.AreEqual(2, transport.Requests.Count);
			Assert.IsTrue(transport.Requests.All(request => request.Headers["Authorization"] == "Bearer token-a"));
		});

		[Test]
		public void NotSignedIn_DoesNotSend()
		{
			var auth = new FakeAuth();
			auth.Tokens.Enqueue(Result<string>.Fail(AccountResultCodes.NotSignedIn));
			var (client, transport) = Create(auth);

			var result = FakeAuthServer.Run(client.GetAsync("me"));

			Assert.AreEqual(AccountResultCodes.NotSignedIn, result.Code);
			Assert.IsEmpty(transport.Requests);
		}
	}
}
