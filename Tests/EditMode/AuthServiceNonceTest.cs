using System;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using Geuneda.Core;
using Geuneda.Http;
using NUnit.Framework;
using Random = System.Random;

// ReSharper disable once CheckNamespace

namespace Geuneda.Account.Tests
{
	/// <summary>nonce 를 쓰는 제공자(애플·구글)의 로그인·연동: nonce 발급 요청, 제공자 전달, 요청 와이어 형식, 실패 처리, 연동 충돌 티켓.</summary>
	[TestFixture]
	public class AuthServiceNonceTest
	{
		private FakeAuthServer _server;
		private FakeSecureStore _store;
		private AppSession _session;
		private TestAuthProvider _provider;
		private ILogSink _originalSink;

		private sealed class DiscardSink : ILogSink
		{
			public void Write(in LogEntry entry)
			{
			}
		}

		[SetUp]
		public void SetUp()
		{
			_originalSink = Log.Sink;
			Log.Sink = new DiscardSink();
			_server = new FakeAuthServer();
			_store = new FakeSecureStore();
			_session = new AppSession(TimeSpan.FromMinutes(10), () => new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), new Random(1));
			_provider = new TestAuthProvider { UsesNonce = true };
		}

		[TearDown]
		public void TearDown()
		{
			Log.Sink = _originalSink;
		}

		private AuthService CreateService(TestAuthProvider provider = null)
		{
			var options = new AuthServiceOptions
			{
				AuthClient = new HttpClientBuilder().WithBaseAddress(FakeAuthServer.BaseAddress).WithTransport(_server).Build(),
				SecureStore = _store,
				Session = _session
			};
			options.Providers.Add(new GuestAuthProvider(_store));
			options.Providers.Add(provider ?? _provider);
			return new AuthService(options, () => 0);
		}

		private static T Run<T>(UniTask<T> task)
		{
			return FakeAuthServer.Run(task);
		}

		[Test]
		public void SignIn_GetsNonceFirst_PassesItToProvider_AndSendsItWithTheLogin()
		{
			var auth = CreateService();

			var result = Run(auth.SignInAsync(TestAuthProvider.Name));

			Assert.IsTrue(result.IsSuccess, result.ToString());
			Assert.AreEqual(1, _server.Count("nonce"));
			Assert.AreEqual("nonce", RouteOf(_server.Requests[0]));
			var nonce = _provider.Nonces.Single();
			Assert.IsNotNull(nonce);
			Assert.AreEqual(new[] { nonce }, _server.ReceivedNonces);
			StringAssert.Contains("\"nonce\":\"" + nonce + "\"", BodyOf(_server.Requests.Last()));
		}

		[Test]
		public void Guest_DoesNotRequestNonce()
		{
			var auth = CreateService();

			var result = Run(auth.SignInAsync(AccountPlatforms.Guest));

			Assert.IsTrue(result.IsSuccess, result.ToString());
			Assert.AreEqual(0, _server.Count("nonce"));
			Assert.AreEqual(new string[] { null }, _server.ReceivedNonces);
			StringAssert.DoesNotContain("nonce", BodyOf(_server.Requests.Single()));
		}

		[TestCase(false, ResultCodes.NetworkError)]
		[TestCase(true, HttpResultCodes.HttpError)]
		public void NonceFailure_FailsSignInAndLink_WithoutPlatformSignIn(bool serverError, int expected)
		{
			var auth = CreateService();
			Run(auth.SignInAsync(AccountPlatforms.Guest));
			var logins = _server.Count("login");
			_server.Intercept = request => RouteOf(request) != "nonce"
				? (Result<HttpResponse>?) null
				: serverError
					? FakeAuthServer.Error(request, 503, AccountResultCodes.RetryLater)
					: Result<HttpResponse>.Fail(ResultCodes.NetworkError, "offline");

			var signIn = Run(auth.SignInAsync(TestAuthProvider.Name));
			var link = Run(auth.LinkAsync(TestAuthProvider.Name));

			Assert.AreEqual(expected, signIn.Code);
			Assert.AreEqual(expected, link.Code);
			Assert.AreEqual(0, _provider.SignInCount);
			Assert.AreEqual(logins, _server.Count("login"));
			Assert.AreEqual(0, _server.Count("links"));
			Assert.IsTrue(auth.IsSignedIn);
		}

		[Test]
		public void NonceResponseWithoutNonce_IsInvalidResponse()
		{
			var auth = CreateService();
			_server.Intercept = request => RouteOf(request) == "nonce" ? FakeAuthServer.Respond(request, 200, new NonceResponse { ExpiresIn = 300 }) : (Result<HttpResponse>?) null;

			var result = Run(auth.SignInAsync(TestAuthProvider.Name));

			Assert.AreEqual(AccountResultCodes.InvalidResponse, result.Code);
			Assert.AreEqual(0, _provider.SignInCount);
		}

		[TestCase(400, AccountResultCodes.NonceMissing)]
		[TestCase(401, AccountResultCodes.NonceMismatch)]
		[TestCase(401, AccountResultCodes.InvalidNonce)]
		public void ServerNonceRejections_AreResultCodes(int status, int code)
		{
			var auth = CreateService();
			_server.Intercept = request => RouteOf(request) == "login" ? FakeAuthServer.Error(request, status, code) : (Result<HttpResponse>?) null;

			var result = Run(auth.SignInAsync(TestAuthProvider.Name));

			Assert.AreEqual(code, result.Code);
			Assert.IsFalse(auth.IsSignedIn);
			Assert.IsFalse(auth.HasSavedLogin);
		}

		[Test]
		public void LinkConflict_TransferSendsTheTicketInsteadOfTheIdToken_Once()
		{
			Run(CreateService(new TestAuthProvider { UsesNonce = true }).SignInAsync(TestAuthProvider.Name));
			var auth = CreateService();
			var mine = Run(auth.SignInAsync(AccountPlatforms.Guest)).Value.AccountId;

			var link = Run(auth.LinkAsync(TestAuthProvider.Name));
			var transfer = Run(auth.TransferAsync(link.Value.Credential));
			var transferBody = BodyOf(_server.Requests.Last());
			var again = Run(auth.TransferAsync(link.Value.Credential));

			Assert.IsTrue(link.Value.IsConflict);
			Assert.IsNotNull(link.Value.Credential.ConflictTicket);
			Assert.IsNull(link.Value.Credential.Nonce);
			StringAssert.Contains("\"conflictTicket\":\"" + link.Value.Credential.ConflictTicket + "\"", transferBody);
			StringAssert.DoesNotContain("credential", transferBody);
			StringAssert.DoesNotContain("\"nonce\"", transferBody);
			Assert.IsTrue(transfer.IsSuccess, transfer.ToString());
			Assert.AreEqual(mine, _server.Owners[TestAuthProvider.Name + "|alice"]);
			Assert.AreEqual(AccountResultCodes.InvalidConflictTicket, again.Code);
			Assert.AreEqual(1, _provider.SignInCount);
		}

		[Test]
		public void LinkConflict_SignInSendsTheTicket_Once()
		{
			var other = Run(CreateService(new TestAuthProvider { UsesNonce = true }).SignInAsync(TestAuthProvider.Name)).Value.AccountId;
			var auth = CreateService();
			Run(auth.SignInAsync(AccountPlatforms.Guest));

			var link = Run(auth.LinkAsync(TestAuthProvider.Name));
			var switched = Run(auth.SignInAsync(link.Value.Credential));
			var signInBody = BodyOf(_server.Requests.Last(request => RouteOf(request) == "login"));
			var again = Run(auth.SignInAsync(link.Value.Credential));

			Assert.IsTrue(switched.IsSuccess, switched.ToString());
			Assert.AreEqual(other, switched.Value.AccountId);
			StringAssert.Contains("\"conflictTicket\":\"" + link.Value.Credential.ConflictTicket + "\"", signInBody);
			StringAssert.DoesNotContain("credential", signInBody);
			Assert.AreEqual(AccountResultCodes.InvalidConflictTicket, again.Code);
			Assert.AreEqual(other, auth.AccountId);
		}

		[TestCase(null)]
		[TestCase("")]
		public void ConflictResponseWithoutTicket_IsInvalidResponse(string ticket)
		{
			var auth = CreateService();
			Run(auth.SignInAsync(AccountPlatforms.Guest));
			_server.Intercept = request => RouteOf(request) == "links"
				? FakeAuthServer.Respond(request, 409,
					new ErrorResponse { Code = AccountResultCodes.LoginConflict, Message = "conflict", ConflictAccountId = "other", ConflictTicket = ticket })
				: (Result<HttpResponse>?) null;

			var link = Run(auth.LinkAsync(TestAuthProvider.Name));

			Assert.AreEqual(AccountResultCodes.InvalidResponse, link.Code);
			Assert.AreEqual(1, _provider.SignOutCount);
		}

		[Test]
		public void DisposeOrCancel_WhileGettingNonce_EndsWithoutPlatformSignIn()
		{
			var auth = CreateService();
			_server.Gates["nonce"] = new UniTaskCompletionSource();
			var cancellation = new CancellationTokenSource();

			var cancelled = auth.SignInAsync(TestAuthProvider.Name, cancellation.Token).Preserve();
			cancellation.Cancel();
			_server.Gates["nonce"].TrySetResult();
			var cancelledResult = Run(cancelled);

			_server.Gates["nonce"] = new UniTaskCompletionSource();
			var disposed = auth.SignInAsync(TestAuthProvider.Name).Preserve();
			auth.Dispose();
			_server.Gates["nonce"].TrySetResult();

			Assert.AreEqual(ResultCodes.Cancelled, cancelledResult.Code);
			Assert.AreEqual(ResultCodes.Cancelled, Run(disposed).Code);
			Assert.AreEqual(0, _provider.SignInCount);
			Assert.AreEqual(0, _server.Count("login"));
		}

		[Test]
		public void Credential_CarriesNonceOrTicket_AndHidesValuesInToString()
		{
			var credential = new PlatformCredential("apple", "token", "nonce-1", null);
			var conflict = credential.WithConflictTicket("ticket-1");

			Assert.IsNull(new PlatformCredential("guest", "secret").Nonce);
			Assert.IsNull(new PlatformCredential("apple", "token", string.Empty, string.Empty).Nonce);
			Assert.AreEqual("nonce-1", credential.Nonce);
			Assert.IsNull(conflict.Nonce);
			Assert.AreEqual("ticket-1", conflict.ConflictTicket);
			Assert.AreEqual("token", conflict.Credential);
			StringAssert.DoesNotContain("nonce-1", credential.ToString());
			StringAssert.DoesNotContain("ticket-1", conflict.ToString());
		}

		private static string RouteOf(HttpRequest request)
		{
			return request.Uri.AbsolutePath.Substring("/auth/".Length);
		}

		private static string BodyOf(HttpRequest request)
		{
			return System.Text.Encoding.UTF8.GetString(request.Body);
		}
	}
}
