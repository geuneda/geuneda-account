using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Geuneda.Core;
using NUnit.Framework;
using Random = System.Random;

// ReSharper disable once CheckNamespace

namespace Geuneda.Account.Tests
{
	[TestFixture]
	public class AppSessionTest
	{
		private static readonly Regex IdFormat = new Regex("^[0-9a-f]{16}$");

		private DateTime _utcNow;
		private AppSession _session;
		private List<string> _events;
		private ILogSink _originalSink;

		private sealed class DiscardSink : ILogSink
		{
			public void Write(in LogEntry entry)
			{
			}
		}

		[SetUp]
		public void SetUp()
		{
			_originalSink = Log.Sink;
			Log.Sink = new DiscardSink();
			_utcNow = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
			_session = new AppSession(TimeSpan.FromMinutes(10), () => _utcNow, new Random(7));
			_events = new List<string>();
			_session.Started += session => _events.Add("start " + session.SessionId);
			_session.Paused += session => _events.Add("pause " + session.SessionId);
			_session.Resumed += session => _events.Add("resume " + session.SessionId);
			_session.Ended += (session, reason) => _events.Add("end " + session.SessionId + " " + reason);
		}

		[TearDown]
		public void TearDown()
		{
			Log.Sink = _originalSink;
		}

		[Test]
		public void Start_UsesReservedId_AndReservesNext()
		{
			var reserved = _session.NextSessionId;

			var started = _session.Start();

			Assert.AreEqual(reserved, started.SessionId);
			Assert.AreEqual(reserved, _session.SessionId);
			Assert.AreNotEqual(reserved, _session.NextSessionId);
			StringAssert.IsMatch(IdFormat.ToString(), reserved);
			Assert.AreEqual(_utcNow, started.StartedAtUtc);
			Assert.AreEqual(new[] { "start " + reserved }, _events);
		}

		[Test]
		public void ShortBackground_Resumes()
		{
			var id = _session.Start().SessionId;

			_session.HandlePause(true);
			_utcNow += TimeSpan.FromMinutes(9);
			_session.HandlePause(false);

			Assert.AreEqual(id, _session.SessionId);
			Assert.AreEqual(new[] { "start " + id, "pause " + id, "resume " + id }, _events);
		}

		[Test]
		public void LongBackground_EndsThenStartsNewSession()
		{
			var id = _session.Start().SessionId;
			var next = _session.NextSessionId;

			_session.HandlePause(true);
			_utcNow += TimeSpan.FromMinutes(10);
			_session.HandlePause(false);

			Assert.AreEqual(next, _session.SessionId);
			Assert.AreEqual(new[] { "start " + id, "pause " + id, "end " + id + " BackgroundTimeout", "start " + next }, _events);
		}

		[Test]
		public void ClockGoingBackwards_Resumes()
		{
			var id = _session.Start().SessionId;

			_session.HandlePause(true);
			_utcNow -= TimeSpan.FromHours(1);
			_session.HandlePause(false);

			Assert.AreEqual(id, _session.SessionId);
		}

		[Test]
		public void StartWhileActive_EndsFirst()
		{
			var first = _session.Start().SessionId;
			var second = _session.Start().SessionId;

			Assert.AreEqual(new[] { "start " + first, "end " + first + " Restarted", "start " + second }, _events);
		}

		[Test]
		public void End_And_Quit_EndOnce()
		{
			var id = _session.Start().SessionId;

			_session.End();
			_session.End();
			_session.HandleQuit();
			var quitId = _session.Start().SessionId;
			_session.HandleQuit();

			Assert.IsFalse(_session.IsActive);
			Assert.IsNull(_session.SessionId);
			Assert.AreEqual(new[] { "start " + id, "end " + id + " Ended", "start " + quitId, "end " + quitId + " ApplicationQuit" }, _events);
		}

		[Test]
		public void PauseWhileInactive_IsIgnored()
		{
			_session.HandlePause(true);
			_session.HandlePause(false);

			Assert.IsEmpty(_events);
		}

		[Test]
		public void HandlerException_DoesNotStopOtherHandlers()
		{
			var session = new AppSession(TimeSpan.FromMinutes(10), () => _utcNow, new Random(1));
			var called = 0;
			session.Started += _ => throw new InvalidOperationException("boom");
			session.Started += _ => called++;

			session.Start();

			Assert.AreEqual(1, called);
		}

		[Test]
		public void SessionIds_AreTimeOrdered()
		{
			var earlier = SessionIds.Create(_utcNow, int.MaxValue);
			var later = SessionIds.Create(_utcNow.AddMilliseconds(1), 0);

			Assert.Less(string.CompareOrdinal(earlier, later), 0);
			StringAssert.IsMatch(IdFormat.ToString(), SessionIds.Create(new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc), 5));
		}

		[Test]
		public void NonPositiveTimeout_Throws()
		{
			Assert.Throws<ArgumentOutOfRangeException>(() => new AppSession(TimeSpan.Zero));
		}
	}
}
