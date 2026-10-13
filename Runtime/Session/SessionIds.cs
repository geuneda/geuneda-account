using System;

// ReSharper disable once CheckNamespace

namespace Geuneda.Account
{
	/// <summary>
	/// 세션 ID 입니다. 2024-01-01 UTC 부터의 밀리초를 23비트 왼쪽으로 밀고 23비트 난수를 더한 64비트 값의 16자리 16진수라, 만든 순서로 정렬됩니다.
	/// </summary>
	internal static class SessionIds
	{
		private const int RandomBits = 23;
		private const int RandomMask = (1 << RandomBits) - 1;
		private static readonly DateTime Epoch = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);

		public static string Create(DateTime utcNow, int random)
		{
			var milliseconds = Math.Max(0L, (long) (utcNow - Epoch).TotalMilliseconds);
			return ((milliseconds << RandomBits) | (uint) (random & RandomMask)).ToString("x16");
		}
	}
}
