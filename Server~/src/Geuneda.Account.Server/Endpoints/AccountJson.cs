using System.Text.Json;
using System.Text.Json.Serialization;

namespace Geuneda.Account.Server
{
	/// <summary>요청·응답 JSON 설정입니다. 이름은 camelCase, 읽을 때 대소문자를 가리지 않습니다(클라이언트는 Newtonsoft camelCase).</summary>
	internal static class AccountJson
	{
		public static readonly JsonSerializerOptions Options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
		{
			DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
		};
	}
}
