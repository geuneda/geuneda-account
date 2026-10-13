using System.Collections.Generic;
using Geuneda.Core;

// ReSharper disable once CheckNamespace

namespace Geuneda.Account.Tests
{
	internal sealed class FakeSecureStore : ISecureStore
	{
		public readonly Dictionary<string, string> Values = new Dictionary<string, string>();
		public bool FailRead;
		public bool FailWrite;
		public bool FailDelete;

		public SecureReadResult TryRead(string key, out string value)
		{
			value = null;

			if (FailRead)
			{
				return SecureReadResult.Failed;
			}

			return Values.TryGetValue(key, out value) ? SecureReadResult.Found : SecureReadResult.NotFound;
		}

		public bool TryWrite(string key, string value)
		{
			if (FailWrite)
			{
				return false;
			}

			Values[key] = value;
			return true;
		}

		public bool TryDelete(string key)
		{
			if (FailDelete)
			{
				return false;
			}

			Values.Remove(key);
			return true;
		}
	}
}
