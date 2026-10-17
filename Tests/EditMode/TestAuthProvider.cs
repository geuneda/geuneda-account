using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Geuneda.Core;

// ReSharper disable once CheckNamespace

namespace Geuneda.Account.Tests
{
	/// <summary>
	/// 자격을 정해 두는 시험용 제공자입니다. <see cref="Gate"/> 를 두면 그것이 끝날 때까지 기다립니다. <see cref="UsesNonce"/> 면 받은 nonce 를 <see cref="Nonces"/> 에 남깁니다.
	/// </summary>
	internal sealed class TestAuthProvider : IAuthProvider
	{
		public const string Name = "test";

		public string Credential = "alice";
		public int SignInCount;
		public int SignOutCount;
		public UniTaskCompletionSource<Result<PlatformCredential>> Gate;
		public bool UsesNonce;
		public readonly List<string> Nonces = new List<string>();

		public string Platform => Name;

		public bool RequiresNonce => UsesNonce;

		public UniTask<Result<PlatformCredential>> SignInAsync(string nonce, CancellationToken cancellationToken)
		{
			SignInCount++;
			Nonces.Add(nonce);
			return Gate != null ? Gate.Task : UniTask.FromResult(Result<PlatformCredential>.Ok(new PlatformCredential(Name, Credential)));
		}

		public UniTask SignOutAsync(CancellationToken cancellationToken)
		{
			SignOutCount++;
			return UniTask.CompletedTask;
		}
	}
}
