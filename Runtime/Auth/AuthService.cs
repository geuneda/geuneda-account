using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using Geuneda.Core;
using UnityEngine;

// ReSharper disable once CheckNamespace

namespace Geuneda.Account
{
	/// <summary>
	/// 기본 계정 인증입니다. 부트스트랩에서 만들어 <see cref="IAuthService"/> 로 바인딩합니다. 동작 규칙은 <see cref="IAuthService"/> 를 봅니다.
	/// </summary>
	/// <remarks>
	/// <para>
	/// refresh 토큰과 계정 ID 는 보안 저장소에 두고, access 토큰은 메모리에만 둡니다. access 토큰의 만료는 받은 시점부터 단조 시계로 세므로 기기 시계와 무관합니다.
	/// 세션이 바뀌면(백그라운드 시간 초과) 그 세션 ID 로 토큰을 다시 받습니다.
	/// </para>
	/// <para>
	/// 로그인이 바뀌기 전에 시작한 요청(재발급, 인증된 호출)이 늦게 끝나면 그 결과는 지금 로그인에 적용하지 않습니다.
	/// <see cref="Dispose"/> 하면 진행 중인 요청을 취소하고, 늦게 끝난 결과도 적용하지 않습니다. 이벤트 처리기의 예외는 로그만 남깁니다.
	/// </para>
	/// </remarks>
	public sealed class AuthService : IAuthService, IDisposable
	{
		private static readonly ModuleLogger Logger = new ModuleLogger("Geuneda.Account");

		// 재발급하는 동안 세션이 바뀌면 새 세션으로 다시 받는 최대 횟수
		private const int MaxTokenAttempts = 3;

		private readonly AccountApi _api;
		private readonly AuthTokenStore _tokenStore;
		private readonly IAppSession _session;
		private readonly IReadOnlyDictionary<string, IAuthProvider> _providers;
		private readonly AccessTokenCache _accessToken;
		private readonly Func<double> _clock;
		private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();

		private SavedLogin _login;
		private bool _loginSaved;
		private bool _signedIn;
		private int _loginVersion;
		private UniTaskCompletionSource<Result<string>> _refresh;
		private int _refreshVersion;
		private bool _operationRunning;
		private bool _disposed;

		public event Action<SignInResult> SignedIn;
		public event Action<SignOutReason> SignedOut;

		/// <summary>저장된 로그인을 읽어 둡니다(자동 로그인은 <see cref="SignInSilentlyAsync"/>). 메인 스레드에서 만듭니다.</summary>
		/// <exception cref="ArgumentNullException"><paramref name="options"/> 가 null 인 경우</exception>
		/// <exception cref="ArgumentException">필수 설정이 없거나, 시간 설정이 범위를 벗어났거나, 제공자의 플랫폼 이름이 비었거나 겹치는 경우</exception>
		public AuthService(AuthServiceOptions options) : this(options, () => Time.realtimeSinceStartupAsDouble)
		{
		}

		internal AuthService(AuthServiceOptions options, Func<double> clock)
		{
			if (options == null)
			{
				throw new ArgumentNullException(nameof(options));
			}

			if (options.AuthClient == null || options.SecureStore == null || options.Session == null)
			{
				throw new ArgumentException("AuthClient, SecureStore and Session are required", nameof(options));
			}

			if (options.AccessTokenRefreshMargin < TimeSpan.Zero || options.RequestTimeout <= TimeSpan.Zero)
			{
				throw new ArgumentException("AccessTokenRefreshMargin must not be negative and RequestTimeout must be positive", nameof(options));
			}

			var providers = new Dictionary<string, IAuthProvider>(StringComparer.Ordinal);

			foreach (var provider in options.Providers)
			{
				if (provider == null || string.IsNullOrEmpty(provider.Platform))
				{
					throw new ArgumentException("Providers must not be null and need a platform name", nameof(options));
				}

				if (!providers.TryAdd(provider.Platform, provider))
				{
					throw new ArgumentException($"Platform '{provider.Platform}' has more than one provider", nameof(options));
				}
			}

			_api = new AccountApi(options.AuthClient, options.RequestTimeout);
			_tokenStore = new AuthTokenStore(options.SecureStore, AccountStorageKeys.Login);
			_session = options.Session;
			_providers = providers;
			_accessToken = new AccessTokenCache(options.AccessTokenRefreshMargin);
			_clock = clock;

			if (_tokenStore.TryLoad(out var saved) == SecureReadResult.Found)
			{
				_login = saved;
			}
		}

		public bool IsSignedIn => _signedIn;
		public string AccountId => _signedIn ? _login.AccountId : null;
		public bool HasSavedLogin => _login != null;

		public UniTask<Result<SignInResult>> SignInAsync(string platform, CancellationToken cancellationToken = default)
		{
			return SignInWithProviderAsync(FindProvider(platform), cancellationToken);
		}

		/// <exception cref="ArgumentNullException"><paramref name="credential"/> 이 null 인 경우</exception>
		public UniTask<Result<SignInResult>> SignInAsync(PlatformCredential credential, CancellationToken cancellationToken = default)
		{
			ThrowIfDisposed();
			return SignInWithCredentialAsync(credential ?? throw new ArgumentNullException(nameof(credential)), cancellationToken);
		}

		public UniTask<Result<SignInResult>> SignInSilentlyAsync(CancellationToken cancellationToken = default)
		{
			ThrowIfDisposed();
			return SignInSilentlyCoreAsync(cancellationToken);
		}

		public UniTask<Result<string>> GetAccessTokenAsync(CancellationToken cancellationToken = default)
		{
			ThrowIfDisposed();
			return GetAccessTokenCoreAsync(cancellationToken);
		}

		public void InvalidateAccessToken(string accessToken)
		{
			_accessToken.Invalidate(accessToken);
		}

		public UniTask<Result> SignOutAsync(CancellationToken cancellationToken = default)
		{
			ThrowIfDisposed();
			return SignOutCoreAsync(cancellationToken);
		}

		public UniTask<Result<IReadOnlyList<LinkedPlatform>>> GetLinksAsync(CancellationToken cancellationToken = default)
		{
			ThrowIfDisposed();
			return GetLinksCoreAsync(cancellationToken);
		}

		public UniTask<Result<LinkResult>> LinkAsync(string platform, CancellationToken cancellationToken = default)
		{
			return LinkCoreAsync(FindProvider(platform), cancellationToken);
		}

		/// <exception cref="ArgumentNullException"><paramref name="credential"/> 이 null 인 경우</exception>
		public UniTask<Result<IReadOnlyList<LinkedPlatform>>> TransferAsync(PlatformCredential credential, CancellationToken cancellationToken = default)
		{
			ThrowIfDisposed();
			return TransferCoreAsync(credential ?? throw new ArgumentNullException(nameof(credential)), cancellationToken);
		}

		/// <exception cref="ArgumentException"><paramref name="platform"/> 이 비어 있는 경우</exception>
		public UniTask<Result<IReadOnlyList<LinkedPlatform>>> UnlinkAsync(string platform, CancellationToken cancellationToken = default)
		{
			ThrowIfDisposed();

			if (string.IsNullOrEmpty(platform))
			{
				throw new ArgumentException("Platform must not be empty", nameof(platform));
			}

			return UnlinkCoreAsync(platform, cancellationToken);
		}

		public UniTask<Result> DeleteAccountAsync(CancellationToken cancellationToken = default)
		{
			ThrowIfDisposed();
			return DeleteAccountCoreAsync(cancellationToken);
		}

		/// <summary>진행 중인 요청을 취소하고 늦게 끝난 결과를 버립니다. 저장된 로그인은 그대로 둡니다.</summary>
		public void Dispose()
		{
			if (_disposed)
			{
				return;
			}

			_disposed = true;
			_lifetime.Cancel();
		}

		private async UniTask<Result<SignInResult>> SignInWithProviderAsync(IAuthProvider provider, CancellationToken cancellationToken)
		{
			if (!TryBeginOperation())
			{
				return InProgress<SignInResult>();
			}

			try
			{
				using (var linked = LinkLifetime(cancellationToken))
				{
					var credential = await provider.SignInAsync(linked.Token);

					if (_disposed)
					{
						return DisposedResult<SignInResult>();
					}

					if (!credential.TryGetValue(out var value))
					{
						return Result<SignInResult>.Fail(credential.Code, credential.Message);
					}

					return await SignInCoreAsync(value, linked.Token);
				}
			}
			finally
			{
				_operationRunning = false;
			}
		}

		private async UniTask<Result<SignInResult>> SignInWithCredentialAsync(PlatformCredential credential, CancellationToken cancellationToken)
		{
			if (!TryBeginOperation())
			{
				return InProgress<SignInResult>();
			}

			try
			{
				using (var linked = LinkLifetime(cancellationToken))
				{
					return await SignInCoreAsync(credential, linked.Token);
				}
			}
			finally
			{
				_operationRunning = false;
			}
		}

		private async UniTask<Result<SignInResult>> SignInSilentlyCoreAsync(CancellationToken cancellationToken)
		{
			if (!TryBeginOperation())
			{
				return InProgress<SignInResult>();
			}

			try
			{
				if (_login == null)
				{
					return Result<SignInResult>.Fail(AccountResultCodes.NotSignedIn, "No saved login");
				}

				if (_signedIn)
				{
					return Result<SignInResult>.Ok(new SignInResult(_login.AccountId, false, _loginSaved));
				}

				var refreshed = await RefreshAsync(cancellationToken);

				if (_disposed)
				{
					return DisposedResult<SignInResult>();
				}

				if (!refreshed.IsSuccess)
				{
					return Result<SignInResult>.Fail(refreshed.Code, refreshed.Message);
				}

				return Result<SignInResult>.Ok(CompleteSignIn(new SignInResult(_login.AccountId, false, _loginSaved)));
			}
			finally
			{
				_operationRunning = false;
			}
		}

		private async UniTask<Result<string>> GetAccessTokenCoreAsync(CancellationToken cancellationToken)
		{
			for (var attempt = 0; attempt < MaxTokenAttempts; attempt++)
			{
				if (!_signedIn)
				{
					return Result<string>.Fail(AccountResultCodes.NotSignedIn, "Not signed in");
				}

				if (_accessToken.TryGet(_clock(), CurrentSessionId(), out var token))
				{
					return Result<string>.Ok(token);
				}

				var version = _loginVersion;
				var refreshed = await RefreshAsync(cancellationToken);

				if (_disposed)
				{
					return DisposedResult<string>();
				}

				if (!refreshed.IsSuccess)
				{
					return refreshed;
				}

				// 같은 재발급을 기다린 다른 호출이 결과를 받자마자 로그아웃·계정 전환을 했을 수 있다
				if (version != _loginVersion || !_signedIn)
				{
					return Result<string>.Fail(AccountResultCodes.NotSignedIn, "The login changed while refreshing");
				}

				// 검사한 토큰을 그대로 돌려준다. 같은 재발급을 기다린 앞 호출이 이미 새 세션으로 다시 받았으면 자기 결과가 아니라 그 토큰이다
				if (_accessToken.TryGet(_clock(), CurrentSessionId(), out var current))
				{
					return Result<string>.Ok(current);
				}

				// 재발급하는 동안 세션이 바뀌었다(백그라운드 시간 초과). 회전된 refresh 토큰은 저장했고, 새 세션의 토큰을 다시 받는다
			}

			return Result<string>.Fail(AccountResultCodes.SessionChanged, "The session kept changing while refreshing. Try again");
		}

		private async UniTask<Result> SignOutCoreAsync(CancellationToken cancellationToken)
		{
			if (!TryBeginOperation())
			{
				return Result.Fail(ResultCodes.InProgress, "Another account operation is in progress");
			}

			try
			{
				var login = _login;

				// 이 기기의 로그인을 먼저 지운다. 서버 폐기가 실패해도(오프라인) 로그아웃은 된다
				var cleared = EndLogin(SignOutReason.SignedOut);

				using (var linked = LinkLifetime(cancellationToken))
				{
					if (login != null)
					{
						var revoked = await _api.LogoutAsync(new LogoutRequest { RefreshToken = login.RefreshToken }, linked.Token);

						if (!revoked.Result.IsSuccess)
						{
							Logger.Info($"Server logout failed ({revoked.Result.Code}). The local login was removed");
						}
					}

					await SignOutProvidersAsync(_providers.Values, linked.Token);
				}

				return cleared
					? Result.Ok()
					: Result.Fail(AccountResultCodes.StorageFailed, "Signed out, but the saved login could not be removed from this device");
			}
			finally
			{
				_operationRunning = false;
			}
		}

		private async UniTask<Result<IReadOnlyList<LinkedPlatform>>> GetLinksCoreAsync(CancellationToken cancellationToken)
		{
			using (var linked = LinkLifetime(cancellationToken))
			{
				var token = linked.Token;
				var result = await CallAuthorizedAsync(accessToken => _api.GetLinksAsync(accessToken, token), token);
				return ToLinks(result);
			}
		}

		private async UniTask<Result<LinkResult>> LinkCoreAsync(IAuthProvider provider, CancellationToken cancellationToken)
		{
			if (!_signedIn)
			{
				return Result<LinkResult>.Fail(AccountResultCodes.NotSignedIn, "Not signed in");
			}

			if (!TryBeginOperation())
			{
				return InProgress<LinkResult>();
			}

			try
			{
				using (var linked = LinkLifetime(cancellationToken))
				{
					var token = linked.Token;
					var credential = await provider.SignInAsync(token);

					if (_disposed)
					{
						return DisposedResult<LinkResult>();
					}

					if (!credential.TryGetValue(out var value))
					{
						return Result<LinkResult>.Fail(credential.Code, credential.Message);
					}

					var request = new LinkRequest { Platform = value.Platform, Credential = value.Credential };
					var result = await CallAuthorizedAsync(accessToken => _api.LinkAsync(accessToken, request, false, token), token);

					if (result.Result.Code == AccountResultCodes.LoginConflict && !string.IsNullOrEmpty(result.Error?.ConflictAccountId))
					{
						return Result<LinkResult>.Ok(LinkResult.Conflict(result.Error.ConflictAccountId, value));
					}

					if (!result.Result.TryGetValue(out var links))
					{
						// 다음 시도에서 플랫폼 계정을 다시 고를 수 있게 SDK 에서 로그아웃한다
						await SignOutProvidersAsync(new[] { provider }, token);
						return Result<LinkResult>.Fail(result.Result.Code, result.Result.Message);
					}

					return Result<LinkResult>.Ok(LinkResult.Linked(ToLinkList(links)));
				}
			}
			finally
			{
				_operationRunning = false;
			}
		}

		private async UniTask<Result<IReadOnlyList<LinkedPlatform>>> TransferCoreAsync(PlatformCredential credential, CancellationToken cancellationToken)
		{
			if (!_signedIn)
			{
				return Result<IReadOnlyList<LinkedPlatform>>.Fail(AccountResultCodes.NotSignedIn, "Not signed in");
			}

			if (!TryBeginOperation())
			{
				return InProgress<IReadOnlyList<LinkedPlatform>>();
			}

			try
			{
				using (var linked = LinkLifetime(cancellationToken))
				{
					var token = linked.Token;
					var request = new LinkRequest { Platform = credential.Platform, Credential = credential.Credential };
					var result = await CallAuthorizedAsync(accessToken => _api.LinkAsync(accessToken, request, true, token), token);
					return ToLinks(result);
				}
			}
			finally
			{
				_operationRunning = false;
			}
		}

		private async UniTask<Result<IReadOnlyList<LinkedPlatform>>> UnlinkCoreAsync(string platform, CancellationToken cancellationToken)
		{
			if (!_signedIn)
			{
				return Result<IReadOnlyList<LinkedPlatform>>.Fail(AccountResultCodes.NotSignedIn, "Not signed in");
			}

			if (!TryBeginOperation())
			{
				return InProgress<IReadOnlyList<LinkedPlatform>>();
			}

			try
			{
				using (var linked = LinkLifetime(cancellationToken))
				{
					var token = linked.Token;
					var result = await CallAuthorizedAsync(accessToken => _api.UnlinkAsync(accessToken, platform, token), token);

					if (result.Result.IsSuccess && _providers.TryGetValue(platform, out var provider))
					{
						await SignOutProvidersAsync(new[] { provider }, token);
					}

					return ToLinks(result);
				}
			}
			finally
			{
				_operationRunning = false;
			}
		}

		private async UniTask<Result> DeleteAccountCoreAsync(CancellationToken cancellationToken)
		{
			if (!_signedIn)
			{
				return Result.Fail(AccountResultCodes.NotSignedIn, "Not signed in");
			}

			if (!TryBeginOperation())
			{
				return Result.Fail(ResultCodes.InProgress, "Another account operation is in progress");
			}

			try
			{
				using (var linked = LinkLifetime(cancellationToken))
				{
					var token = linked.Token;
					var result = await CallAuthorizedAsync(accessToken => _api.DeleteAccountAsync(accessToken, token), token);

					if (!result.Result.IsSuccess)
					{
						return Result.Fail(result.Result.Code, result.Result.Message);
					}

					// 서버 계정은 지워졌으므로 저장된 로그인을 지우지 못해도 다음 자동 로그인은 서버가 거절한다
					EndLogin(SignOutReason.AccountDeleted);
					await SignOutProvidersAsync(_providers.Values, token);
					return Result.Ok();
				}
			}
			finally
			{
				_operationRunning = false;
			}
		}

		private async UniTask<Result<SignInResult>> SignInCoreAsync(PlatformCredential credential, CancellationToken cancellationToken)
		{
			var sessionId = _session.NextSessionId;
			var request = new LoginRequest
			{
				Platform = credential.Platform,
				Credential = credential.Credential,
				SessionId = sessionId
			};
			var response = await _api.LoginAsync(request, cancellationToken);

			if (_disposed)
			{
				return DisposedResult<SignInResult>();
			}

			if (!response.Result.TryGetValue(out var tokens))
			{
				return Result<SignInResult>.Fail(response.Result.Code, response.Result.Message);
			}

			if (!IsValid(tokens))
			{
				return Result<SignInResult>.Fail(AccountResultCodes.InvalidResponse, "Login response is missing tokens");
			}

			var previous = _login;
			var saved = _tokenStore.Save(new SavedLogin { AccountId = tokens.AccountId, RefreshToken = tokens.RefreshToken });

			// 새 로그인을 저장하지 못했으면 이전 저장값을 지워, 다음 실행에서 이전 계정으로 자동 로그인되지 않게 한다.
			// 그것도 못 했는데 저장소에 다른 계정이 남아 있거나 무엇이 있는지 모르면 로그인을 거절한다
			// (지금 로그인은 그대로, 새로 받은 로그인은 서버에서 폐기). 메모리에 로그인이 없어도 읽지 못한 이전 값이 있을 수 있다
			if (!saved && !_tokenStore.Clear() && !_tokenStore.HoldsNothingBut(tokens.AccountId))
			{
				RevokeInBackground(tokens.RefreshToken);
				return Result<SignInResult>.Fail(AccountResultCodes.StorageFailed, "The new login could not be saved and the previous one could not be removed");
			}

			// 이전 로그인에서 시작한 요청이 늦게 끝나도 새 로그인에 적용되지 않게 한다
			_loginVersion++;
			SetLogin(tokens, sessionId, saved);

			if (previous != null && previous.RefreshToken != tokens.RefreshToken)
			{
				RevokeInBackground(previous.RefreshToken);
			}

			return Result<SignInResult>.Ok(CompleteSignIn(new SignInResult(tokens.AccountId, tokens.IsNewAccount, _loginSaved)));
		}

		private SignInResult CompleteSignIn(SignInResult result)
		{
			// 로그인 요청에 실은 NextSessionId 로 세션을 시작한다(이미 세션 중이면 끝내고 다시 시작)
			_session.Start();
			_signedIn = true;
			Logger.Info($"Signed in as {result.AccountId}{(result.IsNewAccount ? " (new account)" : string.Empty)}");

			foreach (var handler in SignedIn?.GetInvocationList() ?? Array.Empty<Delegate>())
			{
				try
				{
					((Action<SignInResult>) handler)(result);
				}
				catch (Exception exception)
				{
					Logger.Error(exception);
				}
			}

			return result;
		}

		private async UniTask<Result<string>> RefreshAsync(CancellationToken cancellationToken)
		{
			// 같은 로그인의 재발급 하나를 여러 대기자가 나눠 받는다(UniTask.Preserve 는 완료 전 다중 대기를 받지 않음).
			// 로그인이 바뀌었으면 이전 로그인의 재발급에 합류하지 않고 새로 시작한다
			var refresh = _refresh;

			if (refresh == null || _refreshVersion != _loginVersion)
			{
				refresh = new UniTaskCompletionSource<Result<string>>();
				_refresh = refresh;
				_refreshVersion = _loginVersion;
				RunRefreshAsync(refresh).Forget();
			}

			try
			{
				return await refresh.Task.AttachExternalCancellation(cancellationToken);
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				// 이 호출만 기다리기를 멈춘다. 함께 기다리는 다른 호출을 위해 재발급은 계속한다(요청 제한 시간이 끝을 보장)
				return Result<string>.Fail(ResultCodes.Cancelled);
			}
		}

		private async UniTaskVoid RunRefreshAsync(UniTaskCompletionSource<Result<string>> refresh)
		{
			Result<string> result;

			try
			{
				result = await RefreshCoreAsync();
			}
			catch (Exception exception)
			{
				ReleaseRefresh(refresh);
				refresh.TrySetException(exception);
				return;
			}

			ReleaseRefresh(refresh);
			refresh.TrySetResult(result);
		}

		private void ReleaseRefresh(UniTaskCompletionSource<Result<string>> refresh)
		{
			// 로그인이 바뀌어 새 재발급이 시작됐으면 그것은 지우지 않는다
			if (ReferenceEquals(_refresh, refresh))
			{
				_refresh = null;
			}
		}

		private async UniTask<Result<string>> RefreshCoreAsync()
		{
			var login = _login;

			if (login == null)
			{
				return Result<string>.Fail(AccountResultCodes.NotSignedIn, "Not signed in");
			}

			var version = _loginVersion;
			var sessionId = _session.IsActive ? _session.SessionId : _session.NextSessionId;
			var request = new RefreshRequest { RefreshToken = login.RefreshToken, SessionId = sessionId };
			var response = await _api.RefreshAsync(request, _lifetime.Token);

			if (_disposed)
			{
				return DisposedResult<string>();
			}

			if (version != _loginVersion)
			{
				return Result<string>.Fail(AccountResultCodes.NotSignedIn, "The login changed while refreshing");
			}

			if (!response.Result.TryGetValue(out var tokens))
			{
				var code = response.Result.Code;

				if (code == AccountResultCodes.InvalidRefreshToken || code == AccountResultCodes.RefreshTokenReused)
				{
					Logger.Info($"The server rejected the login ({code}). Sign in again");
					EndLogin(SignOutReason.LoginExpired);
				}

				return Result<string>.Fail(code, response.Result.Message);
			}

			if (!IsValid(tokens))
			{
				return Result<string>.Fail(AccountResultCodes.InvalidResponse, "Refresh response is missing tokens");
			}

			ApplyTokens(tokens, sessionId);
			return Result<string>.Ok(tokens.AccessToken);
		}

		private async UniTask<AccountApiResult<T>> CallAuthorizedAsync<T>(Func<string, UniTask<AccountApiResult<T>>> call, CancellationToken cancellationToken)
		{
			var version = _loginVersion;

			for (var attempt = 0;; attempt++)
			{
				var token = await GetAccessTokenCoreAsync(cancellationToken);

				if (!token.TryGetValue(out var accessToken))
				{
					return AccountApiResult<T>.Fail(token.Code, token.Message);
				}

				if (_disposed || version != _loginVersion)
				{
					return LoginChanged<T>();
				}

				var result = await call(accessToken);

				// 그사이 로그인이 바뀌었으면(다른 계정으로 로그인, 로그아웃) 응답을 지금 로그인에 적용하지 않는다
				if (_disposed || version != _loginVersion)
				{
					return LoginChanged<T>();
				}

				var code = result.Result.Code;

				if (code == AccountResultCodes.InvalidAccessToken && attempt == 0)
				{
					// 만료 직전이나 서명 키 교체로 거절됨. 재발급해 한 번만 다시 보낸다
					InvalidateAccessToken(accessToken);
					continue;
				}

				if (code == AccountResultCodes.AccountNotFound && _signedIn)
				{
					// 다른 기기에서 삭제된 계정
					EndLogin(SignOutReason.AccountDeleted);
				}

				return result;
			}
		}

		/// <summary>재발급한 토큰을 저장하고 적용합니다. 저장하지 못해도 이번 실행에서는 그대로 씁니다.</summary>
		private void ApplyTokens(TokenResponse tokens, string sessionId)
		{
			var saved = _tokenStore.Save(new SavedLogin { AccountId = tokens.AccountId, RefreshToken = tokens.RefreshToken });
			SetLogin(tokens, sessionId, saved);
		}

		private void SetLogin(TokenResponse tokens, string sessionId, bool saved)
		{
			_login = new SavedLogin { AccountId = tokens.AccountId, RefreshToken = tokens.RefreshToken };
			_loginSaved = saved;
			_accessToken.Set(tokens.AccessToken, tokens.AccessTokenExpiresIn, sessionId, _clock());
		}

		/// <summary>로그인을 끝내고 저장된 로그인을 지웁니다. 지우지 못했으면 false 입니다.</summary>
		private bool EndLogin(SignOutReason reason)
		{
			var wasSignedIn = _signedIn;

			_loginVersion++;
			_login = null;
			_signedIn = false;
			_accessToken.Clear();

			// 처음에 읽지 못했던 저장값도 남지 않게 항상 지운다
			var cleared = _tokenStore.Clear();

			if (wasSignedIn)
			{
				_session.End();
				Logger.Info($"Signed out ({reason})");

				foreach (var handler in SignedOut?.GetInvocationList() ?? Array.Empty<Delegate>())
				{
					try
					{
						((Action<SignOutReason>) handler)(reason);
					}
					catch (Exception exception)
					{
						Logger.Error(exception);
					}
				}
			}

			return cleared;
		}

		private void RevokeInBackground(string refreshToken)
		{
			// 계정을 바꿀 때 이전 로그인을 서버에서 폐기한다. 실패해도 이전 토큰은 이 기기에 남지 않는다
			_api.LogoutAsync(new LogoutRequest { RefreshToken = refreshToken }, _lifetime.Token).Forget();
		}

		private static async UniTask SignOutProvidersAsync(IEnumerable<IAuthProvider> providers, CancellationToken cancellationToken)
		{
			foreach (var provider in providers)
			{
				try
				{
					await provider.SignOutAsync(cancellationToken);
				}
				catch (Exception exception)
				{
					// 플랫폼 SDK 로그아웃은 부가 작업이라 실패해도 계정 작업 결과를 바꾸지 않는다
					Logger.Error(exception, $"{provider.Platform} sign-out failed");
				}
			}
		}

		private string CurrentSessionId()
		{
			return _session.IsActive ? _session.SessionId : null;
		}

		private CancellationTokenSource LinkLifetime(CancellationToken cancellationToken)
		{
			return CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
		}

		private IAuthProvider FindProvider(string platform)
		{
			ThrowIfDisposed();

			if (platform == null || !_providers.TryGetValue(platform, out var provider))
			{
				throw new ArgumentException($"No auth provider is registered for platform '{platform}'", nameof(platform));
			}

			return provider;
		}

		private bool TryBeginOperation()
		{
			if (_operationRunning)
			{
				return false;
			}

			_operationRunning = true;
			return true;
		}

		private void ThrowIfDisposed()
		{
			if (_disposed)
			{
				throw new ObjectDisposedException(nameof(AuthService));
			}
		}

		private static Result<T> InProgress<T>()
		{
			return Result<T>.Fail(ResultCodes.InProgress, "Another account operation is in progress");
		}

		private static Result<T> DisposedResult<T>()
		{
			return Result<T>.Fail(ResultCodes.Cancelled, "AuthService was disposed");
		}

		private AccountApiResult<T> LoginChanged<T>()
		{
			return _disposed
				? AccountApiResult<T>.Fail(ResultCodes.Cancelled, "AuthService was disposed")
				: AccountApiResult<T>.Fail(AccountResultCodes.NotSignedIn, "The login changed during the request");
		}

		private static bool IsValid(TokenResponse tokens)
		{
			return tokens != null && !string.IsNullOrEmpty(tokens.AccountId) && !string.IsNullOrEmpty(tokens.AccessToken) &&
				!string.IsNullOrEmpty(tokens.RefreshToken) && tokens.AccessTokenExpiresIn > 0;
		}

		private static Result<IReadOnlyList<LinkedPlatform>> ToLinks(AccountApiResult<LinksResponse> result)
		{
			return result.Result.TryGetValue(out var links)
				? Result<IReadOnlyList<LinkedPlatform>>.Ok(ToLinkList(links))
				: Result<IReadOnlyList<LinkedPlatform>>.Fail(result.Result.Code, result.Result.Message);
		}

		private static IReadOnlyList<LinkedPlatform> ToLinkList(LinksResponse response)
		{
			return (response.Links ?? new List<LinkedPlatformDto>())
				.Where(link => link != null)
				.Select(link => new LinkedPlatform(link.Platform, DateTimeOffset.FromUnixTimeMilliseconds(link.LinkedAt)))
				.ToArray();
		}
	}
}
