using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.U2D;
using UVMBinding;

namespace UIControl.Adoption
{
	/// <summary>Bindingで得たキーとVMの取得先から、表示前にテキストとアイコンを準備する利用例。</summary>
	public sealed class LocalizedAssetsStep : PrepareStep
	{
		[SerializeField] private View m_View;
		[SerializeField] private UnityEngine.UI.Text m_Text;
		[SerializeField] private UnityEngine.UI.Image m_Icon;

		[Serializable]
		private sealed class Table { public TextEntry[] entries; }
		[Serializable]
		private sealed class TextEntry { public string key; public string text; }

		public override async Task ExecuteAsync(PrepareContext context)
		{
			context.ThrowIfEnded();
			if (m_View == null || m_View.ViewModel == null || m_Text == null || m_Icon == null)
			{
				throw new InvalidOperationException("Assign the bound View, text and icon before LocalizedAssetsStep.");
			}
			string key = m_Text.text;
			string tablePath = m_View.ViewModel.Get<string>("LocaleTable");
			string atlasPath = m_View.ViewModel.Get<string>("IconAtlas");
			string iconName = m_View.ViewModel.Get<string>("IconName");
			TextAsset source = await Load<TextAsset>(tablePath, context.Cancellation);
			context.ThrowIfEnded();
			Table table = JsonUtility.FromJson<Table>(source.text);
			TextEntry entry = Array.Find(table?.entries ?? Array.Empty<TextEntry>(), item => item != null && item.key == key);
			if (entry == null) { throw new InvalidOperationException("Localized key was not found: " + key); }
			SpriteAtlas atlas = await Load<SpriteAtlas>(atlasPath, context.Cancellation);
			context.ThrowIfEnded();
			Sprite sprite = atlas.GetSprite(iconName);
			if (sprite == null) { throw new InvalidOperationException("Atlas sprite was not found: " + iconName); }
			// GetSpriteが生成したSpriteだけを所有する。共有Resources AssetはUnloadしない。
			context.OnRelease(() => { if (sprite != null) { Destroy(sprite); } });
			context.ThrowIfEnded();
			m_Text.text = entry.text;
			m_Icon.sprite = sprite;
		}

		private static async Task<T> Load<T>(string path, CancellationToken cancellation) where T : UnityEngine.Object
		{
			cancellation.ThrowIfCancellationRequested();
			if (string.IsNullOrEmpty(path)) { throw new InvalidOperationException("An asset path is required."); }
			ResourceRequest request = Resources.LoadAsync<T>(path);
			TaskCompletionSource<bool> completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
			void Complete(AsyncOperation operation) { completion.TrySetResult(true); }
			request.completed += Complete;
			try
			{
				if (request.isDone) { completion.TrySetResult(true); }
				using (cancellation.Register(() => completion.TrySetCanceled())) { await completion.Task; }
				cancellation.ThrowIfCancellationRequested();
				return request.asset as T ?? throw new InvalidOperationException("Asset was not found: " + path);
			}
			finally { request.completed -= Complete; }
		}
	}
}
