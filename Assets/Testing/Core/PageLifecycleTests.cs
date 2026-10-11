using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace UIControl.Tests
{
	/// <summary>
	/// UIStack・Entry・Handleの寿命契約を検証する。期待値はPackages/jp.ilib.uicontrol/Documentation~/pages.md の
	/// 「Push・Replace・Close」と「Backdropと演出の順序」から取る。
	/// 実装の現状ではなく仕様を期待値にする。
	/// </summary>
	public sealed class PageLifecycleTests
	{
		private UIStack m_Stack;
		private TestPageLoader m_Loader;
		private OpenOptions m_Options;

		[SetUp]
		public void SetUp()
		{
			m_Loader = new TestPageLoader();
			m_Options = OpenOptions.Default;
			m_Stack = TestPage.CreateStack();
		}

		[UnityTearDown]
		public IEnumerator TearDown()
		{
			if (m_Stack != null)
			{
				m_Stack.ForceReleaseAll();
				Object.Destroy(m_Stack.gameObject);
			}
			m_Loader.DestroyLeaks();
			yield return null;
		}

		/// <summary>4.1「Push / Replaceは受付直後にHandleを返す」と状態列の開始点。</summary>
		[UnityTest]
		public IEnumerator Pushは受付直後にHandleを返す()
		{
			PageHandle handle = m_Stack.Push(UIParameter.WithoutData("A", m_Loader), m_Options);

			Assert.That(handle, Is.Not.Null);
			Assert.That(handle.State, Is.EqualTo(EntryState.Queued), "受付直後はQueued");
			Assert.That(handle.Opened.IsCompleted, Is.False, "受付だけでOpenedを完了させない");

			yield return TestWait.ForCompletion(handle.Opened, "PushのOpened");
			Assert.That(handle.Opened.Result.Status, Is.EqualTo(OpenStatus.Opened));
			Assert.That(handle.State, Is.EqualTo(EntryState.Live));
		}

		/// <summary>
		/// Display Rootを省略したStackは自身のRectTransformを表示先にする。直列化を経た未設定の参照は
		/// 偽のnull（instanceID 0）になるため、Instantiateで複製した側で確認する（Prefabや手置きのSceneと同じ状態）。
		/// </summary>
		[UnityTest]
		public IEnumerator DisplayRoot未設定のStackは直列化後も自身を表示先にする()
		{
			UIStack clone = Object.Instantiate(m_Stack.gameObject).GetComponent<UIStack>();
			try
			{
				Assert.That(clone.DisplayRoot, Is.EqualTo(clone.transform), "省略時のDisplayRootが自身のRectTransformでない");
				PageHandle handle = clone.Push(UIParameter.WithoutData("A", m_Loader), m_Options);
				yield return TestWait.ForCompletion(handle.Opened, "Display Root省略のPushのOpened");
				Assert.That(handle.Opened.Result.Status, Is.EqualTo(OpenStatus.Opened), "Display Root省略のStackへPushできない: " + handle.Opened.Result.Exception);
				Assert.That(handle.Page.transform.parent, Is.EqualTo(clone.transform), "Pageが自身の下に配置されない");
			}
			finally
			{
				clone.ForceReleaseAll();
				Object.Destroy(clone.gameObject);
			}
		}

		/// <summary>4.1「既に取消済みのTokenを渡した正常な要求は生成せず、完了済みのCancelled結果を持つHandleを返す」。</summary>
		[UnityTest]
		public IEnumerator 取消済みTokenのPushは画面を生成せずCancelledで完了する()
		{
			using (CancellationTokenSource cancellation = new CancellationTokenSource())
			{
				cancellation.Cancel();
				PageHandle handle = m_Stack.Push(UIParameter.WithoutData("A", m_Loader), m_Options, cancellation.Token);

				Assert.That(handle.Opened.IsCompleted, Is.True, "取消済みTokenでは完了済みHandleを返す");
				Assert.That(handle.Opened.Result.Status, Is.EqualTo(OpenStatus.Cancelled));
				Assert.That(m_Loader.LoadCount, Is.EqualTo(0), "画面を生成しない");
			}
			yield return null;
		}

		/// <summary>4.1の状態列 Queued → Loading → Preparing → Opening → Live。</summary>
		[UnityTest]
		public IEnumerator Openは仕様の順序で状態を遷移する()
		{
			List<EntryState> observed = new List<EntryState>();
			PageHandle handle = m_Stack.Push(UIParameter.WithoutData("A", m_Loader), m_Options);
			observed.Add(handle.State);
			handle.StateChanged += changed => observed.Add(changed.State);

			yield return TestWait.ForCompletion(handle.Opened, "PushのOpened");

			Assert.That(observed, Is.EqualTo(new[]
			{
				EntryState.Queued, EntryState.Loading, EntryState.Preparing,
				EntryState.Opening, EntryState.Live
			}), "4.1の状態列どおりに遷移すること");
		}

		/// <summary>4.2「Close重複 | 毎回同じClosed Task。Close要求の二重enqueueもしない」。</summary>
		[UnityTest]
		public IEnumerator Closeを重ねても同じTaskを返す()
		{
			PageHandle handle = m_Stack.Push(UIParameter.WithoutData("A", m_Loader), m_Options);
			yield return TestWait.ForCompletion(handle.Opened, "PushのOpened");

			Task<CloseResult> first = handle.Close();
			Task<CloseResult> second = handle.Close();

			Assert.That(second, Is.SameAs(first), "Closeは共有Taskを返す");
			Assert.That(first, Is.SameAs(handle.Closed), "Closedプロパティと同じTask");

			yield return TestWait.ForCompletion(first, "Closeの完了");
			Assert.That(first.Result.Status, Is.EqualTo(CloseStatus.Closed));
		}

		/// <summary>4.1「Closed | Entry失効、入力・描画停止、Stack除去、資源・実体の回収手配」。</summary>
		[UnityTest]
		public IEnumerator LiveなHandleのCloseはClosedで完了する()
		{
			PageHandle handle = m_Stack.Push(UIParameter.WithoutData("A", m_Loader), m_Options);
			yield return TestWait.ForCompletion(handle.Opened, "PushのOpened");
			Assert.That(m_Stack.GetStackSnapshot().Count, Is.EqualTo(1));

			yield return TestWait.ForCompletion(handle.Close(), "Closeの完了");

			Assert.That(handle.Closed.Result.Status, Is.EqualTo(CloseStatus.Closed));
			Assert.That(handle.State, Is.EqualTo(EntryState.Released));
			Assert.That(handle.Page, Is.Null, "失効後のPageはnull");
			Assert.That(m_Stack.GetStackSnapshot().Count, Is.EqualTo(0), "Stackから除去する");
			Assert.That(m_Loader.ReleaseCount, Is.EqualTo(1), "実体の回収を手配する");
		}

		/// <summary>4.2「Queued / Loading / Preparing中の同じHandleへのClose | Openを中断。表示せず回収」。</summary>
		[UnityTest]
		public IEnumerator Loading中のCloseは画面を表示せずに回収する()
		{
			m_Loader.HoldLoads = true;
			PageHandle handle = m_Stack.Push(UIParameter.WithoutData("A", m_Loader), m_Options);
			yield return TestWait.Until(() => handle.State == EntryState.Loading, "Loadingへ入る");

			yield return TestWait.ForCompletion(handle.Close(), "Closeの完了");

			Assert.That(handle.Opened.Result.Status, Is.EqualTo(OpenStatus.ClosedBeforeOpen));
			Assert.That(handle.State, Is.EqualTo(EntryState.Released));
			Assert.That(m_Stack.GetStackSnapshot().Count, Is.EqualTo(0), "一度もStackへ載せない");
		}

		/// <summary>4.1「ForceRelease | 同じ呼び出し内で失効・表示/入力停止・回収手配・待機Task確定」。</summary>
		[UnityTest]
		public IEnumerator ForceReleaseは同じ呼び出し内でReleasedへ進む()
		{
			PageHandle handle = m_Stack.Push(UIParameter.WithoutData("A", m_Loader), m_Options);
			yield return TestWait.ForCompletion(handle.Opened, "PushのOpened");

			handle.ForceRelease();

			Assert.That(handle.State, Is.EqualTo(EntryState.Released), "同じ呼び出し内でReleased");
			Assert.That(handle.Closed.IsCompleted, Is.True, "待機Taskも同じ呼び出し内で確定");
			Assert.That(handle.Closed.Result.Status, Is.EqualTo(CloseStatus.Forced));
		}

		/// <summary>4.2「同じStackの別Entry操作 | 受付順FIFO」。</summary>
		[UnityTest]
		public IEnumerator 同じStackの別Entryは受付順に処理される()
		{
			m_Loader.HoldLoads = true;
			PageHandle first = m_Stack.Push(UIParameter.WithoutData("A", m_Loader), m_Options);
			PageHandle second = m_Stack.Push(UIParameter.WithoutData("B", m_Loader), m_Options);
			yield return TestWait.Until(() => first.State == EntryState.Loading, "先行がLoadingへ入る");

			Assert.That(m_Loader.LoadCount, Is.EqualTo(1), "先行が終わるまで後続の生成を始めない");
			Assert.That(second.State, Is.EqualTo(EntryState.Queued));

			m_Loader.HoldLoads = false;
			m_Loader.ReleaseHeldLoads();
			yield return TestWait.ForCompletion(second.Opened, "後続のOpened");

			Assert.That(first.Opened.Result.Status, Is.EqualTo(OpenStatus.Opened));
			Assert.That(second.Opened.Result.Status, Is.EqualTo(OpenStatus.Opened));
			Assert.That(m_Stack.GetStackSnapshot()[1], Is.SameAs(second), "受付順に積む");
		}

		/// <summary>4.1「CloseResultは…空StackのPop用Emptyを加える」。</summary>
		[UnityTest]
		public IEnumerator 空のStackへのPopはEmptyを返す()
		{
			Task<CloseResult> pop = m_Stack.Pop();
			yield return TestWait.ForCompletion(pop, "Popの完了");
			Assert.That(pop.Result.Status, Is.EqualTo(CloseStatus.Empty));
		}

		/// <summary>4.2「空のReplaceはPush相当」。</summary>
		[UnityTest]
		public IEnumerator 空のStackへのReplaceはPush相当になる()
		{
			PageHandle handle = m_Stack.Replace(UIParameter.WithoutData("A", m_Loader), m_Options);
			yield return TestWait.ForCompletion(handle.Opened, "ReplaceのOpened");

			Assert.That(handle.Opened.Result.Status, Is.EqualTo(OpenStatus.Opened));
			Assert.That(m_Stack.GetStackSnapshot().Count, Is.EqualTo(1));
		}

		/// <summary>4.2「CloseAllは受付時点の待機中を含むHandle集合を固定し、逆順にCloseする。後着Pushまで追跡しない」。</summary>
		[UnityTest]
		public IEnumerator CloseAllは呼び出し時点の集合だけを閉じる()
		{
			PageHandle first = m_Stack.Push(UIParameter.WithoutData("A", m_Loader), m_Options);
			PageHandle second = m_Stack.Push(UIParameter.WithoutData("B", m_Loader), m_Options);
			yield return TestWait.ForCompletion(second.Opened, "2件目のOpened");

			Task closeAll = m_Stack.CloseAll();
			PageHandle late = m_Stack.Push(UIParameter.WithoutData("C", m_Loader), m_Options);
			yield return TestWait.ForCompletion(closeAll, "CloseAllの完了");

			Assert.That(first.State, Is.EqualTo(EntryState.Released));
			Assert.That(second.State, Is.EqualTo(EntryState.Released));
			Assert.That(late.State, Is.Not.EqualTo(EntryState.Released), "後着Pushまで追跡しない");

			yield return TestWait.ForCompletion(late.Opened, "後着のOpened");
		}

		/// <summary>4.1「HasPendingOperationsは…表示中Pageがあるだけではtrueにしない」。</summary>
		[UnityTest]
		public IEnumerator HasPendingOperationsは表示中だけではtrueにならない()
		{
			Assert.That(m_Stack.HasPendingOperations, Is.False, "何も要求していない時点ではfalse");

			PageHandle handle = m_Stack.Push(UIParameter.WithoutData("A", m_Loader), m_Options);
			Assert.That(m_Stack.HasPendingOperations, Is.True, "受付直後は実行中の操作がある");

			yield return TestWait.ForCompletion(handle.Opened, "PushのOpened");

			Assert.That(handle.State, Is.EqualTo(EntryState.Live));
			Assert.That(m_Stack.HasPendingOperations, Is.False, "表示が終わればfalseへ戻る");
		}

		/// <summary>
		/// 1「利用者のcallbackから同じ処理へ同期で戻る再入は、拒否も回数制限もせず、要求を保持して
		/// 次のフレームで処理する」。振動するcallbackがあっても同期呼び出しの中で往復し切らず、
		/// Unityがフレームを送れる状態を保つ。要求は捨てないので次のフレームで続きが進む。
		/// リスナー側の上限は、規則が効いていない場合にEditorごと固まらせず失敗として検出するため。
		/// </summary>
		[UnityTest]
		public IEnumerator 再入した再計算は次のフレームへ送られる()
		{
			const int listenerLimit = 64;
			PageHandle handle = m_Stack.Push(UIParameter.WithoutData("A", m_Loader), m_Options);
			yield return TestWait.ForCompletion(handle.Opened, "PushのOpened");

			int passes = 0;
			handle.Page.RenderingChanged += rendered =>
			{
				passes++;
				if (passes >= listenerLimit) { return; }
				// 通知された描画状態の逆を要求するので、必ず現在のm_Visibleと異なり再入する。
				m_Stack.SetVisible(!rendered);
			};

			m_Stack.SetVisible(false);
			int afterCall = passes;

			Assert.That(afterCall, Is.LessThan(listenerLimit),
				"同期呼び出しの中で往復し切らないこと。切らなければフレームを送れずEditorが無応答になる");

			yield return null;

			Assert.That(passes, Is.GreaterThan(afterCall), "保持した要求を捨てず、次のフレームで続きを処理すること");
		}
	}
}
