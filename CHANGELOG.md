# Changelog
이 패키지의 모든 주요 변경사항은 이 파일에 기록됩니다.

이 형식은 [Keep a Changelog](http://keepachangelog.com/en/1.0.0/)를 기반으로 하며,
이 프로젝트는 [Semantic Versioning](http://semver.org/spec/v2.0.0.html)을 따릅니다.

## [0.3.0] - 2026-10-17

**호환성 파괴 변경**(이전 동작이 필요한 프로젝트는 `0.2.0` 태그를 고정해 씁니다. 인증 서버, 클라이언트, 제공자 패키지 `com.geuneda.account.apple`·`google` 0.2.0 을 함께 올립니다):
- `IAuthProvider` 에 `RequiresNonce` 를 더하고 `SignInAsync(CancellationToken)` 을 `SignInAsync(string nonce, CancellationToken)` 으로 바꿈. 직접 만든 제공자는 고쳐야 함
- `IAuthService`·`IAppSession` 이 `IDisposable` 을 상속함. `MainInstaller.CleanDispose<IAuthService>()`·`CleanDispose<IAppSession>()` 로 정리할 수 있음. 직접 만든 구현·테스트 대역은 `Dispose` 를 구현해야 함
- 서버 `IPlatformVerifier` 에 `RequiresNonce` 를 더하고 `VerifyAsync(credential, cancellationToken)` 을 `VerifyAsync(credential, nonce, cancellationToken)` 으로 바꿈. 직접 만든 검증기는 고쳐야 함
- 서버에 `IOneTimeValueStore` 등록이 필요함(`AddInMemoryStores()` 는 `InMemoryOneTimeValueStore` 도 등록). 없으면 `MapGeunedaAccount` 가 예외
- 애플·구글 로그인·연동·옮겨오기 요청에 nonce 가 없으면 서버가 거절(`NonceMissing`)하므로, account 0.2.x 클라이언트와 제공자 0.1.x 는 0.3.0 서버에 애플·구글로 로그인할 수 없음(게스트는 그대로)
- 연동 충돌 뒤의 로그인·옮겨오기는 id 토큰 대신 409 응답의 1회용 충돌 티켓을 보냄. 0.2.x 클라이언트는 충돌 뒤 같은 id 토큰을 다시 보내므로 0.3.0 서버에서 애플·구글은 `InvalidNonce` 로 실패함

**New**:
- 애플·구글 id 토큰 replay 방어(서버 발급 nonce 챌린지): `POST nonce`(인증 없음) → `{nonce, expiresIn}`(1회용, `AccountServerOptions.NonceLifetime` 기본·최대 5분), 로그인·연동 요청의 `nonce` 필드. 서버는 id 토큰의 `nonce` 클레임(애플은 원문의 SHA-256 16진 소문자, 구글은 원문)을 확인한 뒤, 발급한 적 있고 만료되지 않았고 아직 쓰지 않은 nonce 만 받아 원자적으로 소비. 서명 검증을 통과한 뒤에 소비함
- 연동 충돌 티켓: 연동이 충돌하면 409 응답에 서버가 만든 1회용 `conflictTicket`(플랫폼 계정과 연동을 요청한 계정에 묶임, `NonceLifetime` 동안)을 넣음. 로그인 요청은 티켓만으로, 옮겨오기는 티켓을 받은 계정의 요청만 받고, 둘 다 한 번만 받음. 티켓과 자격·nonce 를 함께 보내거나 연동(`POST links`)에 티켓을 보내면 400. 클라이언트 공개 API 는 그대로이고 `LinkResult.Credential` 안에 티켓이 들어 `SignInAsync(credential)`·`TransferAsync(credential)` 가 id 토큰 대신 보냄. 티켓이 없는 충돌 응답은 `InvalidResponse`
- 결과 코드 `NonceMissing`(3012, 400), `NonceMismatch`(3013, 401), `InvalidNonce`(3014, 401, 발급하지 않았거나 만료되었거나 이미 씀), `InvalidConflictTicket`(3015, 401, 발급하지 않았거나 만료되었거나 이미 썼거나 다른 계정의 티켓). 1회용 값 저장소가 가득 차면 503 `RetryLater`
- `AuthService` 는 nonce 를 쓰는 제공자로 로그인·연동하기 전에 nonce 를 받아 제공자에 넘기고 요청에 함께 보냄. 받지 못하면 플랫폼 로그인 화면 없이 그 결과(네트워크 실패, 5xx 등)로 끝남
- 서버 1회용 값 저장소 `IOneTimeValueStore`(nonce 와 충돌 티켓을 용도 `OneTimeValueKind` 와 해시로 구분, 원자적 소비), `OneTimeValue`, `InMemoryOneTimeValueStore`(두 용도 합쳐 기록 상한 기본 10만, 만료 기록 정리, 소비한 기록도 상한의 두 배 넘게 붙잡지 않음, 가득 차면 새 값 거절), `PlatformVerification.NonceMismatch`/`IsNonceMismatch`
- 샘플 서버의 시험용 `sample` 플랫폼도 nonce 를 씀(자격 `sample:<id>:<nonce>`)

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
