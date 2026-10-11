using System.Threading;
using System.Threading.Tasks;

namespace UIControl
{
	internal sealed class Entry
	{
		internal PageHandle Handle;
		internal UIParameter Parameter;
		internal IPageLoader Loader;
		internal OpenOptions Options;
		internal BackdropSettings DefaultBackdrop;
		internal FocusOnOpen FocusOnOpen;
		internal FocusOnOpen DefaultFocusOnOpen;
		internal TransitionOrder Order;
		internal TransitionOrder DefaultOrder;
		internal bool Replace;
		internal Entry Replacing;
		internal PageInstance Instance;
		internal PrepareContext Context;
		internal BackdropSettings Backdrop;
		internal bool Ended;
		internal bool CloseRequested;
		internal readonly CancellationTokenSource PreparationCancellation = new CancellationTokenSource();
		internal CancellationTokenRegistration OpenCancellation;
		internal readonly TaskCompletionSource<bool> Released = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		internal Page Page => Instance?.Page;
	}
}
