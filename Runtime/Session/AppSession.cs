using System;
using Geuneda.Core;
using UnityEngine;
using Random = System.Random;

// ReSharper disable once CheckNamespace

namespace Geuneda.Account
{
	/// <summary>
	/// 기본 앱 세션입니다. 부트스트랩에서 만들고 <see cref="BindApplicationLifecycle"/> 로 앱 일시정지·종료에 연결합니다.
	/// </summary>
	/// <remarks>
	/// 앱이 일시정지(백그라운드)된 시각을 기기 시계로 기록하고, 돌아왔을 때 <see cref="BackgroundTimeout"/> 이상 지났으면
	/// 이전 세션을 <see cref="SessionEndReason.BackgroundTimeout"/> 으로 끝내고 새 세션을 시작합니다. 시계가 뒤로 가면 0 으로 봅니다.
	/// 이벤트 처리기의 예외는 로그만 남기고 다음 처리기로 넘어갑니다.
	/// </remarks>
	public sealed class AppSession : IAppSession, IDisposable
	{
		public static readonly TimeSpan DefaultBackgroundTimeout = TimeSpan.FromMinutes(10);

		private static readonly ModuleLogger Logger = new ModuleLogger("Geuneda.Account.Session");

		private readonly Func<DateTime> _utcNow;
		private readonly Random _random;
		private DateTime? _pausedAtUtc;
		private GameObject _lifecycleObject;

		public TimeSpan BackgroundTimeout { get; }
		public bool IsActive { get; private set; }
		public string SessionId => IsActive ? Current.SessionId : null;
		public string NextSessionId { get; private set; }
		public SessionInfo Current { get; private set; }

		public event Action<SessionInfo> Started;
		public event Action<SessionInfo> Paused;
		public event Action<SessionInfo> Resumed;
		public event Action<SessionInfo, SessionEndReason> Ended;

		public AppSession() : this(DefaultBackgroundTimeout)
		{
		}

		/// <exception cref="ArgumentOutOfRangeException"><paramref name="backgroundTimeout"/> 이 0 이하인 경우</exception>
		public AppSession(TimeSpan backgroundTimeout) : this(backgroundTimeout, () => DateTime.UtcNow, new Random())
		{
		}

		internal AppSession(TimeSpan backgroundTimeout, Func<DateTime> utcNow, Random random)
		{
			if (backgroundTimeout <= TimeSpan.Zero)
			{
				throw new ArgumentOutOfRangeException(nameof(backgroundTimeout), backgroundTimeout, "Background timeout must be positive");
			}

			BackgroundTimeout = backgroundTimeout;
			_utcNow = utcNow ?? throw new ArgumentNullException(nameof(utcNow));
			_random = random ?? throw new ArgumentNullException(nameof(random));
			NextSessionId = NewId();
		}

		/// <summary>
		/// 숨은 GameObject(DontDestroyOnLoad)를 만들어 앱 일시정지·재개·종료를 이 세션에 알립니다. 메인 스레드에서 한 번 부릅니다(다시 부르면 무시).
		/// </summary>
		public void BindApplicationLifecycle()
		{
			if (_lifecycleObject != null)
			{
				return;
			}

			_lifecycleObject = new GameObject(nameof(AppSessionLifecycle)) { hideFlags = HideFlags.HideAndDontSave };
			UnityEngine.Object.DontDestroyOnLoad(_lifecycleObject);
			_lifecycleObject.AddComponent<AppSessionLifecycle>().Bind(this);
		}

		public SessionInfo Start()
		{
			if (IsActive)
			{
				EndCore(SessionEndReason.Restarted);
			}

			Current = new SessionInfo(NextSessionId, _utcNow());
			NextSessionId = NewId();
			IsActive = true;
			_pausedAtUtc = null;
			Logger.Info($"Session {Current.SessionId} started");
			Raise(Started, Current);
			return Current;
		}

		public void End()
		{
			if (IsActive)
			{
				EndCore(SessionEndReason.Ended);
			}
		}

		/// <summary>숨은 GameObject 를 지웁니다. 세션은 끝내지 않습니다.</summary>
		public void Dispose()
		{
			if (_lifecycleObject != null)
			{
				UnityEngine.Object.Destroy(_lifecycleObject);
				_lifecycleObject = null;
			}
		}

		internal void HandlePause(bool paused)
		{
			if (!IsActive)
			{
				return;
			}

			if (paused)
			{
				if (_pausedAtUtc == null)
				{
					_pausedAtUtc = _utcNow();
					Raise(Paused, Current);
				}

				return;
			}

			if (_pausedAtUtc == null)
			{
				return;
			}

			var elapsed = _utcNow() - _pausedAtUtc.Value;
			_pausedAtUtc = null;

			if (elapsed >= BackgroundTimeout)
			{
				EndCore(SessionEndReason.BackgroundTimeout);
				Start();
				return;
			}

			Raise(Resumed, Current);
		}

		internal void HandleQuit()
		{
			if (IsActive)
			{
				EndCore(SessionEndReason.ApplicationQuit);
			}
		}

		private void EndCore(SessionEndReason reason)
		{
			var ended = Current;
			IsActive = false;
			_pausedAtUtc = null;
			Current = default;
			Logger.Info($"Session {ended.SessionId} ended ({reason})");

			foreach (var handler in Ended?.GetInvocationList() ?? Array.Empty<Delegate>())
			{
				try
				{
					((Action<SessionInfo, SessionEndReason>) handler)(ended, reason);
				}
				catch (Exception exception)
				{
					Logger.Error(exception);
				}
			}
		}

		private string NewId()
		{
			return SessionIds.Create(_utcNow(), _random.Next());
		}

		private static void Raise(Action<SessionInfo> handlers, SessionInfo session)
		{
			foreach (var handler in handlers?.GetInvocationList() ?? Array.Empty<Delegate>())
			{
				try
				{
					((Action<SessionInfo>) handler)(session);
				}
				catch (Exception exception)
				{
					Logger.Error(exception);
				}
			}
		}
	}
}
