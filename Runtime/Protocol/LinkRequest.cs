// ReSharper disable once CheckNamespace

namespace Geuneda.Account
{
	/// <summary>연동(또는 다른 계정에서 옮겨오기) 요청입니다.</summary>
	[Preserve]
	internal sealed class LinkRequest
	{
		/// <summary><see cref="AccountPlatforms"/> 의 이름</summary>
		[Preserve]
		public string Platform { get; set; }

		/// <summary>플랫폼 자격. 서버가 검증합니다.</summary>
		[Preserve]
		public string Credential { get; set; }
	}
}
