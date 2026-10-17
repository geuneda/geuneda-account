# Geuneda Account

계정 인증과 앱 세션입니다. 플랫폼 제공자(게스트 기본)로 로그인하고, 토큰을 안전하게 보관해 자동 로그인과 재발급을 처리하며, 게임 서버 요청에 토큰을 넣습니다. 같은 프로토콜을 쓰는 ASP.NET Core 인증 서버(`Server~/`)를 함께 둡니다. 결과는 core 결과 규약(`Result`)을 따릅니다.

| 기능 | 타입 | 네임스페이스 |
|------|------|-------------|
| 인증 | `IAuthService`, `AuthService`, `AuthServiceOptions`, `SignInResult`, `SignOutReason` | `Geuneda.Account` |
| 연동 | `LinkResult`, `LinkedPlatform` | `Geuneda.Account` |
| 제공자 | `IAuthProvider`, `PlatformCredential`, `GuestAuthProvider`, `AccountPlatforms`(애플·구글 제공자는 별도 패키지 `com.geuneda.account.apple`·`google`) | `Geuneda.Account` |
| 게임 서버 요청 | `AuthTokenMiddleware`(http 미들웨어) | `Geuneda.Account` |
| 세션 | `IAppSession`, `AppSession`, `SessionInfo`, `SessionEndReason` | `Geuneda.Account` |
| 코드 | `AccountResultCodes`(3000 블록) | `Geuneda.Account` |
| 서버(.NET, `Server~/`) | `AddGeunedaAccount`, `MapGeunedaAccount`, `IAccountStore`, `IRefreshTokenStore`, `IOneTimeValueStore`, `IPlatformVerifier`(`AddGuest`, `AddApple`, `AddGoogle`), `IAccessTokenValidator`, `AddGeunedaAccessTokenValidation` | `Geuneda.Account.Server` |

## 설치

`Packages/manifest.json` 에 추가합니다. UPM 은 git 의존성을 자동으로 받지 않으므로 core, http, UniTask 도 함께 넣습니다.

```json
{
  "dependencies": {
    "com.cysharp.unitask": "https://github.com/Cysharp/UniTask.git?path=src/UniTask/Assets/Plugins/UniTask",
    "com.geuneda.core": "https://github.com/geuneda/geuneda-core.git",
    "com.geuneda.http": "https://github.com/geuneda/geuneda-http.git",
    "com.geuneda.account": "https://github.com/geuneda/geuneda-account.git"
  }
}
```

## 사용법

### 부트스트랩

```csharp
using Geuneda.Account;
using Geuneda.Core;
using Geuneda.Http;

var secureStore = new PlatformSecureStore();
var session = new AppSession();
session.BindApplicationLifecycle();   // 앱 일시정지·종료를 세션에 연결

var options = new AuthServiceOptions
{
    // 인증 서버 접두사까지, 끝에 '/'. 요청 제한 시간은 RequestTimeout(기본 15초)
    AuthClient = new HttpClientBuilder().WithBaseAddress("https://auth.example.com/auth/").Build(),
    SecureStore = secureStore,
    Session = session
};
options.Providers.Add(new GuestAuthProvider(secureStore));
var auth = new AuthService(options);

MainInstaller.Bind<ISecureStore>(secureStore);
MainInstaller.Bind<IAppSession>(session);
MainInstaller.Bind<IAuthService>(auth);

// 종료·재시작 정리: 두 인터페이스 모두 IDisposable 이다
MainInstaller.CleanDispose<IAuthService>();
MainInstaller.CleanDispose<IAppSession>();
```

### 로그인

```csharp
// 타이틀: 저장된 로그인이 있으면 자동 로그인
if (auth.HasSavedLogin)
{
    var silent = await auth.SignInSilentlyAsync(token);
    if (silent.IsSuccess) { /* 로비로 */ return; }
    // NetworkError 면 다시 시도, InvalidRefreshToken 등이면 로그인 화면(저장된 로그인은 지워짐)
}

// 로그인 버튼
var result = await auth.SignInAsync(AccountPlatforms.Guest, token);
if (result.IsSuccess && result.Value.IsNewAccount) { /* 튜토리얼 */ }

// 다른 곳에서 로그인이 끝나면(만료·삭제·로그아웃) 타이틀로
auth.SignedOut += reason => GoToTitle(reason);
```

- 애플·구글처럼 nonce 를 쓰는 제공자(`IAuthProvider.RequiresNonce`)는 플랫폼 로그인 전에 인증 서버에서 일회용 nonce 를 받아(`POST nonce`) 제공자에 넘기고, 받은 id 토큰과 함께 서버로 보냅니다(id 토큰 replay 방어). nonce 를 받지 못하면 플랫폼 로그인 화면 없이 그 결과(네트워크 실패, 5xx 는 `HttpError`)로 끝납니다. 게스트는 nonce 를 쓰지 않습니다.
- 제공자를 직접 만들면 `RequiresNonce` 를 서버 검증기와 같게 두고, true 면 `SignInAsync(nonce, ct)` 로 받은 원문에 id 토큰을 묶습니다(그 플랫폼 SDK 의 nonce 요청 값). 서버 검증기가 클레임과 비교하는 형식(원문 그대로 또는 해시)과 맞춥니다.

### 게임 서버 요청

```csharp
// HTTP: 토큰을 넣고 401 이면 재발급해 한 번만 다시 보냄. 재시도 미들웨어보다 바깥(order 가 작음)에 둔다.
// 재시도는 처음 받은 토큰으로 같은 계정의 요청을 다시 보낸다(안쪽에 두면 재시도 사이 계정 전환 때 이전 요청이 새 계정으로 나갈 수 있음)
var gameClient = new HttpClientBuilder()
    .WithBaseAddress("https://game.example.com/")
    .AddMiddleware(new AuthTokenMiddleware(auth), order: -100)
    .AddMiddleware(new RetryMiddleware(), order: 0)
    .Build();

// 실시간 RPC(com.geuneda.realtime): 연결할 때마다 쓸 수 있는 access 토큰으로 로그인
LoginAsync = async (caller, cancellationToken) =>
{
    var accessToken = await auth.GetAccessTokenAsync(cancellationToken);
    if (!accessToken.IsSuccess) return accessToken;
    return await new SessionRpcClient(caller).Login(new ISessionRpc.LoginReq { AccessToken = accessToken.Value }, cancellationToken);
};
```

### 연동과 충돌

```csharp
var link = await auth.LinkAsync(platform, token);   // 등록한 제공자의 플랫폼 이름

if (link.IsSuccess && link.Value.IsConflict)
{
    // 그 플랫폼 계정이 다른 계정(link.Value.ConflictAccountId)에 연동되어 있다. 사용자에게 고르게 한다
    if (userChoseOtherAccount)
        await auth.SignInAsync(link.Value.Credential, token);    // 그 계정으로 로그인(플랫폼 로그인 다시 안 함)
    else
        await auth.TransferAsync(link.Value.Credential, token);  // 지금 계정으로 옮겨오기(확인 문구를 받는다)
}
```

- 충돌 자격(`link.Value.Credential`) 안에는 서버가 준 1회용 충돌 티켓이 있어, `SignInAsync(credential)`·`TransferAsync(credential)` 는 id 토큰 대신 티켓을 보냅니다(id 토큰과 nonce 는 연동 요청에서 이미 씀). 티켓은 한 번만(그 계정으로 로그인 또는 이 계정으로 옮겨오기 중 하나), 발급부터 5분 안에 씁니다. 지났거나 이미 썼으면 `InvalidConflictTicket` 이니 연동부터 다시 합니다.

- `UnlinkAsync(platform)` 은 마지막 로그인 수단이면 `LastLogin` 입니다. `DeleteAccountAsync()` 는 인증 서버의 계정을 지우고 로그아웃합니다. 게임 서버의 계정 데이터는 게임이 따로 지웁니다.
- 게스트 비밀 값은 Android 에서 앱을 지우면 사라집니다. 게스트 계정이면 다른 플랫폼 연동을 권합니다.

### 결과

| 상황 | 결과 코드 |
|------|-----------|
| 성공 | 0 |
| 연결 실패, 타임아웃, 취소 | `ResultCodes.NetworkError`, `Timeout`, `Cancelled` |
| 다른 계정 작업 진행 중(로그인·로그아웃·연동·옮겨오기·해제·삭제) | `ResultCodes.InProgress` |
| 서버 오류 응답(3001~3010) | `InvalidCredential`, `UnsupportedPlatform`, `InvalidRefreshToken`, `RefreshTokenReused`, `InvalidAccessToken`, `AccountNotFound`, `LoginConflict`, `PlatformAlreadyLinked`, `LastLogin`, `BadRequest`(`RetryLater` 3011 은 503 이라 클라이언트에서는 `HttpError`) |
| nonce 거절(3012~3014) | `NonceMissing`(요청에 nonce 없음, 이전 클라이언트), `NonceMismatch`(id 토큰이 그 nonce 에 묶이지 않음), `InvalidNonce`(발급하지 않았거나 만료되었거나 이미 씀). 플랫폼 로그인부터 다시 합니다 |
| 충돌 티켓 거절(3015) | `InvalidConflictTicket`(발급하지 않았거나 만료되었거나 이미 썼거나 다른 계정의 티켓). 연동부터 다시 합니다 |
| 로그인 상태가 아님, 요청 중에 로그인이 바뀜(다른 계정 로그인·로그아웃) | `AccountResultCodes.NotSignedIn` |
| 로그아웃은 됐지만 이 기기의 저장된 로그인을 지우지 못함, 저장소 오류로 다른 계정으로의 전환을 거절 | `AccountResultCodes.StorageFailed` |
| access 토큰을 받는 동안 세션이 계속 바뀜(다시 시도) | `AccountResultCodes.SessionChanged` |
| 플랫폼 SDK 로그인 실패 | `AccountResultCodes.PlatformSignInFailed` |
| 해석할 수 없는 응답 / 5xx(본문의 코드와 관계없이) | `AccountResultCodes.InvalidResponse` / `HttpResultCodes.HttpError` |
| `Dispose` 로 끊긴 요청 | `ResultCodes.Cancelled` |
| 등록하지 않은 플랫폼, null 자격, Dispose 뒤 호출 | 예외 |

- 서버가 refresh 토큰을 거절하면(`InvalidRefreshToken`, `RefreshTokenReused`) 저장된 로그인을 지우고, 로그인 중이었으면 `SignedOut(LoginExpired)` 이 옵니다. 네트워크 실패는 저장된 로그인을 그대로 둡니다.
- 인증된 요청이 `AccountNotFound` 이면(다른 기기에서 삭제) `SignedOut(AccountDeleted)` 입니다.
- 요청이 끝나기 전에 로그인이 바뀌면(다른 계정으로 로그인, 로그아웃) 그 응답은 지금 로그인에 적용하지 않고 `NotSignedIn` 으로 끝냅니다. `AuthTokenMiddleware` 도 401 재전송 전에 계정이 바뀌었으면 다시 보내지 않습니다.
- 인증 서버 요청마다 `RequestTimeout`(기본 15초)을 붙입니다. 공유 재발급이 끝나지 않는 요청에 묶이지 않습니다.
- `Dispose` 하면 진행 중인 요청을 취소하고, 늦게 끝난 결과(로그인 성공 등)는 적용하지 않습니다. 게임 요청을 보낸 뒤 Dispose 되면 `AuthTokenMiddleware` 의 401 처리는 `Cancelled` 입니다.
- 메인 스레드에서 씁니다.

### 토큰

- refresh 토큰과 계정 ID 는 core `ISecureStore`(iOS 키체인, Android 키스토어) 키 `geuneda.account.<환경>.login` 에, access 토큰은 메모리에만 둡니다. iOS 는 앱을 지워도 로그인이 남아 다시 설치하면 자동 로그인됩니다.
- access 토큰은 남은 수명이 `AccessTokenRefreshMargin`(기본 1분, 수명의 절반을 넘지 않음)보다 짧으면 쓰기 전에 재발급합니다. 만료는 받은 시점부터 단조 시계로 세므로 기기 시계와 무관합니다.
- 여러 곳에서 동시에 토큰을 요청해도 재발급은 한 번만 보냅니다(로그인마다 따로). 기다리던 호출 하나를 취소해도 재발급은 계속됩니다.
- 세션이 바뀌면(백그라운드 시간 초과) 남은 수명과 관계없이 새 세션 ID 로 토큰을 다시 받아, 게임 서버가 보는 `sid` 가 클라이언트 세션과 같습니다. 받는 동안 세션이 또 바뀌면 다시 받고(최대 3번), 그래도 맞지 않으면 `SessionChanged` 입니다.
- 로그아웃에서 저장된 로그인을 지우지 못하면 빈 값으로 덮어써 다음 실행에서 읽히지 않게 하고, 그것도 실패하면 `StorageFailed` 를 돌려줍니다.
- 로그인을 저장하지 못하면(보안 저장소 오류) 이번 실행에서는 로그인 상태이고 `SignInResult.IsSaved` 가 false 입니다(다음 실행의 자동 로그인은 보장하지 못함). 이때 이전 저장값은 지워 다음 실행에서 다른 계정으로 자동 로그인되지 않게 하고, 지우지도 못했는데 저장소에 다른 계정이 남았거나 무엇이 있는지 모르면 그 로그인은 `StorageFailed` 로 거절됩니다(지금 로그인은 그대로).

### 세션

- `AppSession` 은 다음 세션 ID 를 미리 만들어 둡니다(`NextSessionId`). 로그인·자동 로그인 요청에 실어 보내고 성공하면 그 ID 로 세션을 시작하므로, 서버(access 토큰의 `sid`)와 클라이언트 로그의 세션 ID 가 첫 요청부터 같습니다.
- ID 는 2024-01-01 UTC 부터의 밀리초와 23비트 난수로 만든 16자리 16진수라 시간순으로 정렬됩니다.
- 백그라운드에서 `BackgroundTimeout`(기본 10분) 이상 있다 돌아오면 이전 세션을 `BackgroundTimeout` 으로 끝내고 새 세션을 시작합니다. 새 세션 전에는 항상 이전 세션의 `Ended` 가 옵니다. 로그아웃·계정 삭제는 `Ended`, 다른 계정으로 로그인은 `Restarted`, 앱 종료는 `ApplicationQuit` 입니다.

## 서버 (ASP.NET Core)

`Server~/` 는 Unity 가 가져오지 않는 .NET 10 솔루션입니다(`dotnet test "Server~/Geuneda.Account.Server.slnx"`). 요청·응답 DTO 와 결과 코드는 `Runtime/Protocol/` 을 링크해 클라이언트와 한 곳에서 관리합니다.

```csharp
using Geuneda.Account.Server;
using Microsoft.IdentityModel.Tokens;

builder.Services.AddGeunedaAccount(options =>
    {
        options.Issuer = "https://auth.example.com";
        options.Audience = "my-game";
        options.SigningCredentials = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256);   // 비밀 저장소에서
    })
    .AddGuest()
    .AddPlatformVerifier<MyPlatformVerifier>();
builder.Services.AddScoped<IAccountStore, MyDbAccountStore>();          // 운영: 데이터베이스 구현
builder.Services.AddScoped<IRefreshTokenStore, MyDbRefreshTokenStore>();

app.MapGeunedaAccount("/auth");   // 클라이언트 기본 주소는 https://.../auth/
```

| 경로(접두사 아래) | 요청 | 응답 |
|---|---|---|
| `POST nonce` | 본문 없음, 인증 없음 | `{nonce, expiresIn}`(1회용, 기본 5분) |
| `POST login` | `{platform, credential, nonce?, sessionId?}` 또는 `{platform, conflictTicket, sessionId?}` | `{accountId, accessToken, accessTokenExpiresIn, refreshToken, refreshTokenExpiresIn, isNewAccount}` |
| `POST refresh` | `{refreshToken, sessionId?}` | 같음(쓴 refresh 토큰은 폐기) |
| `POST logout` | `{refreshToken}` | 204(그 로그인에서 이어진 토큰 모두 폐기, 유효 여부는 알리지 않음) |
| `GET links` | Bearer | `{links: [{platform, linkedAt}]}` |
| `POST links` | Bearer, `{platform, credential, nonce?}` | 연동 목록. 다른 계정 것이면 409 `LoginConflict` + `conflictAccountId`, `conflictTicket` |
| `POST links/transfer` | Bearer, `{platform, credential, nonce?}` 또는 `{platform, conflictTicket}` | 연동 목록(다른 계정에서 옮겨옴) |
| `DELETE links/{platform}` | Bearer | 연동 목록. 마지막 수단이면 409 `LastLogin` |
| `DELETE account` | Bearer | 204(계정과 refresh 토큰 삭제) |

- 오류는 `{code, message, conflictAccountId?, conflictTicket?}` 와 HTTP 상태(400·401·404·409, 같은 로그인의 동시 재발급이 계속 겹치거나 1회용 값 저장소가 가득 차면 503 `RetryLater`)입니다. 요청 본문은 16KB 까지입니다.
- **access 토큰**: JWT(`sub` 계정 ID, `sid` 세션 ID, `jti`, `iss`, `aud`, `exp`). 기본 수명 1시간. 게임 서버는 `AddGeunedaAccessTokenValidation(o => { o.Issuer, o.Audience, o.SigningKeys })` 로 `IAccessTokenValidator` 를 받아 검증합니다(실시간 RPC 로그인에서는 계약 구현이 검증 후 `IRpcContext.SignIn(accountId)`). 토큰 자체만 검증하므로 탈퇴한 계정의 토큰도 만료 전까지 통과합니다.
- **refresh 토큰**: 32바이트 무작위 값이고 서버는 SHA-256 만 저장합니다. 쓸 때마다 새 토큰으로 바뀌고 수명(기본 30일)도 새로 셉니다. 로그인 하나에서 쓸 수 있는 토큰은 언제나 하나(현재 토큰)입니다. 바로 직전 토큰이 `RefreshReuseGrace`(기본 30초, 처음 교체 시각부터) 안에 다시 오면 응답을 잃은 재시도로 보고 현재 토큰을 새 토큰으로 바꿔 줍니다. 그 밖에 지나간 토큰이 오면(유예 뒤 직전 토큰, 두 번 이상 전 토큰, 재시도로 밀려난 토큰) 탈취로 보고 그 로그인을 폐기합니다(`RefreshTokenReused`).
- **검증기**: `IPlatformVerifier`(플랫폼 이름, `RequiresNonce`, 자격·nonce → 플랫폼 안의 고유 ID). 게스트(`AddGuest`)는 Base64url 32~64바이트 비밀 값의 SHA-256 을 고유 ID 로 쓰고 nonce 를 쓰지 않습니다. 자격이 nonce 에 묶이지 않았으면 `PlatformVerification.NonceMismatch` 를 돌려줍니다.
- **nonce(id 토큰 replay 방어)**: 애플·구글 검증기는 `RequiresNonce` 입니다. 클라이언트가 로그인·연동 전에 `POST nonce` 로 받은 원문(Base64url 43자)을 제공자가 id 토큰에 묶고(애플은 원문의 SHA-256 16진 소문자를 요청에, 구글은 원문 그대로. 두 발급자 모두 요청 값을 `nonce` 클레임에 그대로 넣음), 요청의 `nonce` 로 원문을 보냅니다. 서버는 요청에 nonce 가 없으면 400 `NonceMissing`, 클레임이 없거나 다르면 401 `NonceMismatch`, 서명·클레임 검증을 통과한 뒤 저장소에서 원자적으로 소비해 발급한 적 없거나 만료되었거나(`NonceLifetime`, 기본·최대 5분) 이미 썼으면 401 `InvalidNonce` 입니다. 로그인·연동·옮겨오기가 모두 같은 검사를 거칩니다.
- **연동 충돌 티켓**: 연동이 충돌하면(409 `LoginConflict`) id 토큰과 nonce 는 이미 썼으므로, 서버가 무작위 1회용 `conflictTicket` 을 만들어 응답에 넣습니다. 티켓은 그 플랫폼 계정(플랫폼, 고유 ID)과 연동을 요청한 계정에 묶이고 `NonceLifetime` 동안 한 번만 씁니다. 로그인(`{platform, conflictTicket}`)은 티켓만으로 받고(티켓은 인증된 연동 요청의 응답으로만 나감), 옮겨오기는 티켓을 받은 계정의 요청만 받습니다(다른 계정이 보내면 401 `InvalidConflictTicket` 이고 티켓은 소비됨). 티켓과 자격·nonce 를 함께 보내거나 `POST links` 에 티켓을 보내면 400 입니다.
- **애플·구글**: `AddApple(o => o.Audiences.Add("com.company.game"))`(iOS 번들 ID), `AddGoogle(o => { o.ClientIds.Add(웹 클라이언트 ID); o.ClientIds.Add(iOS 클라이언트 ID); })`. 자격은 id 토큰(JWT)이고 발급자 공개 키(OIDC JWKS)로 서명, 발급자, 대상, 만료(기본 시계 오차 5분), `nonce` 클레임을 검사해 `sub` 를 고유 ID 로 씁니다. 공개 키(`OpenIdConnectKeyCache`)는 처음 검증할 때 받아 두고 12시간마다 갱신합니다(실패하면 받아 둔 키를 계속 쓰고 5분 뒤 다시 시도). 모르는 키로 서명된 토큰이 오면 다시 받기를 기다려 한 번 더 확인하므로 발급자의 키 교체 직후에도 첫 요청이 통과합니다. 위조 토큰으로 발급자를 두드리지 않게 다시 받기는 5분에 한 번입니다. 서명 키가 없는 응답은 받기 실패로 보고, 처음 받기가 실패하면 30초 동안은 다시 받지 않고 바로 500 입니다. 공개 키를 확인하지 못하면 500(클라이언트에서는 `HttpError`, 로그인 유지)입니다. 클라이언트 제공자는 [com.geuneda.account.apple](https://github.com/geuneda/geuneda-account-apple), [com.geuneda.account.google](https://github.com/geuneda/geuneda-account-google) 입니다.
- **저장소**: `IAccountStore`(로그인 수단의 고유성, 플랫폼당 하나, 마지막 수단 보호를 원자적으로), `IRefreshTokenStore`(로그인별 `RefreshTokenFamily` 의 현재·직전 토큰과 토큰 기록 `RefreshTokenRecord`. 회전 `TryRotateAsync` 는 현재 토큰을 비교한 뒤 상태 교체와 새 토큰 기록 추가를 한 번에), `IOneTimeValueStore`(nonce 와 충돌 티켓을 용도 `OneTimeValueKind` 와 원문의 SHA-256 으로 구분해 `OneTimeValue` 로 저장. `TryConsumeAsync` 는 원자적 삭제, 데이터베이스는 조건부 삭제·Redis 는 `GETDEL`. 서버가 여러 대면 공유 저장소). 메모리 구현(`AddInMemoryStores`)은 샘플·테스트용이고, `InMemoryOneTimeValueStore` 는 서버 한 대에서 쓸 수 있게 두 용도 합쳐 기록 수 상한(기본 10만)과 만료 정리를 두고(소비한 기록도 상한의 두 배 넘게 붙잡지 않음) 가득 차면 새 값을 거절합니다(503). 저장소는 scoped 로 등록해도 됩니다.
- 시작할 때(`MapGeunedaAccount`) 설정(키로 서명 가능한지 포함), 저장소 등록, 검증기 이름 중복을 검사해 잘못되면 예외입니다.
- 요청 횟수 제한은 하지 않습니다. `POST nonce` 는 인증 없이 부르므로 운영에서는 ASP.NET Core rate limiter 를 이 그룹에 꼭 붙입니다. 로그에는 토큰·자격·nonce 원문을 남기지 않습니다.
- 샘플: `dotnet run --project "Server~/samples/Geuneda.Account.SampleServer" --no-launch-profile -- --urls http://127.0.0.1:5299`(메모리 저장소, 게스트, 시험용 `sample` 플랫폼(자격 `sample:<id>:<nonce>`, nonce 흐름 시험용), 보호된 `GET /game/me` 예시, 시작할 때마다 새 서명 키).

## 요구 사항

- Unity 2021.3 이상
- `com.geuneda.core` 0.2.0, `com.geuneda.http` 0.1.0, `com.cysharp.unitask` 2.5.10, `com.unity.nuget.newtonsoft-json` 3.2.1
- 서버: .NET 10(ASP.NET Core), Microsoft.IdentityModel.JsonWebTokens·Protocols.OpenIdConnect 8.22

변경 이력은 [CHANGELOG.md](CHANGELOG.md) 를 참고하세요.

## 라이선스

MIT. [LICENSE.md](LICENSE.md) 를 참고하세요.
