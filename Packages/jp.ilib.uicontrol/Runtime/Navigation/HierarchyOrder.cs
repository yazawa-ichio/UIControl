using UnityEngine;

namespace UIControl
{
	/// <summary>Hierarchy上の前後関係。割り当てをせずに共通の祖先まで遡って兄弟順を比べる。</summary>
	public static class HierarchyOrder
	{
		public static int Compare(Transform left, Transform right)
		{
			if (left == right) { return 0; }
			// 別Sceneの比較は稀なので、その時だけpath文字列を取る。同じSceneでは割り当てない。
			if (left.gameObject.scene != right.gameObject.scene)
			{
				int scene = string.CompareOrdinal(left.gameObject.scene.path, right.gameObject.scene.path);
				if (scene != 0) { return scene; }
			}
			int leftDepth = Depth(left);
			int rightDepth = Depth(right);
			Transform a = left;
			Transform b = right;
			Transform aChild = null;
			Transform bChild = null;
			while (leftDepth > rightDepth) { aChild = a; a = a.parent; leftDepth--; }
			while (rightDepth > leftDepth) { bChild = b; b = b.parent; rightDepth--; }
			while (a != b) { aChild = a; bChild = b; a = a.parent; b = b.parent; }
			// 祖先は子孫より前。
			if (aChild == null) { return -1; }
			if (bChild == null) { return 1; }
			return aChild.GetSiblingIndex().CompareTo(bChild.GetSiblingIndex());
		}

		private static int Depth(Transform transform)
		{
			int depth = 0;
			for (Transform current = transform.parent; current != null; current = current.parent) { depth++; }
			return depth;
		}
	}
}
