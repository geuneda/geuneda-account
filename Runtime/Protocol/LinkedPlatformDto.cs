// ReSharper disable once CheckNamespace

namespace Geuneda.Account
{
	/// <summary>연동된 로그인 수단 하나입니다.</summary>
	[Preserve]
	internal sealed class LinkedPlatformDto
	{
		[Preserve]
		public string Platform { get; set; }

		/// <summary>연동한 시각(Unix 밀리초, UTC)</summary>
		[Preserve]
		public long LinkedAt { get; set; }
	}
}
