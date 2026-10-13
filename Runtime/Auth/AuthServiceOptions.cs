using System;
using System.Collections.Generic;
using Geuneda.Core;
using Geuneda.Http;

// ReSharper disable once CheckNamespace

namespace Geuneda.Account
{
	/// <summary><see cref="AuthService"/> 설정입니다.</summary>
	public sealed class AuthServiceOptions
	{
		/// <summary>
		/// 인증 서버 클라이언트입니다. 필수입니다. 기본 주소는 서버 접두사까지 넣고 끝에 <c>/</c> 를 붙입니다(예: <c>https://auth.example.com/auth/</c>).
		/// 이 클라이언트에는 <see cref="AuthTokenMiddleware"/> 를 넣지 않습니다.
		/// </summary>
		public IHttpClient AuthClient { get; set; }

		/// <summary>로그인(refresh 토큰)을 둘 보안 저장소입니다. 필수입니다. 보통 core 의 <c>PlatformSecureStore</c> 입니다.</summary>
		public ISecureStore SecureStore { get; set; }

		/// <summary>로그인하면 시작하고 로그아웃하면 끝낼 세션입니다. 필수입니다.</summary>
		public IAppSession Session { get; set; }

		/// <summary>쓸 플랫폼 제공자입니다. 플랫폼 이름은 서로 달라야 합니다.</summary>
		public IList<IAuthProvider> Providers { get; } = new List<IAuthProvider>();

		/// <summary>access 토큰의 남은 수명이 이보다 짧으면 쓰기 전에 재발급합니다(수명의 절반을 넘지 않음). 기본 1분입니다.</summary>
		public TimeSpan AccessTokenRefreshMargin { get; set; } = TimeSpan.FromMinutes(1);

		/// <summary>인증 서버 요청 하나의 제한 시간입니다. 넘으면 <c>ResultCodes.Timeout</c> 입니다. 기본 15초입니다.</summary>
		public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(15);
	}
}
