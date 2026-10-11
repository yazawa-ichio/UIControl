using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace UIControl
{
	public interface IPageLoader
	{
		/// <summary>Pathを解決して非activeの親の下にPageを生成する。返却処理はPageInstanceに載せる。</summary>
		Task<PageInstance> LoadAsync(string path, Transform inactiveParent, CancellationToken cancellation);
		/// <summary>利用予定のPathを先に取得して保持する。返るIDisposableを呼び出し側が持ち、Disposeで保持を解く。Loaderは辞書で抱えない。</summary>
		Task<IDisposable> PreloadAsync(string path, CancellationToken cancellation);
	}
}
