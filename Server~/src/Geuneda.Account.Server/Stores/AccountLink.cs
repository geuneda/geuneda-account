using System;

namespace Geuneda.Account.Server
{
	/// <summary>계정에 연동된 로그인 수단입니다. <paramref name="Subject"/> 는 플랫폼 검증기가 돌려준 고유 ID 입니다.</summary>
	public sealed record AccountLink(string Platform, string Subject, DateTimeOffset LinkedAt);
}
