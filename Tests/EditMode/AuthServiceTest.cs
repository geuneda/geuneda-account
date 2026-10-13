using System;
using System.Collections.Generic;
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
	[TestFixture]
	public class AuthServiceTest
	{
		private FakeAuthServer _server;
		private FakeSecureStore _store;
		private AppSession _session;
		private TestAuthProvider _provider;
		private double _now;
		private DateTime _utcNow;
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
			_utcNow = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
			_session = new AppSession(TimeSpan.FromMinutes(10), () => _utcNow, new Random(1));
			_provider = new TestAuthProvider();
			_now = 0;
		}

		[TearDown]
		public void TearDown()
		{
			Log.Sink = _originalSink;
		}

		private AuthService CreateService()
		{
			var options = new AuthServiceOptions
			{
				AuthClient = new HttpClientBuilder().WithBaseAddress(FakeAuthServer.BaseAddress).WithTransport(_server).Build(),
				SecureStore = _store,
				Session = _session
			};
			options.Providers.Add(new GuestAuthProvider(_store));
			options.Providers.Add(_provider);
			return new AuthService(options, () => _now);
		}

		private static T Run<T>(UniTask<T> task)
		{
			return FakeAuthServer.Run(task);
		}

		[Test]
		public void SignIn_Guest_CreatesSecret_SavesLogin_StartsSessionWithReservedId()
		{
			var auth = CreateService();
			var reserved = _session.NextSessionId;
			SignInResult signedIn = null;
			auth.SignedIn += result => signedIn = result;

			var result = Run(auth.SignInAsync(AccountPlatforms.Guest));

			Assert.IsTrue(result.IsSuccess, result.ToString());
			Assert.IsTrue(result.Value.IsNewAccount);
			Assert.IsTrue(auth.IsSignedIn);
			Assert.AreEqual(result.Value.AccountId, auth.AccountId);
			Assert.AreSame(result.Value, signedIn);
			Assert.AreEqual(reserved, _server.SessionIds.Single());
			Assert.AreEqual(reserved, _session.SessionId);
			Assert.IsTrue(_store.Values.ContainsKey(AccountStorageKeys.GuestSecret));
			StringAssert.Contains(result.Value.AccountId, _store.Values[AccountStorageKeys.Login]);
		}

		[Test]
		public void SignIn_SameGuestAfterRestart_ReturnsSameAccount()
		{
			var first = Run(CreateService().SignInAsync(AccountPlatforms.Guest)).Value;

			var second = Run(CreateService().SignInAsync(AccountPlatforms.Guest)).Value;

			Assert.AreEqual(first.AccountId, second.AccountId);
			Assert.IsFalse(second.IsNewAccount);
		}

		[Test]
		public void SignIn_InvalidCredential_KeepsSignedOut()
		{
			_provider.Credential = "bad";
			var auth = CreateService();

			var result = Run(auth.SignInAsync(TestAuthProvider.Name));

			Assert.AreEqual(AccountResultCodes.InvalidCredential, result.Code);
			Assert.IsFalse(auth.IsSignedIn);
			Assert.IsFalse(auth.HasSavedLogin);
			Assert.IsFalse(_session.IsActive);
		}

		[Test]
		public void SignIn_UnknownPlatform_Throws()
		{
			var auth = CreateService();

			Assert.Throws<ArgumentException>(() => auth.SignInAsync("facebook"));
		}

		[Test]
		public void DuplicateProviders_Throw()
		{
			var options = new AuthServiceOptions
			{
				AuthClient = new HttpClientBuilder().WithBaseAddress(FakeAuthServer.BaseAddress).WithTransport(_server).Build(),
				SecureStore = _store,
				Session = _session
			};
			options.Providers.Add(new TestAuthProvider());
			options.Providers.Add(new TestAuthProvider());

			Assert.Throws<ArgumentException>(() => new AuthService(options, () => 0));
		}

		[Test]
		public void SignInSilently_WithoutSavedLogin_IsNotSignedIn()
		{
			var result = Run(CreateService().SignInSilentlyAsync());

			Assert.AreEqual(AccountResultCodes.NotSignedIn, result.Code);
		}

		[Test]
		public void SignInSilently_AfterRestart_RefreshesAndStartsSession()
		{
			var accountId = Run(CreateService().SignInAsync(AccountPlatforms.Guest)).Value.AccountId;
			_session.End();
			var restarted = CreateService();
			var reserved = _session.NextSessionId;

			var result = Run(restarted.SignInSilentlyAsync());

			Assert.IsTrue(result.IsSuccess, result.ToString());
			Assert.AreEqual(accountId, result.Value.AccountId);
			Assert.IsFalse(result.Value.IsNewAccount);
			Assert.IsTrue(restarted.IsSignedIn);
			Assert.AreEqual(1, _server.Count("refresh"));
			Assert.AreEqual(reserved, _server.SessionIds.Last());
			Assert.AreEqual(reserved, _session.SessionId);
		}

		[Test]
		public void SignInSilently_Rejected_RemovesSavedLogin_WithoutSignedOutEvent()
		{
			Run(CreateService().SignInAsync(AccountPlatforms.Guest));
			_server.RefreshTokens.Clear();
			var restarted = CreateService();
			var signedOut = 0;
			restarted.SignedOut += _ => signedOut++;

			var result = Run(restarted.SignInSilentlyAsync());

			Assert.AreEqual(AccountResultCodes.InvalidRefreshToken, result.Code);
			Assert.IsFalse(restarted.HasSavedLogin);
			Assert.IsFalse(_store.Values.ContainsKey(AccountStorageKeys.Login));
			Assert.AreEqual(0, signedOut);
		}

		[Test]
		public void SignInSilently_NetworkError_KeepsSavedLogin()
		{
			Run(CreateService().SignInAsync(AccountPlatforms.Guest));
			_server.Intercept = request => Result<HttpResponse>.Fail(ResultCodes.NetworkError);
			var restarted = CreateService();

			var result = Run(restarted.SignInSilentlyAsync());

			Assert.AreEqual(ResultCodes.NetworkError, result.Code);
			Assert.IsTrue(restarted.HasSavedLogin);
			Assert.IsTrue(_store.Values.ContainsKey(AccountStorageKeys.Login));
		}

		[Test]
		public void SavedLogin_Malformed_IsRemoved()
		{
			_store.Values[AccountStorageKeys.Login] = "{not json";

			var auth = CreateService();

			Assert.IsFalse(auth.HasSavedLogin);
			Assert.IsFalse(_store.Values.ContainsKey(AccountStorageKeys.Login));
		}

		[Test]
		public void AccessToken_IsReusedUntilMargin_ThenRefreshed()
		{
			var auth = CreateService();
			Run(auth.SignInAsync(AccountPlatforms.Guest));
			var first = Run(auth.GetAccessTokenAsync()).Value;

			_now = 3600 - 61;
			var reused = Run(auth.GetAccessTokenAsync()).Value;
			_now = 3600 - 59;
			var renewed = Run(auth.GetAccessTokenAsync()).Value;

			Assert.AreEqual(first, reused);
			Assert.AreNotEqual(first, renewed);
			Assert.AreEqual(1, _server.Count("refresh"));
		}

		[Test]
		public void AccessToken_ShortLifetime_RefreshesAtHalfLife()
		{
			_server.AccessTokenExpiresIn = 60;
			var auth = CreateService();
			Run(auth.SignInAsync(AccountPlatforms.Guest));

			_now = 29;
			Run(auth.GetAccessTokenAsync());
			_now = 31;
			Run(auth.GetAccessTokenAsync());

			Assert.AreEqual(1, _server.Count("refresh"));
		}

		[Test]
		public void ConcurrentGetAccessToken_RefreshesOnce()
		{
			var auth = CreateService();
			Run(auth.SignInAsync(AccountPlatforms.Guest));
			_now = 4000;
			_server.Gates["refresh"] = new UniTaskCompletionSource();

			var calls = Enumerable.Range(0, 3).Select(_ => auth.GetAccessTokenAsync().Preserve()).ToArray();
			_server.Gates["refresh"].TrySetResult();
			var tokens = calls.Select(call => Run(call).Value).ToArray();

			Assert.AreEqual(1, _server.Count("refresh"));
			Assert.AreEqual(1, tokens.Distinct().Count());
		}

		[Test]
		public void CancelledWaiter_DoesNotCancelSharedRefresh()
		{
			var auth = CreateService();
			Run(auth.SignInAsync(AccountPlatforms.Guest));
			_now = 4000;
			_server.Gates["refresh"] = new UniTaskCompletionSource();
			var cancellation = new CancellationTokenSource();

			var cancelled = auth.GetAccessTokenAsync(cancellation.Token).Preserve();
			var waiting = auth.GetAccessTokenAsync().Preserve();
			cancellation.Cancel();
			_server.Gates["refresh"].TrySetResult();

			Assert.AreEqual(ResultCodes.Cancelled, Run(cancelled).Code);
			Assert.IsTrue(Run(waiting).IsSuccess);
			Assert.AreEqual(1, _server.Count("refresh"));
		}

		[Test]
		public void RefreshRejected_WhileSignedIn_SignsOutWithLoginExpired()
		{
			var auth = CreateService();
			Run(auth.SignInAsync(AccountPlatforms.Guest));
			_server.RefreshTokens.Clear();
			_now = 4000;
			var reasons = new List<SignOutReason>();
			auth.SignedOut += reasons.Add;

			var result = Run(auth.GetAccessTokenAsync());

			Assert.AreEqual(AccountResultCodes.InvalidRefreshToken, result.Code);
			Assert.AreEqual(new[] { SignOutReason.LoginExpired }, reasons);
			Assert.IsFalse(auth.IsSignedIn);
			Assert.IsFalse(_session.IsActive);
			Assert.IsFalse(_store.Values.ContainsKey(AccountStorageKeys.Login));
		}

		[Test]
		public void SignOut_RemovesLoginEvenIfServerFails_AndSignsOutProviders()
		{
			var auth = CreateService();
			Run(auth.SignInAsync(TestAuthProvider.Name));
			_server.Intercept = request => Result<HttpResponse>.Fail(ResultCodes.NetworkError);
			var reasons = new List<SignOutReason>();
			auth.SignedOut += reasons.Add;

			var result = Run(auth.SignOutAsync());

			Assert.IsTrue(result.IsSuccess);
			Assert.IsFalse(auth.IsSignedIn);
			Assert.IsFalse(auth.HasSavedLogin);
			Assert.IsFalse(_session.IsActive);
			Assert.AreEqual(new[] { SignOutReason.SignedOut }, reasons);
			Assert.AreEqual(1, _provider.SignOutCount);
			Assert.AreEqual(AccountResultCodes.NotSignedIn, Run(auth.GetAccessTokenAsync()).Code);
		}

		[Test]
		public void SignOut_DuringRefresh_DoesNotBringLoginBack()
		{
			var auth = CreateService();
			Run(auth.SignInAsync(AccountPlatforms.Guest));
			_now = 4000;
			_server.Gates["refresh"] = new UniTaskCompletionSource();
			var refreshing = auth.GetAccessTokenAsync().Preserve();

			Run(auth.SignOutAsync());
			_server.Gates["refresh"].TrySetResult();

			Assert.IsFalse(Run(refreshing).IsSuccess);
			Assert.IsFalse(auth.IsSignedIn);
			Assert.IsFalse(auth.HasSavedLogin);
			Assert.IsFalse(_store.Values.ContainsKey(AccountStorageKeys.Login));
		}

		[Test]
		public void OverlappingOperations_AreRejectedWithInProgress()
		{
			var auth = CreateService();
			_provider.Gate = new UniTaskCompletionSource<Result<PlatformCredential>>();

			var first = auth.SignInAsync(TestAuthProvider.Name).Preserve();
			var second = Run(auth.SignInAsync(AccountPlatforms.Guest));
			var signOut = Run(auth.SignOutAsync());
			_provider.Gate.TrySetResult(Result<PlatformCredential>.Ok(new PlatformCredential(TestAuthProvider.Name, "alice")));

			Assert.AreEqual(ResultCodes.InProgress, second.Code);
			Assert.AreEqual(ResultCodes.InProgress, signOut.Code);
			Assert.IsTrue(Run(first).IsSuccess);
			Assert.IsTrue(Run(auth.SignInAsync(AccountPlatforms.Guest)).IsSuccess);
		}

		[Test]
		public void Link_Conflict_ReturnsCredential_ThenTransferMovesIt()
		{
			var other = Run(CreateService().SignInAsync(TestAuthProvider.Name)).Value.AccountId;
			var auth = CreateService();
			var mine = Run(auth.SignInAsync(AccountPlatforms.Guest)).Value.AccountId;

			var link = Run(auth.LinkAsync(TestAuthProvider.Name));
			var transfer = Run(auth.TransferAsync(link.Value.Credential));

			Assert.IsTrue(link.IsSuccess, link.ToString());
			Assert.IsTrue(link.Value.IsConflict);
			Assert.AreEqual(other, link.Value.ConflictAccountId);
			Assert.AreEqual(TestAuthProvider.Name, link.Value.Credential.Platform);
			Assert.IsTrue(transfer.IsSuccess, transfer.ToString());
			Assert.AreEqual(new[] { AccountPlatforms.Guest, TestAuthProvider.Name }, transfer.Value.Select(platform => platform.Platform));
			Assert.AreEqual(mine, _server.Owners[TestAuthProvider.Name + "|alice"]);
			Assert.AreEqual(0, _provider.SignOutCount);
		}

		[Test]
		public void Link_Conflict_SignInWithCredential_SwitchesAccountAndRevokesPreviousLogin()
		{
			var other = Run(CreateService().SignInAsync(TestAuthProvider.Name)).Value.AccountId;
			var auth = CreateService();
			Run(auth.SignInAsync(AccountPlatforms.Guest));
			var firstSession = _session.SessionId;
			var previousToken = _server.RefreshTokens.Last().Key;
			var ended = new List<SessionEndReason>();
			_session.Ended += (_, reason) => ended.Add(reason);

			var link = Run(auth.LinkAsync(TestAuthProvider.Name));
			var switched = Run(auth.SignInAsync(link.Value.Credential));

			Assert.AreEqual(other, switched.Value.AccountId);
			Assert.AreEqual(other, auth.AccountId);
			Assert.AreNotEqual(firstSession, _session.SessionId);
			Assert.AreEqual(new[] { SessionEndReason.Restarted }, ended);
			CollectionAssert.Contains(_server.LoggedOut, previousToken);
		}

		[Test]
		public void Link_Failure_SignsOutPlatformSdk()
		{
			var auth = CreateService();
			Run(auth.SignInAsync(AccountPlatforms.Guest));
			_provider.Credential = "bad";

			var result = Run(auth.LinkAsync(TestAuthProvider.Name));

			Assert.AreEqual(AccountResultCodes.InvalidCredential, result.Code);
			Assert.AreEqual(1, _provider.SignOutCount);
		}

		[Test]
		public void Unlink_LastLogin_IsRejected_OtherwiseSignsOutPlatformSdk()
		{
			var auth = CreateService();
			Run(auth.SignInAsync(AccountPlatforms.Guest));

			var last = Run(auth.UnlinkAsync(AccountPlatforms.Guest));
			Run(auth.LinkAsync(TestAuthProvider.Name));
			var unlinked = Run(auth.UnlinkAsync(TestAuthProvider.Name));

			Assert.AreEqual(AccountResultCodes.LastLogin, last.Code);
			Assert.IsTrue(unlinked.IsSuccess);
			Assert.AreEqual(new[] { AccountPlatforms.Guest }, unlinked.Value.Select(platform => platform.Platform));
			Assert.AreEqual(1, _provider.SignOutCount);
		}

		[Test]
		public void AuthorizedCall_RejectedAccessToken_RefreshesAndRetriesOnce()
		{
			var auth = CreateService();
			Run(auth.SignInAsync(AccountPlatforms.Guest));
			_server.RejectIssuedAccessTokens();

			var links = Run(auth.GetLinksAsync());

			Assert.IsTrue(links.IsSuccess, links.ToString());
			Assert.AreEqual(1, _server.Count("refresh"));
			Assert.AreEqual(2, _server.Count("links"));
		}

		[Test]
		public void DeleteAccount_SignsOutWithAccountDeleted()
		{
			var auth = CreateService();
			Run(auth.SignInAsync(TestAuthProvider.Name));
			var reasons = new List<SignOutReason>();
			auth.SignedOut += reasons.Add;

			var result = Run(auth.DeleteAccountAsync());

			Assert.IsTrue(result.IsSuccess, result.ToString());
			Assert.AreEqual(new[] { SignOutReason.AccountDeleted }, reasons);
			Assert.IsFalse(auth.HasSavedLogin);
			Assert.AreEqual(1, _provider.SignOutCount);
		}

		[Test]
		public void AccountDeletedElsewhere_SignsOutOnNextCall()
		{
			var auth = CreateService();
			var accountId = Run(auth.SignInAsync(AccountPlatforms.Guest)).Value.AccountId;
			_server.Links.Remove(accountId);
			var reasons = new List<SignOutReason>();
			auth.SignedOut += reasons.Add;

			var links = Run(auth.GetLinksAsync());

			Assert.AreEqual(AccountResultCodes.AccountNotFound, links.Code);
			Assert.AreEqual(new[] { SignOutReason.AccountDeleted }, reasons);
			Assert.IsFalse(auth.IsSignedIn);
		}

		[Test]
		public void AccountCalls_WhenSignedOut_AreNotSignedIn()
		{
			var auth = CreateService();

			Assert.AreEqual(AccountResultCodes.NotSignedIn, Run(auth.GetAccessTokenAsync()).Code);
			Assert.AreEqual(AccountResultCodes.NotSignedIn, Run(auth.GetLinksAsync()).Code);
			Assert.AreEqual(AccountResultCodes.NotSignedIn, Run(auth.LinkAsync(TestAuthProvider.Name)).Code);
			Assert.AreEqual(AccountResultCodes.NotSignedIn, Run(auth.DeleteAccountAsync()).Code);
			Assert.AreEqual(0, _server.Requests.Count);
		}

		[Test]
		public void ServerError_IsHttpError_UnknownErrorBody_IsInvalidResponse()
		{
			var auth = CreateService();
			_server.Intercept = request => FakeAuthServer.Respond(request, 503, null);
			var unavailable = Run(auth.SignInAsync(AccountPlatforms.Guest));
			_server.Intercept = request => FakeAuthServer.Respond(request, 400, new { code = 42 });
			var unknown = Run(auth.SignInAsync(AccountPlatforms.Guest));

			Assert.AreEqual(HttpResultCodes.HttpError, unavailable.Code);
			Assert.AreEqual(AccountResultCodes.InvalidResponse, unknown.Code);
		}
		private void BackgroundTimeout()
		{
			_session.HandlePause(true);
			_utcNow += TimeSpan.FromMinutes(11);
			_session.HandlePause(false);
		}

		[Test]
		public void NewSessionAfterBackground_GetsTokenForThatSession()
		{
			var auth = CreateService();
			Run(auth.SignInAsync(AccountPlatforms.Guest));
			var first = Run(auth.GetAccessTokenAsync()).Value;

			BackgroundTimeout();
			var second = Run(auth.GetAccessTokenAsync()).Value;

			Assert.AreNotEqual(first, second);
			Assert.AreEqual(_session.SessionId, _server.SessionIds.Last());
			Assert.AreEqual(1, _server.Count("refresh"));
		}

		[Test]
		public void SessionChangesDuringRefresh_RefreshesAgainForNewSession_KeepingRotatedToken()
		{
			var auth = CreateService();
			Run(auth.SignInAsync(AccountPlatforms.Guest));
			_now = 4000;
			_server.Gates["refresh"] = new UniTaskCompletionSource();

			var pending = auth.GetAccessTokenAsync().Preserve();
			BackgroundTimeout();
			_server.Gates["refresh"].TrySetResult();
			var token = Run(pending);

			Assert.IsTrue(token.IsSuccess, token.ToString());
			Assert.AreEqual(2, _server.Count("refresh"));
			Assert.AreEqual(_session.SessionId, _server.SessionIds.Last());
			Assert.AreEqual(_server.AccessTokens.Keys.Last(), token.Value);
			StringAssert.Contains(_server.RefreshTokens.Keys.Single(), _store.Values[AccountStorageKeys.Login]);
		}

		[Test]
		public void LateResponseFromPreviousAccount_IsNotApplied()
		{
			var auth = CreateService();
			var first = Run(auth.SignInAsync(AccountPlatforms.Guest)).Value.AccountId;
			_server.Gates["links"] = new UniTaskCompletionSource();
			var reasons = new List<SignOutReason>();
			auth.SignedOut += reasons.Add;

			var pending = auth.GetLinksAsync().Preserve();
			var second = Run(auth.SignInAsync(TestAuthProvider.Name)).Value.AccountId;
			_server.Links.Remove(first);
			_server.Gates["links"].TrySetResult();
			var links = Run(pending);

			Assert.AreEqual(AccountResultCodes.NotSignedIn, links.Code);
			Assert.IsTrue(auth.IsSignedIn);
			Assert.AreEqual(second, auth.AccountId);
			Assert.IsEmpty(reasons);
		}

		[Test]
		public void RefreshForPreviousAccount_IsNotSharedWithNewAccount()
		{
			var auth = CreateService();
			Run(auth.SignInAsync(AccountPlatforms.Guest));
			_now = 4000;
			_server.Gates["refresh"] = new UniTaskCompletionSource();
			var previous = auth.GetAccessTokenAsync().Preserve();

			Run(auth.SignInAsync(TestAuthProvider.Name));
			auth.InvalidateAccessToken(Run(auth.GetAccessTokenAsync()).Value);
			var current = auth.GetAccessTokenAsync().Preserve();
			_server.Gates["refresh"].TrySetResult();

			Assert.AreEqual(AccountResultCodes.NotSignedIn, Run(previous).Code);
			Assert.IsTrue(Run(current).IsSuccess);
			Assert.AreEqual(2, _server.Count("refresh"));
		}

		[Test]
		public void Dispose_DuringProviderSignIn_DoesNotApplyLateResult()
		{
			var auth = CreateService();
			_provider.Gate = new UniTaskCompletionSource<Result<PlatformCredential>>();
			var signedIn = 0;
			auth.SignedIn += _ => signedIn++;

			var pending = auth.SignInAsync(TestAuthProvider.Name).Preserve();
			auth.Dispose();
			_provider.Gate.TrySetResult(Result<PlatformCredential>.Ok(new PlatformCredential(TestAuthProvider.Name, "alice")));

			Assert.AreEqual(ResultCodes.Cancelled, Run(pending).Code);
			Assert.AreEqual(0, _server.Count("login"));
			Assert.IsFalse(auth.IsSignedIn);
			Assert.IsFalse(_session.IsActive);
			Assert.AreEqual(0, signedIn);
		}

		[Test]
		public void Dispose_DuringLoginRequest_DoesNotApplyLateResult()
		{
			var auth = CreateService();
			_server.Gates["login"] = new UniTaskCompletionSource();

			var pending = auth.SignInAsync(AccountPlatforms.Guest).Preserve();
			auth.Dispose();
			_server.Gates["login"].TrySetResult();

			Assert.AreEqual(ResultCodes.Cancelled, Run(pending).Code);
			Assert.IsFalse(auth.IsSignedIn);
			Assert.IsFalse(_store.Values.ContainsKey(AccountStorageKeys.Login));
			Assert.Throws<ObjectDisposedException>(() => auth.SignInSilentlyAsync());
		}

		[Test]
		public void SignOut_DeleteFails_OverwritesSoRestartIsSignedOut()
		{
			var auth = CreateService();
			Run(auth.SignInAsync(AccountPlatforms.Guest));
			_store.FailDelete = true;

			var result = Run(auth.SignOutAsync());
			_store.FailDelete = false;
			var restarted = CreateService();

			Assert.IsTrue(result.IsSuccess, result.ToString());
			Assert.IsFalse(restarted.HasSavedLogin);
		}

		[Test]
		public void SignOut_StorageCannotBeCleared_ReportsStorageFailed()
		{
			var auth = CreateService();
			Run(auth.SignInAsync(AccountPlatforms.Guest));
			_store.FailDelete = true;
			_store.FailWrite = true;

			var result = Run(auth.SignOutAsync());

			Assert.AreEqual(AccountResultCodes.StorageFailed, result.Code);
			Assert.IsFalse(auth.IsSignedIn);
		}

		[Test]
		public void SignOut_ClearsSavedLoginThatCouldNotBeReadAtStartup()
		{
			Run(CreateService().SignInAsync(AccountPlatforms.Guest));
			_store.FailRead = true;
			var restarted = CreateService();
			_store.FailRead = false;

			Run(restarted.SignOutAsync());

			Assert.IsFalse(_store.Values.ContainsKey(AccountStorageKeys.Login));
		}

		[Test]
		public void ServerErrorWithKnownCode_IsHttpError_AndKeepsSavedLogin()
		{
			Run(CreateService().SignInAsync(AccountPlatforms.Guest));
			_server.Intercept = request => FakeAuthServer.Respond(request, 503, new { code = AccountResultCodes.InvalidRefreshToken });
			var restarted = CreateService();

			var result = Run(restarted.SignInSilentlyAsync());

			Assert.AreEqual(HttpResultCodes.HttpError, result.Code);
			Assert.IsTrue(restarted.HasSavedLogin);
		}

		[Test]
		public void Requests_HaveTimeout()
		{
			Run(CreateService().SignInAsync(AccountPlatforms.Guest));

			Assert.AreEqual(TimeSpan.FromSeconds(15), _server.Requests.Single().Timeout);
		}

		[Test]
		public void LinkResultFactories_ValidateArguments()
		{
			Assert.IsFalse(LinkResult.Linked(new LinkedPlatform[0]).IsConflict);
			Assert.IsTrue(LinkResult.Conflict("acc", new PlatformCredential("test", "x")).IsConflict);
			Assert.Throws<ArgumentException>(() => LinkResult.Conflict("", new PlatformCredential("test", "x")));
			Assert.Throws<ArgumentNullException>(() => LinkResult.Linked(null));
		}
		[Test]
		public void SharedRefresh_FirstWaiterSignsOut_SecondWaiterIsNotSignedIn()
		{
			var auth = CreateService();
			Run(auth.SignInAsync(AccountPlatforms.Guest));
			_now = 4000;
			_server.Gates["refresh"] = new UniTaskCompletionSource();

			async UniTask<Result<string>> FirstThenSignOut()
			{
				var token = await auth.GetAccessTokenAsync();
				await auth.SignOutAsync();
				return token;
			}

			var first = FirstThenSignOut().Preserve();
			var second = auth.GetAccessTokenAsync().Preserve();
			_server.Gates["refresh"].TrySetResult();

			Assert.IsTrue(Run(first).IsSuccess);
			Assert.AreEqual(AccountResultCodes.NotSignedIn, Run(second).Code);
		}

		[Test]
		public void SharedRefresh_FirstWaiterDisposes_SecondWaiterIsCancelled()
		{
			var auth = CreateService();
			Run(auth.SignInAsync(AccountPlatforms.Guest));
			_now = 4000;
			_server.Gates["refresh"] = new UniTaskCompletionSource();

			async UniTask<Result<string>> FirstThenDispose()
			{
				var token = await auth.GetAccessTokenAsync();
				auth.Dispose();
				return token;
			}

			var first = FirstThenDispose().Preserve();
			var second = auth.GetAccessTokenAsync().Preserve();
			_server.Gates["refresh"].TrySetResult();

			Assert.IsTrue(Run(first).IsSuccess);
			Assert.AreEqual(ResultCodes.Cancelled, Run(second).Code);
		}

		[Test]
		public void SessionChangesTwiceDuringRefresh_ReturnsTokenOnlyForCurrentSession()
		{
			var auth = CreateService();
			Run(auth.SignInAsync(AccountPlatforms.Guest));
			_now = 4000;
			var gates = new List<UniTaskCompletionSource> { new UniTaskCompletionSource(), new UniTaskCompletionSource() };
			var refreshes = 0;
			_server.BeforeRespond = request => request.Uri.AbsolutePath.EndsWith("/refresh") && refreshes < gates.Count
				? gates[refreshes++].Task
				: UniTask.CompletedTask;

			var pending = auth.GetAccessTokenAsync().Preserve();
			_session.Start();
			gates[0].TrySetResult();
			_session.Start();
			gates[1].TrySetResult();
			var token = Run(pending);

			Assert.IsTrue(token.IsSuccess, token.ToString());
			Assert.AreEqual(3, _server.Count("refresh"));
			Assert.AreEqual(_session.SessionId, _server.SessionIds.Last());
		}

		[Test]
		public void SessionKeepsChangingDuringRefresh_IsSessionChanged()
		{
			var auth = CreateService();
			Run(auth.SignInAsync(AccountPlatforms.Guest));
			_now = 4000;
			_server.BeforeRespond = request =>
			{
				if (request.Uri.AbsolutePath.EndsWith("/refresh"))
				{
					_session.Start();
				}

				return UniTask.CompletedTask;
			};

			var token = Run(auth.GetAccessTokenAsync());

			Assert.AreEqual(AccountResultCodes.SessionChanged, token.Code);
			Assert.AreEqual(3, _server.Count("refresh"));
			StringAssert.Contains(_server.RefreshTokens.Keys.Single(), _store.Values[AccountStorageKeys.Login]);
		}
		private sealed class GatedGameTransport : IHttpTransport
		{
			public readonly UniTaskCompletionSource<int> Gate = new UniTaskCompletionSource<int>();

			public async UniTask<Result<HttpResponse>> SendAsync(HttpRequest request, CancellationToken cancellationToken)
			{
				var status = await Gate.Task;
				return Result<HttpResponse>.Ok(new HttpResponse(request.Uri, status, new byte[0], null));
			}
		}

		[Test]
		public void SharedRefresh_SessionChanges_BothWaitersGetTokenForCurrentSession()
		{
			var auth = CreateService();
			Run(auth.SignInAsync(AccountPlatforms.Guest));
			_now = 4000;
			_server.Gates["refresh"] = new UniTaskCompletionSource();

			var first = auth.GetAccessTokenAsync().Preserve();
			var second = auth.GetAccessTokenAsync().Preserve();
			_session.Start();
			_server.Gates["refresh"].TrySetResult();
			var latest = _server.AccessTokens.Keys.Last();

			Assert.AreEqual(latest, Run(first).Value);
			Assert.AreEqual(latest, Run(second).Value);
			Assert.AreEqual(2, _server.Count("refresh"));
			Assert.AreEqual(_session.SessionId, _server.SessionIds.Last());
		}

		[Test]
		public void Middleware_ServiceDisposedDuringRequest_IsCancelled()
		{
			var auth = CreateService();
			Run(auth.SignInAsync(AccountPlatforms.Guest));
			var transport = new GatedGameTransport();
			var client = new HttpClientBuilder().WithBaseAddress("https://game.test/").AddMiddleware(new AuthTokenMiddleware(auth)).WithTransport(transport).Build();

			var pending = client.GetAsync("me").Preserve();
			auth.Dispose();
			transport.Gate.TrySetResult(401);

			Assert.AreEqual(ResultCodes.Cancelled, Run(pending).Code);
		}

		[Test]
		public void AccountSwitch_SaveFails_PreviousAccountIsNotRestoredOnRestart()
		{
			var auth = CreateService();
			Run(auth.SignInAsync(AccountPlatforms.Guest));
			_store.FailWrite = true;
			_server.Intercept = request => request.Uri.AbsolutePath.EndsWith("/logout")
				? Result<HttpResponse>.Fail(ResultCodes.NetworkError)
				: (Result<HttpResponse>?) null;

			var switched = Run(auth.SignInAsync(TestAuthProvider.Name));
			_store.FailWrite = false;
			_server.Intercept = null;
			var restarted = CreateService();

			Assert.IsTrue(switched.IsSuccess, switched.ToString());
			Assert.IsFalse(switched.Value.IsSaved);
			Assert.IsTrue(auth.IsSignedIn);
			Assert.IsFalse(restarted.HasSavedLogin);
		}

		[Test]
		public void SignIn_Saved_IsSaved()
		{
			Assert.IsTrue(Run(CreateService().SignInAsync(AccountPlatforms.Guest)).Value.IsSaved);
		}
		[Test]
		public void AccountSwitch_SaveAndClearFail_IsRejected_KeepingCurrentLogin()
		{
			var auth = CreateService();
			var first = Run(auth.SignInAsync(AccountPlatforms.Guest)).Value.AccountId;
			var signedIn = 0;
			auth.SignedIn += _ => signedIn++;
			_store.FailWrite = true;
			_store.FailDelete = true;
			_server.Intercept = request => request.Uri.AbsolutePath.EndsWith("/logout")
				? Result<HttpResponse>.Fail(ResultCodes.NetworkError)
				: (Result<HttpResponse>?) null;

			var switched = Run(auth.SignInAsync(TestAuthProvider.Name));

			var revoked = _server.Requests.Single(request => request.Uri.AbsolutePath.EndsWith("/logout"));
			var revokedToken = Newtonsoft.Json.JsonConvert.DeserializeObject<LogoutRequest>(System.Text.Encoding.UTF8.GetString(revoked.Body), AccountApi.JsonSettings).RefreshToken;
			_server.Intercept = null;
			_now = 4000;
			var refreshed = Run(auth.GetAccessTokenAsync());

			Assert.AreEqual(AccountResultCodes.StorageFailed, switched.Code);
			Assert.AreEqual(first, auth.AccountId);
			Assert.AreEqual(0, signedIn);
			Assert.AreEqual(_server.RefreshTokens.Keys.Last(), revokedToken, "the new account's token is revoked");
			Assert.IsTrue(refreshed.IsSuccess, "the current account's refresh token is kept: " + refreshed);
		}

		[Test]
		public void StartupReadFailed_ThenSaveAndClearFail_SignInIsRejected()
		{
			Run(CreateService().SignInAsync(AccountPlatforms.Guest));
			_store.FailRead = true;
			_store.FailWrite = true;
			_store.FailDelete = true;
			var restarted = CreateService();

			var result = Run(restarted.SignInAsync(TestAuthProvider.Name));

			Assert.AreEqual(AccountResultCodes.StorageFailed, result.Code);
			Assert.IsFalse(restarted.IsSignedIn);
		}

		[Test]
		public void SignOutClearFailed_ThenSaveAndClearFail_OtherAccountIsRejected()
		{
			var auth = CreateService();
			Run(auth.SignInAsync(AccountPlatforms.Guest));
			_store.FailWrite = true;
			_store.FailDelete = true;
			Assert.AreEqual(AccountResultCodes.StorageFailed, Run(auth.SignOutAsync()).Code);

			var result = Run(auth.SignInAsync(TestAuthProvider.Name));

			Assert.AreEqual(AccountResultCodes.StorageFailed, result.Code);
			Assert.IsFalse(auth.IsSignedIn);
		}

		[Test]
		public void FirstSignIn_OnEmptyStorageThatCannotBeWritten_SignsInUnsaved()
		{
			var auth = CreateService();
			_store.FailWrite = true;
			_store.FailDelete = true;

			var result = Run(auth.SignInAsync(TestAuthProvider.Name));

			Assert.IsTrue(result.IsSuccess, result.ToString());
			Assert.IsFalse(result.Value.IsSaved);
			Assert.IsTrue(auth.IsSignedIn);
		}
	}
}
