using UnityEngine;
using UnityEngine.EventSystems;

namespace UIControl
{
	internal static class NavigationGeometry
	{
		internal static Vector2 Direction(MoveDirection direction)
		{
			switch (direction)
			{
				case MoveDirection.Up: return Vector2.up;
				case MoveDirection.Down: return Vector2.down;
				case MoveDirection.Left: return Vector2.left;
				case MoveDirection.Right: return Vector2.right;
				default: return Vector2.zero;
			}
		}

		internal static Camera Camera(NavigationNode node)
		{
			Canvas canvas = node.GetComponentInParent<Canvas>();
			return canvas != null && canvas.rootCanvas.renderMode != RenderMode.ScreenSpaceOverlay
				? canvas.rootCanvas.worldCamera : null;
		}

		internal static Vector2 Center(NavigationNode node)
		{
			RectTransform rect = node.Rect;
			return RectTransformUtility.WorldToScreenPoint(Camera(node),
				rect != null ? rect.TransformPoint(rect.rect.center) : node.transform.position);
		}

		// 画面座標上の進行方向とRectの辺の交点。回転・拡縮・Pivotに依存しない。
		internal static Vector2 ExitPoint(NavigationNode node, Vector2 direction)
		{
			RectTransform transform = node.Rect;
			if (transform == null || direction == Vector2.zero) { return Center(node); }
			Camera camera = Camera(node);
			Rect rect = transform.rect;
			Vector2 center = RectTransformUtility.WorldToScreenPoint(camera, transform.TransformPoint(rect.center));
			Vector2 a = Project(rect.xMin, rect.yMin);
			Vector2 b = Project(rect.xMin, rect.yMax);
			Vector2 c = Project(rect.xMax, rect.yMax);
			Vector2 d = Project(rect.xMax, rect.yMin);
			float distance = float.PositiveInfinity;
			Intersect(center, direction, a, b, ref distance);
			Intersect(center, direction, b, c, ref distance);
			Intersect(center, direction, c, d, ref distance);
			Intersect(center, direction, d, a, ref distance);
			return float.IsInfinity(distance) ? center : center + direction * distance;

			Vector2 Project(float x, float y)
			{
				return RectTransformUtility.WorldToScreenPoint(camera, transform.TransformPoint(new Vector3(x, y, 0)));
			}
		}

		private static void Intersect(Vector2 origin, Vector2 direction, Vector2 a, Vector2 b, ref float distance)
		{
			Vector2 edge = b - a;
			float cross = Cross(direction, edge);
			if (Mathf.Abs(cross) < 0.000001f) { return; }
			Vector2 offset = a - origin;
			float t = Cross(offset, edge) / cross;
			float u = Cross(offset, direction) / cross;
			if (t >= 0 && u >= -0.00001f && u <= 1.00001f && t < distance) { distance = t; }
		}

		private static float Cross(Vector2 a, Vector2 b) { return a.x * b.y - a.y * b.x; }

		internal static float DistanceSquared(NavigationNode node, Vector2 point)
		{
			RectTransform transform = node.Rect;
			if (transform == null) { return (Center(node) - point).sqrMagnitude; }
			Camera camera = Camera(node);
			if (RectTransformUtility.RectangleContainsScreenPoint(transform, point, camera)) { return 0; }
			Rect rect = transform.rect;
			Vector2 a = Project(rect.xMin, rect.yMin);
			Vector2 b = Project(rect.xMin, rect.yMax);
			Vector2 c = Project(rect.xMax, rect.yMax);
			Vector2 d = Project(rect.xMax, rect.yMin);
			return Mathf.Min(Mathf.Min(SegmentDistance(a, b, point), SegmentDistance(b, c, point)),
				Mathf.Min(SegmentDistance(c, d, point), SegmentDistance(d, a, point)));
			Vector2 Project(float x, float y) => RectTransformUtility.WorldToScreenPoint(camera, transform.TransformPoint(new Vector3(x, y, 0)));
		}

		private static float SegmentDistance(Vector2 a, Vector2 b, Vector2 point)
		{
			Vector2 edge = b - a;
			float t = edge.sqrMagnitude > 0 ? Mathf.Clamp01(Vector2.Dot(point - a, edge) / edge.sqrMagnitude) : 0;
			return (point - (a + edge * t)).sqrMagnitude;
		}
	}
}
