using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

namespace UIControl
{
	/// <summary>InputButtonの露出判定と起動。型で候補を集め、Raycastで最前面にあるものだけを押す。</summary>
	internal sealed class InputButtonResolver
	{
		internal static readonly InputButtonResolver Shared = new InputButtonResolver();
		private readonly List<RaycastResult> m_Hits = new List<RaycastResult>();
		private bool m_Invoking;
		private readonly List<InputButton> m_Candidates = new List<InputButton>();
		private PointerEventData m_Pointer;
		private EventSystem m_Events;

		internal bool TryInvoke(System.Type type)
		{
			EventSystem events = EventSystem.current;
			if (type == null || events == null || !events.isActiveAndEnabled || events != EventSystem.current || m_Invoking)
			{
				return false;
			}
			// Guard synchronous callbacks from consuming the same dispatch recursively.
			// A later independent call remains valid; there is no time-based debounce.
			m_Invoking = true;
			try
			{
				if (m_Pointer == null || m_Events != events) { m_Events = events; m_Pointer = new PointerEventData(events); }
				InputButton.Collect(type, m_Candidates);
				InputButton best = null;
				RaycastResult bestHit = default;
				foreach (InputButton candidate in m_Candidates)
				{
					if (!TryHit(candidate, type, events, out RaycastResult hit))
					{
						continue;
					}
					if (best == null || candidate.Priority > best.Priority || candidate.Priority == best.Priority
						&& Compare(hit, bestHit, candidate.transform, best.transform) < 0)
					{
						best = candidate;
						bestHit = hit;
					}
				}
				if (best == null || !TryHit(best, type, events, out _) || events == null || !events.isActiveAndEnabled || events != EventSystem.current)
				{
					return false;
				}
				best.Button.onClick.Invoke();
				return true;
			}
			finally
			{
				m_Candidates.Clear();
				m_Hits.Clear();
				m_Invoking = false;
			}
		}

		private bool TryHit(InputButton candidate, System.Type type, EventSystem events, out RaycastResult hit)
		{
			hit = default;
			if (candidate == null || !type.IsInstanceOfType(candidate) || !candidate.isActiveAndEnabled || candidate.Button == null || candidate.Probe == null
				|| !candidate.Button.isActiveAndEnabled || !candidate.Button.IsInteractable())
			{
				return false;
			}
			Page screen = candidate.Button.GetComponentInParent<Page>(true);
			if (screen != null && (!screen.IsRendered || !screen.IsInputAllowed))
			{
				return false;
			}
			Canvas canvas = candidate.Button.GetComponentInParent<Canvas>();
			if (canvas == null || canvas.rootCanvas.renderMode == RenderMode.WorldSpace)
			{
				return false;
			}
			Camera camera = canvas.rootCanvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.rootCanvas.worldCamera;
			if (canvas.rootCanvas.renderMode == RenderMode.ScreenSpaceCamera && camera == null)
			{
				return false;
			}
			Vector3 world = candidate.Probe.TransformPoint(candidate.Probe.rect.center);
			if (camera != null && camera.WorldToScreenPoint(world).z <= 0)
			{
				return false;
			}
			m_Pointer.Reset();
			m_Pointer.position = RectTransformUtility.WorldToScreenPoint(camera, world);
			m_Pointer.button = PointerEventData.InputButton.Left;
			m_Hits.Clear();
			events.RaycastAll(m_Pointer, m_Hits);
			if (m_Hits.Count == 0)
			{
				return false;
			}
			hit = m_Hits[0];
			if (!(hit.module is UnityEngine.UI.GraphicRaycaster) || hit.gameObject == null)
			{
				return false;
			}
			GameObject click = ExecuteEvents.GetEventHandler<IPointerClickHandler>(hit.gameObject);
			GameObject down = ExecuteEvents.GetEventHandler<IPointerDownHandler>(hit.gameObject);
			return candidate != null && type.IsInstanceOfType(candidate) && candidate.isActiveAndEnabled && candidate.Button != null && candidate.Button.isActiveAndEnabled
				&& candidate.Button.IsInteractable()
				&& (screen == null || screen.IsRendered && screen.IsInputAllowed)
				&& click == candidate.Button.gameObject && (down == null || down == candidate.Button.gameObject);
		}

		private static int Compare(RaycastResult a, RaycastResult b, Transform left, Transform right)
		{
			if (a.module != b.module)
			{
				Camera ac = a.module.eventCamera;
				Camera bc = b.module.eventCamera;
				if (ac != null && bc != null && ac.depth != bc.depth)
				{
					return bc.depth.CompareTo(ac.depth);
				}
				if (a.module.sortOrderPriority != b.module.sortOrderPriority)
				{
					return b.module.sortOrderPriority.CompareTo(a.module.sortOrderPriority);
				}
				if (a.module.renderOrderPriority != b.module.renderOrderPriority)
				{
					return b.module.renderOrderPriority.CompareTo(a.module.renderOrderPriority);
				}
			}
			int layer = SortingLayer.GetLayerValueFromID(b.sortingLayer).CompareTo(SortingLayer.GetLayerValueFromID(a.sortingLayer));
			if (layer != 0)
			{
				return layer;
			}
			if (a.sortingOrder != b.sortingOrder)
			{
				return b.sortingOrder.CompareTo(a.sortingOrder);
			}
			if (a.module.rootRaycaster == b.module.rootRaycaster && a.depth != b.depth)
			{
				return b.depth.CompareTo(a.depth);
			}
			if (a.distance != b.distance)
			{
				return a.distance.CompareTo(b.distance);
			}
			return HierarchyOrder.Compare(left, right);
		}
	}
}
