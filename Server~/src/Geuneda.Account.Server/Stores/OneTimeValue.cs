using System;

namespace Geuneda.Account.Server
{
	/// <summary>발급한 1회용 값(nonce, 충돌 티켓) 하나의 기록입니다. 원문은 저장하지 않고 SHA-256 해시(<see cref="Hash"/>)만 둡니다.</summary>
	public sealed record OneTimeValue
	{
		public OneTimeValueKind Kind { get; init; }

		/// <summary>원문의 SHA-256(16진 소문자). nonce 면 애플 id 토큰의 <c>nonce</c> 클레임과 같은 형식입니다.</summary>
		public string Hash { get; init; }

		/// <summary>이 시각부터는 쓸 수 없습니다.</summary>
		public DateTimeOffset ExpiresAt { get; init; }

		/// <summary>충돌 티켓이 가리키는 플랫폼. nonce 는 null 입니다.</summary>
		public string Platform { get; init; }

		/// <summary>충돌 티켓이 가리키는 플랫폼 안의 고유 ID(검증한 id 토큰의 <c>sub</c> 등). nonce 는 null 입니다.</summary>
		public string Subject { get; init; }

		/// <summary>충돌 티켓을 받은 계정(연동을 요청한 계정). 옮겨오기는 이 계정에서만 받습니다. nonce 는 null 입니다.</summary>
		public string AccountId { get; init; }
	}
}
