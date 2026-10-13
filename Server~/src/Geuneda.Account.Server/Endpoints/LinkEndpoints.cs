using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Geuneda.Account.Server
{
	/// <summary>연동 목록·연동·옮겨오기·해제와 계정 삭제입니다. access 토큰이 필요합니다(<see cref="AccessTokenFilter"/>).</summary>
	internal sealed class LinkEndpoints
	{
		private readonly PlatformVerifierRegistry _verifiers;
		private readonly IAccountStore _accounts;
		private readonly IRefreshTokenStore _refreshTokens;
		private readonly TimeProvider _time;
		private readonly ILogger<LinkEndpoints> _logger;

		public LinkEndpoints(PlatformVerifierRegistry verifiers, IAccountStore accounts, IRefreshTokenStore refreshTokens, TimeProvider time,
			ILogger<LinkEndpoints> logger)
		{
			_verifiers = verifiers;
			_accounts = accounts;
			_refreshTokens = refreshTokens;
			_time = time;
			_logger = logger;
		}

		public async Task<IResult> GetLinksAsync(HttpContext context)
		{
			var links = await _accounts.GetLinksAsync(AccessTokenFilter.AccountId(context), context.RequestAborted);
			return links == null ? AccountErrors.AccountNotFound() : LinksResult(links);
		}

		public Task<IResult> LinkAsync(HttpContext context)
		{
			return LinkOrTransferAsync(context, transfer: false);
		}

		public Task<IResult> TransferAsync(HttpContext context)
		{
			return LinkOrTransferAsync(context, transfer: true);
		}

		public async Task<IResult> UnlinkAsync(HttpContext context, string platform)
		{
			if (!RequestFields.IsPlatform(platform))
			{
				return AccountErrors.BadRequest("Invalid platform");
			}

			var accountId = AccessTokenFilter.AccountId(context);
			var outcome = await _accounts.UnlinkAsync(accountId, platform, context.RequestAborted);

			switch (outcome)
			{
				case UnlinkOutcome.AccountNotFound:
					return AccountErrors.AccountNotFound();
				case UnlinkOutcome.LastLogin:
					return AccountErrors.LastLogin();
				default:
					// 해제했거나 원래 없었음
					return await GetLinksAsync(context);
			}
		}

		public async Task<IResult> DeleteAccountAsync(HttpContext context)
		{
			var accountId = AccessTokenFilter.AccountId(context);

			if (!await _accounts.DeleteAsync(accountId, context.RequestAborted))
			{
				return AccountErrors.AccountNotFound();
			}

			await _refreshTokens.RevokeAccountAsync(accountId, _time.GetUtcNow(), context.RequestAborted);
			_logger.LogInformation("Account {AccountId} deleted", accountId);
			return Results.NoContent();
		}

		private async Task<IResult> LinkOrTransferAsync(HttpContext context, bool transfer)
		{
			var cancellationToken = context.RequestAborted;
			var request = await RequestBody.ReadAsync<LinkRequest>(context.Request, cancellationToken);

			if (request == null || !RequestFields.IsPlatform(request.Platform) || !RequestFields.IsCredential(request.Credential))
			{
				return AccountErrors.BadRequest("Link needs platform and credential");
			}

			var verifier = _verifiers.Find(request.Platform);

			if (verifier == null)
			{
				return AccountErrors.UnsupportedPlatform(request.Platform);
			}

			var verification = await verifier.VerifyAsync(request.Credential, cancellationToken);

			if (!verification.IsValid)
			{
				return AccountErrors.InvalidCredential(verification.FailureReason);
			}

			var accountId = AccessTokenFilter.AccountId(context);
			var now = _time.GetUtcNow();
			var outcome = transfer
				? await _accounts.TransferAsync(accountId, request.Platform, verification.Subject, now, cancellationToken)
				: await _accounts.LinkAsync(accountId, request.Platform, verification.Subject, now, cancellationToken);

			switch (outcome.Status)
			{
				case LinkStatus.AccountNotFound:
					return AccountErrors.AccountNotFound();
				case LinkStatus.PlatformAlreadyLinked:
					return AccountErrors.PlatformAlreadyLinked(request.Platform);
				case LinkStatus.LinkedToOtherAccount:
					return AccountErrors.LoginConflict(outcome.OtherAccountId);
				default:
					if (transfer)
					{
						_logger.LogInformation("Platform {Platform} login moved to account {AccountId}", request.Platform, accountId);
					}

					return await GetLinksAsync(context);
			}
		}

		private static IResult LinksResult(IEnumerable<AccountLink> links)
		{
			return Results.Json(new LinksResponse
			{
				Links = links.Select(link => new LinkedPlatformDto
				{
					Platform = link.Platform,
					LinkedAt = link.LinkedAt.ToUnixTimeMilliseconds()
				}).ToList()
			}, AccountJson.Options);
		}
	}
}
