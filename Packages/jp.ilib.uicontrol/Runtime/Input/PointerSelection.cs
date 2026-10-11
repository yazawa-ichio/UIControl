using UnityEngine;
using UnityEngine.EventSystems;

namespace UIControl
{
	/// <summary>
	/// PointerとNavigationの選択方針。入力取得やカーソル描画は行わない。
	/// 方式の変化をUIFocusへ通知し、Pointer方式の間は通常Buttonの選択を保持しない（オプション）。
	/// Navigationへ戻る時は、未選択なら入力方向側の近い候補から選択を始められる（オプション）。
	/// </summary>
	[DisallowMultipleComponent, DefaultExecutionOrder(-1500)]
	public sealed class PointerSelection : MonoBehaviour
	{
		[SerializeField, Tooltip("IUIInputSourceを実装したComponent。")]
		private MonoBehaviour m_Source;
		[SerializeField, Tooltip("未選択のままNavigationへ戻る時、入力方向側の近い候補から選択を始める。")]
		private bool m_SelectNearestOnNavigation;
		[SerializeField, Tooltip("Pointer方式の間、通常Buttonの選択を保持しない。OFFにするとPointer中も選択を残す。編集中の入力欄は常に保持する。")]
		private bool m_ClearSelectionOnPointerMove = true;
		private IUIInputSource m_BoundSource;
		private Vector2 m_LastPosition;
		public bool IsEditingText => IsTextInput(UIFocus.Selected, true);

		private void OnEnable() { Bind(); }
		private void Update() { Bind(); }
		private void Bind()
		{
			IUIInputSource source = m_Source != null && m_Source.isActiveAndEnabled ? m_Source as IUIInputSource : null;
			if (ReferenceEquals(source, m_BoundSource)) { return; }
			if (m_BoundSource != null) { m_BoundSource.ModeChanged -= OnModeChanged; }
			m_BoundSource = source;
			if (source != null) { source.ModeChanged += OnModeChanged; OnModeChanged(source.Mode); }
		}

		private void OnModeChanged(UIInputMode mode)
		{
			UIFocus.NotifyInputMode(mode == UIInputMode.Navigation ? FocusInputMode.Navigation : FocusInputMode.Pointer);
			if (mode == UIInputMode.Mouse && m_ClearSelectionOnPointerMove) { ClearSelection(false); }
		}

		/// <summary>Navigationへ切り替える前に呼ぶ。方向側に開始候補がなければfalse。consumeInitialInputが真なら、その入力を解放するまで通常配送を保留する。</summary>
		public bool TryBeginNavigation(Vector2 position, MoveDirection direction, out bool consumeInitialInput)
		{
			consumeInitialInput = false;
			// 占有中はNavigationへ移らない。方式が変わると選択の移動が起きる。
			if (UIFocus.IsNavigationOccupied) { return false; }
			if (UIFocus.Selected != null || !m_SelectNearestOnNavigation) { return true; }
			bool started = UIFocus.TrySelectNearest(position, direction);
			if (!started && direction != MoveDirection.None) { return false; }
			consumeInitialInput = started;
			return true;
		}

		private void LateUpdate()
		{
			IUIInputSource source = m_BoundSource;
			if (source == null || !source.IsInputActive || source.Mode == UIInputMode.Navigation || !source.TryGetPosition(out Vector2 position)) { return; }
			bool moving = position != m_LastPosition;
			m_LastPosition = position;
			GameObject selected = UIFocus.Selected;
			// InputFieldは次のLateUpdateで編集を開始することがある。押下フレームの選択も維持する。
			if (IsTextInput(selected, moving && !source.IsPointerPressed)) { return; }
			if (UIFocus.IsNavigationOccupied) { return; }
			if (m_ClearSelectionOnPointerMove) { ClearSelection(moving); }
		}

		private static bool IsTextInput(GameObject selected, bool editingOnly)
		{
			if (selected == null) { return false; }
			UnityEngine.UI.InputField input = selected.GetComponent<UnityEngine.UI.InputField>();
			TMPro.TMP_InputField text = selected.GetComponent<TMPro.TMP_InputField>();
			return input != null && (!editingOnly || input.isFocused) || text != null && (!editingOnly || text.isFocused);
		}

		private void ClearSelection(bool moving)
		{
			GameObject selected = UIFocus.Selected;
			if (selected != null && !IsTextInput(selected, moving)) { UIFocus.Deselect(selected); }
		}

		private void OnDisable()
		{
			if (m_BoundSource != null) { m_BoundSource.ModeChanged -= OnModeChanged; }
			m_BoundSource = null;
		}
	}
}
