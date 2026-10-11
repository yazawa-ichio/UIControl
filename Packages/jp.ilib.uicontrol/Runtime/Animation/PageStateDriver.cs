using System;
using System.Threading.Tasks;
using UnityEngine;

namespace UIControl
{
	/// <summary>Pageの Open / Close / Covered / Hidden / Restored を NamedStatePlayer の状態名へ接続する標準の PageVisual。</summary>
	public sealed class PageStateDriver : PageVisual
	{
		[SerializeField]
		private NamedStatePlayer m_Player;
		[SerializeField]
		private string m_Channel = "Page";
		[SerializeField]
		private string m_Open = "Open";
		[SerializeField]
		private string m_Close = "Close";
		[SerializeField]
		private string m_Covered = "Covered";
		[SerializeField]
		private string m_Hidden = "Hidden";
		[SerializeField]
		private string m_Restored = "Open";

		public override async Task Transition(Presentation presentation, ChangeReason reason)
		{
			if (m_Player == null) { return; }
			Check(await m_Player.Play(Resolve(presentation, reason), m_Channel, true));
		}

		public override void ApplyImmediately(Presentation presentation, ChangeReason reason)
		{
			if (m_Player != null) { Check(m_Player.ApplyImmediately(Resolve(presentation, reason), m_Channel)); }
		}

		public override void Stop() { if (m_Player != null) { m_Player.StopAll(); } }

		private string Resolve(Presentation presentation, ChangeReason reason)
		{
			if (reason == ChangeReason.Close) { return m_Close; }
			if (presentation == Presentation.Hidden) { return m_Hidden; }
			if (reason == ChangeReason.Open) { return m_Open; }
			return presentation == Presentation.Covered ? m_Covered : m_Restored;
		}

		private static void Check(StateResult result)
		{
			if (result.Status == StateStatus.Faulted) { throw result.Exception ?? new InvalidOperationException("Page animation failed."); }
			if (result.Status == StateStatus.Cancelled || result.Status == StateStatus.Superseded) { throw new OperationCanceledException("Page animation was interrupted."); }
		}
	}
}
