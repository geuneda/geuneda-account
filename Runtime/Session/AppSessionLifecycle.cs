using UnityEngine;

// ReSharper disable once CheckNamespace

namespace Geuneda.Account
{
	/// <summary><see cref="AppSession.BindApplicationLifecycle"/> 가 만드는 숨은 컴포넌트입니다. 앱 일시정지·종료를 세션에 넘깁니다.</summary>
	[AddComponentMenu("")]
	internal sealed class AppSessionLifecycle : MonoBehaviour
	{
		private AppSession _session;

		public void Bind(AppSession session)
		{
			_session = session;
		}

		private void OnApplicationPause(bool paused)
		{
			_session?.HandlePause(paused);
		}

		private void OnApplicationQuit()
		{
			_session?.HandleQuit();
		}
	}
}
