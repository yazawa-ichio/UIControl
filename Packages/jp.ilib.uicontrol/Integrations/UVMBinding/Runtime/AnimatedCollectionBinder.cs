using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UVMBinding;
using UVMBinding.Binders;
using UVMBinding.Core;

namespace UIControl.UVMBinding
{
	[AddComponentMenu("UIControl/UVMBinding/Animated Collection Binder")]
	public sealed class AnimatedCollectionBinder : CollectionBinder
	{
		private readonly List<PendingRow> m_Pending = new List<PendingRow>();
		private struct PendingRow
		{
			internal ListItemStateDriver Driver;
			internal IView View;
			internal IViewModel Model;
		}

		protected override void Bind(int index, GameObject instance, IViewModel model)
		{
			IView view = instance.GetComponent<IView>();
			ListItemStateDriver row = instance.GetComponent<ListItemStateDriver>();
			bool changed = view != null && !ReferenceEquals(view.ViewModel, model);
			if (changed) { row?.BeforeReuse(); }
			base.Bind(index, instance, model);
			if (changed && row != null)
			{
				m_Pending.RemoveAll(item => item.Driver == row);
				m_Pending.Add(new PendingRow { Driver = row, View = view, Model = model });
			}
		}

		private void LateUpdate()
		{
			// 初期Binding/PostLoadと、生成した子の有効化が終わってから開始する。
			for (int i = m_Pending.Count - 1; i >= 0; i--)
			{
				PendingRow row = m_Pending[i];
				if (row.Driver == null || !ReferenceEquals(row.View.ViewModel, row.Model))
				{
					m_Pending.RemoveAt(i);
					continue;
				}
				if (!row.Driver.isActiveAndEnabled) { continue; }
				Page screen = row.Driver.GetComponentInParent<Page>(true);
				if (screen != null && (screen.IsPreparing || !screen.IsRendered)) { continue; }
				m_Pending.RemoveAt(i);
				Observe(row.Driver.Show());
			}
		}

		protected override void OnUnbind()
		{
			m_Pending.Clear();
			base.OnUnbind();
		}

		protected override async Task OnBeforeReleaseAsync(GameObject instance, IViewModel model, CancellationToken cancellationToken)
		{
			ListItemStateDriver row = instance.GetComponent<ListItemStateDriver>();
			if (row == null) { return; }
			m_Pending.RemoveAll(item => item.Driver == row);
			// LayoutGroupによる兄弟順の変更から退場行を外す。対象はPrefab作者が設定する。
			UnityEngine.UI.LayoutElement layout = instance.GetComponent<UnityEngine.UI.LayoutElement>();
			bool ignoreLayout = layout != null && layout.ignoreLayout;
			bool restored = false;
			if (layout != null) { layout.ignoreLayout = true; }
			using (cancellationToken.Register(RestoreLayout))
			{
				try
				{
					StateResult result = await row.Hide(cancellationToken);
					if (result.Status == StateStatus.Faulted) { throw result.Exception ?? new InvalidOperationException("Row Hide failed."); }
					if (result.Status == StateStatus.Missing) { Debug.LogWarning("Row Hide mapping is missing on " + instance.name + ".", instance); }
				}
				finally { RestoreLayout(); }
			}

			void RestoreLayout()
			{
				if (restored) { return; }
				restored = true;
				if (layout != null) { layout.ignoreLayout = ignoreLayout; }
			}
		}

		private async void Observe(Task<StateResult> task)
		{
			try
			{
				StateResult result = await task;
				if (result.Status == StateStatus.Faulted) { Debug.LogException(result.Exception, this); }
				else if (result.Status == StateStatus.Missing) { Debug.LogWarning("Row Show mapping is missing on " + name + ".", this); }
			}
			catch (Exception exception) { Debug.LogException(exception, this); }
		}
	}
}
