using System;
using System.Collections.Generic;

namespace Geuneda.Account.Server
{
	/// <summary><see cref="GoogleIdTokenVerifier"/> 설정입니다.</summary>
	public sealed class GoogleSignInOptions
	{
		/// <summary>
		/// 받을 id 토큰의 대상(OAuth 클라이언트 ID, <c>....apps.googleusercontent.com</c>)입니다. Android 용 서버(웹) 클라이언트 ID 와 iOS 클라이언트 ID 를 넣습니다. 하나 이상 필요합니다.
		/// </summary>
		public IList<string> ClientIds { get; } = new List<string>();

		/// <summary>허용하는 시계 오차입니다. 기본 5분입니다.</summary>
		public TimeSpan ClockSkew { get; set; } = TimeSpan.FromMinutes(5);
	}
}
