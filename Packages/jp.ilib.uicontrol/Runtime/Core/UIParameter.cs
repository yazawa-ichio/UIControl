using System;

namespace UIControl
{
	/// <summary>何を出すか（Path）、どう取得するか（Loader）、生成後に実行する初期接続。</summary>
	public class UIParameter
	{
		private readonly Action<Page, PrepareContext> m_Initialize;
		public string Path { get; }
		/// <summary>この要求の取得元。nullならPageLoading.DefaultLoader。</summary>
		public IPageLoader Loader { get; }

		protected UIParameter(string path, Action<Page, PrepareContext> initialize, IPageLoader loader = null)
		{
			if (string.IsNullOrEmpty(path))
			{
				throw new ArgumentException("Path is required.", nameof(path));
			}
			Path = path;
			m_Initialize = initialize ?? throw new ArgumentNullException(nameof(initialize));
			Loader = loader;
		}

		public static UIParameter Create(string path, Action<Page, PrepareContext> initialize, IPageLoader loader = null)
		{
			return new UIParameter(path, initialize, loader);
		}

		public static UIParameter WithoutData(string path, IPageLoader loader = null)
		{
			return new UIParameter(path, (page, context) => { }, loader);
		}

		internal void Initialize(Page page, PrepareContext context)
		{
			context.ThrowIfEnded();
			m_Initialize(page, context);
			context.ThrowIfEnded();
		}
	}
}
