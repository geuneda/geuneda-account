using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Geuneda.Account.Server
{
	/// <summary>
	/// 프로세스 메모리 refresh 토큰 저장소입니다. 샘플과 테스트용입니다. 만료된 기록을 정리하지 않고, 서버를 다시 시작하면 모두 사라집니다.
	/// </summary>
	public sealed class InMemoryRefreshTokenStore : IRefreshTokenStore
	{
		private readonly object _gate = new object();
		private readonly Dictionary<string, RefreshTokenFamily> _families = new Dictionary<string, RefreshTokenFamily>(StringComparer.Ordinal);
		private readonly Dictionary<string, RefreshTokenRecord> _tokens = new Dictionary<string, RefreshTokenRecord>(StringComparer.Ordinal);

		public Task CreateAsync(RefreshTokenFamily family, RefreshTokenRecord token, CancellationToken cancellationToken)
		{
			ArgumentNullException.ThrowIfNull(family);
			ArgumentNullException.ThrowIfNull(token);

			lock (_gate)
			{
				_families.Add(family.FamilyId, family);
				_tokens.Add(token.TokenHash, token);
			}

			return Task.CompletedTask;
		}

		public Task<RefreshTokenRecord> FindTokenAsync(string tokenHash, CancellationToken cancellationToken)
		{
			lock (_gate)
			{
				return Task.FromResult(_tokens.TryGetValue(tokenHash, out var token) ? token : null);
			}
		}

		public Task<RefreshTokenFamily> FindFamilyAsync(string familyId, CancellationToken cancellationToken)
		{
			lock (_gate)
			{
				return Task.FromResult(_families.TryGetValue(familyId, out var family) ? family : null);
			}
		}

		public Task<bool> TryRotateAsync(string familyId, string expectedCurrentTokenHash, RefreshTokenFamily updated, RefreshTokenRecord newToken,
			CancellationToken cancellationToken)
		{
			ArgumentNullException.ThrowIfNull(updated);
			ArgumentNullException.ThrowIfNull(newToken);

			lock (_gate)
			{
				if (!_families.TryGetValue(familyId, out var family) || family.RevokedAt != null || family.CurrentTokenHash != expectedCurrentTokenHash)
				{
					return Task.FromResult(false);
				}

				_families[familyId] = updated;
				_tokens.Add(newToken.TokenHash, newToken);
				return Task.FromResult(true);
			}
		}

		public Task RevokeFamilyAsync(string familyId, DateTimeOffset now, CancellationToken cancellationToken)
		{
			return RevokeWhere(family => family.FamilyId == familyId, now);
		}

		public Task RevokeAccountAsync(string accountId, DateTimeOffset now, CancellationToken cancellationToken)
		{
			return RevokeWhere(family => family.AccountId == accountId, now);
		}

		private Task RevokeWhere(Func<RefreshTokenFamily, bool> predicate, DateTimeOffset now)
		{
			lock (_gate)
			{
				var targets = _families.Values.Where(family => family.RevokedAt == null && predicate(family)).ToArray();

				foreach (var family in targets)
				{
					_families[family.FamilyId] = family with { RevokedAt = now };
				}
			}

			return Task.CompletedTask;
		}
	}
}
