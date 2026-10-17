using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;

namespace Geuneda.Account.Server
{
	/// <summary>
	/// 로그인·연동·옮겨오기의 플랫폼 자격과 연동 충돌 티켓을 확인합니다.
	/// 검증기가 nonce 를 요구하면(<see cref="IPlatformVerifier.RequiresNonce"/>) 자격이 요청의 nonce 에 묶였는지는 검증기가 보고, 그 nonce 가 발급한 적 있고
	/// 만료되지 않았고 아직 쓰지 않은 것인지는 여기서 저장소로 확인해 소비합니다. 검증을 통과한 뒤에 소비하므로 서명이 틀린 토큰은 nonce 를 쓰지 못하게 만들지 못합니다.
	/// </summary>
	internal sealed class PlatformCredentials
	{
		private readonly AccountServerOptions _options;
		private readonly PlatformVerifierRegistry _verifiers;
		private readonly IOneTimeValueStore _oneTimeValues;
		private readonly TimeProvider _time;

		public PlatformCredentials(IOptions<AccountServerOptions> options, PlatformVerifierRegistry verifiers, IOneTimeValueStore oneTimeValues, TimeProvider time)
		{
			_options = options.Value;
			_verifiers = verifiers;
			_oneTimeValues = oneTimeValues;
			_time = time;
		}

		public async Task<CredentialCheck> VerifyAsync(string platform, string credential, string nonce, CancellationToken cancellationToken)
		{
			var verifier = _verifiers.Find(platform);

			if (verifier == null)
			{
				return CredentialCheck.Fail(AccountErrors.UnsupportedPlatform(platform), null);
			}

			if (verifier.RequiresNonce && nonce == null)
			{
				return CredentialCheck.Fail(AccountErrors.NonceMissing(platform), "missing nonce");
			}

			var verification = await verifier.VerifyAsync(credential, verifier.RequiresNonce ? nonce : null, cancellationToken);

			if (!verification.IsValid)
			{
				var error = verification.IsNonceMismatch
					? AccountErrors.NonceMismatch(verification.FailureReason)
					: AccountErrors.InvalidCredential(verification.FailureReason);
				return CredentialCheck.Fail(error, verification.FailureReason);
			}

			if (verifier.RequiresNonce &&
				await _oneTimeValues.TryConsumeAsync(OneTimeValueKind.Nonce, OneTimeValues.Hash(nonce), _time.GetUtcNow(), cancellationToken) == null)
			{
				return CredentialCheck.Fail(AccountErrors.InvalidNonce(), "nonce was not issued, has expired or was already used");
			}

			return CredentialCheck.Ok(verification.Subject);
		}

		/// <summary>
		/// 연동 충돌 티켓을 만듭니다. 티켓은 그 플랫폼 계정과 연동을 요청한 계정에 묶이고 nonce 와 같은 시간(<see cref="AccountServerOptions.NonceLifetime"/>) 동안
		/// 한 번만 쓸 수 있습니다. 저장소가 가득 차 넣지 못하면 null 입니다.
		/// </summary>
		public async Task<string> IssueConflictTicketAsync(string platform, string subject, string accountId, CancellationToken cancellationToken)
		{
			var ticket = OneTimeValues.NewValue();
			var now = _time.GetUtcNow();
			var value = new OneTimeValue
			{
				Kind = OneTimeValueKind.ConflictTicket,
				Hash = OneTimeValues.Hash(ticket),
				ExpiresAt = now + _options.NonceLifetime,
				Platform = platform,
				Subject = subject,
				AccountId = accountId
			};
			return await _oneTimeValues.TryAddAsync(value, now, cancellationToken) ? ticket : null;
		}

		/// <summary>
		/// 연동 충돌 티켓을 소비하고 그 플랫폼 계정의 고유 ID 를 돌려줍니다. 플랫폼이 다르거나 <paramref name="accountId"/> 가 티켓을 받은 계정이 아니면 거절합니다
		/// (그때도 티켓은 소비되어 새어 나간 티켓을 더 못 씀).
		/// </summary>
		/// <param name="accountId">옮겨오기를 요청한 계정. 로그인이면 null 이고 계정을 보지 않습니다(티켓은 인증된 연동 요청의 응답으로만 나감).</param>
		public async Task<CredentialCheck> RedeemConflictTicketAsync(string platform, string ticket, string accountId, CancellationToken cancellationToken)
		{
			var value = await _oneTimeValues.TryConsumeAsync(OneTimeValueKind.ConflictTicket, OneTimeValues.Hash(ticket), _time.GetUtcNow(), cancellationToken);

			if (value == null || value.Platform != platform || (accountId != null && value.AccountId != accountId))
			{
				return CredentialCheck.Fail(AccountErrors.InvalidConflictTicket(), "conflict ticket was not issued, has expired, was already used or belongs to another account");
			}

			return CredentialCheck.Ok(value.Subject);
		}
	}
}
