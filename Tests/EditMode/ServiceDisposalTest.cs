using System;
using Geuneda.Http;
using NUnit.Framework;
using Random = System.Random;

// ReSharper disable once CheckNamespace

namespace Geuneda.Account.Tests
{
	/// <summary>
	/// <see cref="IAuthService"/>·<see cref="IAppSession"/> 은 IDisposable 이라 services 의 <c>MainInstaller.CleanDispose&lt;T&gt;()</c>
	/// (<c>where T : class, IDisposable</c>)로 정리할 수 있습니다. account 는 services 에 의존하지 않으므로 같은 제약의 도우미로 확인합니다
	/// (인터페이스가 IDisposable 이 아니면 이 파일이 컴파일되지 않음).
	/// </summary>
	[TestFixture]
	public class ServiceDisposalTest
	{
		// MainInstaller.CleanDispose<T> 와 같은 제약으로 인터페이스를 통해 Dispose 한다
		private static void CleanDispose<T>(T service) where T : class, IDisposable
		{
			service.Dispose();
		}

		[Test]
		public void AuthService_IsDisposedThroughIAuthService()
		{
			var store = new FakeSecureStore();
			var options = new AuthServiceOptions
			{
				AuthClient = new HttpClientBuilder().WithBaseAddress(FakeAuthServer.BaseAddress).WithTransport(new FakeAuthServer()).Build(),
				SecureStore = store,
				Session = new AppSession(TimeSpan.FromMinutes(10), () => new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), new Random(1))
			};
			IAuthService auth = new AuthService(options, () => 0);

			CleanDispose<IAuthService>(auth);

			Assert.Throws<ObjectDisposedException>(() => auth.SignInSilentlyAsync());
		}

		[Test]
		public void AppSession_IsDisposedThroughIAppSession_WithoutEndingTheSession()
		{
			IAppSession session = new AppSession(TimeSpan.FromMinutes(10), () => new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), new Random(1));
			session.Start();
			var ended = 0;
			session.Ended += (_, _) => ended++;

			CleanDispose<IAppSession>(session);

			Assert.IsTrue(session.IsActive);
			Assert.AreEqual(0, ended);
		}
	}
}
