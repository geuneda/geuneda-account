using System.Threading;
using Cysharp.Threading.Tasks;
using Geuneda.Core;

// ReSharper disable once CheckNamespace

namespace Geuneda.Account
{
	/// <summary>
	/// 제공자 로그인입니다. 제공자가 nonce 를 쓰면(<see cref="IAuthProvider.RequiresNonce"/>) 먼저 인증 서버에서 받아 넘기고, 받은 자격에 그 nonce 를 붙여
	/// 로그인·연동 요청에 함께 보내게 합니다. nonce 를 받지 못하면 플랫폼 로그인 화면을 띄우지 않고 그 결과(네트워크 실패, 5xx 등)로 끝납니다.
	/// </summary>
	internal static class ProviderSignIn
	{
		public static async UniTask<Result<PlatformCredential>> SignInAsync(AccountApi api, IAuthProvider provider, CancellationToken cancellationToken)
		{
			string nonce = null;

			if (provider.RequiresNonce)
			{
				var issued = await api.IssueNonceAsync(cancellationToken);

				if (!issued.Result.TryGetValue(out var response))
				{
					return Result<PlatformCredential>.Fail(issued.Result.Code, issued.Result.Message);
				}

				// 받는 동안 취소·Dispose 되었으면 플랫폼 로그인 화면을 띄우지 않는다
				if (cancellationToken.IsCancellationRequested)
				{
					return Result<PlatformCredential>.Fail(ResultCodes.Cancelled);
				}

				if (string.IsNullOrEmpty(response.Nonce))
				{
					return Result<PlatformCredential>.Fail(AccountResultCodes.InvalidResponse, "Nonce response is missing the nonce");
				}

				nonce = response.Nonce;
			}

			var credential = await provider.SignInAsync(nonce, cancellationToken);
			return nonce != null && credential.TryGetValue(out var value)
				? Result<PlatformCredential>.Ok(new PlatformCredential(value.Platform, value.Credential, nonce, null))
				: credential;
		}
	}
}
