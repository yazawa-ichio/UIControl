using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Pool;

namespace UIControl
{
	[DisallowMultipleComponent]
	[AddComponentMenu("UIControl/Animation/List Item State Driver")]
	public sealed class ListItemStateDriver : MonoBehaviour, ICanvasRaycastFilter
	{
		[SerializeField]
		private NamedStatePlayer m_Player;
		[SerializeField]
		private string m_Channel = "Item";
		[SerializeField]
		private string m_Reset = "Hidden";
		[SerializeField]
		private string m_Show = "Show";
		[SerializeField]
		private string m_Hide = "Hide";
		private Page m_Screen;
		private bool m_IsShown = true;
		internal uint Revision { get; private set; }
		internal event Action VisibilityChanged;
		public bool IsShown => isActiveAndEnabled && m_IsShown;

		private void OnEnable()
		{
			m_Screen = GetComponentInParent<Page>(true);
			if (m_Screen != null) { m_Screen.RenderingChanged += OnRenderingChanged; }
			Revision++;
			VisibilityChanged?.Invoke();
		}

		private void OnDisable()
		{
			VisibilityChanged?.Invoke();
			if (m_Screen != null) { m_Screen.RenderingChanged -= OnRenderingChanged; }
			m_Screen = null;
			Revision++;
			try { StopOwnedAnimations(); }
			catch (Exception exception) { Debug.LogException(exception, this); }
		}

		private void OnTransformParentChanged()
		{
			if (!isActiveAndEnabled) { return; }
			if (m_Screen != null) { m_Screen.RenderingChanged -= OnRenderingChanged; }
			m_Screen = GetComponentInParent<Page>(true);
			if (m_Screen != null) { m_Screen.RenderingChanged += OnRenderingChanged; }
		}

		private void OnRenderingChanged(bool rendered)
		{
			if (!rendered)
			{
				Revision++;
				StopOwnedAnimations();
			}
		}

		/// <summary>別データへ再利用する直前に呼ぶ。旧演出・選択を止め、Prefabの初期姿勢を適用する。VMは扱わない。</summary>
		public void BeforeReuse()
		{
			uint revision = ++Revision;
			SetShown(false);
			if (Revision != revision) { return; }
			Exception error = null;
			try { StopOwnedAnimations(); }
			catch (Exception exception) { error = exception; }
			ForgetSelection();
			if (error != null) { throw error; }
			if (Revision != revision) { return; }
			if (m_Player != null && !string.IsNullOrEmpty(m_Reset))
			{
				StateResult result = m_Player.ApplyImmediately(m_Reset, m_Channel);
				if (result.Status == StateStatus.Faulted) { throw result.Exception ?? new InvalidOperationException("Row reset failed."); }
				if (result.Status == StateStatus.Missing) { throw new InvalidOperationException("Row reset mapping is missing."); }
			}
		}

		/// <summary>表示演出を明示的に再生する。完了結果を返し、GameObjectやVMの寿命は変更しない。</summary>
		public Task<StateResult> Show() { return Play(m_Show, true); }

		/// <summary>非表示演出の完了を待てる。実体の非active化・回収は呼出し側が行う。</summary>
		public Task<StateResult> Hide() { return Play(m_Hide, false); }

		/// <summary>回収側の取消で、このHideだけを停止する。再利用後の演出は停止しない。</summary>
		public async Task<StateResult> Hide(CancellationToken cancellation)
		{
			if (cancellation.IsCancellationRequested) { return new StateResult(StateStatus.Cancelled); }
			Task<StateResult> task = Hide();
			uint revision = Revision;
			using (AnimationAdapter.RegisterCancellation(cancellation, () => StopIfCurrent(revision)))
			{
				return await task;
			}
		}

		public bool IsRaycastLocationValid(Vector2 screenPoint, Camera eventCamera) { return !isActiveAndEnabled || m_IsShown; }

		private Task<StateResult> Play(string state, bool shown)
		{
			uint revision = ++Revision;
			if (!isActiveAndEnabled || m_Screen != null && !m_Screen.IsRendered)
			{
				return Task.FromResult(new StateResult(StateStatus.Cancelled));
			}
			ValidatePlayer();
			SetShown(shown);
			if (!shown) { ForgetSelection(); }
			if (Revision != revision) { return Task.FromResult(new StateResult(StateStatus.Superseded)); }
			if (m_Player == null) { return Task.FromResult(new StateResult(StateStatus.Missing)); }
			if (string.IsNullOrEmpty(state))
			{
				m_Player.Stop(m_Channel);
				return Task.FromResult(new StateResult(StateStatus.Completed));
			}
			return m_Player.Play(state, m_Channel, true);
		}

		internal void StopIfCurrent(uint revision)
		{
			if (Revision != revision) { return; }
			Revision++;
			if (m_Player != null) { m_Player.Stop(m_Channel); }
		}

		private void SetShown(bool shown)
		{
			if (m_IsShown == shown) { return; }
			m_IsShown = shown;
			// 非表示の行は標準の意味で操作不可にする。選択候補の判定はSelectable.IsInteractable()だけを見る。
			CanvasGroup group = GetComponent<CanvasGroup>();
			if (group == null) { group = gameObject.AddComponent<CanvasGroup>(); }
			group.interactable = shown;
			VisibilityChanged?.Invoke();
		}

		private void ForgetSelection()
		{
			UIFocus.Forget(transform);
			UIFocus.ClearSelectionUnder(transform);
		}

		private void ValidatePlayer()
		{
			if (m_Player != null && !m_Player.transform.IsChildOf(transform))
			{
				throw new InvalidOperationException("ListItemStateDriver requires a Player owned by this row.");
			}
		}

		private void StopOwnedAnimations()
		{
			ValidatePlayer();
			using (ListPool<NamedStatePlayer>.Get(out List<NamedStatePlayer> players))
			{
				GetComponentsInChildren(true, players);
				List<Exception> errors = null;
				foreach (NamedStatePlayer player in players)
				{
					try { if (player != null) { player.StopAll(); } }
					catch (Exception exception)
					{
						if (errors == null) { errors = new List<Exception>(); }
						errors.Add(exception);
					}
				}
				if (errors != null) { throw new AggregateException(errors); }
			}
		}

		private void Reset() { m_Player = GetComponent<NamedStatePlayer>(); }
	}
}
