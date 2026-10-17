// ReSharper disable once CheckNamespace

namespace Geuneda.Account
{
	/// <summary>
	/// account 패키지의 결과 코드입니다(core 결과 코드 범위 표의 3000 블록). 서버(<c>Server~</c>)와 함께 컴파일합니다.
	/// </summary>
	/// <remarks>
	/// 3001~3099 는 인증 서버가 오류 응답 본문의 <c>code</c> 로 돌려주는 코드이고, 클라이언트는 그대로 결과 코드로 씁니다.
	/// 3100 부터는 클라이언트에서만 생기는 코드입니다. 연결 실패, 타임아웃, 취소는 core 공용 코드를 씁니다.
	/// </remarks>
	public static class AccountResultCodes
	{
		/// <summary>이 패키지 코드 블록의 시작입니다. 코드로는 쓰지 않습니다.</summary>
		public const int RangeStart = 3000;

		/// <summary>플랫폼 자격(게스트 비밀 값, 애플·구글 id 토큰)을 검증하지 못했습니다. HTTP 401.</summary>
		public const int InvalidCredential = RangeStart + 1;
		/// <summary>서버에 검증기가 없는 플랫폼입니다. HTTP 400.</summary>
		public const int UnsupportedPlatform = RangeStart + 2;
		/// <summary>refresh 토큰이 없거나, 만료되었거나, 폐기되었습니다. 다시 로그인해야 합니다. HTTP 401.</summary>
		public const int InvalidRefreshToken = RangeStart + 3;
		/// <summary>이미 쓴 refresh 토큰을 다시 썼습니다. 그 로그인의 토큰을 모두 폐기했으므로 다시 로그인해야 합니다. HTTP 401.</summary>
		public const int RefreshTokenReused = RangeStart + 4;
		/// <summary>access 토큰이 없거나 검증하지 못했습니다. HTTP 401.</summary>
		public const int InvalidAccessToken = RangeStart + 5;
		/// <summary>계정이 없습니다(탈퇴 등). HTTP 404.</summary>
		public const int AccountNotFound = RangeStart + 6;
		/// <summary>그 플랫폼 계정은 다른 계정에 연동되어 있습니다. 응답에 그 계정 ID 와 충돌 티켓이 옵니다. HTTP 409.</summary>
		public const int LoginConflict = RangeStart + 7;
		/// <summary>이 계정에 같은 플랫폼이 이미 연동되어 있습니다. HTTP 409.</summary>
		public const int PlatformAlreadyLinked = RangeStart + 8;
		/// <summary>마지막 로그인 수단은 해제할 수 없습니다. HTTP 409.</summary>
		public const int LastLogin = RangeStart + 9;
		/// <summary>요청 형식이 잘못되었습니다. HTTP 400.</summary>
		public const int BadRequest = RangeStart + 10;
		/// <summary>
		/// 같은 로그인의 동시 재발급이 겹쳤거나 nonce 를 더 발급할 수 없어(메모리 저장소 상한) 처리하지 못했습니다. 잠시 뒤 다시 시도합니다.
		/// HTTP 503(클라이언트에서는 5xx 라 <c>HttpError</c>).
		/// </summary>
		public const int RetryLater = RangeStart + 11;
		/// <summary>이 플랫폼은 서버가 발급한 nonce 가 필요한데 요청에 없습니다(nonce 를 보내지 않는 이전 클라이언트). HTTP 400.</summary>
		public const int NonceMissing = RangeStart + 12;
		/// <summary>id 토큰의 nonce 클레임이 없거나 요청의 nonce 와 맞지 않습니다. HTTP 401.</summary>
		public const int NonceMismatch = RangeStart + 13;
		/// <summary>발급한 적 없거나, 만료되었거나(기본 5분), 이미 쓴 nonce 입니다. 플랫폼 로그인부터 다시 합니다. HTTP 401.</summary>
		public const int InvalidNonce = RangeStart + 14;
		/// <summary>발급한 적 없거나, 만료되었거나(기본 5분), 이미 썼거나, 다른 계정에서 쓴 충돌 티켓입니다. 연동부터 다시 합니다. HTTP 401.</summary>
		public const int InvalidConflictTicket = RangeStart + 15;

		/// <summary>로그인 상태가 아니거나 저장된 로그인이 없습니다.</summary>
		public const int NotSignedIn = RangeStart + 100;
		/// <summary>플랫폼 SDK 로그인이 실패했습니다.</summary>
		public const int PlatformSignInFailed = RangeStart + 101;
		/// <summary>서버 응답을 해석하지 못했습니다(알 수 없는 오류 응답 포함).</summary>
		public const int InvalidResponse = RangeStart + 102;
		/// <summary>이 기기의 저장된 로그인을 지우지 못했습니다. 로그아웃은 됐지만 다음 실행에서 자동 로그인이 될 수 있습니다.</summary>
		public const int StorageFailed = RangeStart + 103;
		/// <summary>access 토큰을 받는 동안 세션이 계속 바뀌었습니다. 다시 시도하면 됩니다.</summary>
		public const int SessionChanged = RangeStart + 104;

		/// <summary>이 패키지 코드 블록의 끝입니다. 코드로는 쓰지 않습니다.</summary>
		public const int RangeEnd = 3999;
	}
}
