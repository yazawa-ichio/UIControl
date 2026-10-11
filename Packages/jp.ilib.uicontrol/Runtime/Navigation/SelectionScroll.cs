using UnityEngine;
using UnityEngine.EventSystems;

namespace UIControl
{
	[DisallowMultipleComponent]
	[RequireComponent(typeof(UnityEngine.UI.ScrollRect))]
	public sealed class SelectionScroll : MonoBehaviour, IBeginDragHandler, IEndDragHandler
	{
		[SerializeField]
		private Vector2 m_Padding = new Vector2(8, 8);
		private UnityEngine.UI.ScrollRect m_Scroll;
		private NavigationNode m_Pending;
		private uint m_Revision;
		private int m_Frame;
		private MoveDirection m_Direction;
		private readonly Vector3[] m_Corners = new Vector3[4];
		public bool IsDragging { get; private set; }

		internal void Schedule(NavigationNode node, uint revision, MoveDirection direction)
		{
			m_Pending = node;
			m_Revision = revision;
			m_Frame = Time.frameCount;
			m_Direction = direction;
		}

		public void OnBeginDrag(PointerEventData eventData)
		{
			if (eventData.button == PointerEventData.InputButton.Left)
			{
				IsDragging = true;
			}
		}

		public void OnEndDrag(PointerEventData eventData)
		{
			if (eventData.button == PointerEventData.InputButton.Left)
			{
				IsDragging = false;
			}
		}

		private void OnDisable()
		{
			m_Pending = null;
			IsDragging = false;
		}

		private void LateUpdate()
		{
			if (m_Pending == null || Time.frameCount <= m_Frame)
			{
				return;
			}
			FocusArbiter focus = UIFocus.Arbiter;
			if (focus.Revision != m_Revision || !focus.CanSelect(m_Pending) || focus.SelectedNode != m_Pending)
			{
				m_Pending = null;
				return;
			}
			if (IsDragging)
			{
				return;
			}
			m_Scroll = m_Scroll != null ? m_Scroll : GetComponent<UnityEngine.UI.ScrollRect>();
			RectTransform content = m_Scroll.content;
			RectTransform viewport = m_Scroll.viewport != null ? m_Scroll.viewport : transform as RectTransform;
			RectTransform target = m_Pending.Rect;
			m_Pending = null;
			if (target == null || content == null || viewport == null || !target.IsChildOf(content))
			{
				return;
			}
			Canvas.ForceUpdateCanvases();
			target.GetWorldCorners(m_Corners);
			Bounds bounds = new Bounds(viewport.InverseTransformPoint(m_Corners[0]), Vector3.zero);
			for (int i = 1; i < m_Corners.Length; i++)
			{
				bounds.Encapsulate(viewport.InverseTransformPoint(m_Corners[i]));
			}
			Rect view = viewport.rect;
			Vector2 delta = new Vector2(
				m_Scroll.horizontal ? Offset(bounds.min.x, bounds.max.x, view.xMin, view.xMax, Mathf.Max(0, m_Padding.x), m_Direction == MoveDirection.Right) : 0,
				m_Scroll.vertical ? Offset(bounds.min.y, bounds.max.y, view.yMin, view.yMax, Mathf.Max(0, m_Padding.y), m_Direction == MoveDirection.Up) : 0);
			if (delta.sqrMagnitude > 0.0001f)
			{
				m_Scroll.StopMovement();
				content.position += viewport.TransformVector(delta);
			}
		}

		private static float Offset(float min, float max, float viewMin, float viewMax, float padding, bool alignMax)
		{
			padding = Mathf.Min(padding, (viewMax - viewMin) * 0.49f);
			viewMin += padding;
			viewMax -= padding;
			if (max - min > viewMax - viewMin)
			{
				return alignMax ? viewMax - max : viewMin - min;
			}
			return min < viewMin ? viewMin - min : max > viewMax ? viewMax - max : 0;
		}
	}
}
