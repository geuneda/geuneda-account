using System;
using System.Collections.Generic;

// ReSharper disable once CheckNamespace

namespace Geuneda.Account
{
	/// <summary>
	/// 연동 결과입니다. 그 플랫폼 계정이 다른 계정에 연동되어 있으면(<see cref="IsConflict"/>) 실패가 아니라 이 결과로 옵니다.
	/// 게임은 사용자에게 고르게 하고, <see cref="Credential"/> 로 그 계정에 로그인하거나(<see cref="IAuthService.SignInAsync(PlatformCredential, System.Threading.CancellationToken)"/>)
	/// 지금 계정으로 옮겨옵니다(<see cref="IAuthService.TransferAsync"/>). 플랫폼 로그인을 다시 하지 않아도 됩니다.
	/// </summary>
	public sealed class LinkResult
	{
		public bool IsConflict => ConflictAccountId != null;

		/// <summary>연동했을 때의 연동 목록. 충돌이면 null 입니다.</summary>
		public IReadOnlyList<LinkedPlatform> Links { get; }

		/// <summary>충돌일 때 그 플랫폼 계정이 연동된 계정의 ID 입니다.</summary>
		public string ConflictAccountId { get; }

		/// <summary>
		/// 충돌일 때 다시 쓸 자격입니다. 안에 서버가 준 1회용 충돌 티켓이 있어, 로그인·옮겨오기 요청은 id 토큰 대신 티켓을 보냅니다.
		/// 티켓은 한 번만(충돌한 계정으로 로그인하거나 연동을 요청한 이 계정으로 옮겨오기 중 하나), 발급부터 5분(서버 <c>NonceLifetime</c>) 안에 씁니다.
		/// 지났거나 이미 썼으면 <see cref="AccountResultCodes.InvalidConflictTicket"/> 이니 연동부터 다시 합니다.
		/// </summary>
		public PlatformCredential Credential { get; }

		private LinkResult(IReadOnlyList<LinkedPlatform> links, string conflictAccountId, PlatformCredential credential)
		{
			Links = links;
			ConflictAccountId = conflictAccountId;
			Credential = credential;
		}

		/// <summary>연동한 결과입니다. 다른 <see cref="IAuthService"/> 구현이나 테스트 대역에서도 씁니다.</summary>
		/// <exception cref="ArgumentNullException"><paramref name="links"/> 가 null 인 경우</exception>
		public static LinkResult Linked(IReadOnlyList<LinkedPlatform> links)
		{
			return new LinkResult(links ?? throw new ArgumentNullException(nameof(links)), null, null);
		}

		/// <summary>그 플랫폼 계정이 다른 계정에 연동되어 있는 결과입니다.</summary>
		/// <exception cref="ArgumentException"><paramref name="conflictAccountId"/> 가 비어 있는 경우</exception>
		/// <exception cref="ArgumentNullException"><paramref name="credential"/> 이 null 인 경우</exception>
		public static LinkResult Conflict(string conflictAccountId, PlatformCredential credential)
		{
			if (string.IsNullOrEmpty(conflictAccountId))
			{
				throw new ArgumentException("Conflict account id must not be empty", nameof(conflictAccountId));
			}

			return new LinkResult(null, conflictAccountId, credential ?? throw new ArgumentNullException(nameof(credential)));
		}
	}
}
