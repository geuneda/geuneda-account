using System;

// ReSharper disable once CheckNamespace

namespace Geuneda.Account
{
	/// <summary>세션 하나의 정보입니다.</summary>
	public readonly struct SessionInfo
	{
		public string SessionId { get; }

		/// <summary>시작한 시각(UTC, 기기 시계)</summary>
		public DateTime StartedAtUtc { get; }

		public SessionInfo(string sessionId, DateTime startedAtUtc)
		{
			SessionId = sessionId;
			StartedAtUtc = startedAtUtc;
		}

		public override string ToString()
		{
			return $"Session({SessionId})";
		}
	}
}
