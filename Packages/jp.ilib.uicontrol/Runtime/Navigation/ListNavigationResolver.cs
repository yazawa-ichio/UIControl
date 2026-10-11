using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;

namespace UIControl
{
	public enum ListNavigationLayout { Vertical, Horizontal, Grid, First, Last }

	public sealed class ListNavigationResolver : NavigationResolver
	{
		[SerializeField]
		private Transform m_Root;
		[SerializeField]
		private ListNavigationLayout m_Layout;
		[SerializeField, Min(1)]
		private int m_Columns = 1;

		public override NavigationNode Resolve(NavigationNode current, MoveDirection direction)
		{
			Transform root = m_Root != null ? m_Root : transform;
			List<NavigationNode> nodes = root.GetComponentsInChildren<NavigationNode>(true)
				.Where(node => node.Scope == current.Scope).ToList();
			nodes.Sort((a, b) => HierarchyOrder.Compare(a.transform, b.transform));
			FocusArbiter focus = UIFocus.Arbiter;
			if (m_Layout == ListNavigationLayout.First)
			{
				return nodes.FirstOrDefault(node => node != current && focus.CanSelect(node));
			}
			if (m_Layout == ListNavigationLayout.Last)
			{
				return nodes.LastOrDefault(node => node != current && focus.CanSelect(node));
			}
			int index = nodes.IndexOf(current);
			bool horizontal = direction == MoveDirection.Left || direction == MoveDirection.Right;
			if (index < 0 || direction == MoveDirection.None || m_Layout == ListNavigationLayout.Vertical && horizontal
				|| m_Layout == ListNavigationLayout.Horizontal && !horizontal)
			{
				return null;
			}
			int columns = Mathf.Max(1, m_Columns);
			int delta = direction == MoveDirection.Up || direction == MoveDirection.Left ? -1 : 1;
			if (m_Layout == ListNavigationLayout.Grid && !horizontal)
			{
				delta *= columns;
			}
			for (int next = index + delta; next >= 0 && next < nodes.Count; next += delta)
			{
				if (m_Layout == ListNavigationLayout.Grid && horizontal && next / columns != index / columns)
				{
					break;
				}
				if (focus.CanSelect(nodes[next]))
				{
					return nodes[next];
				}
			}
			return null;
		}
	}
}
