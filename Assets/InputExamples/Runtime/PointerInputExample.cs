using UIControl.InputSystem;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace UIControl.Examples
{
	/// <summary>入力補助の利用例。操作結果を画面で確認する。</summary>
	public sealed class PointerInputExample : MonoBehaviour
	{
		[SerializeField] private FocusScope m_Scope;
		[SerializeField] private InputSystemUIInputRouter m_Router;
		[SerializeField] private UnityEngine.UI.Text m_Status;
		private string m_Message = "Drag the blue card to the green area.";
		private void Start() { m_Scope.Activate(); }
		public void SelectButton(string name) { m_Message = "Pressed: " + name; }
		public void Dropped(UIDraggable source) { m_Message = "Accepted: " + source.name; }
		public void DragEnded(bool accepted) { if (!accepted) { m_Message = "Drag ended without acceptance; position restored."; } }
		private void Update()
		{
			Gamepad pad = Gamepad.current;
			string selected = EventSystem.current?.currentSelectedGameObject?.name ?? "(none)";
			m_Status.text = "Mode: " + m_Router.Mode + "    Selected: " + selected
				+ "\nPad: " + (pad != null ? pad.displayName + "   L " + pad.leftStick.ReadValue().ToString("F2") : "(not connected)")
				+ "\n" + m_Message;
		}
	}
}
