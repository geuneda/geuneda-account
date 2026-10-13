using System;

// ReSharper disable once CheckNamespace

namespace Geuneda.Account
{
	/// <summary>패딩 없는 Base64url(RFC 4648 5절)입니다.</summary>
	internal static class AccountBase64
	{
		public static string UrlEncode(byte[] bytes)
		{
			return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
		}
	}
}
