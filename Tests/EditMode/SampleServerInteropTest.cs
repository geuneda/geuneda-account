using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using Geuneda.Core;
using Geuneda.Http;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine.TestTools;
using Random = System.Random;

// ReSharper disable once CheckNamespace

namespace Geuneda.Account.Tests
{
	/// <summary>
	/// 실제 .NET 샘플 인증 서버(<c>Server~/samples/Geuneda.Account.SampleServer</c>)와의 상호 운용을 확인합니다.
	/// 서버를 띄우고 <c>GEUNEDA_ACCOUNT_INTEROP_URL</c>(예: <c>http://127.0.0.1:5299</c>)을 설정한 뒤 이름으로 골라 실행합니다.
	/// </summary>
	[TestFixture]
	[Explicit("샘플 인증 서버가 필요합니다")]
	[Category("Interop")]
	public class SampleServerInteropTest
	{
		private const string UrlVariable = "GEUNEDA_ACCOUNT_INTEROP_URL";

		/// <summary>샘플 서버의 시험용 플랫폼(<c>sample:&lt;id&gt;:&lt;nonce&gt;</c> 를 그대로 믿고, 끝의 nonce 가 요청의 nonce 와 같아야 함)입니다.</summary>
		private sealed class SampleProvider : IAuthProvider
		{
			public string Subject;

			public string Platform => "sample";

			public bool RequiresNonce => true;

			public UniTask<Result<PlatformCredential>> SignInAsync(string nonce, CancellationToken cancellationToken)
			{
				return UniTask.FromResult(Result<PlatformCredential>.Ok(new PlatformCredential(Platform, "sample:" + Subject + ":" + nonce)));
			}

			public UniTask SignOutAsync(CancellationToken cancellationToken)
			{
				return UniTask.CompletedTask;
			}
		}

		private static string ServerUrl()
		{
			var url = Environment.GetEnvironmentVariable(UrlVariable);

			if (string.IsNullOrEmpty(url))
			{
				Assert.Ignore($"{UrlVariable} is not set");
			}

			return url.TrimEnd('/');
		}

		private static AuthService CreateService(string url, FakeSecureStore store, AppSession session, SampleProvider sample)
		{
			var options = new AuthServiceOptions
			{
				AuthClient = new HttpClientBuilder().WithBaseAddress(url + "/auth/").Build(),
				SecureStore = store,
				Session = session
			};
			options.Providers.Add(new GuestAuthProvider(store));
			options.Providers.Add(sample);
			return new AuthService(options);
		}

		private static AppSession NewSession()
		{
			return new AppSession(TimeSpan.FromMinutes(10), () => DateTime.UtcNow, new Random());
		}

		[UnityTest]
		public IEnumerator SampleServer_SignInGameApiRestartLinkTransferDelete() => UniTask.ToCoroutine(async () =>
		{
			var url = ServerUrl();
			var store = new FakeSecureStore();
			var session = NewSession();
			var sample = new SampleProvider { Subject = Guid.NewGuid().ToString("N") };
			var auth = CreateService(url, store, session, sample);
			var gameClient = new HttpClientBuilder().WithBaseAddress(url + "/").AddMiddleware(new AuthTokenMiddleware(auth), 100).Build();

			// 게스트 로그인과 게임 API(access 토큰 검증, 세션 ID 전달)
			var signedIn = await auth.SignInAsync(AccountPlatforms.Guest);
			var me = await gameClient.GetJsonAsync<JObject>("game/me");

			Assert.IsTrue(signedIn.IsSuccess, signedIn.ToString());
			Assert.IsTrue(signedIn.Value.IsNewAccount);
			Assert.IsTrue(me.IsSuccess, me.ToString());
			Assert.AreEqual(signedIn.Value.AccountId, (string) me.Value["accountId"]);
			Assert.AreEqual(session.SessionId, (string) me.Value["sessionId"]);

			// 앱 재시작: 저장된 로그인으로 자동 로그인(refresh 회전)
			auth.Dispose();
			var restartedSession = NewSession();
			var restarted = CreateService(url, store, restartedSession, sample);
			var silent = await restarted.SignInSilentlyAsync();
			var restartedGame = new HttpClientBuilder().WithBaseAddress(url + "/").AddMiddleware(new AuthTokenMiddleware(restarted), 100).Build();
			var meAgain = await restartedGame.GetJsonAsync<JObject>("game/me");

			Assert.IsTrue(silent.IsSuccess, silent.ToString());
			Assert.AreEqual(signedIn.Value.AccountId, silent.Value.AccountId);
			Assert.AreEqual(restartedSession.SessionId, (string) meAgain.Value["sessionId"]);

			// 연동(서버 발급 nonce), 다른 계정과의 충돌, 충돌 티켓으로 옮겨오기, 티켓을 다시 쓰면 거절
			var linked = await restarted.LinkAsync("sample");
			var otherStore = new FakeSecureStore();
			var other = CreateService(url, otherStore, NewSession(), new SampleProvider { Subject = sample.Subject });
			var otherSignedIn = await other.SignInAsync(AccountPlatforms.Guest);
			var conflict = await other.LinkAsync("sample");
			var transferred = await other.TransferAsync(conflict.Value.Credential);
			var replayed = await other.TransferAsync(conflict.Value.Credential);
			var sampleLogin = await CreateService(url, new FakeSecureStore(), NewSession(), sample).SignInAsync("sample");

			Assert.IsTrue(linked.IsSuccess, linked.ToString());
			Assert.IsFalse(linked.Value.IsConflict);
			Assert.AreEqual(new[] { "guest", "sample" }, linked.Value.Links.Select(link => link.Platform));
			Assert.IsTrue(conflict.Value.IsConflict);
			Assert.AreEqual(signedIn.Value.AccountId, conflict.Value.ConflictAccountId);
			Assert.IsFalse(string.IsNullOrEmpty(conflict.Value.Credential.ConflictTicket));
			Assert.IsTrue(transferred.IsSuccess, transferred.ToString());
			Assert.AreEqual(AccountResultCodes.InvalidConflictTicket, replayed.Code, replayed.ToString());
			Assert.AreEqual(otherSignedIn.Value.AccountId, sampleLogin.Value.AccountId);

			// 해제: 마지막 로그인 수단은 거절
			var lastLogin = await restarted.UnlinkAsync(AccountPlatforms.Guest);
			Assert.AreEqual(AccountResultCodes.LastLogin, lastLogin.Code);

			// 로그아웃하면 게임 API 를 보내지 않고, 서버에서도 그 로그인이 폐기된다
			var savedBeforeSignOut = store.Values.Values.ToArray();
			var signOut = await restarted.SignOutAsync();
			var afterSignOut = await restartedGame.GetAsync("game/me");
			store.Values[AccountStorageKeys.Login] = savedBeforeSignOut.Single(value => value.Contains("refreshToken"));
			var revoked = await CreateService(url, store, NewSession(), sample).SignInSilentlyAsync();

			Assert.IsTrue(signOut.IsSuccess);
			Assert.AreEqual(AccountResultCodes.NotSignedIn, afterSignOut.Code);
			Assert.AreEqual(AccountResultCodes.InvalidRefreshToken, revoked.Code);

			// 계정 삭제
			var reasons = new List<SignOutReason>();
			other.SignedOut += reasons.Add;
			var deleted = await other.DeleteAccountAsync();
			var afterDelete = await CreateService(url, new FakeSecureStore(), NewSession(), sample).SignInAsync("sample");

			Assert.IsTrue(deleted.IsSuccess, deleted.ToString());
			Assert.AreEqual(new[] { SignOutReason.AccountDeleted }, reasons);
			Assert.IsTrue(afterDelete.Value.IsNewAccount);
		});
	}
}
