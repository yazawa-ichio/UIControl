using System.Collections;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace UIControl.Tests
{
	/// <summary>
	/// 遷移の規則を検証する。Stack内の遷移は常に1つで、進行中の演出へ割り込まない（改修計画C1）。
	/// 前面と背面の演出順序はTransitionOrderで決まる（C2）。演出はTestAnimationAdapterで止めて観測する。
	/// </summary>
	public sealed class TransitionTests
	{
		private UIStack m_Stack;
		private TestPageLoader m_Loader;

		[SetUp]
		public void SetUp()
		{
			m_Loader = new TestPageLoader();
			m_Loader.AnimatedPaths.Add("A");
			m_Loader.AnimatedPaths.Add("B");
			m_Stack = TestPage.CreateStack();
		}

		[UnityTearDown]
		public IEnumerator TearDown()
		{
			if (m_Stack != null) { m_Stack.ForceReleaseAll(); Object.Destroy(m_Stack.gameObject); }
			m_Loader.DestroyLeaks();
			yield return null;
		}

		private TestAnimationAdapter Anim(string path, string state)
		{
			return m_Loader.Animations.TryGetValue(path, out var states) && states.TryGetValue(state, out TestAnimationAdapter adapter) ? adapter : null;
		}
		private bool Playing(string path, string state) { return Anim(path, state)?.IsPlaying == true; }
		private int PlayCount(string path, string state) { return Anim(path, state)?.PlayCount ?? 0; }

		/// <summary>Pushして、Open演出を完了させ、Liveまで進める。</summary>
		private IEnumerator OpenLive(string path, out PageHandle handle)
		{
			PageHandle h = m_Stack.Push(UIParameter.WithoutData(path, m_Loader));
			handle = h;
			return Complete(h, path);
		}
		private IEnumerator Complete(PageHandle h, string path)
		{
			yield return TestWait.Until(() => Playing(path, "Open"), path + "のOpen演出が始まらない");
			Anim(path, "Open").Complete();
			yield return TestWait.ForCompletion(h.Opened, path + "が開かない");
			Assert.That(h.Opened.Result.Status, Is.EqualTo(OpenStatus.Opened));
		}

		[UnityTest]
		public IEnumerator Opening中のCloseは割り込まずOpen完了後に閉じる()
		{
			PageHandle handle = m_Stack.Push(UIParameter.WithoutData("A", m_Loader));
			yield return TestWait.Until(() => handle.State == EntryState.Opening && Playing("A", "Open"), "Open演出が始まらない");
			Task<CloseResult> closing = handle.Close();
			yield return null;
			yield return null;
			Assert.That(handle.State, Is.EqualTo(EntryState.Opening), "Open中のCloseは割り込まない");
			Assert.That(Playing("A", "Open"), Is.True, "Open演出は続く");
			Assert.That(handle.Opened.IsCompleted, Is.False);
			Anim("A", "Open").Complete();
			yield return TestWait.ForCompletion(handle.Opened, "Openが完了しない");
			Assert.That(handle.Opened.Result.Status, Is.EqualTo(OpenStatus.Opened), "開き切ってから閉じる");
			yield return TestWait.Until(() => handle.State == EntryState.Closing && Playing("A", "Close"), "Close演出が始まらない");
			Anim("A", "Close").Complete();
			yield return TestWait.ForCompletion(closing, "Closeが完了しない");
			Assert.That(closing.Result.Status, Is.EqualTo(CloseStatus.Closed));
			Assert.That(m_Stack.IsTransitioning, Is.False);
			Assert.That(m_Stack.GetStackSnapshot(), Is.Empty);
		}

		[UnityTest]
		public IEnumerator Open中のForceReleaseは演出を止めて後続の要求を進める()
		{
			PageHandle a = m_Stack.Push(UIParameter.WithoutData("A", m_Loader));
			yield return TestWait.Until(() => a.State == EntryState.Opening && Playing("A", "Open"), "Open演出が始まらない");
			PageHandle b = m_Stack.Push(UIParameter.WithoutData("B", m_Loader));
			yield return null;
			Assert.That(b.State, Is.EqualTo(EntryState.Queued), "前提: 後続はFIFOで待つ");
			TestAnimationAdapter open = Anim("A", "Open");
			a.ForceRelease();
			Assert.That(a.State, Is.EqualTo(EntryState.Released), "同じ呼び出し内で失効する");
			Assert.That(a.Opened.Result.Status, Is.EqualTo(OpenStatus.Forced));
			Assert.That(open.IsPlaying, Is.False, "進行中の演出は止まる");
			yield return Complete(b, "B");
			Assert.That(b.State, Is.EqualTo(EntryState.Live));
			Assert.That(m_Stack.IsTransitioning, Is.False, "回収後に遷移中のまま残らない");
			Assert.That(m_Stack.GetStackSnapshot(), Is.EqualTo(new[] { b }));
		}

		[UnityTest]
		public IEnumerator SetVisibleは進行中の演出を止めて最終状態を即時適用する()
		{
			PageHandle a = m_Stack.Push(UIParameter.WithoutData("A", m_Loader));
			yield return TestWait.Until(() => a.State == EntryState.Opening && Playing("A", "Open"), "Open演出が始まらない");
			m_Stack.SetVisible(false);
			yield return TestWait.ForCompletion(a.Opened, "SetVisible後にOpenが完了しない");
			Assert.That(a.Opened.Result.Status, Is.EqualTo(OpenStatus.Opened), "演出の取消は失敗ではない");
			Assert.That(a.State, Is.EqualTo(EntryState.Live));
			Assert.That(a.Page.Presentation, Is.EqualTo(Presentation.Hidden), "非表示中はHidden");
			Assert.That(a.Page.IsRendered, Is.False);
			m_Stack.SetVisible(true);
			yield return null;
			Assert.That(a.Page.Presentation, Is.EqualTo(Presentation.Visible), "表示へ戻すと最終状態を即時適用する");
			Assert.That(a.Page.IsRendered, Is.True);
			Assert.That(Anim("A", "Open").ApplyFinalCount, Is.GreaterThan(0), "演出は再生せず終値を適用する");
			Assert.That(m_Stack.IsTransitioning, Is.False);
		}

		[UnityTest]
		public IEnumerator ReplaceのForegroundFirstは新Openの後に旧Close()
		{
			yield return OpenLive("A", out PageHandle a);
			PageHandle b = m_Stack.Replace(UIParameter.WithoutData("B", m_Loader), OpenOptions.Default.WithTransitionOrder(TransitionOrder.ForegroundFirst));
			yield return TestWait.Until(() => Playing("B", "Open"), "新Open演出が始まらない");
			Assert.That(PlayCount("A", "Close"), Is.EqualTo(0), "新Openの間は旧Closeを始めない");
			Anim("B", "Open").Complete();
			yield return TestWait.Until(() => Playing("A", "Close"), "旧Close演出が始まらない");
			Assert.That(b.Opened.IsCompleted, Is.False, "旧Closeが終わるまでOpenedは確定しない");
			Anim("A", "Close").Complete();
			yield return TestWait.ForCompletion(b.Opened, "Replaceが完了しない");
			Assert.That(b.Opened.Result.Status, Is.EqualTo(OpenStatus.Opened));
			Assert.That(a.Closed.Result.Status, Is.EqualTo(CloseStatus.Replaced));
			Assert.That(m_Stack.GetStackSnapshot(), Is.EqualTo(new[] { b }));
		}

		[UnityTest]
		public IEnumerator ReplaceのBackgroundFirstは旧Closeの後に新Open()
		{
			yield return OpenLive("A", out PageHandle a);
			PageHandle b = m_Stack.Replace(UIParameter.WithoutData("B", m_Loader), OpenOptions.Default.WithTransitionOrder(TransitionOrder.BackgroundFirst));
			yield return TestWait.Until(() => Playing("A", "Close"), "旧Close演出が始まらない");
			Assert.That(PlayCount("B", "Open"), Is.EqualTo(0), "旧Closeの間は新Openを始めない");
			Assert.That(m_Stack.GetStackSnapshot(), Is.EqualTo(new[] { a }), "旧PageはClose演出中もStackに残る");
			Anim("A", "Close").Complete();
			yield return TestWait.Until(() => Playing("B", "Open"), "新Open演出が始まらない");
			Anim("B", "Open").Complete();
			yield return TestWait.ForCompletion(b.Opened, "Replaceが完了しない");
			Assert.That(b.Opened.Result.Status, Is.EqualTo(OpenStatus.Opened));
			Assert.That(a.Closed.Result.Status, Is.EqualTo(CloseStatus.Replaced));
		}

		[UnityTest]
		public IEnumerator ReplaceのConcurrentは旧Closeと新Openを同時に進める()
		{
			yield return OpenLive("A", out PageHandle a);
			PageHandle b = m_Stack.Replace(UIParameter.WithoutData("B", m_Loader), OpenOptions.Default.WithTransitionOrder(TransitionOrder.Concurrent));
			yield return TestWait.Until(() => Playing("A", "Close") && Playing("B", "Open"), "両方の演出が同時に始まらない");
			Anim("B", "Open").Complete();
			yield return null;
			Assert.That(b.Opened.IsCompleted, Is.False, "片方だけでは完了しない");
			Anim("A", "Close").Complete();
			yield return TestWait.ForCompletion(b.Opened, "Replaceが完了しない");
			Assert.That(b.Opened.Result.Status, Is.EqualTo(OpenStatus.Opened));
			Assert.That(a.Closed.Result.Status, Is.EqualTo(CloseStatus.Replaced));
		}

		[UnityTest]
		public IEnumerator Backdropで退く背面もTransitionOrderに従う()
		{
			yield return OpenLive("A", out PageHandle a);
			PageHandle b = m_Stack.Push(UIParameter.WithoutData("B", m_Loader),
				OpenOptions.Default.WithBackdrop(BackdropMode.Hidden, BackdropRange.AllBehind).WithTransitionOrder(TransitionOrder.BackgroundFirst));
			yield return TestWait.Until(() => Playing("A", "Hidden"), "背面のHide演出が始まらない");
			Assert.That(PlayCount("B", "Open"), Is.EqualTo(0), "背面が退くまで前面を始めない");
			Anim("A", "Hidden").Complete();
			yield return TestWait.Until(() => Playing("B", "Open"), "前面のOpen演出が始まらない");
			Anim("B", "Open").Complete();
			yield return TestWait.ForCompletion(b.Opened, "Pushが完了しない");
			Assert.That(a.Page.Presentation, Is.EqualTo(Presentation.Hidden));
			Assert.That(b.Page.Presentation, Is.EqualTo(Presentation.Visible));
			Assert.That(m_Stack.GetStackSnapshot(), Is.EqualTo(new[] { a, b }));
		}
	}
}
