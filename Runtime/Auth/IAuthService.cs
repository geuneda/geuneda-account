using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Geuneda.Core;

// ReSharper disable once CheckNamespace

namespace Geuneda.Account
{
	/// <summary>
	/// 계정 인증입니다. 기본 구현은 <see cref="AuthService"/> 이고, services 의 <c>MainInstaller</c> 에 이 인터페이스로 바인딩합니다. 메인 스레드에서 씁니다.
	/// </summary>
	/// <remarks>
	/// <para>
	/// 실패는 core 결과 규약대로 코드로 돌려줍니다(<see cref="AccountResultCodes"/>, 연결 실패·타임아웃·취소는 core 공용 코드). 등록하지 않은 플랫폼 이름 같은 오용은 예외입니다.
	/// </para>
	/// <para>
	/// 로그인, 로그아웃, 연동·옮겨오기·해제, 계정 삭제는 한 번에 하나만 합니다. 진행 중에 또 부르면 기다리지 않고 <c>ResultCodes.InProgress</c> 입니다(중복 클릭 방지).
	/// access 토큰 재발급은 동시에 여러 곳에서 불러도 한 번만 서버에 보내고 결과를 함께 받습니다.
	/// </para>
	/// </remarks>
	public interface IAuthService
	{
		/// <summary>이번 실행에서 로그인(또는 자동 로그인)에 성공했고 아직 로그아웃하지 않았는지</summary>
		bool IsSignedIn { get; }

		/// <summary>로그인한 계정. 로그인 상태가 아니면 null 입니다.</summary>
		string AccountId { get; }

		/// <summary>저장된 로그인이 있는지(자동 로그인을 시도할지 정할 때). 서버에서 만료되었을 수도 있습니다.</summary>
		bool HasSavedLogin { get; }

		/// <summary>로그인(자동 로그인 포함)에 성공했습니다.</summary>
		event Action<SignInResult> SignedIn;

		/// <summary>로그인 상태가 끝났습니다. 타이틀 화면으로 돌아가는 등의 처리를 합니다.</summary>
		event Action<SignOutReason> SignedOut;

		/// <summary>플랫폼으로 로그인합니다. 처음인 플랫폼 계정이면 새 계정을 만듭니다. 이미 로그인 중이면 이 계정으로 바꿉니다.</summary>
		/// <exception cref="ArgumentException"><paramref name="platform"/> 제공자를 등록하지 않은 경우</exception>
		UniTask<Result<SignInResult>> SignInAsync(string platform, CancellationToken cancellationToken = default);

		/// <summary>이미 받은 자격으로 로그인합니다(연동 충돌에서 그 계정으로 들어갈 때).</summary>
		UniTask<Result<SignInResult>> SignInAsync(PlatformCredential credential, CancellationToken cancellationToken = default);

		/// <summary>
		/// 저장된 로그인으로 자동 로그인합니다(토큰 재발급). 저장된 로그인이 없으면 <see cref="AccountResultCodes.NotSignedIn"/>,
		/// 서버가 거절하면(<see cref="AccountResultCodes.InvalidRefreshToken"/> 등) 저장된 로그인을 지웁니다. 네트워크 실패는 저장된 로그인을 그대로 둡니다.
		/// </summary>
		UniTask<Result<SignInResult>> SignInSilentlyAsync(CancellationToken cancellationToken = default);

		/// <summary>
		/// 쓸 수 있는 access 토큰입니다. 남은 수명이 짧으면 먼저 재발급합니다. 게임 서버 HTTP 요청(<see cref="AuthTokenMiddleware"/>)과 실시간 RPC 로그인에 씁니다.
		/// </summary>
		UniTask<Result<string>> GetAccessTokenAsync(CancellationToken cancellationToken = default);

		/// <summary>서버가 거절한 access 토큰을 알립니다. 지금 토큰이면 다음 <see cref="GetAccessTokenAsync"/> 가 재발급합니다.</summary>
		void InvalidateAccessToken(string accessToken);

		/// <summary>로그아웃합니다. 이 기기의 로그인을 먼저 지우고 서버 폐기와 플랫폼 SDK 로그아웃은 실패해도 넘어갑니다.</summary>
		UniTask<Result> SignOutAsync(CancellationToken cancellationToken = default);

		UniTask<Result<IReadOnlyList<LinkedPlatform>>> GetLinksAsync(CancellationToken cancellationToken = default);

		/// <summary>플랫폼 로그인을 해서 이 계정에 연동합니다. 다른 계정에 연동된 플랫폼 계정이면 <see cref="LinkResult.IsConflict"/> 입니다.</summary>
		/// <exception cref="ArgumentException"><paramref name="platform"/> 제공자를 등록하지 않은 경우</exception>
		UniTask<Result<LinkResult>> LinkAsync(string platform, CancellationToken cancellationToken = default);

		/// <summary>다른 계정에 연동된 플랫폼 계정을 이 계정으로 옮겨옵니다. 그 계정은 그 플랫폼으로 더 로그인할 수 없습니다.</summary>
		UniTask<Result<IReadOnlyList<LinkedPlatform>>> TransferAsync(PlatformCredential credential, CancellationToken cancellationToken = default);

		/// <summary>연동을 해제합니다. 마지막 로그인 수단은 <see cref="AccountResultCodes.LastLogin"/> 입니다.</summary>
		UniTask<Result<IReadOnlyList<LinkedPlatform>>> UnlinkAsync(string platform, CancellationToken cancellationToken = default);

		/// <summary>계정을 삭제하고 로그아웃합니다. 게임 서버의 계정 데이터 삭제는 게임이 따로 합니다.</summary>
		UniTask<Result> DeleteAccountAsync(CancellationToken cancellationToken = default);
	}
}
