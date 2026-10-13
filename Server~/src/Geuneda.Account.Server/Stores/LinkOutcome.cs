namespace Geuneda.Account.Server
{
	/// <summary>연동·옮겨오기 결과입니다.</summary>
	/// <param name="OtherAccountId"><see cref="LinkStatus.LinkedToOtherAccount"/> 일 때 그 계정 ID</param>
	public readonly record struct LinkOutcome(LinkStatus Status, string OtherAccountId = null);
}
