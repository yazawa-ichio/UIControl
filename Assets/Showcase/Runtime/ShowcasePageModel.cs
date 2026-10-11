using System;
using System.Collections.Generic;
using UVMBinding;
using UVMBinding.Core;

namespace UIControl.Showcase
{
	// 仮データだけを保持する。Page へは通常の IViewModel として渡す。
	public sealed class ShowcasePageModel : GeneralViewModel, IDisposable
	{
		readonly List<IDisposable> m_Subscriptions = new List<IDisposable>();
		readonly List<ShowcasePageModel> m_Rows = new List<ShowcasePageModel>();
		readonly ShowcaseApp m_App;

		public string Title { get; }

		public ShowcasePageModel(ShowcaseApp app, string title, string description = "")
		{
			m_App = app;
			Title = title;
			Set("Title", title);
			Set("Description", description);
			Set("Detail", "");
			Set("Label", title);
			Set("Enabled", true);
			Set("BackVisible", false);
			Set("InputVisible", false);
			Set("BackLabel", "BACK");
			Set<IList<IViewModel>>("Actions", new List<IViewModel>());
			Set<IList<IViewModel>>("Items", new List<IViewModel>());
		}

		public void On(string name, Action callback)
		{
			m_Subscriptions.Add(Event.Subscribe(name, () => m_App.Dispatch(Title + "/" + name, callback)));
		}

		public void Back(Action callback, string label = "BACK")
		{
			Set("BackVisible", true);
			Set("BackLabel", label);
			On("Back", callback);
		}

		public ShowcasePageModel Row(string label, Action callback, bool enabled = true)
		{
			var row = new ShowcasePageModel(m_App, label);
			row.Set("Enabled", enabled);
			row.On("Click", callback);
			m_Rows.Add(row);
			return row;
		}

		public void Actions(params (string Label, Action Click)[] actions)
		{
			var rows = new List<IViewModel>();
			foreach (var action in actions) { rows.Add(Row(action.Label, action.Click)); }
			Set<IList<IViewModel>>("Actions", rows);
		}

		public void Items(int count, Func<int, string> label, Action<int> select)
		{
			var rows = new List<IViewModel>();
			for (int i = 0; i < count; i++)
			{
				int index = i;
				rows.Add(Row(label(index), () => select(index)));
			}
			Set<IList<IViewModel>>("Items", rows);
		}

		public void Dispose()
		{
			foreach (var subscription in m_Subscriptions) { subscription.Dispose(); }
			m_Subscriptions.Clear();
			foreach (var row in m_Rows) { row.Dispose(); }
			m_Rows.Clear();
		}
	}
}
