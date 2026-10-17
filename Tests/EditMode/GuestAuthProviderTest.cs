using System.Threading;
using Geuneda.Core;
using NUnit.Framework;

// ReSharper disable once CheckNamespace

namespace Geuneda.Account.Tests
{
	[TestFixture]
	public class GuestAuthProviderTest
	{
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
		}

		[TearDown]
		public void TearDown()
		{
			Log.Sink = _originalSink;
		}

		[Test]
		public void FirstSignIn_CreatesSecret_LaterSignInsReuseIt()
		{
			var store = new FakeSecureStore();
			var provider = new GuestAuthProvider(store, "guest");

			var first = FakeAuthServer.Run(provider.SignInAsync(null, CancellationToken.None)).Value;
			var second = FakeAuthServer.Run(new GuestAuthProvider(store, "guest").SignInAsync(null, CancellationToken.None)).Value;

			Assert.AreEqual(AccountPlatforms.Guest, first.Platform);
			Assert.AreEqual(43, first.Credential.Length);
			StringAssert.IsMatch("^[A-Za-z0-9_-]+$", first.Credential);
			Assert.AreEqual(first.Credential, second.Credential);
			Assert.AreEqual(first.Credential, store.Values["guest"]);
		}

		[Test]
		public void ReadFailure_DoesNotCreateNewSecret()
		{
			var store = new FakeSecureStore { FailRead = true };
			store.Values["guest"] = "existing";

			var result = FakeAuthServer.Run(new GuestAuthProvider(store, "guest").SignInAsync(null, CancellationToken.None));

			Assert.AreEqual(AccountResultCodes.PlatformSignInFailed, result.Code);
			Assert.AreEqual("existing", store.Values["guest"]);
		}

		[Test]
		public void WriteFailure_Fails()
		{
			var store = new FakeSecureStore { FailWrite = true };

			var result = FakeAuthServer.Run(new GuestAuthProvider(store, "guest").SignInAsync(null, CancellationToken.None));

			Assert.AreEqual(AccountResultCodes.PlatformSignInFailed, result.Code);
		}

		[Test]
		public void Cancelled_IsCancelled()
		{
			var cancelled = new CancellationTokenSource();
			cancelled.Cancel();

			var result = FakeAuthServer.Run(new GuestAuthProvider(new FakeSecureStore(), "guest").SignInAsync(null, cancelled.Token));

			Assert.AreEqual(ResultCodes.Cancelled, result.Code);
		}
	}
}
