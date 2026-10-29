using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace UIControl
{
	/// <summary>
	/// 選択の単位。配下のSelectableを自動で候補にし、Activate()で操作対象の候補に入る。
	/// Rootは不要で、有効化時にUIFocusへ自分で登録する。優先度が高いScope、同値なら後から有効化したScopeが操作対象になる。
	/// 明示的な方向候補・除外が要る対象だけNavigationNodeを付ける。動的に生成する行にはNavigationNodeを付けておく
	/// （生成をScopeが検知できないため。付けない場合は生成後にRefresh()を呼ぶ）。
	/// </summary>
	[DisallowMultipleComponent]
	public sealed class FocusScope : MonoBehaviour
	{
		[SerializeField, Tooltip("最初に選ぶ対象。未設定なら配下のHierarchy順で最初の候補。")]
		private Selectable m_Initial;
		[SerializeField, Tooltip("Initialが選べない時に順に試す対象。")]
		private Selectable[] m_Fallbacks = Array.Empty<Selectable>();
		[SerializeField] private int m_Priority;
		[SerializeField, Tooltip("再び操作対象になった時、最後に選んでいた対象へ戻す。")]
		private bool m_RestoreLast = true;
		[SerializeField, Tooltip("選択が空・無効になった時、自動で選択を戻す。OFFでは次の方向入力（UIFocus.NotifyMove）か、Activate / RequestSelect / RequestRestoreまで空のまま。")]
		private bool m_RestoreOnEmpty = true;
		[SerializeField, Tooltip("配下のSelectableを自動で候補にし、NavigationNodeが無ければ付ける。OFFではNavigationNodeを付けた対象だけを候補にする。")]
		private bool m_CollectSelectables = true;
		[SerializeField, Tooltip("この配下のSelectableは候補にしない（自動収集・NavigationNodeのどちらも）。")]
		private Transform[] m_Exclude = Array.Empty<Transform>();
		[SerializeField, Tooltip("マウス等で押した配下の対象を選択し、Pointer方式の間も選択を保つ。Navigationへ戻るとそこから続ける。OFFではPointer中は選択を持たない。")]
		private bool m_SelectOnPointer;
		private bool m_Dirty = true;
		internal readonly List<NavigationNode> Nodes = new List<NavigationNode>();
		internal NavigationNode LastSelectedNode;
		public Selectable Initial => m_Initial;
		public IReadOnlyList<Selectable> Fallbacks => m_Fallbacks;
		public int Priority { get => m_Priority; set { m_Priority = value; UIFocus.Reconcile(); } }
		public bool RestoreLast => m_RestoreLast;
		/// <summary>選択が空・無効になった時に自動で戻すか。既定ON。OFFでは明示の要求か方向入力まで空のまま。</summary>
		public bool RestoreOnEmpty { get => m_RestoreOnEmpty; set => m_RestoreOnEmpty = value; }
		/// <summary>配下のSelectableを自動で候補にするか。OFFではNavigationNodeを付けた対象だけ。</summary>
		public bool CollectSelectables { get => m_CollectSelectables; set { if (m_CollectSelectables != value) { m_CollectSelectables = value; Invalidate(); } } }
		/// <summary>押した対象を選択し、Pointer方式の間も選択を保つか。既定OFF。</summary>
		public bool SelectOnPointer { get => m_SelectOnPointer; set => m_SelectOnPointer = value; }
		/// <summary>候補にしない配下。</summary>
		public IReadOnlyList<Transform> Exclude => m_Exclude;
		/// <summary>最後に選んでいた対象。</summary>
		public Selectable LastSelected => LastSelectedNode != null ? LastSelectedNode.Selectable : null;
		/// <summary>有効化されて操作対象の候補に入っている。</summary>
		public bool IsActive => UIFocus.Arbiter.IsActivated(this);
		/// <summary>いま操作対象になっている。</summary>
		public bool IsCurrent => ReferenceEquals(UIFocus.Current, this);

		/// <summary>
		/// 焦点を持つ資格があるか。表示され準備が終わっていれば真とする。
		/// 入力の可否は含めない。含めると遷移中などの一時的な不可で焦点の所属まで消え、戻す先が分からなくなる。
		/// </summary>
		public bool CanFocus
		{
			get
			{
				Page page = GetComponentInParent<Page>(true);
				return isActiveAndEnabled && (page == null || !page.IsPreparing && page.IsRendered);
			}
		}

		/// <summary>いま操作を受け付けてよいか。Pageが入力を許していない間は偽。</summary>
		public bool AllowsInput
		{
			get
			{
				Page page = GetComponentInParent<Page>(true);
				return page == null || page.IsInputAllowed;
			}
		}

		/// <summary>操作対象の候補に入れる。優先度と有効化順で現在の対象が決まる。登録だけでは選択を奪わない。</summary>
		public bool Activate() { return UIFocus.Arbiter.Activate(this); }
		/// <summary>操作対象の候補から外す。</summary>
		public bool Release() { return UIFocus.Arbiter.Release(this); }
		/// <summary>このScope内の対象を選ぶ。Scopeも有効化する。</summary>
		public bool RequestSelect(Selectable target)
		{
			NavigationNode node = NodeOf(target);
			return node != null && UIFocus.Arbiter.RequestSelect(node);
		}
		/// <summary>履歴・Initial・Fallbacks・Hierarchy順の規則で選択を戻す。Scopeも有効化する。</summary>
		public bool RequestRestore() { return UIFocus.Arbiter.RequestRestore(this); }
		/// <summary>現在の選択がこのScopeの中にあれば、履歴として記憶する。</summary>
		public void Remember() { UIFocus.Arbiter.Remember(this); }
		/// <summary>選択は動かさず、復元先だけを履歴に記憶する。Pointer方式でタブを切り替えた時などに使う。</summary>
		public void RememberRestore() { UIFocus.Arbiter.RememberRestore(this); }
		/// <summary>現在の選択がこのScopeの中にある。</summary>
		public bool IsInside() { return UIFocus.Arbiter.IsInside(this); }
		public bool Contains(GameObject selection)
		{
			NavigationNode node = selection != null ? selection.GetComponent<NavigationNode>() : null;
			return node != null && ReferenceEquals(node.Scope, this);
		}
		/// <summary>配下のSelectableを集め直す。動的に生成した対象にNavigationNodeが無い場合に呼ぶ。</summary>
		public void Refresh() { m_Dirty = true; UIFocus.Arbiter.Invalidate(); }

		internal void Invalidate() { m_Dirty = true; UIFocus.Arbiter.Invalidate(); }

		internal NavigationNode NodeOf(Selectable selectable)
		{
			if (selectable == null) { return null; }
			Collect();
			NavigationNode node = selectable.GetComponent<NavigationNode>();
			return node != null && ReferenceEquals(node.Scope, this) ? node : null;
		}

		private bool IsExcluded(Transform target)
		{
			foreach (Transform root in m_Exclude)
			{
				if (root != null && target.IsChildOf(root)) { return true; }
			}
			return false;
		}

		/// <summary>
		/// 配下のSelectableを候補にする。最寄りのScopeが自分で、同じPageに属し、Excludeの配下でないものだけ。
		/// CollectSelectablesがONならNavigationNodeが無い対象にも付け、OFFなら付いている対象だけを候補にする。
		/// </summary>
		internal void Collect()
		{
			if (!m_Dirty) { return; }
			m_Dirty = false;
			bool registered = UIFocus.Arbiter.IsRegistered(this);
			foreach (NavigationNode previous in Nodes)
			{
				if (previous != null && ReferenceEquals(previous.Scope, this)) { previous.Scope = null; previous.OwnNavigation(false); }
			}
			Nodes.Clear();
			Page page = GetComponentInParent<Page>(true);
			foreach (Selectable selectable in GetComponentsInChildren<Selectable>(true))
			{
				if (selectable.GetComponentInParent<FocusScope>(true) != this) { continue; }
				if (selectable.GetComponentInParent<Page>(true) != page) { continue; }
				if (IsExcluded(selectable.transform)) { continue; }
				NavigationNode node = selectable.GetComponent<NavigationNode>();
				if (node == null)
				{
					if (!m_CollectSelectables) { continue; }
					node = selectable.gameObject.AddComponent<NavigationNode>();
					node.MarkAuto();
				}
				node.Scope = this;
				node.OwnNavigation(registered && isActiveAndEnabled);
				Nodes.Add(node);
			}
			Nodes.Sort((a, b) => HierarchyOrder.Compare(a.transform, b.transform));
		}

		private void OnEnable() { m_Dirty = true; UIFocus.Arbiter.Register(this); }
		private void OnDisable()
		{
			foreach (NavigationNode node in Nodes) { if (node != null) { node.OwnNavigation(false); } }
			UIFocus.Arbiter.Unregister(this);
			m_Dirty = true;
		}
		private void OnDestroy()
		{
			foreach (NavigationNode node in Nodes) { if (node != null && ReferenceEquals(node.Scope, this)) { node.Scope = null; } }
			Nodes.Clear();
		}
		private void OnTransformChildrenChanged() { Invalidate(); }
		private void OnTransformParentChanged() { Invalidate(); }
		private void OnValidate() { if (Application.isPlaying) { Invalidate(); } }
	}
}
