using System;
using System.Collections.Generic;
using UVMBinding;
using UVMBinding.Core;

namespace UIControl.Adoption
{
	// アプリのデータと通常Buttonのイベント。Viewの派生や型登録は不要。
	public sealed class DialogModel : GeneralViewModel, IDisposable
	{
		private readonly IDisposable m_Accept;
		private readonly IDisposable m_Dismiss;
		public DialogModel(string title, Action accept, Action dismiss)
		{
			Set("Title", title);
			Set("Message", "Choose Confirm or Cancel. The next dialog waits for Close to finish.");
			List<IViewModel> items = new List<IViewModel>();
			for (int i = 0; i < 9; i++)
			{
				GeneralViewModel row = new GeneralViewModel();
				row.Set("Label", "Reward " + (i + 1));
				items.Add(row);
			}
			Set<IList<IViewModel>>("Items", items);
			m_Accept = Event.Subscribe("Accept", accept);
			m_Dismiss = Event.Subscribe("Dismiss", dismiss);
		}
		public void Dispose() { m_Accept.Dispose(); m_Dismiss.Dispose(); }
	}
}
