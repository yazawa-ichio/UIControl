using System;
using System.Reflection;
using UVMBinding.Core;

namespace UIControl.UVMBinding
{
	[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
	public sealed class UIPageFromVMAttribute : Attribute
	{
		public string Path { get; }
		public UIPageFromVMAttribute(string path)
		{
			if (string.IsNullOrEmpty(path)) { throw new ArgumentException("Path is required.", nameof(path)); }
			Path = path;
		}
	}

	public sealed class ViewModelUIParameter : UIParameter
	{
		public IViewModel Model { get; }
		internal ViewModelUIParameter(string path, IViewModel model, IPageLoader loader)
			: base(path, (page, context) => InitializeModel(page, context, model), loader)
		{
			Model = model ?? throw new ArgumentNullException(nameof(model));
		}

		private static void InitializeModel(Page page, PrepareContext context, IViewModel model)
		{
			context.ThrowIfEnded();
			ViewBindStep step = page.GetComponent<ViewBindStep>();
			bool registered = false;
			foreach (PrepareStep item in page.Preparation) { if (step != null && item == step) { registered = true; break; } }
			if (!registered) { throw new InvalidOperationException("Place ViewBindStep on the Page root and register it in Preparation."); }
			context.OnRelease(() => { if (step != null) { step.Model = null; } });
			step.Model = model;
		}
	}

	public static class ViewModelUIParameterExtensions
	{
		public static ViewModelUIParameter CreateUIParameter(this IViewModel model, string path, IPageLoader loader = null)
		{
			if (model == null) { throw new ArgumentNullException(nameof(model)); }
			return new ViewModelUIParameter(path, model, loader);
		}

		public static ViewModelUIParameter CreateUIParameter(this IViewModel model, IPageLoader loader = null)
		{
			if (model == null) { throw new ArgumentNullException(nameof(model)); }
			UIPageFromVMAttribute attribute = model.GetType().GetCustomAttribute<UIPageFromVMAttribute>(false);
			if (attribute == null) { throw new InvalidOperationException("Add UIPageFromVM to the ViewModel type or pass an explicit Path."); }
			return model.CreateUIParameter(attribute.Path, loader);
		}
	}
}
