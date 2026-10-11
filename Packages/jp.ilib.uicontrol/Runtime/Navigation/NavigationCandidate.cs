using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace UIControl
{
	public enum CandidateKind { Node, Scope, Resolver, Automatic }
	public enum FocusInputMode { Navigation, Pointer }
	[Flags]
	public enum NavigationDirections { None = 0, Up = 1, Down = 2, Left = 4, Right = 8, All = 15 }

	/// <summary>方向ごとの移動先候補。Node: 明示のSelectable。Scope: そのScopeの復元先。Automatic: 幾何探索。Resolver: 独自解決。</summary>
	[Serializable]
	public sealed class NavigationCandidate
	{
		[SerializeField]
		private CandidateKind m_Kind;
		[SerializeField, UnityEngine.Serialization.FormerlySerializedAs("m_Node")]
		private Selectable m_Target;
		[SerializeField]
		private FocusScope m_Scope;
		[SerializeField]
		private NavigationResolver m_Resolver;
		public CandidateKind Kind => m_Kind;
		public Selectable Target => m_Target;
		public FocusScope Scope => m_Scope;
		public NavigationResolver Resolver => m_Resolver;
	}

	public abstract class NavigationResolver : MonoBehaviour
	{
		public abstract NavigationNode Resolve(NavigationNode current, MoveDirection direction);
	}
}
