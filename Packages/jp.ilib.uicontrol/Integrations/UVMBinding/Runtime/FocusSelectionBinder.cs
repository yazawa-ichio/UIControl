using UnityEngine;
using UnityEngine.EventSystems;
using UVMBinding;

namespace UIControl.UVMBinding
{
	[RequireComponent(typeof(NavigationNode))]
	[AddComponentMenu("UIControl/UVMBinding/Focus Selection Binder")]
	public sealed class FocusSelectionBinder : Binder<bool>, ISelectHandler, IDeselectHandler
	{
		private NavigationNode m_Node;
		private bool m_IsBound;
		private bool m_IsWriting;
		protected override void OnBind() { m_Node = GetComponent<NavigationNode>(); m_IsBound = true; }
		protected override void OnUnbind() { m_IsBound = false; }

		protected override void UpdateValue(bool selected)
		{
			if (m_IsWriting || !isActiveAndEnabled) { return; }
			if (m_Node == null || m_Node.Scope == null) { return; }
			if (selected) { m_Node.RequestSelect(); }
			else if (UIFocus.Selected == m_Node.gameObject) { UIFocus.Deselect(m_Node.gameObject); }
		}

		public void OnSelect(BaseEventData eventData)
		{
			if (m_Node != null && m_Node.Scope != null && EventSystem.current != null && EventSystem.current.currentSelectedGameObject == gameObject) { Write(true); }
		}
		public void OnDeselect(BaseEventData eventData) { Write(false); }

		private void Write(bool value)
		{
			if (!m_IsBound || m_IsWriting) { return; }
			m_IsWriting = true;
			try { Set(value); }
			finally { m_IsWriting = false; }
		}
	}
}
