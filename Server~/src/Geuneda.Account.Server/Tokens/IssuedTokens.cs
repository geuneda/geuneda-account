namespace Geuneda.Account.Server
{
	/// <summary>새로 만든 토큰 한 쌍입니다. refresh 토큰 기록은 아직 저장하지 않은 상태입니다.</summary>
	internal sealed record IssuedTokens(string AccessToken, string RefreshToken, RefreshTokenRecord RefreshRecord);
}
