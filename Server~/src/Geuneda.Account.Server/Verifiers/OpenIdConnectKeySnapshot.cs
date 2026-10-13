using System;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace Geuneda.Account.Server
{
	/// <summary>
	/// <see cref="OpenIdConnectKeyCache"/> 가 받아 둔 설정과 마지막 받기 시도입니다. 바꿀 때는 새로 만들어 통째로 갈아 끼웁니다.
	/// </summary>
	/// <param name="Configuration">마지막으로 받은 설정. 아직 받지 못했으면 null 입니다.</param>
	/// <param name="RetrievedAt">설정을 받은 시각입니다.</param>
	/// <param name="AttemptedAt">마지막으로 받기를 시도한 시각입니다(성공·실패 모두).</param>
	/// <param name="AttemptFailed">마지막 시도가 실패했는지입니다.</param>
	internal sealed record OpenIdConnectKeySnapshot(OpenIdConnectConfiguration Configuration, DateTimeOffset RetrievedAt, DateTimeOffset AttemptedAt, bool AttemptFailed)
	{
		public static readonly OpenIdConnectKeySnapshot Empty = new OpenIdConnectKeySnapshot(null, DateTimeOffset.MinValue, DateTimeOffset.MinValue, false);
	}
}
