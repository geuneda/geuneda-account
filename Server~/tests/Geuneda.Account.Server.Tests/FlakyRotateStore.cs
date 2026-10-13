using System;
using System.Threading;
using System.Threading.Tasks;

namespace Geuneda.Account.Server.Tests
{
	/// <summary>회전의 비교 후 교체를 정해진 횟수만큼 실패시키는 저장소입니다(다른 요청에 계속 지는 경쟁 재현).</summary>
	internal sealed class FlakyRotateStore : IRefreshTokenStore
	{
		private readonly InMemoryRefreshTokenStore _inner = new InMemoryRefreshTokenStore();

		public int FailRotations;

		public Task CreateAsync(RefreshTokenFamily family, RefreshTokenRecord token, CancellationToken cancellationToken) =>
			_inner.CreateAsync(family, token, cancellationToken);

		public Task<RefreshTokenRecord> FindTokenAsync(string tokenHash, CancellationToken cancellationToken) =>
			_inner.FindTokenAsync(tokenHash, cancellationToken);

		public Task<RefreshTokenFamily> FindFamilyAsync(string familyId, CancellationToken cancellationToken) =>
			_inner.FindFamilyAsync(familyId, cancellationToken);

		public Task<bool> TryRotateAsync(string familyId, string expectedCurrentTokenHash, RefreshTokenFamily updated, RefreshTokenRecord newToken,
			CancellationToken cancellationToken)
		{
			if (Interlocked.Decrement(ref FailRotations) >= 0)
			{
				return Task.FromResult(false);
			}

			return _inner.TryRotateAsync(familyId, expectedCurrentTokenHash, updated, newToken, cancellationToken);
		}

		public Task RevokeFamilyAsync(string familyId, DateTimeOffset now, CancellationToken cancellationToken) =>
			_inner.RevokeFamilyAsync(familyId, now, cancellationToken);

		public Task RevokeAccountAsync(string accountId, DateTimeOffset now, CancellationToken cancellationToken) =>
			_inner.RevokeAccountAsync(accountId, now, cancellationToken);
	}
}
