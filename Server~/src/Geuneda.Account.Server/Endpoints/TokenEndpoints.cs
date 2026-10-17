using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Geuneda.Account.Server
{
	/// <summary>로그인, 재발급, 로그아웃입니다. 요청마다 DI 범위에서 만듭니다(저장소가 scoped 일 수 있음).</summary>
	internal sealed class TokenEndpoints
	{
		// 같은 로그인의 동시 재발급에서 비교 후 교체에 진 요청이 다시 읽는 횟수
		private const int MaxRotateAttempts = 3;

		private readonly AccountServerOptions _options;
		private readonly PlatformCredentials _credentials;
		private readonly IAccountStore _accounts;
		private readonly IRefreshTokenStore _refreshTokens;
		private readonly AccountTokenIssuer _issuer;
		private readonly TimeProvider _time;
		private readonly ILogger<TokenEndpoints> _logger;

		public TokenEndpoints(IOptions<AccountServerOptions> options, PlatformCredentials credentials, IAccountStore accounts,
			IRefreshTokenStore refreshTokens, AccountTokenIssuer issuer, TimeProvider time, ILogger<TokenEndpoints> logger)
		{
			_options = options.Value;
			_credentials = credentials;
			_accounts = accounts;
			_refreshTokens = refreshTokens;
			_issuer = issuer;
			_time = time;
			_logger = logger;
		}

		public async Task<IResult> LoginAsync(HttpContext context)
		{
			var cancellationToken = context.RequestAborted;
			var request = await RequestBody.ReadAsync<LoginRequest>(context.Request, cancellationToken);

			if (request == null || !RequestFields.IsPlatform(request.Platform) ||
				!RequestFields.IsCredentialOrTicket(request.Credential, request.Nonce, request.ConflictTicket) || !RequestFields.IsOptionalSessionId(request.SessionId))
			{
				return AccountErrors.BadRequest("Login needs platform, either a credential with an optional nonce or a conflict ticket, and an optional session id");
			}

			// 충돌 티켓은 인증된 연동 요청의 응답으로만 나가므로 로그인은 티켓만으로 받는다
			var check = request.ConflictTicket != null
				? await _credentials.RedeemConflictTicketAsync(request.Platform, request.ConflictTicket, null, cancellationToken)
				: await _credentials.VerifyAsync(request.Platform, request.Credential, request.Nonce, cancellationToken);

			if (check.Error != null)
			{
				if (check.FailureReason != null)
				{
					_logger.LogInformation("Login rejected for platform {Platform}: {Reason}", request.Platform, check.FailureReason);
				}

				return check.Error;
			}

			var now = _time.GetUtcNow();
			var account = await _accounts.FindOrCreateAsync(request.Platform, check.Subject, now, cancellationToken);
			var tokens = _issuer.Issue(account.AccountId, request.SessionId, RefreshTokens.NewFamilyId(), now);
			var family = new RefreshTokenFamily
			{
				FamilyId = tokens.RefreshRecord.FamilyId,
				AccountId = account.AccountId,
				SessionId = request.SessionId,
				CurrentTokenHash = tokens.RefreshRecord.TokenHash,
				CreatedAt = now
			};
			await _refreshTokens.CreateAsync(family, tokens.RefreshRecord, cancellationToken);

			if (account.Created)
			{
				_logger.LogInformation("Account {AccountId} created with platform {Platform}", account.AccountId, request.Platform);
			}

			return Results.Json(_issuer.ToResponse(account.AccountId, tokens, account.Created), AccountJson.Options);
		}

		public async Task<IResult> RefreshAsync(HttpContext context)
		{
			var cancellationToken = context.RequestAborted;
			var request = await RequestBody.ReadAsync<RefreshRequest>(context.Request, cancellationToken);

			if (request == null || !RequestFields.IsRefreshToken(request.RefreshToken) || !RequestFields.IsOptionalSessionId(request.SessionId))
			{
				return AccountErrors.BadRequest("Refresh needs a refresh token and an optional session id");
			}

			var hash = RefreshTokens.Hash(request.RefreshToken);

			for (var attempt = 0; attempt < MaxRotateAttempts; attempt++)
			{
				var now = _time.GetUtcNow();
				var token = await _refreshTokens.FindTokenAsync(hash, cancellationToken);
				var family = token == null ? null : await _refreshTokens.FindFamilyAsync(token.FamilyId, cancellationToken);

				if (family == null || family.RevokedAt != null || now >= token.ExpiresAt || !await _accounts.ExistsAsync(family.AccountId, cancellationToken))
				{
					return AccountErrors.InvalidRefreshToken();
				}

				var isCurrent = family.CurrentTokenHash == hash;

				// 직전 토큰은 처음 교체한 시각부터 유예 시간 안에만 응답을 잃은 재시도로 받는다(재시도가 유예를 늘리지 않음)
				var isRetry = !isCurrent && family.PreviousTokenHash == hash && family.RotatedAt != null &&
					now - family.RotatedAt.Value < _options.RefreshReuseGrace;

				if (!isCurrent && !isRetry)
				{
					// 이미 지나간 토큰이 다시 왔다. 탈취로 보고 이 로그인의 토큰을 모두 폐기한다
					await _refreshTokens.RevokeFamilyAsync(family.FamilyId, now, cancellationToken);
					_logger.LogWarning("Refresh token reuse detected for account {AccountId}. Its login was revoked", family.AccountId);
					return AccountErrors.RefreshTokenReused();
				}

				var sessionId = request.SessionId ?? family.SessionId;
				var tokens = _issuer.Issue(family.AccountId, sessionId, family.FamilyId, now);

				// 재시도면 앞서 준 토큰(지금 현재 토큰)을 새 토큰으로 바꾸고 직전 토큰과 교체 시각은 그대로 둔다. 쓸 수 있는 토큰은 언제나 하나다
				var updated = isCurrent
					? family with { PreviousTokenHash = hash, CurrentTokenHash = tokens.RefreshRecord.TokenHash, RotatedAt = now, SessionId = sessionId }
					: family with { CurrentTokenHash = tokens.RefreshRecord.TokenHash, SessionId = sessionId };

				if (!await _refreshTokens.TryRotateAsync(family.FamilyId, family.CurrentTokenHash, updated, tokens.RefreshRecord, cancellationToken))
				{
					// 같은 로그인의 다른 재발급이 먼저 끝났다. 다시 읽는다
					continue;
				}

				return Results.Json(_issuer.ToResponse(family.AccountId, tokens, false), AccountJson.Options);
			}

			// 경쟁에 계속 졌을 뿐 토큰은 유효할 수 있다. 클라이언트가 로그인을 지우지 않게 일시적 실패로 돌려준다
			return AccountErrors.RetryLater("Concurrent refresh of the same login. Retry shortly");
		}

		public async Task<IResult> LogoutAsync(HttpContext context)
		{
			var cancellationToken = context.RequestAborted;
			var request = await RequestBody.ReadAsync<LogoutRequest>(context.Request, cancellationToken);

			if (request == null || !RequestFields.IsRefreshToken(request.RefreshToken))
			{
				return AccountErrors.BadRequest("Logout needs a refresh token");
			}

			var token = await _refreshTokens.FindTokenAsync(RefreshTokens.Hash(request.RefreshToken), cancellationToken);

			if (token != null)
			{
				await _refreshTokens.RevokeFamilyAsync(token.FamilyId, _time.GetUtcNow(), cancellationToken);
			}

			// 토큰이 유효했는지 알려 주지 않는다
			return Results.NoContent();
		}
	}
}
