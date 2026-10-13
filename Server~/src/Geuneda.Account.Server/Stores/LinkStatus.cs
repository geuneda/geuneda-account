namespace Geuneda.Account.Server
{
	/// <summary>연동과 옮겨오기의 결과입니다.</summary>
	public enum LinkStatus
	{
		/// <summary>연동했습니다. 이미 이 계정에 연동되어 있던 경우도 포함합니다.</summary>
		Linked,
		AccountNotFound,
		/// <summary>이 계정에 같은 플랫폼의 다른 로그인 수단이 이미 있습니다.</summary>
		PlatformAlreadyLinked,
		/// <summary>그 로그인 수단이 다른 계정에 연동되어 있습니다(연동만 해당, 옮겨오기에서는 옮김).</summary>
		LinkedToOtherAccount
	}
}
