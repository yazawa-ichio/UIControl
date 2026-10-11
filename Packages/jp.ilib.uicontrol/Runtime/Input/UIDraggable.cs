using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;

namespace UIControl
{
	/// <summary>標準PointerイベントでRectTransformを移動し、Dropの受理結果を通知する。仮想Mouseデバイス方式のカーソルからも同じ経路で動く。</summary>
	[DisallowMultipleComponent, RequireComponent(typeof(RectTransform))]
	public sealed class UIDraggable : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler, ICanvasRaycastFilter
	{
		[SerializeField] private RectTransform m_Target;
		[SerializeField] private bool m_ReturnOnRejection = true;
		[SerializeField] private UnityEvent m_OnBeginDrag = new UnityEvent();
		[SerializeField] private UnityEvent<Vector2> m_OnDrag = new UnityEvent<Vector2>();
		[SerializeField] private UnityEvent<bool> m_OnEndDrag = new UnityEvent<bool>();
		private RectTransform m_DragTarget;
		private RectTransform m_Parent;
		private Camera m_Camera;
		private Vector2 m_StartPointer;
		private Vector3 m_StartPosition;
		private int m_PointerId;
		private bool m_Accepted;
		public bool IsDragging { get; private set; }
		public UnityEvent BeginDrag => m_OnBeginDrag;
		public UnityEvent<Vector2> Drag => m_OnDrag;
		public UnityEvent<bool> EndDrag => m_OnEndDrag;

		public void OnBeginDrag(PointerEventData eventData)
		{
			if (!isActiveAndEnabled || IsDragging || eventData.button != PointerEventData.InputButton.Left) { return; }
			RectTransform target = m_Target != null ? m_Target : (RectTransform)transform;
			RectTransform parent = target.parent as RectTransform;
			Camera camera = PointerGeometry.Camera(target);
			if (parent == null || !RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, eventData.pressPosition, camera, out Vector2 start)) { return; }
			m_DragTarget = target;
			m_Parent = parent;
			m_Camera = camera;
			m_StartPointer = start;
			m_StartPosition = target.localPosition;
			m_PointerId = eventData.pointerId;
			m_Accepted = false;
			IsDragging = true;
			m_OnBeginDrag.Invoke();
		}

		public void OnDrag(PointerEventData eventData)
		{
			if (!Matches(eventData)) { return; }
			if (m_DragTarget == null || m_Parent == null || m_DragTarget.parent != m_Parent) { CancelDrag(); return; }
			if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(m_Parent, eventData.position, m_Camera, out Vector2 point)) { return; }
			m_DragTarget.localPosition = m_StartPosition + (Vector3)(point - m_StartPointer);
			m_OnDrag.Invoke(eventData.position);
		}

		/// <summary>IDropHandler内で呼ぶ。業務上の受理条件は呼び出し側が判定する。</summary>
		public bool TryAcceptDrop(PointerEventData eventData)
		{
			if (!isActiveAndEnabled || !Matches(eventData) || m_Accepted || eventData.pointerDrag != gameObject) { return false; }
			m_Accepted = true;
			return true;
		}

		public void OnEndDrag(PointerEventData eventData)
		{
			if (Matches(eventData)) { Finish(m_Accepted); }
		}

		public void CancelDrag() { if (IsDragging) { Finish(false); } }
		private bool Matches(PointerEventData data) => IsDragging && data != null && data.pointerId == m_PointerId && data.button == PointerEventData.InputButton.Left;
		private void Finish(bool accepted)
		{
			IsDragging = false;
			if (!accepted && m_ReturnOnRejection && m_DragTarget != null && m_DragTarget.parent == m_Parent) { m_DragTarget.localPosition = m_StartPosition; }
			m_DragTarget = null;
			m_Parent = null;
			m_Accepted = false;
			m_OnEndDrag.Invoke(accepted);
		}

		// 自身と子Graphicだけを透過する。CanvasGroupの共有状態は変更しない。
		public bool IsRaycastLocationValid(Vector2 screenPoint, Camera eventCamera) => !IsDragging;
		private void OnDisable() { if (IsDragging) { Finish(m_Accepted); } }
		private void OnApplicationFocus(bool focused) { if (!focused) { CancelDrag(); } }
	}
}
