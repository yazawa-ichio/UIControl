using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace UIControl
{
	public interface IUIStack
	{
		PageHandle Push(UIParameter parameter, OpenOptions options = null, CancellationToken cancellation = default);
		PageHandle Replace(UIParameter parameter, OpenOptions options = null, CancellationToken cancellation = default);
		Task<CloseResult> Pop();
		Task CloseAll();
		void ForceReleaseAll();
		void SetVisible(bool visible);
		bool HasPendingOperations { get; }
		bool IsTransitioning { get; }
		IReadOnlyList<PageHandle> GetStackSnapshot();
	}
}
