// ReSharper disable once CheckNamespace

namespace Geuneda.Account
{
	/// <summary>토큰 재발급 요청입니다. 쓴 refresh 토큰은 폐기되고 새 토큰이 옵니다(회전).</summary>
	[Preserve]
	internal sealed class RefreshRequest
	{
		[Preserve]
		public string RefreshToken { get; set; }

		/// <summary>새 access 토큰의 <c>sid</c>. 세션 중이면 현재 세션 ID, 아니면 곧 시작할 세션 ID 입니다. 없어도 됩니다.</summary>
		[Preserve]
		public string SessionId { get; set; }
	}
}
