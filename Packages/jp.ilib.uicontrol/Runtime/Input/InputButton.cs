using System;
using System.Collections.Generic;
using UnityEngine;

namespace UIControl
{
	/// <summary>
	/// 物理入力（Back / Menu / L / R など）を通常のButtonへ届けるための印。用途ごとに派生型を作り、型で識別する。
	/// 入力側は <c>InputButton.TryInvoke&lt;BackButton&gt;()</c> のように呼ぶ。候補のうちRaycastで最前面に露出している
	/// 通常Buttonを1つ選び、そのonClickを呼ぶ。マウスで押せないものはここでも押せない。
	/// 派生型はOnEnable / OnDisableを宣言せず、必要なら <see cref="OnEnabled"/> / <see cref="OnDisabled"/> を使う。
	/// </summary>
	[DisallowMultipleComponent]
	public abstract class InputButton : MonoBehaviour
	{
		[SerializeField]
		private UnityEngine.UI.Button m_Button;
		[SerializeField, Tooltip("同じ型の候補が複数露出している時に優先する。大きい方が先。")]
		private int m_Priority;
		[SerializeField, Tooltip("露出判定に使う点を持つRect。未設定ならButtonのRect中央。")]
		private RectTransform m_Probe;
		private static readonly List<InputButton> s_Active = new List<InputButton>();
		public UnityEngine.UI.Button Button => m_Button != null ? m_Button : GetComponent<UnityEngine.UI.Button>();
		public int Priority => m_Priority;
		public RectTransform Probe => m_Probe != null ? m_Probe : Button != null ? Button.transform as RectTransform : null;

		/// <summary>型Tの候補から、いま押せる1つを選んでonClickを呼ぶ。届いたらtrue。</summary>
		public static bool TryInvoke<T>() where T : InputButton { return InputButtonResolver.Shared.TryInvoke(typeof(T)); }

		internal static void Collect(Type type, List<InputButton> candidates)
		{
			candidates.Clear();
			foreach (InputButton button in s_Active)
			{
				if (button != null && type.IsInstanceOfType(button)) { candidates.Add(button); }
			}
		}

		protected virtual void OnEnabled() { }
		protected virtual void OnDisabled() { }

		private void OnEnable()
		{
			if (!s_Active.Contains(this)) { s_Active.Add(this); }
			OnEnabled();
		}
		private void OnDisable()
		{
			s_Active.Remove(this);
			OnDisabled();
		}
		private void Reset() { m_Button = GetComponent<UnityEngine.UI.Button>(); }
	}
}
