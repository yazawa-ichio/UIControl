using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;

namespace UIControl
{
	/// <summary>標準uGUIのDrop先。データの移動や親の変更は利用側で行う。</summary>
	[DisallowMultipleComponent]
	public class UIDropTarget : MonoBehaviour, IDropHandler
	{
		[SerializeField] private bool m_AcceptDrops = true;
		[SerializeField] private UIDraggable m_RequiredSource;
		[SerializeField] private UnityEvent<UIDraggable> m_OnDrop = new UnityEvent<UIDraggable>();
		public bool AcceptDrops { get => m_AcceptDrops; set => m_AcceptDrops = value; }
		public UnityEvent<UIDraggable> Dropped => m_OnDrop;
		public virtual bool CanAccept(UIDraggable source) => m_AcceptDrops && source != null && (m_RequiredSource == null || m_RequiredSource == source);
		public void OnDrop(PointerEventData eventData)
		{
			if (!isActiveAndEnabled || eventData.pointerDrag == null) { return; }
			UIDraggable source = eventData.pointerDrag.GetComponent<UIDraggable>();
			if (source == null || transform.IsChildOf(source.transform) || !CanAccept(source) || !source.TryAcceptDrop(eventData)) { return; }
			m_OnDrop.Invoke(source);
		}
	}
}
