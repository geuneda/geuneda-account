# Geuneda.Account - AI 에이전트 가이드

> **관련 파일**: `CLAUDE.md`는 이 파일을 감쌉니다 — `CLAUDE.md`가 아니라 `AGENTS.md`를 편집하세요. `README.md`는 사용자 대상 문서입니다.

## 1. 패키지 개요
- **패키지**: `com.geuneda.account`
- **Unity**: 2021.3+
- **의존성**: `com.geuneda.core`(Result, ModuleLogger, ISecureStore, AppEnv), `com.geuneda.http`(인증 서버 호출, 미들웨어), `com.cysharp.unitask`, `com.unity.nuget.newtonsoft-json`
- **서버**: `Server~/`(Unity 가 가져오지 않는 폴더)의 .NET 10 솔루션. ASP.NET Core 인증 서버 라이브러리, 샘플 서버, xUnit 테스트
- **출처**: Lucky Defense 의 Percent PercentAuth·AuthSessionManagement(로드맵 3-4). 제공자 레지스트리, 결과 struct 대신 Result 코드, 0ms 세마포어식 중복 작업 거절(`InProgress`), 연동 충돌 시 자격을 다시 써서 그 계정 로그인·옮겨오기, 다음 세션 ID 예약은 따랐습니다. 바꾼 것: PlayerPrefs 평문 대신 core 보안 저장소, JWT 를 클라이언트에서 파싱하지 않고 서버가 준 남은 수명(초)을 단조 시계로 셈, `(exp - iat) - 5분` 수명 기준 대신 남은 수명 기준 재발급, 미들웨어의 재발급도 같은 single-flight, 세션 종료 이벤트를 대칭으로(시간 초과 새 세션 전에 `Ended`), 게스트 자격은 기기 ID 가 아니라 무작위 비밀 값(서버는 해시), 토큰 원문 로그 금지, refresh 토큰 회전·재사용 감지 서버.

## 2. 구조

| 파일 | 역할 |
|------|------|
| `Runtime/Protocol/` | 요청·응답 DTO(internal, `[Preserve]`), 경로(`AccountRoutes`), `AccountResultCodes`, `AccountPlatforms`. 서버가 링크해 컴파일 |
| `Runtime/Auth/AuthService.cs` | 로그인 상태, 토큰 적용, 재발급 single-flight, 계정 작업 잠금, 이벤트 |
| `Runtime/Auth/ProviderSignIn.cs` | 제공자 로그인. `RequiresNonce` 면 먼저 `POST nonce` 로 받아 넘기고 자격에 붙임 |
| `Runtime/Auth/AccountApi.cs` | 인증 서버 HTTP 호출과 오류 응답 → 결과 코드 변환 |
| `Runtime/Auth/AuthTokenStore.cs` | `SavedLogin`(계정 ID, refresh 토큰) JSON 을 보안 저장소 키 하나에 |
| `Runtime/Auth/AuthTokenMiddleware.cs` | 게임 서버 요청의 Bearer, 401 재시도 한 번 |
| `Runtime/Auth/GuestAuthProvider.cs` | 게스트 비밀 값 |
| `Runtime/Session/` | `AppSession`(ID 예약, 일시정지·재개·시간 초과, 대칭 이벤트), `AppSessionLifecycle`(숨은 MonoBehaviour), `SessionIds` |
| `Server~/src/Geuneda.Account.Server/` | `AddGeunedaAccount`/`MapGeunedaAccount`, `Endpoints/`(NonceEndpoints nonce 발급, TokenEndpoints 로그인·재발급·로그아웃, LinkEndpoints 연동·삭제, PlatformCredentials 자격 검증·nonce 소비·충돌 티켓 발급과 사용, 필터·본문·오류), `Tokens/`(JWT 발급, refresh 토큰, 1회용 값), `Stores/`(계정, refresh 토큰, 1회용 값), `Verifiers/`, `AccessTokenValidator` |
| `Server~/samples/`, `Server~/tests/` | 샘플 서버(메모리 저장소, 게스트, `sample` 플랫폼, `/game/me`), xUnit 테스트 |

## 3. 규칙

- **결과 규약**: 정상 실패(네트워크, 서버 거절, 진행 중 작업, 로그인 안 됨)는 `Result` 코드, 오용(등록하지 않은 플랫폼, null 자격, 빈 플랫폼, Dispose 뒤 호출)은 예외입니다. 공개 메서드는 async 가 아닌 겉 메서드에서 인자를 검사해 바로 던지고, 실제 작업은 private async 메서드가 합니다(예외가 반환 UniTask 안에 갇히지 않게). 서버 오류 본문의 코드는 3001~3099 만 결과 코드로 쓰고, 그 밖은 `InvalidResponse`(5xx 는 `HttpError`)입니다.
- **로그인 버전**: 로그인·로그아웃·만료로 로그인이 바뀔 때마다 `_loginVersion` 을 올립니다. 재발급과 인증된 호출(`CallAuthorizedAsync`)은 시작할 때 버전을 기억하고, 각 await 뒤 다르면 결과를 버립니다(로그아웃한 로그인이 되살아나거나 이전 계정의 응답·404 가 새 계정에 적용되지 않게). 재발급 결과 적용(`ApplyTokens`)은 버전을 올리지 않습니다. `AuthTokenMiddleware` 는 401 재전송 전에 `AccountId` 가 그대로인지 봅니다.
- **재발급 single-flight**: 진행 중 재발급은 `UniTaskCompletionSource<Result<string>>` 하나로 나누고, 로그인 버전(`_refreshVersion`)이 다르면 합류하지 않고 새로 시작합니다. 끝날 때 `ReferenceEquals` 로 자기 것일 때만 비웁니다. `UniTask.Preserve()` 는 완료 전 여러 대기를 받지 못하므로 쓰지 마세요("Already continuation registered"). 각 호출자의 취소는 `AttachExternalCancellation` 으로 그 호출만 멈춥니다. 재발급 요청은 서비스 수명 토큰과 요청 제한 시간(`RequestTimeout`, `AccountApi` 가 모든 요청에 붙임)으로 끝납니다.
- **세션과 토큰**: `AccessTokenCache` 는 발급 요청에 실은 세션 ID 를 함께 둡니다. 진행 중인 세션과 다르면(백그라운드 시간 초과) 남은 수명과 관계없이 재발급하고, 재발급 중 세션이 또 바뀌면 다시 받습니다(최대 `MaxTokenAttempts` 3번, 회전된 refresh 토큰은 그때마다 저장). 세션이 맞는 토큰만 성공으로 돌려주고, 끝내 맞지 않으면 `SessionChanged` 입니다.
- **공유 재발급 대기자**: `UniTaskCompletionSource` 는 대기자 이어지는 코드를 차례로 동기 실행하므로, 앞 대기자가 결과를 받자마자 로그아웃·Dispose·세션 보정 재발급을 할 수 있습니다. `GetAccessTokenCoreAsync` 는 재발급을 기다린 뒤 자기 시작 버전·로그인 상태·Dispose 를 다시 확인하고(재발급 실패 코드는 그대로 둠), 자기가 기다린 결과가 아니라 지금 캐시에서 세션을 검사해 통과한 토큰을 돌려줍니다(검사한 토큰과 돌려주는 토큰이 같아야 함).
- **저장 실패**: 저장 결과는 `_loginSaved` 에 남고 `SignInResult.IsSaved` 로 알립니다. 로그인에서는 메모리 상태를 바꾸기 전에 저장하고, 실패하면 `Clear` 로 이전 저장값을 지웁니다(다음 실행에서 이전 계정으로 자동 로그인되지 않게). 그것도 실패했는데 저장소에 다른 계정이 남아 있거나 무엇이 있는지 모르면(`AuthTokenStore.HoldsNothingBut`) 로그인을 `StorageFailed` 로 거절하고(지금 로그인 유지, 새 refresh 토큰은 백그라운드 폐기) 상태를 바꾸지 않습니다. 저장소 내용은 메모리 로그인과 따로 `AuthTokenStore` 가 읽기(Found/NotFound/Failed)·저장·삭제 결과로 따라갑니다. 시작할 때 읽지 못했거나 로그아웃에서 지우지 못한 이전 값이 남을 수 있어서입니다. 재발급에서 저장하지 못하면 다음 실행은 회전 전 토큰을 보냅니다. 서버 유예(기본 30초) 안이면 같은 계정으로 자동 로그인되고, 그 뒤면 재사용으로 폐기되어 다시 로그인합니다.
- **미들웨어와 Dispose**: 첫 토큰을 받은 뒤 서비스가 Dispose 되면 401 처리의 `GetAccessTokenAsync` 가 `ObjectDisposedException` 을 던집니다. 미들웨어는 그 호출에서만 잡아 `Cancelled` 로 바꿉니다(처음부터 Dispose 된 서비스는 첫 호출에서 예외).
- **미들웨어 순서**: `AuthTokenMiddleware` 는 `RetryMiddleware` 바깥(order 가 작음)에 둡니다. 안쪽이면 재시도마다 토큰과 계정을 새로 읽어, 재시도 사이 계정 전환 때 이전 요청이 새 계정 토큰으로 나갑니다(테스트 `RetryInsideAuth_...`).
- **Dispose**: 수명 토큰을 취소만 하고 CTS 는 해제하지 않습니다(진행 중 작업이 `Token` 을 읽음). 계정 작업은 호출자 토큰과 수명 토큰을 연결해 쓰고, 제공자·서버 await 뒤 `_disposed` 면 `Cancelled` 로 끝내 늦은 결과를 적용하지 않습니다.
- **계정 작업 잠금**: 로그인·자동 로그인·로그아웃·연동·옮겨오기·해제·삭제는 `_operationRunning` 하나로 겹치지 않게 하고 겹치면 기다리지 않고 `InProgress` 입니다. `GetLinksAsync`·`GetAccessTokenAsync` 는 잠그지 않습니다.
- **로그아웃 순서**: 이 기기의 로그인을 먼저 지우고(`EndLogin`) 서버 폐기와 SDK 로그아웃을 합니다. 서버·SDK 실패는 결과를 바꾸지 않습니다. `EndLogin` 은 처음에 읽지 못한 저장값도 남지 않게 항상 `AuthTokenStore.Clear` 를 부르고, 삭제가 실패하면 빈 값으로 덮어쓰며(다음 실행에서 깨진 값으로 보고 지움), 그것도 실패하면 로그아웃 결과가 `StorageFailed` 입니다. 계정을 바꾸는 로그인은 이전 refresh 토큰을 백그라운드로 폐기하고, `SignedOut` 없이 `SignedIn` 만 다시 냅니다(세션은 `Restarted`).
- **세션**: 로그인 요청의 `SessionId` 는 `NextSessionId`, 세션 중 재발급은 현재 `SessionId` 입니다. 로그인 성공 뒤 `Start()` 가 그 ID 로 시작합니다. 새 세션 전에는 항상 `Ended` 를 먼저 냅니다(시간 초과·재시작 포함).
- **저장 키**: `geuneda.account.<AppEnv.Name>.login`, `.guestSecret`. 바꾸면 기존 설치의 로그인·게스트 계정을 잃습니다. 게스트 비밀 값은 로그아웃·계정 삭제에도 지우지 않고, 읽기가 `Failed` 면 새로 만들지 않습니다.
- **공유 파일**: `Runtime/Protocol/` 은 서버가 링크해 컴파일합니다. Unity 형식·UniTask·Geuneda.Core·Newtonsoft 를 쓰지 말고, 고치면 `dotnet test "Server~/Geuneda.Account.Server.slnx"` 도 돌립니다. DTO 는 public 속성(서버 System.Text.Json Web, 클라이언트 Newtonsoft camelCase)이고 IL2CPP 제거에 대비해 `[Preserve]`(이름으로 인식되는 자체 특성)를 붙입니다.
- **서버 refresh 회전**: 로그인(`RefreshTokenFamily`)마다 쓸 수 있는 토큰은 현재 토큰 하나입니다. 현재 토큰이 오면 직전 = 현재, 현재 = 새 토큰, `RotatedAt` = 지금으로 바꿉니다. 직전 토큰이 `now - RotatedAt < grace` 안에 오면 현재 토큰만 새 토큰으로 바꾸고 직전·`RotatedAt` 은 그대로 둡니다(재시도가 유예를 늘리지 않게). 그 밖의 토큰(기록에 있지만 현재·유예 안 직전이 아님)은 재사용이라 로그인을 폐기합니다. 변경은 `TryRotateAsync`(현재 토큰 비교 + 상태 교체 + 새 토큰 기록 추가, 원자적)로 하고, 지면 다시 읽습니다(최대 3회). 3회 모두 지면 토큰 무효가 아니라 503 `RetryLater` 입니다(클라이언트가 로그인을 지우지 않게). 후속 토큰을 하나씩 폐기하는 방식으로 돌아가지 마세요. 이미 다시 회전한 후손이 살아남아 분기가 생깁니다.
- **서버 처리기 수명**: `TokenEndpoints`·`LinkEndpoints` 는 scoped 이고 최소 API 처리기 인자로 받습니다(저장소가 scoped 여도 됨). `MapGeunedaAccount` 의 저장소 등록 검사는 `IServiceProviderIsService` 로 합니다(루트에서 scoped 를 꺼내지 않게).
- **애플·구글 검증기**: `OpenIdConnectPlatformVerifier` 가 `OpenIdConnectKeyCache`(메타데이터 주소 + `IDocumentRetriever`, 운영은 `HttpDocumentRetriever`, 테스트는 `FakeIdTokenIssuer`)로 공개 키를 받습니다. IdentityModel `ConfigurationManager` 로 돌아가지 마세요. 처음 이후의 갱신이 백그라운드라 `RequestRefresh` 직후에도 옛 키로 검증해, 키 교체 직후의 정상 토큰을 자격 오류(401)로 처리합니다(2026-10-14 리뷰). 캐시 규칙: 12시간마다 다시 받기(실패하면 옛 키 유지, 5분 뒤 재시도), 서명 키를 못 찾으면(`SecurityTokenSignatureKeyNotFoundException`) 다시 받기를 기다려 새로 받았을 때만 한 번 더 검증, 5분 안에 이미 받았으면 다시 받지 않음(위조 토큰 대비), 동시 요청은 한 번만 받음, 서명 키가 없는 응답은 받기 실패(받아 둔 키를 빈 목록으로 덮지 않게), 받기 실패로 보는 예외는 `IOException`·`HttpRequestException`·`JsonException`·`ArgumentException`(IdentityModel 의 JWKS 해석 오류, 빈 문서는 `ArgumentNullException`)·받기 시간 초과뿐이고 그 밖(구현 버그)은 자동 갱신 중에도 그대로 던짐, 키가 아직 없는데 받기가 실패하면 30초(`RetryIntervalWithoutKeys`) 동안 바로 실패, 키를 확인하지 못하면 `InvalidOperationException`(500, 자격 오류 아님). 수명은 `TimeProvider` 로 판단하고 시계 오차는 음수를 거절합니다. 구글 `iss` 는 `GoogleIdTokenVerifier.Issuer`·`LegacyIssuer` 둘 다 받습니다. 클라이언트 쪽 제공자(네이티브 코드)는 D3 결정대로 별도 패키지입니다.
- **nonce(id 토큰 replay 방어, 2026-10-17)**: 애플·구글처럼 `RequiresNonce` 인 플랫폼은 로그인·연동·옮겨오기 모두 `PlatformCredentials.VerifyAsync` 를 거칩니다. 순서는 요청 nonce 유무(`NonceMissing` 400) → 검증기(서명·발급자·대상·만료와 `nonce` 클레임, 불일치는 `NonceMismatch` 401) → 저장소 원자적 소비(`InvalidNonce` 401)입니다. 소비를 검증 뒤에 두어 서명이 틀린 토큰으로 남의 nonce 를 태우지 못하게 합니다. 클레임 형식은 검증기의 `NonceClaimOf` 가 정합니다(구글 원문, 애플 `OneTimeValues.Hash` = SHA-256 16진 소문자. 클라이언트 `AppleNonce.Hash` 와 같아야 함). 게스트는 nonce 를 쓰지 않습니다. 클라이언트는 nonce 를 받지 못하면 제공자를 부르지 않습니다.
- **연동 충돌 티켓(2026-10-17, 메인 결정 C)**: 연동이 `LinkedToOtherAccount` 로 끝나면 `IssueConflictTicketAsync` 가 무작위 티켓을 만들어 409 응답(`ErrorResponse.ConflictTicket`)에 넣습니다. 티켓은 플랫폼·고유 ID·요청 계정에 묶이고 `NonceLifetime` 동안 한 번만 씁니다. 로그인은 티켓만으로 받고(인증된 연동 응답으로만 나가므로), 옮겨오기는 티켓을 받은 계정만 받습니다(다른 계정이면 거절하고 티켓은 소비). 요청은 자격(+nonce) 또는 티켓 중 하나만(`RequestFields.IsCredentialOrTicket`), `POST links` 는 티켓을 받지 않습니다. 충돌한 id 토큰의 nonce 를 다시 받게 하는 방식(이전 안 A)은 충돌 뒤 계정에 묶이지 않은 로그인으로 같은 토큰이 한 번 더 통과해 쓰지 않습니다. 클라이언트는 `LinkResult.Credential` 안(internal `ConflictTicket`)에 티켓을 넣어 공개 API 를 바꾸지 않았습니다.
- **1회용 값 저장소**: nonce 와 충돌 티켓은 `IOneTimeValueStore` 하나에 용도(`OneTimeValueKind`)와 원문 SHA-256 을 키로 둡니다(한 용도의 값을 다른 용도로 못 씀). 원자적 소비와 상한은 같은 규칙입니다. `InMemoryOneTimeValueStore` 는 인증 없는 nonce 발급에 대비해 두 용도 합쳐 상한·만료 정리를 두고 가득 차면 새 값을 거절합니다(이미 나간 값을 밀어내지 않게). 소비·덮어쓰기로 사전에서 빠진 기록이 만료 순서 큐에 상한만큼 쌓이면 살아 있는 것만 남겨 큐도 상한의 두 배 안입니다(2026-10-17 리뷰). 티켓을 넣지 못하면 연동은 503 입니다. `NonceLifetime` 은 티켓에도 쓰므로 시작 검사에서 5분 이하로 막습니다. 클라이언트는 티켓 없는 충돌 응답을 `InvalidResponse` 로 끝냅니다(이미 쓴 id 토큰으로 돌아가지 않게).
- **서버 로그**: 토큰·자격 원문을 남기지 않습니다. 검증 실패 이유(`FailureReason`)는 클라이언트에도 메시지로 가므로 자격 내용을 넣지 않습니다.
- 결과 코드는 3000 블록(core README 범위 표에 등록). 값은 바꾸거나 재사용하지 않습니다(서버와 클라이언트가 같은 값을 씀).
- static 가변 상태 금지(D2). 서비스는 인스턴스로 만들어 `MainInstaller` 에 인터페이스로 바인딩합니다.

## 4. 테스트

- `Tests/EditMode/` (`Geuneda.Account.Tests.EditMode`)
  - `AuthServiceTest`: `FakeAuthServer`(인증 서버 프로토콜을 흉내 내는 http 전송, `Gates[경로]` 로 그 요청을 붙잡음)와 `FakeSecureStore`(읽기·쓰기·삭제 실패), 가짜 단조 시계·세션 시계로 로그인·세션 ID, 재시작 후 자동 로그인, 서버 거절·네트워크 실패, 남은 수명·절반 수명 재발급, 동시 재발급 한 번, 취소한 대기자, 로그아웃 중 재발급, `InProgress`, 충돌·옮겨오기·계정 전환, 401 재시도, 계정 삭제·다른 곳 삭제, 오류 코드 변환(알려진 코드가 든 5xx 포함), 세션 변경 후·재발급 중 세션 변경, 이전 계정의 늦은 응답, 로그인별 재발급 분리, 제공자·로그인 요청 중 Dispose, 저장소 삭제 실패(덮어쓰기, `StorageFailed`, 처음에 못 읽은 값 삭제), 요청 제한 시간
  - 각 수정의 회귀 테스트는 해당 수정을 임시로 되돌리면 실패하는 것을 확인했습니다(2026-10-14).
  - `AuthServiceNonceTest`: nonce 를 쓰는 제공자의 nonce 요청·전달·요청 본문, 게스트는 요청 안 함, nonce 실패(네트워크·5xx·빈 응답) 때 제공자를 부르지 않음, 서버 nonce 코드 전달, 충돌 자격의 티켓으로 옮겨오기·로그인(본문에 id 토큰 없음, 두 번째는 `InvalidConflictTicket`), nonce 받는 중 취소·Dispose. `FakeAuthServer` 는 발급한 nonce 만 한 번 받고, 충돌이면 티켓을 주고 한 번 받습니다(옮겨오기는 받은 계정만). nonce 를 받지 않거나 요청에 넣지 않게 되돌리면 실패하는 것을 확인했습니다(2026-10-17).
  - `ServiceDisposalTest`: `IAuthService`·`IAppSession` 을 `MainInstaller.CleanDispose<T>` 와 같은 제약(`where T : class, IDisposable`)의 도우미로 정리(account 는 services 에 의존하지 않음).
  - `AuthTokenMiddlewareTest`, `GuestAuthProviderTest`, `AppSessionTest`(대칭 이벤트 순서, 시계 역행, ID 형식)
  - `SampleServerInteropTest`(`[Explicit]`, `Interop` 범주): 실제 샘플 서버와 UnityWebRequest 로 로그인·게임 API·재시작·연동(서버 발급 nonce)·충돌·충돌 티켓으로 옮겨오기·티켓 재사용 거절(`InvalidConflictTicket`)·로그아웃 폐기·삭제. 서버를 띄우고(`dotnet run --project "Server~/samples/Geuneda.Account.SampleServer" --no-launch-profile -- --urls http://127.0.0.1:5299`) `GEUNEDA_ACCOUNT_INTEROP_URL=http://127.0.0.1:5299` 로 이 테스트를 이름으로 골라 실행합니다.
- `Server~/tests/Geuneda.Account.Server.Tests`: 127.0.0.1 Kestrel 과 `FakeTimeProvider` 로 로그인(동시 첫 로그인 한 계정, 잘못된 본문·크기), 회전·유예 안 재시도·재시도로 밀려난 토큰·두 번 전 토큰 재사용·유예 연장 없음·만료·세션 ID·경쟁 3회 실패 503(`FlakyRotateStore`), 동시 재발급 뒤 현재 토큰 하나, 애플·구글 검증기(`IdTokenVerifierTest`: 로컬 RSA 키로 서명한 토큰과 가짜 메타데이터·JWKS 문서로 정상·대상·발급자·만료·키 교체 첫 요청 통과·모르는 키 재요청 간격·갱신 실패는 서버 오류·동시 갱신 한 번·자동 갱신 실패 시 옛 키 유지·첫 받기 실패 뒤 30초 백오프·빈 키 목록(처음은 500, 갱신은 옛 키 유지)·잘못된 메타데이터·retriever 구현 버그는 전파·Apple·Google·게스트 동시 등록·음수 시계 오차·구글 발급자 두 형식·로그인 연동), 로그아웃, 연동·충돌·옮겨오기·해제·삭제, 토큰 검증(만료·키·대상·변조·alg none), 시작 검사(1회용 값 저장소, `NonceLifetime`), scoped 저장소. `NonceTest`: 정상·재사용·만료·불일치(어느 nonce 도 소비 안 함)·누락·발급 안 한 nonce·형식 오류, 게스트, 같은 nonce 동시 로그인 16개 중 하나만 성공, 저장소 동시 소비·상한·만료 정리, 가득 차면 503, 연동 소비, 1회용 값 저장소 용도 구분. `ConflictTicketTest`: 충돌 티켓으로 로그인 한 번(충돌한 id 토큰은 `InvalidNonce`), 받은 계정의 옮겨오기 한 번, 다른 계정은 거절·소비, 만료, 티켓과 자격·nonce 동시 전송과 `POST links` 티켓은 400(티켓은 남음), 다른 플랫폼 티켓, 티켓·nonce 섞어 쓰기, nonce 없는 플랫폼도 티켓, 저장소가 가득 차면 503. `IdTokenVerifierTest` 의 애플 해시·구글 원문 클레임과 애플 HTTP 로그인. nonce 소비·클레임 검사·애플 해시·누락 검사·원자적 소비, 티켓 발급·티켓 계정 검사·티켓 플랫폼 검사·티켓과 자격 동시 거절·용도 구분을 하나씩 되돌리면 해당 테스트가 실패하는 것을 확인했습니다(2026-10-17).
