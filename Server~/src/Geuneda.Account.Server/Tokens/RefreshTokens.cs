using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace Geuneda.Account.Server
{
	/// <summary>refresh 토큰 원문 생성과 저장용 해시입니다.</summary>
	internal static class RefreshTokens
	{
		private const int TokenBytes = 32;

		/// <summary>추측할 수 없는 무작위 토큰(Base64url 43자)</summary>
		public static string NewToken()
		{
			return Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(TokenBytes));
		}

		public static string Hash(string token)
		{
			return Base64Url.EncodeToString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
		}

		public static string NewFamilyId()
		{
			return Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(16));
		}
	}
}
