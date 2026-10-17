namespace Geuneda.Account.Server
{
	/// <summary>1회용 값의 용도입니다. 저장소는 용도와 해시를 함께 키로 써서 한 용도의 값을 다른 용도로 쓰지 못하게 합니다.</summary>
	public enum OneTimeValueKind
	{
		/// <summary>애플·구글 로그인 전에 받는 nonce(<c>POST nonce</c>). id 토큰이 이 값에 묶입니다.</summary>
		Nonce,
		/// <summary>연동 충돌 응답의 티켓. 그 플랫폼 계정으로 로그인하거나 연동을 요청한 계정으로 옮겨올 때 id 토큰 대신 씁니다.</summary>
		ConflictTicket
	}
}
