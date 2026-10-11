using System;
using System.Threading;

namespace UIControl
{
	/// <summary>生成元へ一回だけ返す取得結果。返却処理は取得時のLoaderが決める。</summary>
	public sealed class PageInstance : IDisposable
	{
		private Action m_Release;
		public Page Page { get; }

		public PageInstance(Page page, Action release)
		{
			Page = page != null ? page : throw new ArgumentNullException(nameof(page));
			m_Release = release ?? throw new ArgumentNullException(nameof(release));
		}

		public void Dispose() { Release(); }

		internal void Release()
		{
			Action release = Interlocked.Exchange(ref m_Release, null);
			release?.Invoke();
		}
	}
}
