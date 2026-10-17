using System;
using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace Geuneda.Account.Server
{
	/// <summary>1회용 값(nonce, 충돌 티켓) 원문 생성과 해시입니다.</summary>
	internal static class OneTimeValues
	{
		private const int ValueBytes = 32;

		/// <summary>추측할 수 없는 무작위 값(Base64url 43자). URL·JSON 에 그대로 넣을 수 있습니다.</summary>
		public static string NewValue()
		{
			return Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(ValueBytes));
		}

		/// <summary>SHA-256(UTF-8) 16진 소문자입니다. 저장소 키이자 애플 id 토큰의 <c>nonce</c> 클레임 형식입니다.</summary>
		public static string Hash(string value)
		{
			return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
		}
	}
}
