using System;

// ReSharper disable once CheckNamespace

namespace Geuneda.Account
{
	/// <summary>
	/// 앱 세션입니다. 로그인하면 시작하고, 백그라운드에 오래 있다 돌아오면 새로 시작합니다. 기본 구현은 <see cref="AppSession"/> 이고,
	/// services 의 <c>MainInstaller</c> 에 이 인터페이스로 바인딩합니다. 메인 스레드에서 씁니다.
	/// </summary>
	/// <remarks>
	/// 다음 세션 ID(<see cref="NextSessionId"/>)를 미리 만들어 두므로, 로그인 요청에 실어 보내면 서버 로그와 클라이언트 로그의 세션 ID 가 첫 요청부터 같습니다.
	/// 이벤트는 대칭입니다. 새 세션이 시작되기 전에는 항상 이전 세션의 <see cref="Ended"/> 가 먼저 옵니다.
	/// </remarks>
	public interface IAppSession
	{
		bool IsActive { get; }

		/// <summary>진행 중인 세션의 ID. 세션이 없으면 null 입니다.</summary>
		string SessionId { get; }

		/// <summary>다음에 시작할 세션의 ID. <see cref="Start"/> 하면 이 ID 로 시작하고 새 값을 예약합니다.</summary>
		string NextSessionId { get; }

		/// <summary>진행 중인 세션. 세션이 없으면 default 입니다.</summary>
		SessionInfo Current { get; }

		event Action<SessionInfo> Started;
		event Action<SessionInfo> Paused;
		event Action<SessionInfo> Resumed;
		event Action<SessionInfo, SessionEndReason> Ended;

		/// <summary><see cref="NextSessionId"/> 로 세션을 시작합니다. 이미 세션 중이면 먼저 <see cref="SessionEndReason.Restarted"/> 로 끝냅니다.</summary>
		SessionInfo Start();

		/// <summary>세션을 끝냅니다(<see cref="SessionEndReason.Ended"/>). 세션이 없으면 아무것도 하지 않습니다.</summary>
		void End();
	}
}
