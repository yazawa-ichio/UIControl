using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace UIControl.Tests
{
	/// <summary>
	/// 寿命テスト用のUIStackとPageを組み立てる。Prefabを持たず、Pageの最小構成を
	/// コードで作る。Page.m_InputGroupは[SerializeField] privateで、コード生成した
	/// Pageには直列化された値がないため、Runtimeへテスト専用のsetterを足す代わりに
	/// ここだけreflectionで設定する。
	/// </summary>
	internal static class TestPage
	{
		private static readonly FieldInfo s_InputGroup =
			typeof(Page).GetField("m_InputGroup", BindingFlags.Instance | BindingFlags.NonPublic);

		internal static UIStack CreateStack(string name = "TestStack")
		{
			if (s_InputGroup == null)
			{
				throw new InvalidOperationException("Page.m_InputGroup was renamed. Update TestPage.");
			}
			GameObject root = new GameObject(name, typeof(RectTransform));
			return root.AddComponent<UIStack>();
		}

		/// <summary>Pageの最小構成。CanvasGroupは自分自身に置く（IsChildOfは自身にも真）。</summary>
		internal static Page CreatePage(string name, Transform inactiveParent)
		{
			GameObject instance = new GameObject(name, typeof(RectTransform), typeof(CanvasGroup));
			instance.transform.SetParent(inactiveParent, false);
			Page page = instance.AddComponent<Page>();
			s_InputGroup.SetValue(page, instance.GetComponent<CanvasGroup>());
			return page;
		}
	}

	/// <summary>
	/// 生成回数を数え、必要なら生成を止められるLoader。止めている間はLoadingのまま
	/// 留まるので、Loading / Preparing中のCloseや取消を観測できる。
	/// </summary>
	internal sealed class TestPageLoader : IPageLoader
	{
		private readonly List<TaskCompletionSource<bool>> m_Gates = new List<TaskCompletionSource<bool>>();
		private readonly List<GameObject> m_Created = new List<GameObject>();
		internal int LoadCount { get; private set; }
		internal int ReleaseCount { get; private set; }
		internal bool HoldLoads { get; set; }
		/// <summary>ここに含むPathのPageには、テストから制御できる演出（TestAnimation）を付ける。</summary>
		internal readonly HashSet<string> AnimatedPaths = new HashSet<string>(StringComparer.Ordinal);
		/// <summary>Pathごとの最後に生成したPageの状態名→Adapter。</summary>
		internal readonly Dictionary<string, Dictionary<string, TestAnimationAdapter>> Animations = new Dictionary<string, Dictionary<string, TestAnimationAdapter>>(StringComparer.Ordinal);

		internal void ReleaseHeldLoads()
		{
			TaskCompletionSource<bool>[] gates = m_Gates.ToArray();
			m_Gates.Clear();
			foreach (TaskCompletionSource<bool> gate in gates)
			{
				gate.TrySetResult(true);
			}
		}

		public async Task<PageInstance> LoadAsync(string path, Transform inactiveParent, CancellationToken cancellation)
		{
			LoadCount++;
			cancellation.ThrowIfCancellationRequested();
			if (HoldLoads)
			{
				TaskCompletionSource<bool> gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
				m_Gates.Add(gate);
				using (cancellation.Register(() => gate.TrySetCanceled()))
				{
					await gate.Task;
				}
			}
			cancellation.ThrowIfCancellationRequested();
			Page page = TestPage.CreatePage(path, inactiveParent);
			if (AnimatedPaths.Contains(path)) { Animations[path] = TestAnimation.Attach(page); }
			GameObject instance = page.gameObject;
			m_Created.Add(instance);
			return new PageInstance(page, () =>
			{
				ReleaseCount++;
				m_Created.Remove(instance);
				if (instance != null) { UnityEngine.Object.Destroy(instance); }
			});
		}

		public Task<IDisposable> PreloadAsync(string path, CancellationToken cancellation)
		{
			cancellation.ThrowIfCancellationRequested();
			return Task.FromResult<IDisposable>(new NoRetention());
		}
		private sealed class NoRetention : IDisposable { public void Dispose() { } }

		/// <summary>テストが失敗して回収が走らなかった場合の後始末。</summary>
		internal void DestroyLeaks()
		{
			foreach (GameObject instance in m_Created.ToArray())
			{
				if (instance != null) { UnityEngine.Object.Destroy(instance); }
			}
			m_Created.Clear();
		}
	}
}
