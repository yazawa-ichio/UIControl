using System.Threading.Tasks;
using UnityEngine;

namespace UIControl
{
	/// <summary>
	/// Pageの表示状態を演出へ結び付ける差し替え口。Coreは具体的なAnimation実装を知らず、この型だけを扱う。
	/// 標準実装は UIControl.Animation の PageStateDriver。
	/// </summary>
	public abstract class PageVisual : MonoBehaviour
	{
		/// <summary>状態遷移の演出を再生し、完了を待つ。取消はOperationCanceledExceptionで返す。</summary>
		public abstract Task Transition(Presentation presentation, ChangeReason reason);
		/// <summary>演出を再生せず、その状態の終値を適用する。</summary>
		public abstract void ApplyImmediately(Presentation presentation, ChangeReason reason);
		/// <summary>進行中の演出を止める。待機中のTransitionは取消で返る。</summary>
		public abstract void Stop();
	}
}
