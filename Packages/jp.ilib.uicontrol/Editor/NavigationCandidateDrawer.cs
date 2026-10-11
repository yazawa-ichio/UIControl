using UnityEditor;
using UnityEngine;

namespace UIControl.Editor
{
	[CustomPropertyDrawer(typeof(NavigationCandidate))]
	internal sealed class NavigationCandidateDrawer : PropertyDrawer
	{
		public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
		{
			return EditorGUIUtility.singleLineHeight * 2 + EditorGUIUtility.standardVerticalSpacing;
		}

		public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
		{
			EditorGUI.BeginProperty(position, label, property);
			SerializedProperty kind = property.FindPropertyRelative("m_Kind");
			position.height = EditorGUIUtility.singleLineHeight;
			EditorGUI.PropertyField(position, kind, label);
			position.y += position.height + EditorGUIUtility.standardVerticalSpacing;
			if (kind.hasMultipleDifferentValues)
			{
				EditorGUI.LabelField(position, "候補種別が異なります");
			}
			else
			{
				CandidateKind value = (CandidateKind)kind.enumValueIndex;
				string field = value == CandidateKind.Node ? "m_Target" : value == CandidateKind.Resolver ? "m_Resolver" : "m_Scope";
				string title = value == CandidateKind.Automatic ? "探索Scope（空欄＝現在Scope）" : value == CandidateKind.Scope ? "復元先Scope" : "対象";
				EditorGUI.PropertyField(position, property.FindPropertyRelative(field), new GUIContent(title));
			}
			EditorGUI.EndProperty();
		}
	}
}
