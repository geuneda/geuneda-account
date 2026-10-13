// ReSharper disable once CheckNamespace

namespace Geuneda.Account
{
	/// <summary>로그인 결과입니다.</summary>
	public sealed class SignInResult
	{
		public string AccountId { get; }

		/// <summary>이 로그인으로 계정을 새로 만들었으면 true 입니다(튜토리얼, 약관 동의 등). 자동 로그인에서는 항상 false 입니다.</summary>
		public bool IsNewAccount { get; }

		/// <summary>
		/// 이번에 받은 로그인을 이 기기에 저장했는지입니다(보안 저장소 오류면 false). false 면 이번 실행에서는 로그인 상태지만 다음 실행의 자동 로그인을 보장하지 못합니다.
		/// 이때 이전 저장값은 지워 다른 계정으로 자동 로그인되지 않게 하고, 지우지도 못했는데 저장소에 다른 계정이 남았거나 무엇이 있는지 모르면
		/// 로그인은 <see cref="AccountResultCodes.StorageFailed"/> 로 거절됩니다.
		/// </summary>
		public bool IsSaved { get; }

		public SignInResult(string accountId, bool isNewAccount, bool isSaved = true)
		{
			AccountId = accountId;
			IsNewAccount = isNewAccount;
			IsSaved = isSaved;
		}
	}
}
