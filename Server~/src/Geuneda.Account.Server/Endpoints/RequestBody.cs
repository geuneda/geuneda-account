using System;
using System.Buffers;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace Geuneda.Account.Server
{
	/// <summary>크기를 제한해 JSON 요청 본문을 읽습니다. 인증 요청은 작으므로 큰 본문은 읽지 않고 거절합니다.</summary>
	internal static class RequestBody
	{
		/// <summary>본문 최대 크기. id 토큰(수 KB)을 넉넉히 담습니다.</summary>
		public const int MaxBytes = 16 * 1024;

		/// <summary>읽은 값을 돌려줍니다. 크기 초과, JSON 형식 오류, null 이면 null 입니다.</summary>
		public static async Task<T> ReadAsync<T>(HttpRequest request, CancellationToken cancellationToken) where T : class
		{
			if (request.ContentLength > MaxBytes)
			{
				return null;
			}

			var buffer = ArrayPool<byte>.Shared.Rent(MaxBytes + 1);

			try
			{
				var length = 0;

				while (length <= MaxBytes)
				{
					var read = await request.Body.ReadAsync(buffer.AsMemory(length, MaxBytes + 1 - length), cancellationToken);

					if (read == 0)
					{
						break;
					}

					length += read;
				}

				if (length > MaxBytes)
				{
					return null;
				}

				return JsonSerializer.Deserialize<T>(buffer.AsSpan(0, length), AccountJson.Options);
			}
			catch (JsonException)
			{
				return null;
			}
			catch (IOException)
			{
				return null;
			}
			finally
			{
				ArrayPool<byte>.Shared.Return(buffer);
			}
		}
	}
}
