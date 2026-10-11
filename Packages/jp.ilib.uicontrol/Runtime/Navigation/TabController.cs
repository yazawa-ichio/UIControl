using System;
using UnityEngine;
using UnityEngine.UI;

namespace UIControl
{
	public enum TabFocus { Contextual, EnterContent, Anchor }

	[Serializable]
	public sealed class TabDefinition
	{
		[SerializeField]
		private Selectable m_Anchor;
		[SerializeField]
		private FocusScope m_Content;
		public Selectable Anchor => m_Anchor;
		public FocusScope Content => m_Content;
	}

	/// <summary>タブの切替。Contentの表示を切り替え、選択の記憶・復元をUIFocusの規則で行う。</summary>
	public sealed class TabController : MonoBehaviour
	{
		[SerializeField]
		private TabDefinition[] m_Tabs = Array.Empty<TabDefinition>();
		[SerializeField]
		private int m_InitialIndex;
		[SerializeField]
		private TabFocus m_Focus = TabFocus.Contextual;
		public int SelectedIndex { get; private set; } = -1;

		private void Start()
		{
			if (SelectedIndex < 0 && m_Tabs.Length > 0) { Select(Mathf.Clamp(m_InitialIndex, 0, m_Tabs.Length - 1)); }
		}

		public void Select(int index)
		{
			if (index < 0 || index >= m_Tabs.Length) { throw new ArgumentOutOfRangeException(nameof(index)); }
			if (index == SelectedIndex) { return; }
			foreach (TabDefinition tab in m_Tabs)
			{
				if (tab == null || tab.Content == null || tab.Anchor == null || tab.Content.gameObject == gameObject || transform.IsChildOf(tab.Content.transform))
				{
					throw new InvalidOperationException("Tabs require an Anchor and Content outside the controller's ancestor chain.");
				}
			}
			FocusScope previous = SelectedIndex >= 0 ? m_Tabs[SelectedIndex].Content : null;
			bool wasInside = previous != null && previous.IsInside();
			FocusScope anchorScope = m_Tabs[index].Anchor.GetComponentInParent<FocusScope>(true);
			using (UIFocus.DeferSelection())
			{
				previous?.Remember();
				for (int i = 0; i < m_Tabs.Length; i++) { m_Tabs[i].Content.gameObject.SetActive(i == index); }
				SelectedIndex = index;
				bool enter = m_Focus == TabFocus.EnterContent || m_Focus == TabFocus.Contextual && wasInside;
				if (UIFocus.InputMode == FocusInputMode.Pointer)
				{
					if (enter) { m_Tabs[index].Content.RememberRestore(); }
					else { anchorScope?.NodeOf(m_Tabs[index].Anchor)?.Remember(); }
				}
				else if (enter) { m_Tabs[index].Content.RequestRestore(); }
				else { anchorScope?.RequestSelect(m_Tabs[index].Anchor); }
			}
		}
	}
}
