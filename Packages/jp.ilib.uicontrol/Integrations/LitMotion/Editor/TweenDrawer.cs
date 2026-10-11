using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace UIControl.LitMotion.Editor
{
	[CustomPropertyDrawer(typeof(LitMotionAnimationAdapter.Tween))]
	public sealed class TweenDrawer : PropertyDrawer
	{
		private static IEnumerable<string> Fields(SerializedProperty property)
		{
			yield return "m_Property";
			TweenProperty kind = (TweenProperty)property.FindPropertyRelative("m_Property").enumValueIndex;
			yield return kind == TweenProperty.Alpha ? "m_Group" : kind == TweenProperty.Color ? "m_Graphic" : "m_Rect";
			yield return "m_FromCurrent";
			bool current = property.FindPropertyRelative("m_FromCurrent").boolValue;
			if (kind == TweenProperty.Circle)
			{
				yield return "m_Center";
				if (!current) { yield return "m_Radius"; yield return "m_StartAngle"; }
				yield return "m_Angle";
			}
			else
			{
				if (!current) { yield return kind == TweenProperty.Alpha ? "m_FromAlpha" : kind == TweenProperty.Color ? "m_FromColor" : "m_From"; }
				yield return kind == TweenProperty.Alpha ? "m_ToAlpha" : kind == TweenProperty.Color ? "m_ToColor" : "m_To";
			}
			yield return "m_Duration";
			yield return "m_Delay";
			yield return "m_Ease";
			yield return "m_ApplyStartImmediately";
		}
		public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
		{
			float height = EditorGUIUtility.singleLineHeight;
			if (property.isExpanded)
			{
				foreach (string field in Fields(property)) { height += EditorGUI.GetPropertyHeight(property.FindPropertyRelative(field), true) + EditorGUIUtility.standardVerticalSpacing; }
			}
			return height;
		}
		public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
		{
			using (new EditorGUI.PropertyScope(position, label, property))
			{
				Rect row = position;
				row.height = EditorGUIUtility.singleLineHeight;
				property.isExpanded = EditorGUI.Foldout(row, property.isExpanded, label, true);
				if (!property.isExpanded) { return; }
				using (new EditorGUI.IndentLevelScope())
				{
					foreach (string field in Fields(property))
					{
						row.y += row.height + EditorGUIUtility.standardVerticalSpacing;
						SerializedProperty child = property.FindPropertyRelative(field);
						row.height = EditorGUI.GetPropertyHeight(child, true);
						EditorGUI.PropertyField(row, child, true);
					}
				}
			}
		}
	}
}
