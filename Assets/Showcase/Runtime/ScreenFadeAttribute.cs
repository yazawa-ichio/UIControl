using System;
using System.Threading.Tasks;
using GameStateStructure;

namespace UIControl.Showcase
{
	[AttributeUsage(AttributeTargets.Class, Inherited = false)]
	public sealed class ScreenFadeAttribute : Attribute
	{
		public string OutState { get; }
		public bool Enter { get; set; } = true;
		public bool Exit { get; set; } = true;
		public bool WhiteOnEnter { get; set; }
		public ScreenFadeAttribute(string outState = "Out") { OutState = outState; }
	}

	sealed class ScreenFadeDecorator : Decorator<ScreenFadeAttribute>
	{
		readonly ScreenFade m_Fade;
		public ScreenFadeDecorator(ScreenFade fade) { m_Fade = fade; }
		public override int Priority => 10;
		protected override Task OnPreInitialize(GameState state, ScreenFadeAttribute maker)
		{
			return maker.Enter ? m_Fade.Cover(maker.OutState, maker.WhiteOnEnter) : Task.CompletedTask;
		}
		protected override Task OnPreExit(GameState state, ScreenFadeAttribute maker)
		{
			return maker.Exit ? m_Fade.Cover(maker.OutState) : Task.CompletedTask;
		}
		protected override void OnForceRelease() { if (m_Fade != null) { m_Fade.Clear(); } }
	}
}
