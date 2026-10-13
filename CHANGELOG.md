# Changelog
이 패키지의 모든 주요 변경사항은 이 파일에 기록됩니다.

이 형식은 [Keep a Changelog](http://keepachangelog.com/en/1.0.0/)를 기반으로 하며,
이 프로젝트는 [Semantic Versioning](http://semver.org/spec/v2.0.0.html)을 따릅니다.

## [0.2.0] - 2026-10-14

**New**:
- 서버 `AddApple(options => options.Audiences...)`, `AddGoogle(options => options.ClientIds...)`: 애플·구글 id 토큰 검증기(`AppleIdTokenVerifier`, `GoogleIdTokenVerifier`, 공통 `OpenIdConnectPlatformVerifier`). 공개 키 캐시 `OpenIdConnectKeyCache`(OIDC JWKS 를 받아 두고 12시간마다 갱신, 모르는 키면 다시 받기를 기다려 한 번 더 검증, 재요청은 5분에 한 번, 동시 요청은 한 번만 받음, 서명 키 없는 응답은 실패, 처음 받기 실패 뒤 30초 백오프, 확인하지 못하면 500), 발급자·대상·만료 검사, `sub` 를 고유 ID 로. 대상이 없거나 시계 오차가 음수면 시작할 때 예외
- 클라이언트 제공자는 별도 패키지 `com.geuneda.account.apple`, `com.geuneda.account.google`

## [0.1.0] - 2026-10-14

**New**:
- 계정 인증 `IAuthService`/`AuthService`: 플랫폼 제공자로 로그인(처음이면 계정 생성), 저장된 로그인으로 자동 로그인, 로그아웃(이 기기 먼저, 서버 폐기·SDK 로그아웃은 실패해도 진행), 연동 목록·연동·충돌(자격을 돌려줘 그 계정으로 로그인하거나 옮겨오기)·해제·계정 삭제, `SignedIn`/`SignedOut` 이벤트. 계정 작업은 한 번에 하나(`InProgress`)
- 토큰: refresh 토큰은 core 보안 저장소(빌드 환경별 키), access 토큰은 메모리. 남은 수명 기준 재발급(기본 1분 전, 수명의 절반 이하), 재발급 single-flight, 로그인·로그아웃 뒤 늦게 끝난 재발급은 버림, 서버가 거절하면 저장된 로그인 삭제
- 게임 서버 HTTP 미들웨어 `AuthTokenMiddleware`: Bearer 토큰, 401 이면 재발급해 한 번만 다시 보냄
- 제공자 `IAuthProvider`, 게스트 `GuestAuthProvider`(무작위 32바이트 비밀 값을 보안 저장소에, 읽기 실패 때 새로 만들지 않음), `PlatformCredential`
- 앱 세션 `IAppSession`/`AppSession`: 다음 세션 ID 예약(로그인 요청에 실음), 시간순 16자리 ID, 백그라운드 10분 이상이면 이전 세션 종료 후 새 세션(대칭 이벤트), 앱 일시정지·종료 연결
- 결과 코드 `AccountResultCodes`(3000 블록, 서버 코드 3001~3011, 클라이언트 코드 3100~3104)
- 요청 중 로그인이 바뀌면 응답을 적용하지 않음(늦은 응답, 401 재전송, 재발급 모두), `Dispose` 는 진행 중 요청 취소와 늦은 결과 무시, 세션이 바뀌면 그 세션 ID 로 토큰 재발급, 인증 서버 요청 제한 시간(`RequestTimeout`), 로그아웃 저장소 삭제 실패는 덮어쓰기 후 `StorageFailed`, 로그인 저장 실패는 `SignInResult.IsSaved` false 와 이전 계정 저장값 삭제
- ASP.NET Core 인증 서버 `Geuneda.Account.Server`(`Server~/`, .NET 10): `AddGeunedaAccount`/`MapGeunedaAccount`, JWT access 토큰(sub, sid), refresh 토큰 회전과 재사용 감지(로그인마다 현재 토큰 하나, 유예 안의 직전 토큰 재시도만 허용, 그 밖의 지나간 토큰은 그 로그인 폐기, 원자적 회전 `TryRotateAsync`), 로그아웃, 연동·옮겨오기·해제(마지막 수단 보호)·계정 삭제, 게스트 검증기(비밀 값의 SHA-256 만 저장), 저장소 인터페이스와 메모리 구현, 다른 서버용 `AddGeunedaAccessTokenValidation`/`IAccessTokenValidator`, 요청 크기 제한, 시작 검사. 샘플 서버와 xUnit 테스트
