using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace UIControl
{
	[DisallowMultipleComponent]
	[AddComponentMenu("UIControl/Animation/List State Driver")]
	public sealed class ListStateDriver : MonoBehaviour
	{
		[SerializeField, Min(0)]
		private float m_Interval = 0.05f;
		private Run m_Current;
		private Page m_Screen;
		private uint m_RequestRevision;

		private sealed class Run
		{
			internal readonly TaskCompletionSource<StateResult> Completion = new TaskCompletionSource<StateResult>(TaskCreationOptions.RunContinuationsAsynchronously);
			internal ListItemStateDriver[] Rows;
			internal uint[] Revisions;
			internal Task<StateResult>[] Tasks;
			internal double Started;
			internal float Interval;
			internal int Next;
		}

		private void OnEnable()
		{
			m_Screen = GetComponentInParent<Page>(true);
			if (m_Screen != null) { m_Screen.RenderingChanged += OnRenderingChanged; }
		}

		private void OnDisable()
		{
			if (m_Screen != null) { m_Screen.RenderingChanged -= OnRenderingChanged; }
			m_Screen = null;
			Stop();
		}

		private void OnRenderingChanged(bool rendered) { if (!rendered) { Stop(); } }

		private void OnTransformParentChanged()
		{
			if (!isActiveAndEnabled) { return; }
			if (m_Screen != null) { m_Screen.RenderingChanged -= OnRenderingChanged; }
			m_Screen = GetComponentInParent<Page>(true);
			if (m_Screen != null) { m_Screen.RenderingChanged += OnRenderingChanged; }
			if (m_Screen != null && !m_Screen.IsRendered) { Stop(); }
		}

		/// <summary>呼出し時点の子行をHierarchy順で時間差表示する。後から追加した行は含めない。</summary>
		public Task<StateResult> Show() { return Show(GetComponentsInChildren<ListItemStateDriver>()); }

		/// <summary>行集合をコピーしてunscaled時間で再生する。削除・再利用された旧行は省略する。</summary>
		public Task<StateResult> Show(IReadOnlyList<ListItemStateDriver> rows)
		{
			if (rows == null) { throw new ArgumentNullException(nameof(rows)); }
			if (float.IsNaN(m_Interval) || float.IsInfinity(m_Interval) || m_Interval < 0)
			{
				throw new InvalidOperationException("List animation interval must be finite and non-negative.");
			}
			if (!isActiveAndEnabled || m_Screen != null && !m_Screen.IsRendered)
			{
				return Task.FromResult(new StateResult(StateStatus.Cancelled));
			}
			Run run = new Run
			{
				Rows = new ListItemStateDriver[rows.Count], Revisions = new uint[rows.Count], Tasks = new Task<StateResult>[rows.Count],
				Started = Time.unscaledTimeAsDouble, Interval = m_Interval
			};
			HashSet<ListItemStateDriver> seen = new HashSet<ListItemStateDriver>();
			for (int i = 0; i < rows.Count; i++)
			{
				ListItemStateDriver row = rows[i];
				if (row == null || !row.isActiveAndEnabled) { continue; }
				if (!row.transform.IsChildOf(transform) || !seen.Add(row))
				{
					throw new InvalidOperationException("List rows must be unique descendants of this driver.");
				}
				run.Rows[i] = row;
				run.Revisions[i] = row.Revision;
			}
			uint revision = ++m_RequestRevision;
			Finish(m_Current, new StateResult(StateStatus.Superseded));
			if (revision != m_RequestRevision)
			{
				return Task.FromResult(new StateResult(StateStatus.Superseded));
			}
			m_Current = run;
			StartCoroutine(PlayRows(run));
			return run.Completion.Task;
		}

		/// <summary>この一括表示が開始した、まだ再利用されていない行だけを同期停止する。</summary>
		public void Stop()
		{
			m_RequestRevision++;
			Finish(m_Current, new StateResult(StateStatus.Cancelled));
		}

		private IEnumerator PlayRows(Run run)
		{
			while (m_Current == run)
			{
				try
				{
					while (run.Next < run.Rows.Length && Time.unscaledTimeAsDouble - run.Started >= (double)run.Interval * run.Next)
					{
						int index = run.Next++;
						ListItemStateDriver row = run.Rows[index];
						if (IsCurrent(run, index))
						{
							// Showのcallbackが別データを再生しても、その後のRevisionを旧要求へ取り込まない。
							run.Revisions[index] = unchecked(row.Revision + 1);
							run.Tasks[index] = row.Show();
							if (m_Current != run)
							{
								row.StopIfCurrent(run.Revisions[index]);
								yield break;
							}
							if (CheckFailure(run, index)) { yield break; }
						}
					}
					bool completed = run.Next == run.Rows.Length;
					for (int i = 0; i < run.Next; i++)
					{
						Task<StateResult> task = run.Tasks[i];
						if (task == null || !IsCurrent(run, i)) { continue; }
						if (!task.IsCompleted) { completed = false; continue; }
						if (CheckFailure(run, i)) { yield break; }
					}
					if (completed) { Finish(run, new StateResult(StateStatus.Completed)); yield break; }
				}
				catch (Exception exception)
				{
					Finish(run, new StateResult(StateStatus.Faulted, exception));
					yield break;
				}
				yield return null;
			}
		}

		private bool CheckFailure(Run run, int index)
		{
			Task<StateResult> task = run.Tasks[index];
			if (task == null || !IsCurrent(run, index) || !task.IsCompleted) { return false; }
			StateResult result = task.GetAwaiter().GetResult();
			if (result.Status == StateStatus.Completed) { return false; }
			Finish(run, result.Status == StateStatus.Missing
				? new StateResult(StateStatus.Faulted, new InvalidOperationException("List row Show mapping is missing.")) : result);
			return true;
		}

		private static bool IsCurrent(Run run, int index)
		{
			ListItemStateDriver row = run.Rows[index];
			return row != null && row.isActiveAndEnabled && row.Revision == run.Revisions[index];
		}

		private void Finish(Run run, StateResult result)
		{
			if (run == null || m_Current != run) { return; }
			m_Current = null;
			// 停止callbackから始まる新しい再生を、旧一括表示のcleanupで停止しない。
			List<Exception> errors = null;
			if (result.Status != StateStatus.Completed)
			{
				for (int i = 0; i < run.Next; i++)
				{
					if (run.Tasks[i] == null || !IsCurrent(run, i)) { continue; }
					try { run.Rows[i].StopIfCurrent(run.Revisions[i]); }
					catch (Exception exception)
					{
						if (errors == null) { errors = new List<Exception>(); }
						errors.Add(exception);
					}
				}
			}
			if (errors != null)
			{
				if (result.Exception != null) { errors.Insert(0, result.Exception); }
				result = new StateResult(StateStatus.Faulted, new AggregateException(errors));
			}
			run.Completion.TrySetResult(result);
		}
	}
}
