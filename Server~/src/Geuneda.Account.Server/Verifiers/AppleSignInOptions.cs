using System;
using System.Collections.Generic;

namespace Geuneda.Account.Server
{
	/// <summary><see cref="AppleIdTokenVerifier"/> 설정입니다.</summary>
	public sealed class AppleSignInOptions
	{
		/// <summary>받을 id 토큰의 대상입니다. iOS 앱 번들 ID 를 넣습니다(웹 로그인도 받으면 Services ID 도). 하나 이상 필요합니다.</summary>
		public IList<string> Audiences { get; } = new List<string>();

		/// <summary>허용하는 시계 오차입니다. 기본 5분입니다.</summary>
		public TimeSpan ClockSkew { get; set; } = TimeSpan.FromMinutes(5);
	}
}
