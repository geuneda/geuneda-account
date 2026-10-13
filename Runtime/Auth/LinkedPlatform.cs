using System;

// ReSharper disable once CheckNamespace

namespace Geuneda.Account
{
	/// <summary>계정에 연동된 로그인 수단 하나입니다.</summary>
	public sealed class LinkedPlatform
	{
		/// <summary><see cref="AccountPlatforms"/> 의 이름</summary>
		public string Platform { get; }

		public DateTimeOffset LinkedAt { get; }

		public LinkedPlatform(string platform, DateTimeOffset linkedAt)
		{
			Platform = platform;
			LinkedAt = linkedAt;
		}
	}
}
