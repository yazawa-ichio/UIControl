using System;
using UnityEngine;

namespace UIControl
{
	/// <summary>Mouse: 実マウスまたは仮想Mouseデバイスによる標準Pointer操作。Navigation: 方向キーによる選択移動。</summary>
	public enum UIInputMode { Mouse, Navigation }

	/// <summary>入力方式と、UIへ配送したPointerの状態。入力パッケージに依存しない。</summary>
	public interface IUIInputSource : IPointerPositionSource
	{
		UIInputMode Mode { get; }
		bool IsInputActive { get; }
		/// <summary>Pointerの左ボタンが押されている。</summary>
		bool IsPointerPressed { get; }
		event Action<UIInputMode> ModeChanged;
	}
}
