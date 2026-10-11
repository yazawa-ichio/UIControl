using System;
using System.Collections;
using System.Threading.Tasks;

namespace UIControl.Tests
{
	/// <summary>
	/// PlayModeテストはasync Taskをコルーチンで1フレームずつ進めるため、Task.Waitや
	/// Result、Assert.ThrowsAsyncのようにメインスレッドを同期待ちするAPIを使うと継続が
	/// ポンプされず停止する。待機は必ずフレームを送りながら行い、上限を超えたら失敗させる。
	/// </summary>
	internal static class TestWait
	{
		/// <summary>既定の上限。実時間ではなくフレーム数で頭打ちにする。</summary>
		internal const int DefaultFrames = 600;

		internal static IEnumerator Until(Func<bool> condition, string description, int frames = DefaultFrames)
		{
			for (int i = 0; i < frames; i++)
			{
				if (condition()) { yield break; }
				yield return null;
			}
			throw new TimeoutException($"{frames}フレーム待っても成立しなかった: {description}");
		}

		/// <summary>完了だけを待つ。失敗・取消の内容はテスト側が結果値で確認する。</summary>
		internal static IEnumerator ForCompletion(Task task, string description, int frames = DefaultFrames)
		{
			if (task == null) { throw new ArgumentNullException(nameof(task)); }
			return Until(() => task.IsCompleted, description, frames);
		}

		/// <summary>完了を待ってから例外を表面化する。完了済みなので同期待ちにはならない。</summary>
		internal static IEnumerator ForTask(Task task, string description, int frames = DefaultFrames)
		{
			yield return ForCompletion(task, description, frames);
			task.GetAwaiter().GetResult();
		}
	}
}
