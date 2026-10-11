using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Layouts;
using UnityEngine.InputSystem.LowLevel;

namespace UIControl.InputSystem
{
	/// <summary>
	/// Mouse派生の専用Layout。利用者のUI Actionにある &lt;Mouse&gt;/position 等の束縛にそのまま一致し、
	/// 型で実マウスと区別できる。Mouse.current / Pointer.current は奪わない。
	/// Input Moduleの設定・参照は触らない。追加されるデバイスはこの1種類だけ。
	/// </summary>
	[InputControlLayout(stateType = typeof(MouseState), displayName = "UIControl Virtual Mouse", hideInUI = true)]
	public class UIControlVirtualMouse : Mouse
	{
		/// <summary>実マウスを押しのけてcurrentにならない。Mouse.currentを読むゲーム側コードへ影響しない。</summary>
		public override void MakeCurrent() { }

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
		public static void EnsureRegistered()
		{
			UnityEngine.InputSystem.InputSystem.RegisterLayout<UIControlVirtualMouse>();
		}

#if UNITY_EDITOR
		[UnityEditor.InitializeOnLoadMethod]
		private static void RegisterInEditor() { EnsureRegistered(); }
#endif
	}
}
