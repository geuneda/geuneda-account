namespace Geuneda.Account.Server
{
	/// <summary><see cref="IAccountStore.FindOrCreateAsync"/> 의 결과입니다.</summary>
	/// <param name="AccountId">찾았거나 만든 계정</param>
	/// <param name="Created">이번에 새로 만들었으면 true</param>
	public readonly record struct AccountLookup(string AccountId, bool Created);
}
