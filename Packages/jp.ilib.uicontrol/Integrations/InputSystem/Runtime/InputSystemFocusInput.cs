using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;

namespace UIControl.InputSystem
{
	[DisallowMultipleComponent]
	public sealed class InputSystemFocusInput : MonoBehaviour
	{
		[SerializeField] private InputSystemUIInputModule m_Module;
		private InputSystemUIInputModule m_BoundModule;
		private InputAction m_Move;
		private InputAction m_Click;

		private void OnEnable() { Refresh(); }
		private void Update() { Refresh(); }
		private void Refresh()
		{
			InputSystemUIInputModule module = m_Module != null ? m_Module : EventSystem.current?.GetComponent<InputSystemUIInputModule>();
			InputAction move = module != null ? module.move?.action : null;
			InputAction click = module != null ? module.leftClick?.action : null;
			if (m_BoundModule == module && m_Move == move && m_Click == click) { return; }
			Unbind();
			m_BoundModule = module;
			m_Move = move;
			m_Click = click;
			if (m_Move != null) { m_Move.performed += OnMove; }
			if (m_Click != null) { m_Click.performed += OnClick; }
		}
		private void Unbind()
		{
			if (m_Move != null) { m_Move.performed -= OnMove; }
			if (m_Click != null) { m_Click.performed -= OnClick; }
			m_Move = null;
			m_Click = null;
			m_BoundModule = null;
		}
		private void OnDisable() { Unbind(); }
		private void OnMove(InputAction.CallbackContext context)
		{
			if (context.ReadValue<Vector2>().sqrMagnitude > 0) { UIFocus.NotifyInputMode(FocusInputMode.Navigation); }
		}
		private void OnClick(InputAction.CallbackContext context)
		{
			if (context.ReadValueAsButton()) { UIFocus.NotifyInputMode(FocusInputMode.Pointer); }
		}
	}
}
