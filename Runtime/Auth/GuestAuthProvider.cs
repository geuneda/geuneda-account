using System;
using System.Security.Cryptography;
using System.Threading;
using Cysharp.Threading.Tasks;
using Geuneda.Core;

// ReSharper disable once CheckNamespace

namespace Geuneda.Account
{
	/// <summary>
	/// 게스트 로그인입니다. 처음 로그인할 때 무작위 비밀 값(32바이트)을 만들어 보안 저장소에 두고, 그 값을 자격으로 보냅니다. 서버는 해시만 저장합니다.
	/// </summary>
	/// <remarks>
	/// 비밀 값은 로그아웃해도 지우지 않습니다(같은 게스트로 다시 로그인). iOS 는 앱을 지워도 남고, Android 는 앱을 지우면 사라져 그 게스트 계정에 다시 들어갈 수 없습니다.
	/// 게임은 다른 플랫폼 연동을 권합니다. 저장소를 읽지 못한 실행에서는 새 비밀 값을 만들지 않습니다(기존 게스트 계정을 잃지 않게).
	/// </remarks>
	public sealed class GuestAuthProvider : IAuthProvider
	{
		internal const int SecretBytes = 32;

		private static readonly ModuleLogger Logger = new ModuleLogger("Geuneda.Account");

		private readonly ISecureStore _store;
		private readonly string _key;

		/// <exception cref="ArgumentNullException"><paramref name="secureStore"/> 가 null 인 경우</exception>
		public GuestAuthProvider(ISecureStore secureStore) : this(secureStore, AccountStorageKeys.GuestSecret)
		{
		}

		internal GuestAuthProvider(ISecureStore secureStore, string key)
		{
			_store = secureStore ?? throw new ArgumentNullException(nameof(secureStore));
			_key = key;
		}

		public string Platform => AccountPlatforms.Guest;

		/// <summary>비밀 값은 이 기기에서만 만들고 보내므로 nonce 를 쓰지 않습니다.</summary>
		public bool RequiresNonce => false;

		public UniTask<Result<PlatformCredential>> SignInAsync(string nonce, CancellationToken cancellationToken)
		{
			if (cancellationToken.IsCancellationRequested)
			{
				return UniTask.FromResult(Result<PlatformCredential>.Fail(ResultCodes.Cancelled));
			}

			switch (_store.TryRead(_key, out var secret))
			{
				case SecureReadResult.Found:
					return UniTask.FromResult(Result<PlatformCredential>.Ok(new PlatformCredential(Platform, secret)));
				case SecureReadResult.Failed:
					Logger.Warning("Guest secret could not be read. A new one is not created so the existing guest account is kept");
					return UniTask.FromResult(Result<PlatformCredential>.Fail(AccountResultCodes.PlatformSignInFailed, "Guest secret could not be read"));
			}

			var created = NewSecret();

			if (!_store.TryWrite(_key, created))
			{
				// 저장하지 못한 비밀 값으로 계정을 만들면 다시 들어갈 수 없다
				return UniTask.FromResult(Result<PlatformCredential>.Fail(AccountResultCodes.PlatformSignInFailed, "Guest secret could not be saved"));
			}

			return UniTask.FromResult(Result<PlatformCredential>.Ok(new PlatformCredential(Platform, created)));
		}

		public UniTask SignOutAsync(CancellationToken cancellationToken)
		{
			return UniTask.CompletedTask;
		}

		private static string NewSecret()
		{
			var bytes = new byte[SecretBytes];

			using (var random = RandomNumberGenerator.Create())
			{
				random.GetBytes(bytes);
			}

			return AccountBase64.UrlEncode(bytes);
		}
	}
}
