// ReSharper disable once CheckNamespace

namespace Geuneda.Account
{
	/// <summary>로그인 상태가 끝난 이유입니다.</summary>
	public enum SignOutReason
	{
		/// <summary><see cref="IAuthService.SignOutAsync"/> 를 불렀습니다.</summary>
		SignedOut,
		/// <summary>서버가 로그인을 더 받아 주지 않습니다(refresh 토큰 만료·폐기·재사용 감지). 다시 로그인해야 합니다.</summary>
		LoginExpired,
		/// <summary>계정이 삭제되었습니다(이 기기에서 삭제했거나 서버에 계정이 없음).</summary>
		AccountDeleted
	}
}
