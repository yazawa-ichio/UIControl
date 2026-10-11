using UnityEngine;

namespace UIControl
{
	internal static class PointerGeometry
	{
		internal static Camera Camera(RectTransform target)
		{
			Canvas canvas = target.GetComponentInParent<Canvas>()?.rootCanvas;
			return canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
		}

		internal static void Clamp(RectTransform target, RectTransform bounds, Vector3[] corners)
		{
			target.GetWorldCorners(corners);
			Vector2 min = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
			Vector2 max = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
			for (int i = 0; i < 4; i++)
			{
				Vector2 point = bounds.InverseTransformPoint(corners[i]);
				min = Vector2.Min(min, point);
				max = Vector2.Max(max, point);
			}
			Rect rect = bounds.rect;
			Vector3 offset = new Vector3(Offset(min.x, max.x, rect.xMin, rect.xMax), Offset(min.y, max.y, rect.yMin, rect.yMax), 0);
			target.position += bounds.TransformVector(offset);
		}

		private static float Offset(float min, float max, float low, float high)
		{
			if (max - min > high - low) { return (low + high - min - max) * 0.5f; }
			return min < low ? low - min : max > high ? high - max : 0;
		}
	}
}
