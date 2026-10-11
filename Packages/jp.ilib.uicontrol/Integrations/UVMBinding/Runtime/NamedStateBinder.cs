using System;
using System.Threading.Tasks;
using UnityEngine;
using UVMBinding;

namespace UIControl.UVMBinding
{
	[AddComponentMenu("UIControl/UVMBinding/Named State Binder")]
	public sealed class NamedStateBinder : Binder<string>
	{
		[SerializeField]
		private NamedStatePlayer m_Player;
		[SerializeField]
		private string m_Channel = "Default";
		private Page m_Screen;
		private string m_State;
		private bool m_HasValue;
		private bool m_Pending;

		private void OnEnable()
		{
			m_Screen = GetComponentInParent<Page>(true);
			if (m_Screen != null) { m_Screen.RenderingChanged += RenderingChanged; }
			m_Pending = m_HasValue;
		}

		private void OnDisable()
		{
			if (m_Screen != null) { m_Screen.RenderingChanged -= RenderingChanged; }
			Stop();
		}

		protected override void UpdateValue(string value)
		{
			m_State = value;
			m_HasValue = true;
			m_Pending = true;
			Page screen = m_Screen != null ? m_Screen : GetComponentInParent<Page>(true);
			if (screen != null && screen.IsPreparing && m_Player != null)
			{
				if (string.IsNullOrEmpty(value)) { Stop(); }
				else { Report(m_Player.ApplyImmediately(value, m_Channel)); }
			}
		}

		private void LateUpdate()
		{
			if (!m_Pending || !m_HasValue || m_Player == null || !m_Player.isActiveAndEnabled
				|| m_Screen != null && (m_Screen.IsPreparing || !m_Screen.IsRendered)) { return; }
			m_Pending = false;
			if (string.IsNullOrEmpty(m_State)) { Stop(); }
			else { Observe(m_Player.Play(m_State, m_Channel, true)); }
		}

		private void RenderingChanged(bool rendered)
		{
			if (!rendered) { Stop(); }
			m_Pending = m_HasValue;
		}

		protected override void OnUnbind()
		{
			m_HasValue = false;
			m_Pending = false;
			m_State = null;
			Stop();
		}

		private void Stop()
		{
			try { if (m_Player != null) { m_Player.Stop(m_Channel); } }
			catch (Exception exception) { Debug.LogException(exception, this); }
		}

		private async void Observe(Task<StateResult> task)
		{
			try { Report(await task); }
			catch (Exception exception) { Debug.LogException(exception, this); }
		}

		private void Report(StateResult result)
		{
			if (result.Status == StateStatus.Faulted) { Debug.LogException(result.Exception, this); }
			else if (result.Status == StateStatus.Missing) { Debug.LogWarning("Named state mapping is missing on " + name + ".", this); }
		}
		private void Reset() { m_Player = GetComponent<NamedStatePlayer>(); }
	}
}
