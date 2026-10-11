using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace UIControl
{
	public sealed class DefaultPageLoader : IPageLoader
	{
		private readonly Dictionary<string, GameObject> m_Registered = new Dictionary<string, GameObject>(StringComparer.Ordinal);

		public void Register(string path, GameObject prefab, bool overwrite = false)
		{
			if (string.IsNullOrEmpty(path) || prefab == null) { throw new ArgumentException("A key and Prefab are required."); }
			if (!overwrite && m_Registered.TryGetValue(path, out GameObject existing) && existing != prefab) { throw new InvalidOperationException("Prefab key is already registered: " + path); }
			m_Registered[path] = prefab;
		}

		public async Task<PageInstance> LoadAsync(string path, Transform inactiveParent, CancellationToken cancellation)
		{
			GameObject prefab = await LoadPrefabAsync(path, cancellation);
			cancellation.ThrowIfCancellationRequested();
			if (inactiveParent == null || inactiveParent.gameObject.activeInHierarchy)
			{
				throw new InvalidOperationException("Page instantiation requires an inactive parent.");
			}
			GameObject instance = UnityEngine.Object.Instantiate(prefab, inactiveParent, false);
			try
			{
				Page screen = instance.GetComponent<Page>();
				if (screen == null) { throw new InvalidOperationException("The Prefab root must contain Page."); }
				return new PageInstance(screen, () => { if (instance != null) { UnityEngine.Object.Destroy(instance); } GC.KeepAlive(prefab); });
			}
			catch
			{
				UnityEngine.Object.Destroy(instance);
				throw;
			}
		}

		public async Task<IDisposable> PreloadAsync(string path, CancellationToken cancellation = default)
		{
			return new PrefabRetention(await LoadPrefabAsync(path, cancellation));
		}

		private async Task<GameObject> LoadPrefabAsync(string path, CancellationToken cancellation)
		{
			if (string.IsNullOrEmpty(path)) { throw new ArgumentException("Path is required.", nameof(path)); }
			cancellation.ThrowIfCancellationRequested();
			if (m_Registered.TryGetValue(path, out GameObject registered)) { return registered; }
			ResourceRequest request = Resources.LoadAsync<GameObject>(path);
			TaskCompletionSource<bool> completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
			request.completed += _ => completion.TrySetResult(true);
			if (request.isDone) { completion.TrySetResult(true); }
			using (cancellation.Register(() => completion.TrySetCanceled())) { await completion.Task; }
			cancellation.ThrowIfCancellationRequested();
			return request.asset as GameObject ?? throw new InvalidOperationException("Page Prefab was not found: " + path);
		}

		private sealed class PrefabRetention : IDisposable
		{
			private GameObject m_Prefab;
			public PrefabRetention(GameObject prefab) { m_Prefab = prefab; }
			public void Dispose() { m_Prefab = null; }
		}
	}
}
