using Geuneda.Core;
using Newtonsoft.Json;

// ReSharper disable once CheckNamespace

namespace Geuneda.Account
{
	/// <summary>
	/// 로그인(<see cref="SavedLogin"/>)을 보안 저장소의 키 하나에 JSON 으로 둡니다. 저장소에 지금 무엇이 있는지(비었는지, 어느 계정인지, 모르는지)도
	/// 읽기·쓰기·삭제 결과로 따라갑니다. 메모리의 로그인과 달리, 읽지 못했거나 지우지 못한 이전 계정의 값이 남아 있을 수 있어서입니다.
	/// </summary>
	internal sealed class AuthTokenStore
	{
		private static readonly ModuleLogger Logger = new ModuleLogger("Geuneda.Account");

		private readonly ISecureStore _store;
		private readonly string _key;
		private bool _contentKnown;
		private string _storedAccountId;

		public AuthTokenStore(ISecureStore store, string key)
		{
			_store = store;
			_key = key;
		}

		/// <summary>저장된 로그인을 읽습니다. 형식이 깨진 값은 지우고 NotFound 입니다.</summary>
		public SecureReadResult TryLoad(out SavedLogin login)
		{
			login = null;
			var result = _store.TryRead(_key, out var json);

			if (result != SecureReadResult.Found)
			{
				// NotFound 면 비었음을 알고, Failed 면 무엇이 있는지 모른다
				Remember(result == SecureReadResult.NotFound, null);
				return result;
			}

			try
			{
				login = JsonConvert.DeserializeObject<SavedLogin>(json, AccountApi.JsonSettings);
			}
			catch (JsonException)
			{
				login = null;
			}

			if (login == null || string.IsNullOrEmpty(login.AccountId) || string.IsNullOrEmpty(login.RefreshToken))
			{
				Logger.Warning("Saved login is malformed and was removed");
				login = null;
				_store.TryDelete(_key);

				// 지우지 못해도 깨진 값으로는 자동 로그인할 수 없다
				Remember(true, null);
				return SecureReadResult.NotFound;
			}

			Remember(true, login.AccountId);
			return SecureReadResult.Found;
		}

		/// <summary>저장소가 비었거나 이 계정의 로그인만 있다고 확인됐는지입니다(다음 실행에서 다른 계정으로 자동 로그인될 수 없는지).</summary>
		public bool HoldsNothingBut(string accountId)
		{
			return _contentKnown && (_storedAccountId == null || _storedAccountId == accountId);
		}

		public bool Save(SavedLogin login)
		{
			var saved = _store.TryWrite(_key, JsonConvert.SerializeObject(login, AccountApi.JsonSettings));

			if (saved)
			{
				Remember(true, login.AccountId);
			}
			else
			{
				Logger.Warning("Login could not be saved. It stays valid for this run only");
			}

			return saved;
		}

		/// <summary>
		/// 저장된 로그인을 지웁니다. 지우지 못하면 빈 값으로 덮어써 다음 실행에서 읽히지 않게 합니다. 둘 다 실패하면 false 입니다.
		/// </summary>
		public bool Clear()
		{
			if (_store.TryDelete(_key))
			{
				Remember(true, null);
				return true;
			}

			// 빈 값은 TryLoad 가 깨진 값으로 보고 지운 뒤 NotFound 로 돌려준다
			if (_store.TryWrite(_key, string.Empty))
			{
				Logger.Warning("Saved login could not be deleted. It was overwritten instead");
				Remember(true, null);
				return true;
			}

			Logger.Warning("Saved login could not be deleted or overwritten");
			return false;
		}

		private void Remember(bool known, string accountId)
		{
			_contentKnown = known;
			_storedAccountId = accountId;
		}
	}
}
