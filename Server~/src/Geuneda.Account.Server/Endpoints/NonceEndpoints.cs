using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Geuneda.Account.Server
{
	/// <summary>로그인·연동 전에 받는 일회용 nonce 를 발급합니다. 인증 없이 부르므로 운영에서는 rate limiter 를 붙입니다.</summary>
	internal sealed class NonceEndpoints
	{
		private readonly AccountServerOptions _options;
		private readonly IOneTimeValueStore _oneTimeValues;
		private readonly TimeProvider _time;
		private readonly ILogger<NonceEndpoints> _logger;

		public NonceEndpoints(IOptions<AccountServerOptions> options, IOneTimeValueStore oneTimeValues, TimeProvider time, ILogger<NonceEndpoints> logger)
		{
			_options = options.Value;
			_oneTimeValues = oneTimeValues;
			_time = time;
			_logger = logger;
		}

		public async Task<IResult> IssueAsync(HttpContext context)
		{
			var nonce = OneTimeValues.NewValue();
			var now = _time.GetUtcNow();
			var value = new OneTimeValue { Kind = OneTimeValueKind.Nonce, Hash = OneTimeValues.Hash(nonce), ExpiresAt = now + _options.NonceLifetime };

			if (!await _oneTimeValues.TryAddAsync(value, now, context.RequestAborted))
			{
				_logger.LogWarning("The one-time value store is full. Attach a rate limiter to the auth endpoints");
				return AccountErrors.RetryLater("Too many pending nonces. Retry shortly");
			}

			return Results.Json(new NonceResponse { Nonce = nonce, ExpiresIn = (int) _options.NonceLifetime.TotalSeconds }, AccountJson.Options);
		}
	}
}
