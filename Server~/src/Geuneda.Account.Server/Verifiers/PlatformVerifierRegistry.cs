using System;
using System.Collections.Generic;
using System.Linq;

namespace Geuneda.Account.Server
{
	/// <summary>등록된 플랫폼 검증기를 이름으로 찾습니다.</summary>
	internal sealed class PlatformVerifierRegistry
	{
		private readonly Dictionary<string, IPlatformVerifier> _verifiers;

		/// <exception cref="InvalidOperationException">검증기가 없거나, 이름이 비었거나, 같은 이름이 둘 이상인 경우</exception>
		public PlatformVerifierRegistry(IEnumerable<IPlatformVerifier> verifiers)
		{
			var list = verifiers.ToArray();

			if (list.Length == 0)
			{
				throw new InvalidOperationException("No platform verifier is registered. Call AddGuest() or AddPlatformVerifier<T>()");
			}

			_verifiers = new Dictionary<string, IPlatformVerifier>(StringComparer.Ordinal);

			foreach (var verifier in list)
			{
				if (string.IsNullOrEmpty(verifier.Platform))
				{
					throw new InvalidOperationException($"{verifier.GetType().Name}.Platform is empty");
				}

				if (!_verifiers.TryAdd(verifier.Platform, verifier))
				{
					throw new InvalidOperationException($"Platform '{verifier.Platform}' has more than one verifier");
				}
			}
		}

		public IPlatformVerifier Find(string platform)
		{
			return platform != null && _verifiers.TryGetValue(platform, out var verifier) ? verifier : null;
		}
	}
}
