using System;
using System.Threading.Tasks;
using UnityEngine;
using UVMBinding;

namespace UIControl.UVMBinding
{
	[AddComponentMenu("UIControl/UVMBinding/View Refresh Step")]
	public sealed class ViewRefreshStep : PrepareStep
	{
		[SerializeField]
		private View m_View;
		public override Task ExecuteAsync(PrepareContext context)
		{
			context.ThrowIfEnded();
			if (m_View == null || !m_View.transform.IsChildOf(context.Page.transform))
			{
				throw new InvalidOperationException("Assign a View owned by this Page.");
			}
			m_View.TryUpdate();
			context.ThrowIfEnded();
			return Task.CompletedTask;
		}
		private void Reset() { m_View = GetComponent<View>(); }
	}
}
