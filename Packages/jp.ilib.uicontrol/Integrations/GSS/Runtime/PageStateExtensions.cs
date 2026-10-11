using System;
using GameStateStructure;

namespace UIControl.GSS
{
	public static class PageStateExtensions
	{
		/// <summary>Push直後、Openedを待つ前に登録する。登録に失敗した画面も回収する。</summary>
		public static PageHandle AttachToState(this PageHandle handle, Context context)
		{
			if (handle == null) { throw new ArgumentNullException(nameof(handle)); }
			try
			{
				if (context == null) { throw new ArgumentNullException(nameof(context)); }
				context.RegisterExitTask(new PageExitTask(handle));
				return handle;
			}
			catch
			{
				handle.ForceRelease();
				throw;
			}
		}
	}
}
