using System;

namespace Geuneda.Account.Server
{
	/// <summary>
	/// 발급한 refresh 토큰 하나의 기록입니다. 토큰 원문은 저장하지 않고 SHA-256 해시(<see cref="TokenHash"/>)만 둡니다.
	/// 쓴 토큰도 지우지 않고 남겨, 나중에 다시 오면 어느 로그인의 토큰인지 찾아 재사용으로 처리합니다.
	/// </summary>
	public sealed record RefreshTokenRecord
	{
		public string TokenHash { get; init; }

		/// <summary>이 토큰이 속한 로그인(<see cref="RefreshTokenFamily"/>)</summary>
		public string FamilyId { get; init; }

		public DateTimeOffset IssuedAt { get; init; }
		public DateTimeOffset ExpiresAt { get; init; }
	}
}
