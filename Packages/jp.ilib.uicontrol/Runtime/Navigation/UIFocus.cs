using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace UIControl
{
	/// <summary>
	/// 選択の調停の公開入口。EventSystem.currentに対して1つの調停器があり、有効化されたFocusScopeの
	/// 優先度と有効化順から現在のScopeを決め、EventSystemの選択を書く唯一の場所になる。
	/// Sceneへ配置するComponentは無い。FocusScopeは有効化時に自分で登録する。
	/// </summary>
	public static class UIFocus
	{
		private static FocusArbiter s_Arbiter;
		internal static FocusArbiter Arbiter => s_Arbiter ??= new FocusArbiter();

		/// <summary>現在操作対象のScope。無ければnull。</summary>
		public static FocusScope Current => s_Arbiter?.Active;
		/// <summary>現在の選択。EventSystemの選択と同じ。</summary>
		public static GameObject Selected => EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
		public static FocusInputMode InputMode => s_Arbiter?.InputMode ?? FocusInputMode.Navigation;
		public static bool IsNavigationOccupied => s_Arbiter?.IsNavigationOccupied == true;
		public static event Action<Exception> Error { add { Arbiter.Error += value; } remove { if (s_Arbiter != null) { s_Arbiter.Error -= value; } } }

		/// <summary>入力方式の通知。Pointer中は自動選択を行わず、Navigationへ戻ると復元する。</summary>
		public static void NotifyInputMode(FocusInputMode mode) { Arbiter.NotifyInputMode(mode); }
		/// <summary>ナビゲーションを占有する。占有中は選択の移動と復元を止める。Drag中などに使う。</summary>
		public static IDisposable OccupyNavigation() { return Arbiter.OccupyNavigation(); }
		/// <summary>選択の確定を遅らせる。複数の変更をまとめてから1回で整合を取る。</summary>
		public static IDisposable DeferSelection() { return Arbiter.DeferSelection(); }
		/// <summary>未選択時だけ、現在Scope内で画面座標に最も近い候補から選択を始める。</summary>
		public static bool TrySelectNearest(Vector2 screenPosition, MoveDirection direction = MoveDirection.None) { return Arbiter.TrySelectNearest(screenPosition, direction); }
		/// <summary>いま選択の整合を取る。Pageの入力可否など、選択の有効性が変わった直後に呼ぶ。</summary>
		public static void Reconcile() { s_Arbiter?.Reconcile(); }
		/// <summary>この配下にある選択と履歴を忘れる。行の再利用時に呼ぶ。</summary>
		public static void Forget(Transform rowRoot) { s_Arbiter?.Forget(rowRoot); }
		/// <summary>この選択を外す。調停器が管理していない選択も対象にする。</summary>
		public static void Deselect(GameObject selected) { Arbiter.Deselect(selected); }
		/// <summary>この配下にある選択を外す。</summary>
		public static void ClearSelectionUnder(Transform root) { Arbiter.ClearSelectionUnder(root); }
		/// <summary>画面座標を含む候補へ選択を移す。近傍へは吸着しない。仮想カーソルの選択追従に使う。</summary>
		public static bool MoveSelectionTo(Vector2 screenPosition) { return Arbiter.MoveSelectionTo(screenPosition); }

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		private static void Reset()
		{
			s_Arbiter?.Dispose();
			s_Arbiter = null;
		}
	}

	/// <summary>UIFocusの実体。Root Componentを置かずに、Scopeの登録・有効化順・選択の整合を1箇所で扱う。</summary>
	internal sealed class FocusArbiter
	{
		private sealed class Registration
		{
			internal FocusScope Scope;
			internal long Order;
			internal bool Activated;
		}

		private readonly List<Registration> m_Order = new List<Registration>();
		private readonly Dictionary<FocusScope, Registration> m_Registrations = new Dictionary<FocusScope, Registration>();
		private EventSystem m_Events;
		private FocusArbiterRunner m_Runner;
		private long m_ActivationOrder;
		private bool m_Reconciling;
		private bool m_ReconcileAgain;
		private bool m_Dirty;
		private int m_Deferrals;
		private int m_Occupancy;
		private bool m_ExplicitActivation;
		private bool m_SuppressAutomaticSelection;
		private NavigationNode m_Selected;
		private FocusScope m_LastScope;
		private Transform m_LastParent;
		private int m_LastIndex;
		private NavigationNode m_RequestedNode;
		private Transform m_Excluded;
		private MoveDirection m_MoveDirection;
		internal FocusScope Active { get; private set; }
		internal FocusInputMode InputMode { get; private set; } = FocusInputMode.Navigation;
		internal uint Revision { get; private set; }
		internal bool IsNavigationOccupied => m_Occupancy > 0;
		internal event Action<Exception> Error;
		internal NavigationNode SelectedNode => m_Events != null && m_Events.currentSelectedGameObject != null ? m_Events.currentSelectedGameObject.GetComponent<NavigationNode>() : null;

		// ---- 登録と有効化 ----

		internal void Register(FocusScope scope)
		{
			if (scope == null || m_Registrations.ContainsKey(scope)) { return; }
			Registration registration = new Registration { Scope = scope };
			m_Registrations.Add(scope, registration);
			m_Order.Add(registration);
			EnsureRunner();
			Invalidate();
		}

		internal void Unregister(FocusScope scope)
		{
			if (scope == null || !m_Registrations.TryGetValue(scope, out Registration registration)) { return; }
			m_Registrations.Remove(scope);
			m_Order.Remove(registration);
			if (m_Events != null && !m_Events.alreadySelecting && Owns(m_Events.currentSelectedGameObject, scope)) { m_Events.SetSelectedGameObject(null); }
			if (ReferenceEquals(m_LastScope, scope)) { m_LastScope = null; m_Selected = null; }
			if (m_RequestedNode != null && ReferenceEquals(m_RequestedNode.Scope, scope)) { m_RequestedNode = null; }
			// 占有中や保留中はReconcileが現在Scopeを決め直さない。登録を外したScopeが Current に残らないよう、ここで外す。
			if (ReferenceEquals(Active, scope)) { Active = null; Revision++; }
			Invalidate();
			Reconcile();
		}

		internal bool IsRegistered(FocusScope scope) => scope != null && m_Registrations.ContainsKey(scope);
		internal bool IsActivated(FocusScope scope) => scope != null && m_Registrations.TryGetValue(scope, out Registration registration) && registration.Activated;

		/// <summary>
		/// 有効化。CanFocusはここで要求しない。ResolveActiveが毎回見ているため、有効化の瞬間だけ塞がれていた場合に
		/// 意図ごと捨てると、塞ぎが解けても誰も再試行しない。
		/// </summary>
		internal bool Activate(FocusScope scope)
		{
			if (scope == null || !scope.isActiveAndEnabled) { return false; }
			Register(scope);
			Registration registration = m_Registrations[scope];
			if (!ReferenceEquals(Active, scope) || !registration.Activated) { registration.Order = ++m_ActivationOrder; }
			registration.Activated = true;
			m_SuppressAutomaticSelection = false;
			m_ExplicitActivation = true;
			Reconcile();
			return true;
		}

		internal bool Release(FocusScope scope)
		{
			if (scope == null || !m_Registrations.TryGetValue(scope, out Registration registration) || !registration.Activated) { return false; }
			registration.Activated = false;
			Reconcile();
			return true;
		}

		internal void Invalidate() { m_Dirty = true; }

		private void EnsureRunner()
		{
			if (m_Runner != null) { return; }
			GameObject host = new GameObject("UIFocus") { hideFlags = HideFlags.HideAndDontSave };
			UnityEngine.Object.DontDestroyOnLoad(host);
			m_Runner = host.AddComponent<FocusArbiterRunner>();
			m_Runner.Arbiter = this;
		}

		internal void Dispose()
		{
			if (m_Runner != null) { UnityEngine.Object.Destroy(m_Runner.gameObject); m_Runner = null; }
			m_Order.Clear();
			m_Registrations.Clear();
			Active = null;
		}

		/// <summary>毎フレームの再構築はしない。変更通知があった時と、選択中の対象が無効になった時だけ整合を取る。</summary>
		internal void LateUpdate()
		{
			if (m_Dirty || m_ReconcileAgain) { Reconcile(); return; }
			NavigationNode selected = SelectedNode;
			if (selected != null && selected.Scope != null && m_Registrations.ContainsKey(selected.Scope) && !IsValid(selected)) { Reconcile(); }
		}

		// ---- EventSystem ----

		private bool BindEventSystem()
		{
			EventSystem events = EventSystem.current;
			if (m_Events == events) { return events != null && events.isActiveAndEnabled; }
			EventSystem previous = m_Events;
			m_Events = events;
			if (previous != null && !previous.alreadySelecting && Owns(previous.currentSelectedGameObject)) { previous.SetSelectedGameObject(null); }
			if (events == null) { return false; }
			m_SuppressAutomaticSelection = events.currentSelectedGameObject != null && !Owns(events.currentSelectedGameObject);
			return events.isActiveAndEnabled;
		}

		private bool Owns(GameObject selection, FocusScope scope = null)
		{
			if (selection == null) { return false; }
			NavigationNode node = selection.GetComponent<NavigationNode>();
			if (node == null || node.Scope == null || !m_Registrations.ContainsKey(node.Scope)) { return false; }
			return scope == null || ReferenceEquals(node.Scope, scope);
		}

		internal void Deselect(GameObject selected)
		{
			EventSystem events = EventSystem.current;
			if (selected == null || events == null || events.alreadySelecting || events.currentSelectedGameObject != selected) { return; }
			events.SetSelectedGameObject(null);
		}

		internal void ClearSelectionUnder(Transform root)
		{
			EventSystem events = EventSystem.current;
			if (root == null || events == null || events.alreadySelecting) { return; }
			GameObject selected = events.currentSelectedGameObject;
			if (selected != null && selected.transform.IsChildOf(root)) { events.SetSelectedGameObject(null); }
		}

		// ---- 現在Scopeの決定 ----

		private void ResolveActive()
		{
			Registration best = null;
			int priority = int.MinValue;
			for (int i = m_Order.Count - 1; i >= 0; i--)
			{
				if (m_Order[i].Scope == null) { m_Registrations.Remove(m_Order[i].Scope); m_Order.RemoveAt(i); }
			}
			foreach (Registration candidate in m_Order)
			{
				if (!candidate.Activated) { continue; }
				try
				{
					if (!candidate.Scope.CanFocus) { continue; }
					int rank = candidate.Scope.Priority;
					if (best == null || rank > priority || rank == priority && candidate.Order > best.Order) { best = candidate; priority = rank; }
				}
				catch (Exception exception) { Report(exception); }
			}
			FocusScope next = best?.Scope;
			if (ReferenceEquals(Active, next)) { return; }
			Active = next;
			Revision++;
		}

		private bool CanEnter(FocusScope scope)
		{
			// 焦点の所属はCanFocus、いま操作してよいかはAllowsInput。入るには両方が要る。
			return scope != null && m_Registrations.ContainsKey(scope) && scope.CanFocus && scope.AllowsInput
				&& (Active == null || !Active.CanFocus || scope.Priority >= Active.Priority);
		}

		internal bool IsValid(NavigationNode node)
		{
			return node != null && node.isActiveAndEnabled && node.Selectable != null && node.Selectable.gameObject == node.gameObject
				&& node.Selectable.isActiveAndEnabled && node.Selectable.IsInteractable()
				&& node.Scope != null && m_Registrations.ContainsKey(node.Scope) && node.Scope.CanFocus && node.Scope.AllowsInput
				&& (m_Excluded == null || !node.transform.IsChildOf(m_Excluded));
		}

		internal bool CanSelect(NavigationNode node)
		{
			if (IsNavigationOccupied) { return false; }
			if (!BindEventSystem()) { return false; }
			Rebuild();
			ResolveActive();
			return IsValid(node) && CanEnter(node.Scope);
		}

		internal bool CanUsePointer(NavigationNode node)
		{
			Rebuild();
			return node != null && node.isActiveAndEnabled && node.Selectable != null && node.Selectable.IsInteractable() && node.Scope != null && node.Scope.CanFocus;
		}

		private void Rebuild()
		{
			if (!m_Dirty) { return; }
			m_Dirty = false;
			foreach (Registration registration in m_Order.ToArray())
			{
				if (registration.Scope != null) { registration.Scope.Collect(); }
			}
		}

		// ---- 入力方式・占有・遅延 ----

		internal void NotifyInputMode(FocusInputMode mode)
		{
			if (InputMode == mode) { return; }
			InputMode = mode;
			if (mode == FocusInputMode.Navigation) { m_SuppressAutomaticSelection = false; m_ExplicitActivation = true; }
			else if (BindEventSystem())
			{
				GameObject selected = m_Events.currentSelectedGameObject;
				if (selected != null && Owns(selected) && !IsTextInput(selected)) { m_Events.SetSelectedGameObject(null); }
			}
			Revision++;
			Reconcile();
		}

		private static bool IsTextInput(GameObject selected)
		{
			return selected.GetComponent<InputField>() != null || selected.GetComponent<TMPro.TMP_InputField>() != null;
		}

		internal IDisposable OccupyNavigation()
		{
			m_Occupancy++;
			return new Counter(() => { if (m_Occupancy > 0) { m_Occupancy--; } });
		}

		internal IDisposable DeferSelection()
		{
			m_Deferrals++;
			return new Counter(() => { m_Deferrals--; Reconcile(); });
		}

		private sealed class Counter : IDisposable
		{
			private Action m_Release;
			internal Counter(Action release) { m_Release = release; }
			public void Dispose() { Action release = m_Release; m_Release = null; release?.Invoke(); }
		}

		// ---- 選択の要求 ----

		internal bool RequestSelect(NavigationNode node, MoveDirection direction = MoveDirection.None)
		{
			if (!CanSelect(node)) { return false; }
			using (DeferSelection())
			{
				m_RequestedNode = node;
				m_MoveDirection = direction;
				return Activate(node.Scope);
			}
		}

		internal bool RequestRestore(FocusScope scope)
		{
			if (!BindEventSystem()) { return false; }
			Rebuild();
			ResolveActive();
			if (!CanEnter(scope)) { return false; }
			using (DeferSelection())
			{
				m_RequestedNode = Restore(scope);
				return Activate(scope);
			}
		}

		internal bool TrySelectNearest(Vector2 screenPosition, MoveDirection direction)
		{
			if (!BindEventSystem() || m_Events.currentSelectedGameObject != null) { return false; }
			if (float.IsNaN(screenPosition.x) || float.IsNaN(screenPosition.y) || float.IsInfinity(screenPosition.x) || float.IsInfinity(screenPosition.y)) { return false; }
			Rebuild();
			ResolveActive();
			FocusScope scope = Active;
			if (scope == null) { return false; }
			Vector2 forward = NavigationGeometry.Direction(direction);
			NavigationNode best = null;
			float distance = float.PositiveInfinity;
			foreach (NavigationNode node in scope.Nodes)
			{
				if (node.ExcludeFromAutomatic || !IsValid(node)) { continue; }
				float candidate = NavigationGeometry.DistanceSquared(node, screenPosition);
				if (forward != Vector2.zero && candidate > 0 && Vector2.Dot(NavigationGeometry.Center(node) - screenPosition, forward) <= 0) { continue; }
				if (candidate < distance) { best = node; distance = candidate; }
			}
			return best != null && RequestSelect(best);
		}

		/// <summary>仮想カーソルの位置へ選択を移す。位置を含むRectだけを対象にし、近傍へ吸着しない。重なりは前面（Hierarchyで後）を選ぶ。</summary>
		internal bool MoveSelectionTo(Vector2 screenPosition)
		{
			if (IsNavigationOccupied || !BindEventSystem()) { return false; }
			if (float.IsNaN(screenPosition.x) || float.IsNaN(screenPosition.y) || float.IsInfinity(screenPosition.x) || float.IsInfinity(screenPosition.y)) { return false; }
			Rebuild();
			ResolveActive();
			FocusScope scope = Active;
			if (scope == null) { return false; }
			NavigationNode best = null;
			foreach (NavigationNode node in scope.Nodes)
			{
				if (!IsValid(node) || NavigationGeometry.DistanceSquared(node, screenPosition) > 0) { continue; }
				best = node;
			}
			return best != null && RequestSelect(best);
		}

		internal bool IsInside(FocusScope scope)
		{
			NavigationNode selected = SelectedNode;
			return scope != null && selected != null && ReferenceEquals(selected.Scope, scope);
		}

		internal void Remember(FocusScope scope)
		{
			if (IsInside(scope)) { RememberNode(SelectedNode); }
		}

		internal bool Remember(NavigationNode node)
		{
			if (!CanSelect(node)) { return false; }
			Revision++;
			RememberNode(node);
			return true;
		}

		internal void RememberRestore(FocusScope scope)
		{
			Rebuild();
			ResolveActive();
			NavigationNode next = Restore(scope);
			if (next != null) { Remember(next); }
		}

		private void RememberNode(NavigationNode node)
		{
			if (node.Scope != null) { node.Scope.LastSelectedNode = node; }
			m_Selected = node;
			m_LastScope = node.Scope;
			m_LastParent = node.transform.parent;
			m_LastIndex = node.transform.GetSiblingIndex();
		}

		internal void ObserveSelection(NavigationNode node)
		{
			if (InputMode == FocusInputMode.Pointer ? CanUsePointer(node) : CanSelect(node))
			{
				Revision++;
				RememberNode(node);
				if (InputMode == FocusInputMode.Navigation) { node.GetComponentInParent<SelectionScroll>()?.Schedule(node, Revision, m_MoveDirection); }
			}
			// EventSystemのOnSelectの中。整合はその配送が戻ってから取る。
		}

		internal void Forget(Transform rowRoot)
		{
			if (rowRoot == null) { return; }
			Revision++;
			foreach (Registration registration in m_Order)
			{
				FocusScope scope = registration.Scope;
				if (scope != null && scope.LastSelectedNode != null && scope.LastSelectedNode.transform.IsChildOf(rowRoot)) { scope.LastSelectedNode = null; }
			}
			if (m_RequestedNode != null && m_RequestedNode.transform.IsChildOf(rowRoot)) { m_RequestedNode = null; }
			m_Excluded = rowRoot;
			Reconcile();
			if (m_Deferrals == 0) { m_Excluded = null; }
		}

		// ---- 整合 ----

		internal void Reconcile()
		{
			if (m_Reconciling) { m_ReconcileAgain = true; return; }
			// 再入した要求は保持したまま現在のパスを終え、次のLateUpdateで続きを行う。
			m_ReconcileAgain = false;
			ReconcileOnce();
		}

		private void ReconcileOnce()
		{
			// 占有中は選択の整合を取らない。占有した側の選択をそのまま残す。
			if (m_Deferrals > 0 || IsNavigationOccupied) { return; }
			m_Reconciling = true;
			try
			{
				Rebuild();
				ResolveActive();
				if (m_ReconcileAgain || !BindEventSystem() || m_Events.alreadySelecting) { return; }
				if (InputMode == FocusInputMode.Pointer && !m_ExplicitActivation) { return; }
				bool explicitRequest = m_ExplicitActivation || m_RequestedNode != null;
				m_ExplicitActivation = false;
				GameObject currentObject = m_Events.currentSelectedGameObject;
				if (!explicitRequest && (m_SuppressAutomaticSelection || currentObject != null && !Owns(currentObject))) { return; }
				GameObject selection = null;
				if (Active != null)
				{
					NavigationNode next = IsValid(m_RequestedNode) && ReferenceEquals(m_RequestedNode.Scope, Active) ? m_RequestedNode : null;
					NavigationNode current = SelectedNode;
					if (next == null && IsValid(current) && ReferenceEquals(current.Scope, Active) && m_RequestedNode == null)
					{
						RememberNode(current);
						return;
					}
					if (next == null) { next = Recover() ?? Restore(Active); }
					selection = next != null ? next.gameObject : null;
				}
				if (selection != null && currentObject != selection) { m_Events.SetSelectedGameObject(selection); }
				else if (selection == null && Owns(currentObject)) { m_Events.SetSelectedGameObject(null); }
			}
			catch (Exception exception) { Report(exception); }
			finally
			{
				if (!m_ReconcileAgain)
				{
					m_RequestedNode = null;
					m_Excluded = null;
					m_MoveDirection = MoveDirection.None;
				}
				m_Reconciling = false;
			}
		}

		private NavigationNode Recover()
		{
			if (m_LastScope == null || !ReferenceEquals(m_LastScope, Active) || !m_LastScope.CanFocus) { return null; }
			if (m_Selected != null && IsValid(m_Selected)) { return null; }
			NavigationNode next = null;
			NavigationNode last = null;
			int nextIndex = int.MaxValue;
			int lastIndex = -1;
			foreach (NavigationNode node in m_LastScope.Nodes)
			{
				if (node == null || node.transform.parent != m_LastParent || !IsValid(node)) { continue; }
				int index = node.transform.GetSiblingIndex();
				if (index >= m_LastIndex && index < nextIndex) { next = node; nextIndex = index; }
				if (index > lastIndex) { last = node; lastIndex = index; }
			}
			return next != null ? next : last;
		}

		internal NavigationNode Restore(FocusScope scope)
		{
			if (scope == null || !scope.CanFocus) { return null; }
			scope.Collect();
			if (scope.RestoreLast && IsValid(scope.LastSelectedNode) && ReferenceEquals(scope.LastSelectedNode.Scope, scope)) { return scope.LastSelectedNode; }
			NavigationNode initial = scope.NodeOf(scope.Initial);
			if (IsValid(initial) && ReferenceEquals(initial.Scope, scope)) { return initial; }
			foreach (Selectable fallback in scope.Fallbacks)
			{
				NavigationNode node = scope.NodeOf(fallback);
				if (IsValid(node) && ReferenceEquals(node.Scope, scope)) { return node; }
			}
			foreach (NavigationNode node in scope.Nodes)
			{
				if (IsValid(node)) { return node; }
			}
			return null;
		}

		// ---- 移動 ----

		internal NavigationNode ResolveMove(NavigationNode current, MoveDirection direction)
		{
			if (!CanSelect(current)) { return null; }
			IReadOnlyList<NavigationCandidate> candidates = current.Candidates(direction);
			for (int i = 0; i < candidates.Count; i++)
			{
				NavigationCandidate candidate = candidates[i];
				if (candidate == null) { continue; }
				NavigationNode next = null;
				try
				{
					switch (candidate.Kind)
					{
						case CandidateKind.Node: next = candidate.Target != null ? candidate.Target.GetComponent<NavigationNode>() : null; break;
						case CandidateKind.Scope: next = Restore(candidate.Scope); break;
						case CandidateKind.Automatic: next = ResolveAutomatic(current, direction, candidate.Scope); break;
						case CandidateKind.Resolver:
							if (candidate.Resolver != null && candidate.Resolver.isActiveAndEnabled) { next = candidate.Resolver.Resolve(current, direction); }
							break;
					}
				}
				catch (Exception exception) { Report(exception); }
				if (next != current && CanSelect(next)) { return next; }
			}
			return null;
		}

		private NavigationNode ResolveAutomatic(NavigationNode current, MoveDirection direction, FocusScope scope)
		{
			scope = scope != null ? scope : current.Scope;
			if (!CanEnter(scope)) { return null; }
			scope.Collect();
			Vector2 axis = NavigationGeometry.Direction(direction);
			Vector2 origin = NavigationGeometry.ExitPoint(current, axis);
			NavigationNode best = null;
			float score = 0;
			foreach (NavigationNode candidate in scope.Nodes)
			{
				if (candidate == current || candidate.ExcludeFromAutomatic || !IsValid(candidate)) { continue; }
				Vector2 delta = NavigationGeometry.Center(candidate) - origin;
				float dot = Vector2.Dot(delta, axis);
				if (dot <= 0 || delta.sqrMagnitude < 0.0001f) { continue; }
				float value = dot / delta.sqrMagnitude;
				if (value > score || Mathf.Approximately(value, score) && best != null && HierarchyOrder.Compare(candidate.transform, best.transform) < 0)
				{
					best = candidate;
					score = value;
				}
			}
			return best;
		}

		internal void Report(Exception exception)
		{
			if (Error == null) { Debug.LogException(exception); return; }
			foreach (Action<Exception> observer in Error.GetInvocationList())
			{
				try { observer(exception); }
				catch (Exception error) { Debug.LogException(error); }
			}
		}
	}

	/// <summary>調停器のLateUpdateを回す非公開の実体。Sceneには見えない。</summary>
	[AddComponentMenu("")]
	internal sealed class FocusArbiterRunner : MonoBehaviour
	{
		internal FocusArbiter Arbiter;
		private void LateUpdate() { Arbiter?.LateUpdate(); }
	}
}
