using UnityEngine;
using UnityEngine.EventSystems;

namespace UIControl
{
	/// <summary>要素内のどこをPointerの位置にするか。</summary>
	public enum SelectionAnchor { Center, TopLeft, TopRight, BottomLeft, BottomRight }

	/// <summary>uGUIの要素を画面座標へ変換する小さな補助。Pointerの起点や追従先を「選択中の要素」にする時に使う。</summary>
	public static class ScreenPoint
	{
		/// <summary>RectTransformの中心を画面座標にする。Canvasのカメラ（Overlayならnull）で変換する。</summary>
		public static bool TryGetCenter(RectTransform rect, out Vector2 screenPosition)
		{
			return TryGetPoint(rect, SelectionAnchor.Center, 0, Vector2.zero, out screenPosition);
		}

		/// <summary>
		/// RectTransform内の指定位置を画面座標にする。四隅はinset（画面ピクセル）だけ中心方向へ寄せる。
		/// insetが0でも1px内側に置き、その点でのRaycastが要素に当たるようにする。offsetは最後に足す。
		/// </summary>
		public static bool TryGetPoint(RectTransform rect, SelectionAnchor anchor, float inset, Vector2 offset, out Vector2 screenPosition)
		{
			screenPosition = default;
			if (rect == null || !rect.gameObject.activeInHierarchy) { return false; }
			Camera camera = CameraOf(rect);
			Rect local = rect.rect;
			Vector2 center = RectTransformUtility.WorldToScreenPoint(camera, rect.TransformPoint(local.center));
			Vector2 point = center;
			if (anchor != SelectionAnchor.Center)
			{
				Vector2 normalized = Normalized(anchor);
				point = RectTransformUtility.WorldToScreenPoint(camera, rect.TransformPoint(local.min + Vector2.Scale(local.size, normalized)));
				float pull = Mathf.Max(1, inset);
				point.x = Mathf.Abs(center.x - point.x) <= pull ? center.x : point.x + Mathf.Sign(center.x - point.x) * pull;
				point.y = Mathf.Abs(center.y - point.y) <= pull ? center.y : point.y + Mathf.Sign(center.y - point.y) * pull;
			}
			screenPosition = point + offset;
			return true;
		}

		/// <summary>EventSystemで選択中の要素の中心。選択が無い、RectTransformでない、非表示ならfalse。</summary>
		public static bool TryGetSelectedCenter(out Vector2 screenPosition)
		{
			return TryGetSelectedPoint(SelectionAnchor.Center, 0, Vector2.zero, out screenPosition);
		}

		/// <summary>
		/// EventSystemで選択中の要素内の指定位置。選択が無い、RectTransformでない、非表示ならfalse。
		/// 選択中の要素またはその配下に <see cref="PointerAnchor"/> があれば、anchorとinsetの代わりにその位置を使う（offsetは足す）。
		/// </summary>
		public static bool TryGetSelectedPoint(SelectionAnchor anchor, float inset, Vector2 offset, out Vector2 screenPosition)
		{
			GameObject selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
			if (selected == null || !(selected.transform is RectTransform rect)) { screenPosition = default; return false; }
			PointerAnchor marker = selected.GetComponentInChildren<PointerAnchor>(true);
			if (marker != null && marker.Target != null && marker.Target.gameObject.activeInHierarchy)
			{
				RectTransform target = marker.Target;
				screenPosition = RectTransformUtility.WorldToScreenPoint(CameraOf(target), target.TransformPoint(target.rect.center)) + offset;
				return true;
			}
			return TryGetPoint(rect, anchor, inset, offset, out screenPosition);
		}

		private static Camera CameraOf(RectTransform rect)
		{
			Canvas canvas = rect.GetComponentInParent<Canvas>();
			Canvas root = canvas != null ? canvas.rootCanvas : null;
			return root != null && root.renderMode != RenderMode.ScreenSpaceOverlay ? root.worldCamera : null;
		}

		private static Vector2 Normalized(SelectionAnchor anchor)
		{
			switch (anchor)
			{
				case SelectionAnchor.TopLeft: return new Vector2(0, 1);
				case SelectionAnchor.TopRight: return new Vector2(1, 1);
				case SelectionAnchor.BottomLeft: return new Vector2(0, 0);
				case SelectionAnchor.BottomRight: return new Vector2(1, 0);
				default: return new Vector2(0.5f, 0.5f);
			}
		}
	}
}
