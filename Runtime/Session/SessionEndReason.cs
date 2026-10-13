// ReSharper disable once CheckNamespace

namespace Geuneda.Account
{
	/// <summary>세션이 끝난 이유입니다.</summary>
	public enum SessionEndReason
	{
		/// <summary><see cref="IAppSession.End"/> 를 불렀습니다(로그아웃, 계정 삭제 등).</summary>
		Ended,
		/// <summary>백그라운드에 오래 있었습니다. 바로 새 세션이 시작됩니다.</summary>
		BackgroundTimeout,
		/// <summary>세션 중에 <see cref="IAppSession.Start"/> 를 다시 불렀습니다(다른 계정으로 로그인 등). 바로 새 세션이 시작됩니다.</summary>
		Restarted,
		/// <summary>앱이 종료됩니다.</summary>
		ApplicationQuit
	}
}
