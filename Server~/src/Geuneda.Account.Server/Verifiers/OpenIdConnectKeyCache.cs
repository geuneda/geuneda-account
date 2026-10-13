using System;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace Geuneda.Account.Server
{
	/// <summary>
	/// 발급자(애플·구글)의 OpenID Connect 메타데이터와 공개 키(JWKS)를 받아 두는 캐시입니다.
	/// IdentityModel 의 <c>ConfigurationManager</c> 는 처음 이후의 갱신을 백그라운드에서 해 기다릴 수 없으므로, 키 교체 직후의 정상 토큰을 첫 요청에서 받으려고 직접 받습니다.
	/// </summary>
	/// <remarks>
	/// <para>받은 지 <see cref="AutomaticRefreshInterval"/> 가 지나면 다음 요청이 다시 받습니다. 이때 실패하면 받아 둔 키를 계속 쓰고 <see cref="RefreshInterval"/> 뒤에 다시 시도합니다.</para>
	/// <para>모르는 키로 서명된 토큰이 오면 <see cref="RefreshAsync"/> 로 다시 받되, <see cref="RefreshInterval"/> 안에 이미 받았으면 받지 않습니다(위조 토큰으로 발급자를 두드리지 않게).</para>
	/// <para>아직 키가 없는데 받기가 실패하면 <see cref="RetryIntervalWithoutKeys"/> 동안은 다시 받지 않고 바로 실패합니다(발급자 장애 중 요청마다 기다리지 않게).</para>
	/// <para>서명 키가 하나도 없는 응답은 받기 실패로 봅니다(받아 둔 키를 빈 목록으로 덮어 정상 토큰을 거절하지 않게).</para>
	/// <para>동시에 받기를 요청하면 한 번만 받고 그 결과를 함께 씁니다. 한 번 받는 데 <see cref="RetrieveTimeout"/> 까지 기다립니다.</para>
	/// </remarks>
	public sealed class OpenIdConnectKeyCache
	{
		/// <summary>받아 둔 키를 이 간격마다 다시 받습니다.</summary>
		public static readonly TimeSpan AutomaticRefreshInterval = TimeSpan.FromHours(12);
		/// <summary>받기를 이 간격보다 자주 시도하지 않습니다(자동 갱신 실패 뒤의 재시도, 모르는 키로 다시 받기).</summary>
		public static readonly TimeSpan RefreshInterval = TimeSpan.FromMinutes(5);
		/// <summary>아직 키가 없는데 받기가 실패했을 때 다시 시도하기까지의 간격입니다. 짧은 일시 장애로 오래 막히지 않게 <see cref="RefreshInterval"/> 보다 짧습니다.</summary>
		public static readonly TimeSpan RetryIntervalWithoutKeys = TimeSpan.FromSeconds(30);
		/// <summary>한 번 받는 데 기다리는 최대 시간입니다.</summary>
		public static readonly TimeSpan RetrieveTimeout = TimeSpan.FromSeconds(10);

		private readonly string _metadataAddress;
		private readonly IDocumentRetriever _documents;
		private readonly TimeProvider _time;
		private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);
		private OpenIdConnectKeySnapshot _snapshot = OpenIdConnectKeySnapshot.Empty;

		/// <param name="metadataAddress">발급자의 OpenID Connect 메타데이터 주소(<c>.well-known/openid-configuration</c>)입니다.</param>
		/// <param name="documents">문서를 받는 방법입니다. 운영은 <see cref="HttpDocumentRetriever"/> 입니다.</param>
		/// <param name="time">갱신 시점을 판단할 시계입니다.</param>
		public OpenIdConnectKeyCache(string metadataAddress, IDocumentRetriever documents, TimeProvider time)
		{
			if (string.IsNullOrWhiteSpace(metadataAddress))
			{
				throw new ArgumentException("A metadata address is required", nameof(metadataAddress));
			}

			_metadataAddress = metadataAddress;
			_documents = documents ?? throw new ArgumentNullException(nameof(documents));
			_time = time ?? throw new ArgumentNullException(nameof(time));
		}

		/// <summary>받아 둔 설정을 돌려줍니다. 처음이거나 오래됐으면 받습니다.</summary>
		/// <exception cref="InvalidOperationException">아직 받은 설정이 없는데 받지 못한 경우(발급자 장애, 네트워크, 서명 키 없음)</exception>
		public Task<OpenIdConnectConfiguration> GetAsync(CancellationToken cancellationToken)
		{
			var snapshot = Volatile.Read(ref _snapshot);
			var now = _time.GetUtcNow();

			if (snapshot.Configuration == null)
			{
				return snapshot.AttemptFailed && now < snapshot.AttemptedAt + RetryIntervalWithoutKeys
					? Task.FromException<OpenIdConnectConfiguration>(Unavailable(null))
					: RetrieveAsync(snapshot, false, cancellationToken);
			}

			var due = now >= snapshot.RetrievedAt + AutomaticRefreshInterval && now >= snapshot.AttemptedAt + RefreshInterval;
			return due ? RetrieveAsync(snapshot, false, cancellationToken) : Task.FromResult(snapshot.Configuration);
		}

		/// <summary>
		/// <paramref name="seen"/> 에 없는 키로 서명된 토큰이 왔을 때 다시 받습니다. 그 사이 다른 요청이 새로 받았으면 그 설정을,
		/// <see cref="RefreshInterval"/> 안에 이미 받았으면 <paramref name="seen"/> 을 그대로 돌려줍니다(정말 모르는 키).
		/// </summary>
		/// <exception cref="InvalidOperationException">키를 확인하지 못한 경우(지금 또는 최근의 받기 실패). 자격 오류가 아니라 서버 오류입니다.</exception>
		public Task<OpenIdConnectConfiguration> RefreshAsync(OpenIdConnectConfiguration seen, CancellationToken cancellationToken)
		{
			var snapshot = Volatile.Read(ref _snapshot);

			if (snapshot.Configuration != null && !ReferenceEquals(snapshot.Configuration, seen))
			{
				return Task.FromResult(snapshot.Configuration);
			}

			if (_time.GetUtcNow() < snapshot.AttemptedAt + RefreshInterval)
			{
				return snapshot.AttemptFailed ? Task.FromException<OpenIdConnectConfiguration>(Unavailable(null)) : Task.FromResult(seen);
			}

			return RetrieveAsync(snapshot, true, cancellationToken);
		}

		private async Task<OpenIdConnectConfiguration> RetrieveAsync(OpenIdConnectKeySnapshot observed, bool forced, CancellationToken cancellationToken)
		{
			await _gate.WaitAsync(cancellationToken);

			try
			{
				var current = _snapshot;

				if (!ReferenceEquals(current, observed))
				{
					// 기다리는 동안 다른 요청이 받았다(또는 받다 실패했다). 그 결과를 함께 쓴다
					return Settle(current, forced, null);
				}

				var now = _time.GetUtcNow();
				OpenIdConnectConfiguration configuration;
				Exception failure;

				try
				{
					using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
					timeout.CancelAfter(RetrieveTimeout);
					configuration = await OpenIdConnectConfigurationRetriever.GetAsync(_metadataAddress, _documents, timeout.Token);
					failure = configuration.SigningKeys.Count == 0 ? new InvalidDataException("The metadata has no signing keys") : null;
				}
				catch (Exception exception) when (IsRetrievalFailure(exception, cancellationToken))
				{
					configuration = null;
					failure = exception;
				}

				if (failure == null)
				{
					Volatile.Write(ref _snapshot, new OpenIdConnectKeySnapshot(configuration, now, now, false));
					return configuration;
				}

				var failed = current with { AttemptedAt = now, AttemptFailed = true };
				Volatile.Write(ref _snapshot, failed);
				return Settle(failed, forced, failure);
			}
			finally
			{
				_gate.Release();
			}
		}

		// 발급자 장애·잘못된 문서·받기 시간 초과만 받기 실패로 본다. 그 밖(구현 버그)은 숨기지 않고 그대로 던진다.
		// 문서 해석 오류는 IdentityModel 이 ArgumentException(JWKS, 빈 문서는 ArgumentNullException)이나 JsonException(메타데이터)으로 던진다
		private static bool IsRetrievalFailure(Exception exception, CancellationToken cancellationToken)
		{
			return exception is IOException || exception is HttpRequestException || exception is JsonException || exception is ArgumentException
				|| (exception is OperationCanceledException && !cancellationToken.IsCancellationRequested);
		}

		// 받기 결과를 고른다. 실패했어도 자동 갱신이면 받아 둔 키를 계속 쓰고, 다시 받기(모르는 키)나 처음 받기면 확인할 수 없다
		private OpenIdConnectConfiguration Settle(OpenIdConnectKeySnapshot snapshot, bool forced, Exception exception)
		{
			if (!snapshot.AttemptFailed || (!forced && snapshot.Configuration != null))
			{
				return snapshot.Configuration;
			}

			throw Unavailable(exception);
		}

		private InvalidOperationException Unavailable(Exception exception)
		{
			return new InvalidOperationException($"OpenID Connect keys from {_metadataAddress} are unavailable", exception);
		}
	}
}
