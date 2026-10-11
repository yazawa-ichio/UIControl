using System;
using UnityEngine;

namespace UIControl
{
	[Serializable]
	public sealed class StateDefinition
	{
		[SerializeField] private string m_Channel = "Default";
		[SerializeField] private string m_Name;
		[SerializeField] private AnimationAdapter m_Animation;

		public string Channel => string.IsNullOrEmpty(m_Channel) ? "Default" : m_Channel;
		public string Name => m_Name;
		public AnimationAdapter Animation => m_Animation;
	}
}
