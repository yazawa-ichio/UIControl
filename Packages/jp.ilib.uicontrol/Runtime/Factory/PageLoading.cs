using System;
using UnityEngine;

namespace UIControl
{
	public static class PageLoading
	{
		private static IPageLoader s_DefaultLoader = new DefaultPageLoader();
		public static IPageLoader DefaultLoader
		{
			get => s_DefaultLoader;
			set => s_DefaultLoader = value ?? throw new ArgumentNullException(nameof(value));
		}

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		public static void ResetDefaultLoader()
		{
			s_DefaultLoader = new DefaultPageLoader();
		}
	}
}
