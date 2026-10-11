using System;
using UnityEngine;

namespace UIControl
{
	public enum BackdropMode { Inherit, KeepVisible, Covered, Hidden }
	public enum BackdropRange { Inherit, None, Previous, AllBehind }

	[Serializable]
	public struct BackdropSettings
	{
		[SerializeField, UnityEngine.Serialization.FormerlySerializedAs("m_Effect")]
		private BackdropMode m_Mode;
		[SerializeField, UnityEngine.Serialization.FormerlySerializedAs("m_Scope")]
		private BackdropRange m_Range;
		public BackdropMode Mode => m_Mode;
		public BackdropRange Range => m_Range;

		public BackdropSettings(BackdropMode mode, BackdropRange range = BackdropRange.Inherit)
		{
			if (!Enum.IsDefined(typeof(BackdropMode), mode)) { throw new ArgumentOutOfRangeException(nameof(mode)); }
			if (!Enum.IsDefined(typeof(BackdropRange), range)) { throw new ArgumentOutOfRangeException(nameof(range)); }
			m_Mode = mode;
			m_Range = range;
		}

		/// <summary>Inheritの項目だけを指定値で埋める。UIStackの既定を具体値に保つために使う。</summary>
		internal BackdropSettings OrDefault(BackdropMode mode, BackdropRange range)
		{
			return new BackdropSettings(m_Mode == BackdropMode.Inherit ? mode : m_Mode, m_Range == BackdropRange.Inherit ? range : m_Range);
		}

		/// <summary>要求 → Prefab → UIStackの順に解決する。UIStackの既定が終端で、そこにInheritは残らない。</summary>
		internal static BackdropSettings Resolve(BackdropSettings request, BackdropSettings prefab, BackdropSettings stackDefault)
		{
			BackdropMode mode = request.Mode != BackdropMode.Inherit ? request.Mode : prefab.Mode;
			if (mode == BackdropMode.Inherit) { mode = stackDefault.Mode; }
			BackdropRange range = request.Range != BackdropRange.Inherit ? request.Range : prefab.Range;
			if (range == BackdropRange.Inherit) { range = stackDefault.Range; }
			if (mode == BackdropMode.Inherit || range == BackdropRange.Inherit) { throw new InvalidOperationException("UIStack Backdrop default must be a concrete value."); }
			return new BackdropSettings(mode, range);
		}
	}
}
