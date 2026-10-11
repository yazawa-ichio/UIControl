using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace UIControl.Tests
{
	/// <summary>
	/// テストから完了・停止を制御できるAdapter。時間で進まず、Complete()を呼ぶまで再生中のまま留まる。
	/// 遷移の順序と割り込み規則を、実時間に依存せず観測するために使う。
	/// </summary>
	internal sealed class TestAnimationAdapter : AnimationAdapter
	{
		private TaskCompletionSource<bool> m_Run;
		internal int PlayCount { get; private set; }
		internal int StopCount { get; private set; }
		internal int ApplyFinalCount { get; private set; }
		internal bool IsPlaying => m_Run != null && !m_Run.Task.IsCompleted;
		public override bool SupportsExternalTime => false;

		public override Task PlayAsync(AnimationTimeMode timeMode, CancellationToken cancellation)
		{
			Stop();
			var run = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
			m_Run = run;
			PlayCount++;
			cancellation.Register(() => { if (m_Run == run) { Stop(); } });
			return run.Task;
		}

		public override void Stop()
		{
			TaskCompletionSource<bool> run = m_Run;
			m_Run = null;
			if (run != null) { StopCount++; run.TrySetCanceled(); }
		}

		public override void ApplyFinal()
		{
			Stop();
			ApplyFinalCount++;
		}

		internal void Complete()
		{
			TaskCompletionSource<bool> run = m_Run;
			m_Run = null;
			run?.TrySetResult(true);
		}
	}

	/// <summary>Pageへ Open / Close / Covered / Hidden の状態を持つPlayerとDriverを付ける。直列化フィールドはreflectionで設定する。</summary>
	internal static class TestAnimation
	{
		private static readonly string[] s_States = { "Open", "Close", "Covered", "Hidden" };

		internal static Dictionary<string, TestAnimationAdapter> Attach(Page page)
		{
			NamedStatePlayer player = page.gameObject.AddComponent<NamedStatePlayer>();
			PageStateDriver driver = page.gameObject.AddComponent<PageStateDriver>();
			var adapters = new Dictionary<string, TestAnimationAdapter>(StringComparer.Ordinal);
			var definitions = new StateDefinition[s_States.Length];
			for (int i = 0; i < s_States.Length; i++)
			{
				GameObject host = new GameObject(s_States[i]);
				host.transform.SetParent(page.transform, false);
				TestAnimationAdapter adapter = host.AddComponent<TestAnimationAdapter>();
				adapters[s_States[i]] = adapter;
				StateDefinition definition = new StateDefinition();
				Set(definition, "m_Channel", "Page");
				Set(definition, "m_Name", s_States[i]);
				Set(definition, "m_Animation", adapter);
				definitions[i] = definition;
			}
			Set(player, "m_States", definitions);
			Set(driver, "m_Player", player);
			Set(page, "m_Visual", driver);
			return adapters;
		}

		private static void Set(object target, string field, object value)
		{
			FieldInfo info = target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic);
			if (info == null) { throw new InvalidOperationException(target.GetType().Name + "." + field + " was renamed. Update TestAnimation."); }
			info.SetValue(target, value);
		}
	}
}
