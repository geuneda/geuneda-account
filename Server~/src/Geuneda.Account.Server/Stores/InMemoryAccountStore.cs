using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Geuneda.Account.Server
{
	/// <summary>
	/// 프로세스 메모리 계정 저장소입니다. 샘플과 테스트용이고, 서버를 다시 시작하면 모두 사라집니다.
	/// </summary>
	public sealed class InMemoryAccountStore : IAccountStore
	{
		private readonly object _gate = new object();
		private readonly Dictionary<string, List<AccountLink>> _accounts = new Dictionary<string, List<AccountLink>>(StringComparer.Ordinal);
		private readonly Dictionary<(string Platform, string Subject), string> _owners = new Dictionary<(string, string), string>();

		public Task<AccountLookup> FindOrCreateAsync(string platform, string subject, DateTimeOffset now, CancellationToken cancellationToken)
		{
			lock (_gate)
			{
				if (_owners.TryGetValue((platform, subject), out var existing))
				{
					return Task.FromResult(new AccountLookup(existing, false));
				}

				var accountId = Guid.NewGuid().ToString("N");
				_accounts[accountId] = new List<AccountLink> { new AccountLink(platform, subject, now) };
				_owners[(platform, subject)] = accountId;
				return Task.FromResult(new AccountLookup(accountId, true));
			}
		}

		public Task<bool> ExistsAsync(string accountId, CancellationToken cancellationToken)
		{
			lock (_gate)
			{
				return Task.FromResult(_accounts.ContainsKey(accountId));
			}
		}

		public Task<IReadOnlyList<AccountLink>> GetLinksAsync(string accountId, CancellationToken cancellationToken)
		{
			lock (_gate)
			{
				IReadOnlyList<AccountLink> links = _accounts.TryGetValue(accountId, out var list) ? list.ToArray() : null;
				return Task.FromResult(links);
			}
		}

		public Task<LinkOutcome> LinkAsync(string accountId, string platform, string subject, DateTimeOffset now, CancellationToken cancellationToken)
		{
			lock (_gate)
			{
				if (!_accounts.TryGetValue(accountId, out var links))
				{
					return Task.FromResult(new LinkOutcome(LinkStatus.AccountNotFound));
				}

				if (_owners.TryGetValue((platform, subject), out var owner))
				{
					return Task.FromResult(owner == accountId
						? new LinkOutcome(LinkStatus.Linked)
						: new LinkOutcome(LinkStatus.LinkedToOtherAccount, owner));
				}

				if (links.Any(link => link.Platform == platform))
				{
					return Task.FromResult(new LinkOutcome(LinkStatus.PlatformAlreadyLinked));
				}

				links.Add(new AccountLink(platform, subject, now));
				_owners[(platform, subject)] = accountId;
				return Task.FromResult(new LinkOutcome(LinkStatus.Linked));
			}
		}

		public Task<LinkOutcome> TransferAsync(string accountId, string platform, string subject, DateTimeOffset now, CancellationToken cancellationToken)
		{
			lock (_gate)
			{
				if (!_accounts.TryGetValue(accountId, out var links))
				{
					return Task.FromResult(new LinkOutcome(LinkStatus.AccountNotFound));
				}

				_owners.TryGetValue((platform, subject), out var owner);

				if (owner == accountId)
				{
					return Task.FromResult(new LinkOutcome(LinkStatus.Linked));
				}

				if (links.Any(link => link.Platform == platform))
				{
					return Task.FromResult(new LinkOutcome(LinkStatus.PlatformAlreadyLinked));
				}

				if (owner != null)
				{
					_accounts[owner].RemoveAll(link => link.Platform == platform && link.Subject == subject);
				}

				links.Add(new AccountLink(platform, subject, now));
				_owners[(platform, subject)] = accountId;
				return Task.FromResult(new LinkOutcome(LinkStatus.Linked));
			}
		}

		public Task<UnlinkOutcome> UnlinkAsync(string accountId, string platform, CancellationToken cancellationToken)
		{
			lock (_gate)
			{
				if (!_accounts.TryGetValue(accountId, out var links))
				{
					return Task.FromResult(UnlinkOutcome.AccountNotFound);
				}

				var link = links.FirstOrDefault(candidate => candidate.Platform == platform);

				if (link == null)
				{
					return Task.FromResult(UnlinkOutcome.NotLinked);
				}

				if (links.Count == 1)
				{
					return Task.FromResult(UnlinkOutcome.LastLogin);
				}

				links.Remove(link);
				_owners.Remove((link.Platform, link.Subject));
				return Task.FromResult(UnlinkOutcome.Unlinked);
			}
		}

		public Task<bool> DeleteAsync(string accountId, CancellationToken cancellationToken)
		{
			lock (_gate)
			{
				if (!_accounts.Remove(accountId, out var links))
				{
					return Task.FromResult(false);
				}

				foreach (var link in links)
				{
					_owners.Remove((link.Platform, link.Subject));
				}

				return Task.FromResult(true);
			}
		}
	}
}
