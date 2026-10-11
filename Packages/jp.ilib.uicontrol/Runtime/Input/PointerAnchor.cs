using UnityEngine;

namespace UIControl
{
	/// <summary>
	/// 選択中の要素に対するPointerの位置を、RectTransformで直接指定する。Selectableと同じGameObjectか、その配下に置く。
	/// Target未設定なら自身のRectTransformの中心。Routerの Selection Anchor / Inset より優先し、Selection Offset は足される。
	/// </summary>
	[DisallowMultipleComponent]
	public sealed class PointerAnchor : MonoBehaviour
	{
		[SerializeField, Tooltip("Pointerを置くRectTransform。未設定なら自身。")]
		private RectTransform m_Target;

		public RectTransform Target => m_Target != null ? m_Target : transform as RectTransform;
	}
}
