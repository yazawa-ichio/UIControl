using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace UIControl
{
	/// <summary>
	/// 同じStack内の操作は受付順（FIFO）に1つずつ実行し、遷移演出は常に1つだけ進行する。
	/// 進行中の演出へ割り込まない。Open中のCloseはOpenの完了後に実行する。
	/// ForceRelease・Destroy・SetVisibleは進行中の演出を止めるが、所有者はその取消を失敗とせず、
	/// 最終状態を即時適用して要求を完了させる。
	/// </summary>
	[DisallowMultipleComponent]
	public sealed partial class UIStack : MonoBehaviour, IUIStack
	{
		[SerializeField] private RectTransform m_DisplayRoot;
		[SerializeField, Tooltip("このStackの既定。PrefabとOptionsで指定しない項目の終端。Inheritは使わない。")]
		private BackdropSettings m_Backdrop = new BackdropSettings(BackdropMode.KeepVisible, BackdropRange.AllBehind);
		[SerializeField, Tooltip("このStackの既定。PrefabとOptionsで指定しない場合の終端。Inheritは使わない。")]
		private FocusOnOpen m_FocusOnOpen = FocusOnOpen.Activate;
		[SerializeField, Tooltip("前面Pageの演出と背面（Replaceの旧Page、Backdropで退く背面）の演出の順序。PrefabとOptionsで指定しない場合の終端。Inheritは使わない。")]
		private TransitionOrder m_TransitionOrder = TransitionOrder.ForegroundFirst;
		private readonly List<Entry> m_Entries = new List<Entry>();
		private readonly List<Entry> m_Stack = new List<Entry>();
		private OperationQueue m_Queue = new OperationQueue();
		private Transform m_InactiveRoot;
		private bool m_Destroyed;
		private bool m_Recomputing;
		private bool m_RecomputeAgain;
		private bool m_Visible = true;
		private bool m_LastActivity;
		private bool m_LastTransition;
		private bool m_Transitioning;
		private readonly List<TaskCompletionSource<CloseResult>> m_Pops = new List<TaskCompletionSource<CloseResult>>();
		private SynchronizationContext m_MainContext;
		private int m_MainThread;
		public RectTransform DisplayRoot
		{
			// Unityの==で判定する。直列化を経た未設定の参照は偽のnull（instanceID 0）になり、ReferenceEqualsでは拾えない。
			get => m_DisplayRoot != null ? m_DisplayRoot : transform as RectTransform;
			set
			{
				CheckThread();
				if (m_Entries.Count != 0) { throw new InvalidOperationException("Set the display root before accepting Pages."); }
				m_DisplayRoot = value != null ? value : throw new ArgumentNullException(nameof(value));
			}
		}
		/// <summary>遷移演出が進行中。この間は上端のPageも入力を受け付けない。</summary>
		public bool IsTransitioning => m_Transitioning;
		public bool HasPendingOperations => m_Pops.Count != 0 || m_Entries.Any(entry => !entry.Ended && (!entry.Handle.Opened.IsCompleted || entry.CloseRequested));
		public event Action<Exception> Error;
		/// <summary>IsTransitioningの変化。遷移中の遮断板などを利用側で出すための通知。</summary>
		public event Action<bool> TransitionChanged;
		/// <summary>HasPendingOperationsの変化。ロード・準備・待機を含む処理中表示のための通知。</summary>
		public event Action<bool> ActivityChanged;

		internal void CheckThread()
		{
			if (m_MainThread == 0)
			{
				// 最初の呼び出しがメインスレッドであることをUnity APIの呼び出しで確かめる（別スレッドではUnityが例外を投げる）。
				// GetInstanceIDは6000.6でobsolete（error）になったため、版に依存しないgameObjectの参照を使う。
				_ = gameObject;
				m_MainContext = SynchronizationContext.Current ?? throw new InvalidOperationException("UIStack requires Unity's main-thread context.");
				m_MainThread = Thread.CurrentThread.ManagedThreadId;
			}
			if (Thread.CurrentThread.ManagedThreadId != m_MainThread) { throw new InvalidOperationException("UIStack operations must run on Unity's main thread."); }
		}

		private void OnValidate()
		{
			// Stackの既定は解決の終端なので具体値だけを持つ。
			m_Backdrop = m_Backdrop.OrDefault(BackdropMode.KeepVisible, BackdropRange.AllBehind);
			if (m_FocusOnOpen == FocusOnOpen.Inherit) { m_FocusOnOpen = FocusOnOpen.Activate; }
			if (m_TransitionOrder == TransitionOrder.Inherit) { m_TransitionOrder = TransitionOrder.ForegroundFirst; }
		}
		private void ValidateDisplayRoot()
		{
			if (DisplayRoot == null) { throw new InvalidOperationException("Assign a live display RectTransform to UIStack."); }
		}

		private void Initialize()
		{
			ValidateDisplayRoot();
			if (m_InactiveRoot != null) { return; }
			GameObject staging = new GameObject("UIStack Inactive Instances", typeof(RectTransform));
			staging.SetActive(false);
			staging.transform.SetParent(transform, false);
			m_InactiveRoot = staging.transform;
		}

		public PageHandle Push(UIParameter parameter, OpenOptions options = null, CancellationToken cancellation = default)
		{
			return Accept(parameter, options, cancellation, false);
		}
		public PageHandle Replace(UIParameter parameter, OpenOptions options = null, CancellationToken cancellation = default)
		{
			return Accept(parameter, options, cancellation, true);
		}

		private PageHandle Accept(UIParameter parameter, OpenOptions options, CancellationToken cancellation, bool replace)
		{
			CheckThread();
			if (m_Destroyed || !isActiveAndEnabled) { throw new InvalidOperationException("UIStack is not accepting Pages."); }
			if (parameter == null) { throw new ArgumentNullException(nameof(parameter)); }
			if (m_Backdrop.Mode == BackdropMode.Inherit || m_Backdrop.Range == BackdropRange.Inherit
				|| m_FocusOnOpen == FocusOnOpen.Inherit || m_TransitionOrder == TransitionOrder.Inherit)
			{
				throw new InvalidOperationException("UIStack defaults (Backdrop, Focus On Open, Transition Order) must be concrete values.");
			}
			Initialize();
			options ??= OpenOptions.Default;
			Entry entry = new Entry
			{
				Parameter = parameter, Options = options, Loader = parameter.Loader ?? PageLoading.DefaultLoader,
				Replace = replace, DefaultBackdrop = m_Backdrop, DefaultOrder = m_TransitionOrder, Order = options.TransitionOrder,
				FocusOnOpen = options.FocusOnOpen, DefaultFocusOnOpen = m_FocusOnOpen
			};
			entry.Handle = new PageHandle(this, entry);
			m_Entries.Add(entry);
			entry.OpenCancellation = cancellation.Register(() =>
			{
				if (Thread.CurrentThread.ManagedThreadId == m_MainThread) { CancelOpen(entry); }
				else { m_MainContext.Post(_ => CancelOpen(entry), null); }
			});
			if (entry.Ended) { entry.OpenCancellation.Dispose(); }
			else { Observe(m_Queue.Run(() => Open(entry))); }
			RefreshActivity();
			return entry.Handle;
		}

		public Task<CloseResult> Pop()
		{
			CheckThread();
			if (m_Destroyed) { return Task.FromResult(new CloseResult(CloseStatus.Empty)); }
			var completion = new TaskCompletionSource<CloseResult>(TaskCreationOptions.RunContinuationsAsynchronously);
			m_Pops.Add(completion);
			Observe(m_Queue.Run(async () =>
			{
				try
				{
					if (completion.Task.IsCompleted) { return; }
					Entry top = m_Stack.LastOrDefault();
					if (top == null) { completion.TrySetResult(new CloseResult(CloseStatus.Empty)); return; }
					top.CloseRequested = true;
					await CloseEntry(top);
					completion.TrySetResult(await top.Handle.Closed);
				}
				catch (Exception exception) { completion.TrySetResult(new CloseResult(CloseStatus.Faulted, exception)); ReportError(exception); }
				finally { m_Pops.Remove(completion); RefreshActivity(); }
			}));
			RefreshActivity();
			return completion.Task;
		}

		public Task CloseAll()
		{
			CheckThread();
			return Task.WhenAll(m_Entries.ToArray().Reverse().Select(entry => entry.Handle.Close()));
		}
		public void ForceReleaseAll()
		{
			CheckThread();
			Entry[] entries = m_Entries.ToArray();
			var pops = m_Pops.ToArray();
			foreach (var pop in pops) { m_Pops.Remove(pop); pop.TrySetResult(new CloseResult(CloseStatus.Forced)); }
			foreach (Entry entry in entries) { Release(entry, OpenStatus.Forced, CloseStatus.Forced); }
			RefreshActivity();
		}
		public IReadOnlyList<PageHandle> GetStackSnapshot()
		{
			return m_Stack.Select(entry => entry.Handle).ToArray();
		}

		/// <summary>生成済みPageを保持したまま表示を切り替える。進行中の演出は止め、最終状態を即時適用する。</summary>
		public void SetVisible(bool visible)
		{
			if (m_Destroyed) { return; }
			if (m_Visible == visible) { return; }
			m_Visible = visible;
			foreach (Entry entry in m_Entries.ToArray())
			{
				if (entry.Ended || entry.Page == null) { continue; }
				try { entry.Page.StopAnimations(); }
				catch (Exception exception) { ReportError(exception); }
			}
			Recompute();
		}

		internal void RequestClose(Entry entry)
		{
			if (entry.Ended || entry.CloseRequested) { return; }
			entry.CloseRequested = true;
			EntryState state = entry.Handle.State;
			if (state == EntryState.Opening || state == EntryState.Live || state == EntryState.Closing)
			{
				// Opening中でも割り込まない。Openの完了後にFIFOで閉じる。
				Observe(m_Queue.Run(() => CloseEntry(entry)));
			}
			else
			{
				// Queued / Loading / Preparing。表示前なので即時に回収する。
				Release(entry, OpenStatus.ClosedBeforeOpen, CloseStatus.Closed);
			}
			RefreshActivity();
		}

		private void CancelOpen(Entry entry)
		{
			if (!entry.Ended && !entry.Handle.Opened.IsCompleted) { Release(entry, OpenStatus.Cancelled, CloseStatus.Closed); }
		}

		private async Task Open(Entry entry)
		{
			if (entry.Ended) { return; }
			try
			{
				ValidateDisplayRoot();
				entry.Replacing = entry.Replace ? m_Stack.LastOrDefault() : null;
				entry.Handle.SetState(EntryState.Loading);
				ThrowIfEnded(entry);
				Task<PageInstance> load = entry.Loader.LoadAsync(entry.Parameter.Path, m_InactiveRoot, entry.PreparationCancellation.Token);
				if (load == null) { throw new InvalidOperationException("Loader returned a null task."); }
				await Task.WhenAny(load, entry.Released.Task);
				if (entry.Ended) { CollectLate(load); return; }
				entry.Instance = await load ?? throw new InvalidOperationException("Loader returned no PageInstance.");
				ThrowIfEnded(entry);
				Page page = entry.Page;
				if (page == null || page.gameObject.activeInHierarchy || !page.transform.IsChildOf(m_InactiveRoot))
				{
					throw new InvalidOperationException("Loader must return a Page beneath the inactive parent.");
				}
				page.ValidateConfiguration();
				page.Destroyed = () => Release(entry, OpenStatus.Faulted, CloseStatus.Faulted, new InvalidOperationException("Page was destroyed outside its handle."));
				if (entry.FocusOnOpen == FocusOnOpen.Inherit) { entry.FocusOnOpen = page.FocusOnOpen; }
				if (entry.FocusOnOpen == FocusOnOpen.Inherit) { entry.FocusOnOpen = entry.DefaultFocusOnOpen; }
				if (entry.FocusOnOpen == FocusOnOpen.Inherit) { throw new InvalidOperationException("UIStack Focus On Open must be a concrete value."); }
				if (entry.Order == TransitionOrder.Inherit) { entry.Order = page.TransitionOrder; }
				if (entry.Order == TransitionOrder.Inherit) { entry.Order = entry.DefaultOrder; }
				if (entry.Order == TransitionOrder.Inherit) { throw new InvalidOperationException("UIStack Transition Order must be a concrete value."); }
				entry.Backdrop = BackdropSettings.Resolve(entry.Options.Backdrop, page.Backdrop, entry.DefaultBackdrop);
				entry.Context = new PrepareContext(page, entry.PreparationCancellation.Token);
				page.IsPreparing = true;
				page.Attach(entry.Handle);
				page.SetInputAllowed(false);
				page.SetRendering(false);
				ThrowIfEnded(entry);
				page.transform.SetParent(DisplayRoot, false);
				ThrowIfEnded(entry);
				page.gameObject.SetActive(true);
				ThrowIfEnded(entry);
				entry.Handle.SetState(EntryState.Preparing);
				ThrowIfEnded(entry);
				UIParameter parameter = entry.Parameter;
				OpenOptions options = entry.Options;
				entry.Parameter = null;
				entry.Options = null;
				entry.Loader = null;
				parameter.Initialize(page, entry.Context);
				options.Setup(page, entry.Context);
				PrepareStep[] preparation = page.Preparation.ToArray();
				foreach (PrepareStep step in preparation)
				{
					ThrowIfEnded(entry);
					await UnlessReleased(step.ExecuteAsync(entry.Context), entry);
				}
				ThrowIfEnded(entry);
				Canvas.ForceUpdateCanvases();
				ThrowIfEnded(entry);
				page.IsPreparing = false;
				await PresentOpen(entry);
			}
			catch (Exception exception)
			{
				if (!entry.Ended) { Release(entry, OpenStatus.Faulted, CloseStatus.Faulted, exception); }
				else if (!(exception is OperationCanceledException)) { ReportError(exception); }
			}
		}

		private void LateUpdate()
		{
			if (DisplayRoot == null)
			{
				foreach (Entry entry in m_Entries.ToArray())
				{
					Release(entry, OpenStatus.Faulted, CloseStatus.Faulted, new InvalidOperationException("UIStack display root was destroyed."));
				}
				return;
			}
			// 再入で保持した再計算をこのフレームで行う。
			if (m_RecomputeAgain) { Recompute(); }
		}

		private void RefreshActivity()
		{
			bool pending = HasPendingOperations;
			if (m_LastActivity == pending) { return; }
			m_LastActivity = pending;
			Notify(ActivityChanged, pending);
		}
		private void NotifyTransition()
		{
			bool transitioning = IsTransitioning;
			if (m_LastTransition == transitioning) { return; }
			m_LastTransition = transitioning;
			Notify(TransitionChanged, transitioning);
		}
		private void Notify(Action<bool> handlers, bool value)
		{
			if (handlers == null) { return; }
			foreach (Action<bool> handler in handlers.GetInvocationList())
			{
				try { handler(value); }
				catch (Exception exception) { ReportError(exception); }
			}
		}

		private void BeginTransition()
		{
			if (m_Transitioning) { return; }
			m_Transitioning = true;
			RefreshInput();
			NotifyTransition();
		}

		private void EndTransition()
		{
			if (!m_Transitioning) { return; }
			m_Transitioning = false;
			RefreshInput();
			NotifyTransition();
		}

		/// <summary>
		/// 新Pageの表示。前面（新Pageの演出）と背面（Backdropで退く背面、Replaceの旧PageのClose）を
		/// TransitionOrderの順に進め、Stackへ採用する。演出の取消は失敗ではなく、最終状態の即時適用で補う。
		/// </summary>
		private async Task PresentOpen(Entry entry)
		{
			if (entry.Ended) { return; }
			ValidateDisplayRoot();
			BeginTransition();
			try
			{
				entry.Handle.SetState(EntryState.Opening);
				ThrowIfEnded(entry);
				Entry replaced = entry.Replacing;
				List<Entry> proposed = OrderedStack();
				proposed.Remove(replaced);
				proposed.Add(entry);
				Dictionary<Entry, Presentation> states = Resolve(proposed);
				Presentation next = states[entry];
				Exception oldCloseError = null;
				bool interrupted = false;
				async Task Foreground()
				{
					if (!m_Visible || next == Presentation.Hidden) { entry.Page.ApplyImmediately(Presentation.Hidden); return; }
					interrupted |= !await Animate(entry.Page.Transition(Presentation.Visible, ChangeReason.Open));
					ThrowIfEnded(entry);
					if (next == Presentation.Covered) { interrupted |= !await Animate(entry.Page.Transition(next, ChangeReason.Cover)); }
				}
				async Task Background()
				{
					interrupted |= await TransitionSurvivors(states, entry);
					if (replaced == null || replaced.Ended || replaced.Page == null || !replaced.Page.IsRendered) { return; }
					try { interrupted |= !await Animate(replaced.Page.Transition(Presentation.Hidden, ChangeReason.Close)); }
					catch (Exception exception) { oldCloseError = exception; }
				}
				await Run(entry.Order, Foreground, Background);
				ThrowIfEnded(entry);
				m_Stack.Remove(replaced);
				m_Stack.Add(entry);
				entry.Replacing = null;
				entry.OpenCancellation.Dispose();
				ApplyStates(states, interrupted);
				EndTransition();
				if (replaced != null) { Release(replaced, OpenStatus.Replaced, CloseStatus.Replaced, oldCloseError); }
				if (entry.Ended) { return; }
				entry.Handle.SetState(EntryState.Live);
				RefreshInput();
				if (!entry.CloseRequested && entry.FocusOnOpen == FocusOnOpen.Activate && entry.Page.Focus != null) { entry.Page.Focus.Activate(); }
				if (!entry.Ended) { entry.Handle.OpenCompletion.TrySetResult(new OpenResult(OpenStatus.Opened)); }
				RefreshActivity();
			}
			finally
			{
				EndTransition();
				Recompute();
			}
		}

		/// <summary>Pageを閉じる。閉じるPage自身の演出の失敗は記録しつつ回収まで進める。</summary>
		private async Task CloseEntry(Entry entry)
		{
			if (entry.Ended) { return; }
			BeginTransition();
			try
			{
				entry.Handle.SetState(EntryState.Closing);
				List<Entry> proposed = OrderedStack();
				proposed.Remove(entry);
				Dictionary<Entry, Presentation> states = Resolve(proposed);
				Exception failure = null;
				bool interrupted = false;
				async Task Foreground()
				{
					if (entry.Page == null || !entry.Page.IsRendered) { return; }
					try { interrupted |= !await Animate(entry.Page.Transition(Presentation.Hidden, ChangeReason.Close)); }
					catch (Exception exception) { failure = exception; }
				}
				async Task Background()
				{
					interrupted |= await TransitionSurvivors(states, entry);
				}
				await Run(entry.Order, Foreground, Background);
				if (entry.Ended) { return; }
				m_Stack.Remove(entry);
				ApplyStates(states, interrupted);
				EndTransition();
				Release(entry, OpenStatus.ClosedBeforeOpen, CloseStatus.Closed, failure);
			}
			catch (Exception exception)
			{
				if (!entry.Ended) { Release(entry, OpenStatus.Faulted, CloseStatus.Faulted, exception); }
			}
			finally
			{
				EndTransition();
				Recompute();
			}
		}

		private static async Task Run(TransitionOrder order, Func<Task> foreground, Func<Task> background)
		{
			switch (order)
			{
				case TransitionOrder.BackgroundFirst:
					await background();
					await foreground();
					break;
				case TransitionOrder.Concurrent:
					await Task.WhenAll(foreground(), background());
					break;
				default:
					await foreground();
					await background();
					break;
			}
		}

		/// <summary>演出の完了を待つ。取消（ForceRelease / SetVisible / 別の再生による停止）は失敗ではなくfalseで返す。</summary>
		private static async Task<bool> Animate(Task task)
		{
			if (task == null) { throw new InvalidOperationException("A Page operation returned a null task."); }
			try { await task; return true; }
			catch (OperationCanceledException) { return false; }
		}

		/// <summary>背面Pageの演出。失敗しても所有者の要求を失敗にせず、報告して最終状態を即時適用する。取消があればtrue。</summary>
		private async Task<bool> TransitionSurvivors(Dictionary<Entry, Presentation> states, Entry owner)
		{
			bool interrupted = false;
			foreach (KeyValuePair<Entry, Presentation> pair in states)
			{
				Entry entry = pair.Key;
				if (entry == owner || entry.Ended || entry.Page == null) { continue; }
				if (!m_Visible) { interrupted = true; continue; }
				if (entry.Page.Presentation == pair.Value) { continue; }
				ChangeReason reason = pair.Value == Presentation.Hidden ? ChangeReason.Hide : pair.Value == Presentation.Covered ? ChangeReason.Cover : ChangeReason.Restore;
				try { interrupted |= !await Animate(entry.Page.Transition(pair.Value, reason)); }
				catch (Exception exception)
				{
					ReportError(exception);
					interrupted = true;
				}
			}
			return interrupted;
		}

		private void ThrowIfEnded(Entry entry)
		{
			if (entry.Ended) { throw new OperationCanceledException(); }
			ValidateDisplayRoot();
		}

		/// <summary>準備処理の完了を待つ。途中でEntryが回収されたら待たずに抜ける。</summary>
		private async Task UnlessReleased(Task task, Entry entry)
		{
			if (task == null) { throw new InvalidOperationException("A Page operation returned a null task."); }
			await Task.WhenAny(task, entry.Released.Task);
			if (entry.Ended) { Observe(task); throw new OperationCanceledException(); }
			await task;
		}

		private async void CollectLate(Task<PageInstance> load)
		{
			try { (await load)?.Dispose(); }
			catch (OperationCanceledException) { }
			catch (Exception exception) { ReportError(exception); }
		}

		private async void Observe(Task task)
		{
			try { await task; }
			catch (OperationCanceledException) { }
			catch (Exception exception) { ReportError(exception); }
		}

		internal void Release(Entry entry, OpenStatus openStatus, CloseStatus closeStatus, Exception error = null)
		{
			if (entry.Ended) { return; }
			entry.Ended = true;
			entry.Context?.Invalidate();
			entry.Released.TrySetResult(true);
			List<Exception> errors = new List<Exception>();
			if (error != null) { errors.Add(error); }
			void Cleanup(Action action)
			{
				try { action(); }
				catch (Exception exception) { errors.Add(exception); }
			}
			entry.Parameter = null;
			entry.Options = null;
			entry.Loader = null;

			Page screen = entry.Page;
			if (screen != null)
			{
				screen.Destroyed = null;
				screen.IsPreparing = false;
				Cleanup(() => screen.SetInputAllowed(false));
				Cleanup(() => screen.SetRendering(false));
				Cleanup(() => screen.Detach());
			}
			Cleanup(entry.OpenCancellation.Dispose);
			Cleanup(entry.PreparationCancellation.Cancel);
			// 進行中の演出はここで止まる。所有者はCancelledを受けて最終状態の即時適用へ進む。
			if (screen != null) { Cleanup(screen.StopAnimations); }
			m_Stack.Remove(entry);
			m_Entries.Remove(entry);
			entry.Context?.Release(errors.Add);
			Cleanup(() => entry.Instance?.Release());
			entry.PreparationCancellation.Dispose();
			entry.Instance = null;
			entry.Context = null;
			entry.Replacing = null;
			Cleanup(Recompute);
			entry.Handle.SetState(EntryState.Released);
			Exception failure = errors.Count == 0 ? null : new AggregateException(errors);
			if (failure != null)
			{
				if (closeStatus != CloseStatus.Forced) { closeStatus = CloseStatus.Faulted; }
				if (openStatus != OpenStatus.Forced && openStatus != OpenStatus.ClosedBeforeOpen) { openStatus = OpenStatus.Faulted; }
			}
			entry.Handle.OpenCompletion.TrySetResult(new OpenResult(openStatus, failure));
			entry.Handle.CloseCompletion.TrySetResult(new CloseResult(closeStatus, failure));
			foreach (Entry dependent in m_Entries.Where(item => item.Replacing == entry).ToArray())
			{
				Release(dependent, OpenStatus.Cancelled, CloseStatus.Closed);
			}
			RefreshActivity();
			if (failure != null) { ReportError(failure); }
		}

		private List<Entry> OrderedStack()
		{
			return m_Stack.Where(entry => !entry.Ended).ToList();
		}

		private static Dictionary<Entry, Presentation> Resolve(List<Entry> ordered)
		{
			Dictionary<Entry, Presentation> result = new Dictionary<Entry, Presentation>();
			for (int i = 0; i < ordered.Count; i++)
			{
				Entry target = ordered[i];
				Presentation presentation = Presentation.Visible;
				for (int j = i + 1; j < ordered.Count; j++)
				{
					Entry front = ordered[j];
					BackdropSettings policy = front.Backdrop;
					bool includes = policy.Range == BackdropRange.Previous && j == i + 1 || policy.Range == BackdropRange.AllBehind;
					if (!includes) { continue; }
					if (policy.Mode == BackdropMode.Hidden) { presentation = Presentation.Hidden; }
					else if (policy.Mode == BackdropMode.Covered && presentation != Presentation.Hidden) { presentation = Presentation.Covered; }
				}
				result.Add(target, presentation);
			}
			return result;
		}

		private void ApplyStates(Dictionary<Entry, Presentation> states, bool immediate)
		{
			foreach (KeyValuePair<Entry, Presentation> pair in states)
			{
				if (pair.Key.Ended || pair.Key.Page == null) { continue; }
				try
				{
					Presentation presentation = m_Visible ? pair.Value : Presentation.Hidden;
					if (immediate && pair.Key.Page.Presentation != presentation) { pair.Key.Page.ApplyImmediately(presentation); }
					else { pair.Key.Page.SetPresentation(presentation); }
				}
				catch (Exception exception) { ReportError(exception); }
			}
		}

		private void Recompute()
		{
			// 再入した要求は保持したまま現在のパスを終え、LateUpdateで続きを行う。
			// 同期で往復し続けるとフレームを送れずEditorが無応答になるため、回数では止めずフレームへ分散する。
			if (m_Recomputing) { m_RecomputeAgain = true; return; }
			m_Recomputing = true;
			try
			{
				m_RecomputeAgain = false;
				ApplyStates(Resolve(OrderedStack()), true);
				RefreshInput();
			}
			finally { m_Recomputing = false; }
		}

		private void RefreshInput()
		{
			Entry top = m_Stack.LastOrDefault();
			foreach (Entry entry in m_Entries.ToArray())
			{
				if (entry.Ended || entry.Page == null) { continue; }
				bool allowed = m_Visible && !IsTransitioning && entry == top && entry.Handle.State == EntryState.Live && entry.Page.IsRendered;
				try { entry.Page.SetInputAllowed(allowed); }
				catch (Exception exception) { ReportError(exception); }
			}
			UIFocus.Reconcile();
		}

		internal void ReportError(Exception exception)
		{
			if (Error == null) { Debug.LogException(exception); return; }
			foreach (Action<Exception> observer in Error.GetInvocationList())
			{
				try { observer(exception); }
				catch (Exception observerError) { Debug.LogException(observerError); }
			}
		}

		private void OnDestroy()
		{
			m_Destroyed = true;
			ForceReleaseAll();
			if (m_InactiveRoot != null) { UnityEngine.Object.Destroy(m_InactiveRoot.gameObject); }
		}
	}
}
