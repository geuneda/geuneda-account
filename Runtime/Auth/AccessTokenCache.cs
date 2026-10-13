using System;

// ReSharper disable once CheckNamespace

namespace Geuneda.Account
{
	/// <summary>
	/// 메모리의 access 토큰과, 다시 받아야 하는 시점(단조 시계), 발급 요청에 실은 세션 ID 입니다.
	/// </summary>
	internal sealed class AccessTokenCache
	{
		private readonly double _marginSeconds;
		private string _sessionId;
		private double _refreshAt;

		public AccessTokenCache(TimeSpan margin)
		{
			_marginSeconds = margin.TotalSeconds;
		}

		public string Token { get; private set; }

		/// <summary>남은 수명이 여유(최대 수명의 절반)보다 짧아지면 다시 받게 기록합니다.</summary>
		public void Set(string token, int lifetimeSeconds, string sessionId, double now)
		{
			Token = token;
			_sessionId = sessionId;
			_refreshAt = now + lifetimeSeconds - Math.Min(_marginSeconds, lifetimeSeconds / 2.0);
		}

		/// <summary>
		/// 아직 쓸 수 있는 토큰을 돌려줍니다. 진행 중인 세션이 발급 때의 세션과 다르면(백그라운드 시간 초과로 새 세션) 쓰지 않습니다.
		/// </summary>
		/// <param name="currentSessionId">진행 중인 세션 ID. 세션이 없으면 null 이고 세션을 비교하지 않습니다.</param>
		public bool TryGet(double now, string currentSessionId, out string token)
		{
			token = Token;
			return token != null && now < _refreshAt && MatchesSession(currentSessionId);
		}

		public bool MatchesSession(string currentSessionId)
		{
			return currentSessionId == null || currentSessionId == _sessionId;
		}

		/// <summary>지금 토큰이면 다음에 다시 받게 합니다.</summary>
		public void Invalidate(string token)
		{
			if (token != null && token == Token)
			{
				_refreshAt = 0;
			}
		}

		public void Clear()
		{
			Token = null;
			_sessionId = null;
			_refreshAt = 0;
		}
	}
}
