using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;

namespace UIControl.Tests
{
	/// <summary>
	/// Focusの有効化が、その瞬間の入力可否に左右されないことを検証する。
	/// 入力が許されないのは「同じStackの上端でないPage」であり、上に別のPageを積むことで作る
	/// （旧BlockInputは廃止した。全画面遮断はアプリ側の遮断板で行う）。
	/// 背面PageはKeepVisibleのまま描画されているのでCanFocusは真、AllowsInputだけが偽になる。
	/// </summary>
	public sealed class FocusActivationTests
	{
		private GameObject m_Events;
		private UIStack m_Stack;
		private TestPageLoader m_Loader;

		[SetUp]
		public void SetUp()
		{
			m_Events = new GameObject("EventSystem", typeof(EventSystem));
			// 調停器はEventSystemに対して1つで、入力方式は前のテストから引き継がれる。Pointer中は自動選択しないので、Navigationを前提にする。
			UIFocus.NotifyInputMode(FocusInputMode.Navigation);
			m_Loader = new TestPageLoader();
			m_Stack = TestPage.CreateStack();
		}

		[UnityTearDown]
		public IEnumerator TearDown()
		{
			if (m_Stack != null) { m_Stack.ForceReleaseAll(); UnityEngine.Object.Destroy(m_Stack.gameObject); }
			if (m_Events != null) { UnityEngine.Object.Destroy(m_Events); }
			m_Loader.DestroyLeaks();
			yield return null;
		}

		private IEnumerator OpenWithScope(string path, System.Action<PageHandle, FocusScope> ready)
		{
			PageHandle handle = m_Stack.Push(UIParameter.WithoutData(path, m_Loader));
			yield return TestWait.ForCompletion(handle.Opened, "Pageが開かない");
			Assert.That(handle.Page, Is.Not.Null, "前提: Pageが開いている");
			GameObject scopeObject = new GameObject("Scope", typeof(RectTransform));
			scopeObject.transform.SetParent(handle.Page.transform, false);
			FocusScope scope = scopeObject.AddComponent<FocusScope>();
			ready(handle, scope);
		}

		/// <summary>入力が許されない間の有効化は捨てずに保持し、許された時点で効く。</summary>
		[UnityTest]
		public IEnumerator 入力が許されない間の有効化も許されたら効く()
		{
			FocusScope scope = null;
			yield return OpenWithScope("A", (_, s) => scope = s);
			Assert.That(scope.CanFocus, Is.True, "前提: 上に積む前は有効化できる");

			PageHandle cover = m_Stack.Push(UIParameter.WithoutData("B", m_Loader));
			yield return TestWait.ForCompletion(cover.Opened, "上のPageが開かない");
			yield return null;
			Assert.That(scope.AllowsInput, Is.False, "前提: 上端でない間は操作を受け付けない");
			Assert.That(scope.Activate(), Is.True, "許されていなくても有効化の要求は受け付ける");

			yield return TestWait.ForCompletion(cover.Close(), "上のPageが閉じない");
			yield return null;
			Assert.That(scope.AllowsInput, Is.True);
			Assert.That(UIFocus.Current, Is.EqualTo(scope), "許されない間に要求した有効化が効いている");
		}

		/// <summary>一時的に入力が許されなくても焦点の所属は失わない。CanFocusは資格、AllowsInputが可否。</summary>
		[UnityTest]
		public IEnumerator 入力が許されなくても焦点の所属は失わない()
		{
			FocusScope scope = null;
			yield return OpenWithScope("A", (_, s) => scope = s);
			Assert.That(scope.Activate(), Is.True);
			yield return null;
			Assert.That(UIFocus.Current, Is.EqualTo(scope), "前提: 有効になっている");

			PageHandle cover = m_Stack.Push(UIParameter.WithoutData("B", m_Loader));
			yield return TestWait.ForCompletion(cover.Opened, "上のPageが開かない");
			yield return null;
			Assert.That(scope.CanFocus, Is.True, "背面でも焦点を持つ資格は残る");
			Assert.That(scope.AllowsInput, Is.False, "背面の間は操作を受け付けない");
			Assert.That(UIFocus.Current, Is.EqualTo(scope), "背面の間も焦点の所属は残る");

			yield return TestWait.ForCompletion(cover.Close(), "上のPageが閉じない");
			yield return null;
			Assert.That(UIFocus.Current, Is.EqualTo(scope), "上端に戻っても変わらない");
		}

		/// <summary>入力が許されるようになったら選択が戻る。所属を保つのはこの復元のため。</summary>
		[UnityTest]
		public IEnumerator 入力が許されるようになったら選択が戻る()
		{
			FocusScope scope = null;
			yield return OpenWithScope("A", (_, s) => scope = s);

			GameObject buttonObject = new GameObject("Button", typeof(RectTransform));
			buttonObject.transform.SetParent(scope.transform, false);
			buttonObject.AddComponent<UnityEngine.UI.Image>();
			buttonObject.AddComponent<UnityEngine.UI.Button>();
			buttonObject.AddComponent<NavigationNode>();

			Assert.That(scope.Activate(), Is.True);
			yield return null;
			yield return null;
			Assert.That(EventSystem.current.currentSelectedGameObject, Is.EqualTo(buttonObject), "前提: 選択が付いている");

			PageHandle cover = m_Stack.Push(UIParameter.WithoutData("B", m_Loader));
			yield return TestWait.ForCompletion(cover.Opened, "上のPageが開かない");
			yield return null;
			Assert.That(EventSystem.current.currentSelectedGameObject, Is.Not.EqualTo(buttonObject), "背面の間は選択を持たない");

			yield return TestWait.ForCompletion(cover.Close(), "上のPageが閉じない");
			yield return null;
			yield return null;

			Assert.That(EventSystem.current.currentSelectedGameObject, Is.EqualTo(buttonObject),
				"上端に戻っても選択が戻らない。selected=" + (EventSystem.current.currentSelectedGameObject == null
					? "(なし)" : EventSystem.current.currentSelectedGameObject.name));
		}
	}
}
