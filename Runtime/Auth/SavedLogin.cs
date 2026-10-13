// ReSharper disable once CheckNamespace

namespace Geuneda.Account
{
	/// <summary>보안 저장소에 두는 로그인(JSON)입니다. access 토큰은 저장하지 않습니다(앱을 켤 때 재발급).</summary>
	[Preserve]
	internal sealed class SavedLogin
	{
		[Preserve]
		public string AccountId { get; set; }
		[Preserve]
		public string RefreshToken { get; set; }
	}
}
