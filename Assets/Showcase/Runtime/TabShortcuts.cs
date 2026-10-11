using UnityEngine;

namespace UIControl.Showcase
{
	public sealed class TabShortcuts : MonoBehaviour
	{
		[SerializeField] TabController m_Tabs;
		public void Next() { m_Tabs.Select(Mathf.Min(3, m_Tabs.SelectedIndex + 1)); }
		public void Previous() { m_Tabs.Select(Mathf.Max(0, m_Tabs.SelectedIndex - 1)); }
	}
}
