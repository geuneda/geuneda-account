using System.Collections.Generic;

// ReSharper disable once CheckNamespace

namespace Geuneda.Account
{
	/// <summary>계정에 연동된 로그인 수단 목록입니다.</summary>
	[Preserve]
	internal sealed class LinksResponse
	{
		[Preserve]
		public List<LinkedPlatformDto> Links { get; set; }
	}
}
