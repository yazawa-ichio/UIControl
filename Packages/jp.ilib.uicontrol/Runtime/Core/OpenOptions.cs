using System;

namespace UIControl
{
	public enum FocusOnOpen { Inherit, Activate, None }

	/// <summary>
	/// 要求ごとの表示の振る舞い。Withは元の値を変更せず新しいOptionsを返す。
	/// 通常はPrefabとUIStackの設定で足りる。ここで指定するのは例外的な上書きだけ。
	/// 何を出すか・どう取得するかはUIParameterが持ち、ここには含めない。
	/// </summary>
	public sealed class OpenOptions
	{
		private readonly Action<Page, PrepareContext>[] m_Setup;
		public static OpenOptions Default { get; } = new OpenOptions();
		public BackdropSettings Backdrop { get; }
		public FocusOnOpen FocusOnOpen { get; }
		public TransitionOrder TransitionOrder { get; }

		private OpenOptions(BackdropSettings backdrop = default, FocusOnOpen focusOnOpen = FocusOnOpen.Inherit,
			TransitionOrder transitionOrder = TransitionOrder.Inherit, Action<Page, PrepareContext>[] setup = null)
		{
			Backdrop = backdrop;
			FocusOnOpen = focusOnOpen;
			TransitionOrder = transitionOrder;
			m_Setup = setup ?? Array.Empty<Action<Page, PrepareContext>>();
		}

		public OpenOptions WithBackdrop(BackdropMode? mode = null, BackdropRange? range = null)
		{
			return new OpenOptions(new BackdropSettings(mode ?? Backdrop.Mode, range ?? Backdrop.Range), FocusOnOpen, TransitionOrder, m_Setup);
		}

		public OpenOptions WithFocusOnOpen(FocusOnOpen focus)
		{
			if (!Enum.IsDefined(typeof(FocusOnOpen), focus)) { throw new ArgumentOutOfRangeException(nameof(focus)); }
			return new OpenOptions(Backdrop, focus, TransitionOrder, m_Setup);
		}

		public OpenOptions WithTransitionOrder(TransitionOrder order)
		{
			if (!Enum.IsDefined(typeof(TransitionOrder), order)) { throw new ArgumentOutOfRangeException(nameof(order)); }
			return new OpenOptions(Backdrop, FocusOnOpen, order, m_Setup);
		}

		/// <summary>必須の初期接続（UIParameter）の後、PrefabのPreparationの前に同期で実行する追加処理。</summary>
		public OpenOptions WithSetup(Action<Page, PrepareContext> setup)
		{
			if (setup == null) { throw new ArgumentNullException(nameof(setup)); }
			var copy = new Action<Page, PrepareContext>[m_Setup.Length + 1];
			Array.Copy(m_Setup, copy, m_Setup.Length);
			copy[m_Setup.Length] = setup;
			return new OpenOptions(Backdrop, FocusOnOpen, TransitionOrder, copy);
		}

		internal void Setup(Page page, PrepareContext context)
		{
			foreach (Action<Page, PrepareContext> setup in m_Setup)
			{
				context.ThrowIfEnded();
				setup(page, context);
				context.ThrowIfEnded();
			}
		}
	}
}
