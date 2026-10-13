using Geuneda.Core;

// ReSharper disable once CheckNamespace

namespace Geuneda.Account
{
	/// <summary>보안 저장소 키입니다. 빌드 환경(dev/qa/prod)마다 따로 둬서 환경을 바꿔도 토큰이 섞이지 않습니다.</summary>
	internal static class AccountStorageKeys
	{
		public static string Login => "geuneda.account." + AppEnv.Name + ".login";

		public static string GuestSecret => "geuneda.account." + AppEnv.Name + ".guestSecret";
	}
}
