using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Geuneda.Account.Server
{
	/// <summary>
	/// 프로세스 메모리 1회용 값 저장소입니다. 샘플·테스트와 서버 한 대용이고(여러 대면 공유 저장소 구현), 다시 시작하면 사라집니다.
	/// 두 용도를 합쳐 기록 수를 <see cref="Capacity"/> 로 제한하고, 넣을 때마다 만료된 기록을 오래된 것부터 지웁니다.
	/// 가득 차면 새 값을 거절해 이미 나간 값은 지킵니다. 소비한 기록이 만료 순서 큐에 쌓여도 큐는 상한의 두 배를 넘지 않습니다.
	/// </summary>
	public sealed class InMemoryOneTimeValueStore : IOneTimeValueStore
	{
		/// <summary>기본 상한. 기록 하나는 수백 바이트라 상한까지 차도 수십 MB 입니다.</summary>
		public const int DefaultCapacity = 100_000;

		private readonly object _gate = new object();
		private readonly Dictionary<(OneTimeValueKind Kind, string Hash), OneTimeValue> _values = new Dictionary<(OneTimeValueKind, string), OneTimeValue>();
		// 넣은 순서. 유효 시간이 같으면 만료 순서이므로 앞에서부터 만료된 것을 지운다
		private readonly Queue<OneTimeValue> _order = new Queue<OneTimeValue>();

		public InMemoryOneTimeValueStore() : this(DefaultCapacity)
		{
		}

		/// <exception cref="ArgumentOutOfRangeException"><paramref name="capacity"/> 가 1 보다 작은 경우</exception>
		public InMemoryOneTimeValueStore(int capacity)
		{
			ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
			Capacity = capacity;
		}

		/// <summary>함께 둘 수 있는 기록 수입니다(두 용도 합계).</summary>
		public int Capacity { get; }

		/// <summary>메모리에 붙잡고 있는 기록 수(소비했지만 아직 큐에 남은 것 포함). 테스트용입니다.</summary>
		internal int RetainedCount
		{
			get
			{
				lock (_gate)
				{
					return _order.Count;
				}
			}
		}

		public Task<bool> TryAddAsync(OneTimeValue value, DateTimeOffset now, CancellationToken cancellationToken)
		{
			ArgumentNullException.ThrowIfNull(value);

			lock (_gate)
			{
				RemoveExpired(now);
				var key = (value.Kind, value.Hash);

				if (_values.Count >= Capacity && !_values.ContainsKey(key))
				{
					return Task.FromResult(false);
				}

				_values[key] = value;
				_order.Enqueue(value);

				// 소비·덮어쓰기로 사전에서 빠진 기록은 만료 전까지 큐에 남는다. 그런 기록이 상한만큼 쌓이면 살아 있는 것만 남긴다(분할 상환 O(1))
				if (_order.Count - _values.Count >= Capacity)
				{
					RemoveDead();
				}

				return Task.FromResult(true);
			}
		}

		public Task<OneTimeValue> TryConsumeAsync(OneTimeValueKind kind, string hash, DateTimeOffset now, CancellationToken cancellationToken)
		{
			lock (_gate)
			{
				return Task.FromResult(_values.Remove((kind, hash), out var value) && now < value.ExpiresAt ? value : null);
			}
		}

		private void RemoveDead()
		{
			var live = new List<OneTimeValue>(_values.Count);

			foreach (var value in _order)
			{
				if (IsLive(value))
				{
					live.Add(value);
				}
			}

			_order.Clear();

			foreach (var value in live)
			{
				_order.Enqueue(value);
			}
		}

		private bool IsLive(OneTimeValue value)
		{
			return _values.TryGetValue((value.Kind, value.Hash), out var current) && ReferenceEquals(current, value);
		}

		private void RemoveExpired(DateTimeOffset now)
		{
			while (_order.Count > 0 && _order.Peek().ExpiresAt <= now)
			{
				var expired = _order.Dequeue();

				// 같은 키를 덮어쓴 뒤의 기록은 다른 기록이므로 지우지 않는다
				if (IsLive(expired))
				{
					_values.Remove((expired.Kind, expired.Hash));
				}
			}
		}
	}
}
