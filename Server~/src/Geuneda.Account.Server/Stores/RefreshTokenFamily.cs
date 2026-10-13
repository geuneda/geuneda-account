using System;

namespace Geuneda.Account.Server
{
	/// <summary>
	/// 로그인 하나에서 재발급으로 이어지는 refresh 토큰의 상태입니다. 쓸 수 있는 토큰은 <see cref="CurrentTokenHash"/> 하나이고,
	/// <see cref="PreviousTokenHash"/> 는 응답을 잃은 클라이언트의 재시도로 <see cref="RotatedAt"/> 부터 유예 시간 동안만 받습니다.
	/// </summary>
	public sealed record RefreshTokenFamily
	{
		public string FamilyId { get; init; }
		public string AccountId { get; init; }

		/// <summary>재발급 요청에 세션 ID 가 없으면 쓰는 값입니다. 없을 수 있습니다.</summary>
		public string SessionId { get; init; }

		/// <summary>지금 쓸 수 있는 토큰의 해시</summary>
		public string CurrentTokenHash { get; init; }

		/// <summary>바로 전에 쓴 토큰의 해시. 아직 재발급하지 않았으면 null 입니다.</summary>
		public string PreviousTokenHash { get; init; }

		/// <summary>직전 토큰을 처음 재발급에 쓴 시각입니다. 유예 안의 재시도는 이 값을 바꾸지 않습니다.</summary>
		public DateTimeOffset? RotatedAt { get; init; }

		public DateTimeOffset CreatedAt { get; init; }

		/// <summary>폐기된 시각입니다. 폐기된 로그인의 토큰은 모두 쓸 수 없습니다.</summary>
		public DateTimeOffset? RevokedAt { get; init; }
	}
}
