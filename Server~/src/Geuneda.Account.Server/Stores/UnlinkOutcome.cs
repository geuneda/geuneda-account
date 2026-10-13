namespace Geuneda.Account.Server
{
	/// <summary>연동 해제 결과입니다.</summary>
	public enum UnlinkOutcome
	{
		Unlinked,
		AccountNotFound,
		/// <summary>이 계정에 그 플랫폼이 연동되어 있지 않습니다.</summary>
		NotLinked,
		/// <summary>마지막 로그인 수단이라 해제하지 않았습니다.</summary>
		LastLogin
	}
}
