using UnityEngine;

namespace UIControl
{
	public interface IPointerPositionSource
	{
		bool TryGetPosition(out Vector2 screenPosition);
	}

	/// <summary>画面座標を対象の親Rectへ変換する。Rootや入力パッケージを必要としない。</summary>
	[DisallowMultipleComponent, RequireComponent(typeof(RectTransform))]
	public sealed class PointerFollower : MonoBehaviour
	{
		[SerializeField, Tooltip("IPointerPositionSourceを実装したComponent。未設定時はSetPositionで座標を渡す。")]
		private MonoBehaviour m_Source;
		[SerializeField] private RectTransform m_Target;
		[SerializeField, Tooltip("親Rectのローカル座標単位。")]
		private Vector2 m_Offset;
		[SerializeField, Tooltip("任意。対象Rect全体をこの範囲へ収める。範囲より大きい軸は中央に寄せる。")]
		private RectTransform m_Bounds;
		private readonly Vector3[] m_Corners = new Vector3[4];

		private void LateUpdate()
		{
			if (m_Source != null && m_Source.isActiveAndEnabled && m_Source is IPointerPositionSource source && source.TryGetPosition(out Vector2 position)) { SetPosition(position); }
		}

		public bool SetPosition(Vector2 screenPosition)
		{
			RectTransform target = m_Target != null ? m_Target : (RectTransform)transform;
			if (!(target.parent is RectTransform parent) || !RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, screenPosition, PointerGeometry.Camera(target), out Vector2 local)) { return false; }
			Vector3 position = target.localPosition;
			position.x = local.x + m_Offset.x;
			position.y = local.y + m_Offset.y;
			target.localPosition = position;
			if (m_Bounds != null && m_Bounds != target && !m_Bounds.IsChildOf(target)) { PointerGeometry.Clamp(target, m_Bounds, m_Corners); }
			return true;
		}
	}
}
