using System.Threading;
using Cysharp.Threading.Tasks;
using Geuneda.Core;

// ReSharper disable once CheckNamespace

namespace Geuneda.Account.Tests
{
	/// <summary>자격을 정해 두는 시험용 제공자입니다. <see cref="Gate"/> 를 두면 그것이 끝날 때까지 기다립니다.</summary>
	internal sealed class TestAuthProvider : IAuthProvider
	{
		public const string Name = "test";

		public string Credential = "alice";
		public int SignInCount;
		public int SignOutCount;
		public UniTaskCompletionSource<Result<PlatformCredential>> Gate;

		public string Platform => Name;

		public UniTask<Result<PlatformCredential>> SignInAsync(CancellationToken cancellationToken)
		{
			SignInCount++;
			return Gate != null ? Gate.Task : UniTask.FromResult(Result<PlatformCredential>.Ok(new PlatformCredential(Name, Credential)));
		}

		public UniTask SignOutAsync(CancellationToken cancellationToken)
		{
			SignOutCount++;
			return UniTask.CompletedTask;
		}
	}
}
