using System;
using System.Threading.Tasks;
using UnityEngine;
using UVMBinding;
using UVMBinding.Core;

namespace UIControl.UVMBinding
{
	[DisallowMultipleComponent]
	[AddComponentMenu("UIControl/UVMBinding/View Bind Step")]
	public sealed class ViewBindStep : PrepareStep
	{
		[SerializeField]
		private View m_View;
		public IViewModel Model { get; set; }

		public override Task ExecuteAsync(PrepareContext context)
		{
			context.ThrowIfEnded();
			if (Model == null) { throw new InvalidOperationException("ViewBindStep requires an IViewModel before preparation."); }
			if (m_View == null || !m_View.transform.IsChildOf(context.Page.transform))
			{
				throw new InvalidOperationException("Assign a View owned by this Page.");
			}
			View view = m_View;
			context.OnRelease(() =>
			{
				try { if (view != null) { view.ViewModel = null; } }
				finally { Model = null; }
			});
			view.Prepare();
			view.ViewModel = Model;
			context.ThrowIfEnded();
			view.TryUpdate();
			context.ThrowIfEnded();
			return Task.CompletedTask;
		}

		private void Reset() { m_View = GetComponent<View>(); }
	}
}
