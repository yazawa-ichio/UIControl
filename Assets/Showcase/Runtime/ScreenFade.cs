using System;
using System.Threading.Tasks;
using UnityEngine;

namespace UIControl.Showcase
{
	// 全画面フェードの順序と入力制限は、このサンプルの業務側で決める。
	public sealed class ScreenFade : MonoBehaviour
	{
		[SerializeField] ShowcaseApp m_App;
		[SerializeField] NamedStatePlayer m_Player;
		[SerializeField] CanvasGroup m_Group;
		[SerializeField] UnityEngine.UI.Graphic m_Graphic;
		Task m_Cover;
		IDisposable m_Occupancy;
		bool m_Revealing;
		int m_Version;
		public bool IsBusy => m_Cover != null;

		public Task Cover(string state, bool white = false)
		{
			// GoTo は遷移先の初期化が遷移元の終了より先。重なる暗転は共有する。
			if (IsBusy && !m_Revealing) { return m_Cover; }
			if (!IsBusy)
			{
				// 遮断はアプリ側の責務。Pointerはフェード面のblocksRaycastsで止め、Padは選択の移動と復元を占有して止める。
				m_Group.blocksRaycasts = true;
				m_Occupancy ??= UIFocus.OccupyNavigation();
				m_Graphic.color = white ? Color.white : Color.black;
			}
			m_Version++;
			m_Revealing = false;
			m_App.Log("FADE OUT " + state + (white ? " WHITE" : " BLACK"));
			// 明転途中のプログラム側の遷移要求では、一旦覆ってから切り替える。
			if (m_Cover != null) { state = "OutImmediate"; }
			m_Cover = Play(state);
			return m_Cover;
		}

		void LateUpdate()
		{
			if (!IsBusy || !m_Cover.IsCompleted || m_Revealing) { return; }
			// Reload のような連続 GoTo と Page の回収が終わるまで、覆ったまま待つ。
			if (m_App.Manager.IsBusy || m_App.Main.HasPendingOperations || m_App.Front.HasPendingOperations) { return; }
			m_Revealing = true;
			m_App.Run(Reveal);
		}

		async Task Reveal()
		{
			int version = m_Version;
			m_App.Log("FADE IN");
			try { await Play("In"); }
			finally { if (version == m_Version) { Clear(); } }
		}

		async Task Play(string state)
		{
			StateResult result = await m_Player.Play(state, restart: true);
			if (result.Status == StateStatus.Cancelled || result.Status == StateStatus.Superseded) { throw new OperationCanceledException(); }
			if (result.Status != StateStatus.Completed) { throw result.Exception ?? new InvalidOperationException("Screen fade failed: " + state + " / " + result.Status); }
		}

		public void Clear()
		{
			m_Version++;
			if (m_Player != null) { m_Player.Stop(); }
			m_Cover = null;
			m_Revealing = false;
			if (m_Group != null) { m_Group.alpha = 0; m_Group.blocksRaycasts = false; }
			if (m_Occupancy != null) { m_Occupancy.Dispose(); m_Occupancy = null; UIFocus.Reconcile(); }
		}
		void OnDisable() { Clear(); }
	}
}
