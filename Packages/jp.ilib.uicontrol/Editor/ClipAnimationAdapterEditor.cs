using UnityEditor;
using UnityEngine;

namespace UIControl.Editor
{
	[CustomEditor(typeof(ClipAnimationAdapter))]
	public sealed class ClipAnimationAdapterEditor : UnityEditor.Editor
	{
		public override void OnInspectorGUI()
		{
			DrawDefaultInspector();
			var adapter = (ClipAnimationAdapter)target;
			if (adapter.Clip == null || adapter.Clip.legacy) { return; }
			GameObject root = adapter.Root != null ? adapter.Root : adapter.gameObject;
			if (root.GetComponent<Animator>() == null)
			{
				EditorGUILayout.HelpBox("通常ClipはPlayerでの再生にAnimatorが必要です。再生対象のPrefabへ明示配置してください。Animator Controllerは不要です。", MessageType.Error);
			}
		}
	}
}
