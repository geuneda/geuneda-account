// ReSharper disable once CheckNamespace

namespace Geuneda.Account
{
	/// <summary>nonce 발급 응답입니다. 한 번만 쓸 수 있고, 로그인·연동·옮겨오기 요청에서 검증하면 소비됩니다.</summary>
	[Preserve]
	internal sealed class NonceResponse
	{
		/// <summary>nonce 원문(Base64url 43자). 제공자가 id 토큰에 묶고(애플은 SHA-256 16진 소문자로 요청), 요청의 <c>nonce</c> 로 그대로 보냅니다.</summary>
		[Preserve]
		public string Nonce { get; set; }

		/// <summary>남은 유효 시간(초)</summary>
		[Preserve]
		public int ExpiresIn { get; set; }
	}
}
