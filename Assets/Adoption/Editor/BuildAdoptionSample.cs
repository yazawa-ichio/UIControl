using System.IO;
using UIControl.InputSystem;
using UIControl.UVMBinding;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using UVMBinding;
using UVMBinding.Binders;
using UVMBinding.Events;

using UIControl.LitMotion;

namespace UIControl.Adoption
{
	public static class BuildAdoptionSample
	{
		private const string Folder = "Assets/Adoption/Resources/Adoption/";
		[MenuItem("UIControl/Samples/Create Queued Dialogs")]
		public static void Create()
		{
			Directory.CreateDirectory(Folder); Directory.CreateDirectory("Assets/Adoption/Scenes"); AssetDatabase.Refresh();
			GameObject row = CreateRow();
			GameObject rowPrefab = Save(row, "RewardRow");
			GameObject current = Save(Dialog(rowPrefab), "QueueDialog");
			GameObject host = Host(current);
			GameObject hostPrefab = Save(host, "QueuedDialogs");
			UnityEngine.SceneManagement.Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
			PrefabUtility.InstantiatePrefab(hostPrefab);
			EditorSceneManager.SaveScene(scene, "Assets/Adoption/Scenes/QueuedDialogs.unity");
			AssetDatabase.SaveAssets(); Debug.Log("UICONTROL_ADOPTION_SAMPLE_READY");
		}
		private static GameObject Save(GameObject root, string name)
		{
			try { return PrefabUtility.SaveAsPrefabAsset(root, Folder + name + ".prefab"); }
			finally { UnityEngine.Object.DestroyImmediate(root); }
		}
		private static GameObject Child(string name, Transform parent, Vector2 size, Vector2 position)
		{
			GameObject child = new GameObject(name, typeof(RectTransform)); child.transform.SetParent(parent, false);
			RectTransform rect = (RectTransform)child.transform; rect.sizeDelta = size; rect.anchoredPosition = position;
			return child;
		}
		private static void Stretch(GameObject root)
		{
			RectTransform rect = (RectTransform)root.transform; rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero;
		}
		private static Text Label(string name, Transform parent, string text, int size, Vector2 dimensions, Vector2 position)
		{
			Text label = Child(name, parent, dimensions, position).AddComponent<Text>();
			label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); label.fontSize = size; label.text = text; label.color = new Color(0.9f, 0.94f, 1);
			label.alignment = TextAnchor.MiddleCenter; label.raycastTarget = false; return label;
		}
		private static Button Button(string name, Transform parent, string text, Vector2 position)
		{
			GameObject root = Child(name, parent, new Vector2(230, 58), position);
			Image image = root.AddComponent<Image>(); image.color = name == "Accept" || name == "Launch" ? new Color(0.12f, 0.49f, 0.58f) : new Color(0.2f, 0.26f, 0.35f);
			Button button = root.AddComponent<Button>(); button.targetGraphic = image; button.transition = Selectable.Transition.None;
			Label("Label", root.transform, text, 19, new Vector2(220, 50), Vector2.zero);
			root.AddComponent<CanvasGroup>();
			NamedStatePlayer player = root.AddComponent<NamedStatePlayer>();
			States(player, root, "Selection", new[] { "Normal", "Highlighted", "Selected", "Pressed", "Disabled" }, new[] { 1f, 1.03f, 1.05f, 0.96f, 1f }, new[] { 1f, 1f, 1f, 1f, 0.45f });
			Set(root.AddComponent<SelectableStateDriver>(), "m_Player", player);
			root.AddComponent<NavigationNode>();
			return button;
		}
		private static GameObject CreateRow()
		{
			GameObject row = Child("RewardRow", null, new Vector2(185, 72), Vector2.zero); row.SetActive(false);
			row.AddComponent<LayoutElement>();
			row.AddComponent<View>(); GameObject visual = Child("Visual", row.transform, new Vector2(185, 72), Vector2.zero);
			visual.AddComponent<Image>().color = new Color(0.14f, 0.2f, 0.29f); visual.AddComponent<CanvasGroup>();
			Label("Label", visual.transform, "Reward", 18, new Vector2(180, 65), Vector2.zero).gameObject.AddComponent<TextBinder>().ChangePath("Label");
			NamedStatePlayer player = row.AddComponent<NamedStatePlayer>();
			States(player, visual, "Item", new[] { "Hidden", "Show", "Hide" }, new[] { 0.95f, 1f, 0.95f }, new[] { 0f, 1f, 0f });
			Set(row.AddComponent<ListItemStateDriver>(), "m_Player", player); row.SetActive(true); return row;
		}
		private static GameObject Dialog(GameObject row)
		{
			GameObject root = Child("Dialog", null, Vector2.zero, Vector2.zero); root.SetActive(false); Stretch(root);
			root.AddComponent<Canvas>(); CanvasGroup input = root.AddComponent<CanvasGroup>(); root.AddComponent<GraphicRaycaster>();
			View view = root.AddComponent<View>();
			GameObject mask = Child("Mask", root.transform, Vector2.zero, Vector2.zero); Stretch(mask); mask.AddComponent<Image>().color = new Color(0, 0, 0, 0.7f);
			GameObject panel = Child("Panel", root.transform, new Vector2(700, 570), Vector2.zero); panel.AddComponent<CanvasGroup>(); panel.AddComponent<Image>().color = new Color(0.07f, 0.1f, 0.16f);
			Label("Title", panel.transform, "REWARD DELIVERY", 27, new Vector2(650, 55), new Vector2(0, 230)).gameObject.AddComponent<TextBinder>().ChangePath("Title");
			Label("Message", panel.transform, "", 17, new Vector2(610, 50), new Vector2(0, 175)).gameObject.AddComponent<TextBinder>().ChangePath("Message");
			GameObject list = Child("Rewards", panel.transform, new Vector2(589, 260), new Vector2(0, 0));
			GridLayoutGroup layout = list.AddComponent<GridLayoutGroup>(); layout.cellSize = new Vector2(185, 72); layout.spacing = new Vector2(17, 16); layout.constraint = GridLayoutGroup.Constraint.FixedColumnCount; layout.constraintCount = 3;
			AnimatedCollectionBinder collection = list.AddComponent<AnimatedCollectionBinder>(); Set(collection, "m_Preafb", row); collection.ChangePath("Items");
			Button accept = Button("Accept", panel.transform, "CONFIRM", new Vector2(145, -215));
			Button dismiss = Button("Dismiss", panel.transform, "CANCEL", new Vector2(-145, -215));
			Event(accept, "Accept"); Event(dismiss, "Dismiss");
			dismiss.gameObject.AddComponent<BackButton>();
			NamedStatePlayer player = root.AddComponent<NamedStatePlayer>();
			States(player, panel, "Page", new[] { "Open", "Close", "Hidden", "Covered" }, new[] { 1f, 0.94f, 0.94f, 1f }, new[] { 1f, 0f, 0f, 0.8f });
			Page screen = root.AddComponent<Page>(); FocusScope scope = root.AddComponent<FocusScope>();
			Set(scope, "m_Initial", accept);
			SerializedObject focusData = new SerializedObject(scope); Array(focusData, "m_Fallbacks", dismiss); focusData.ApplyModifiedPropertiesWithoutUndo();
			Set(screen, "m_Focus", scope); Set(screen, "m_InputGroup", input);
			PageStateDriver driver = root.AddComponent<PageStateDriver>(); Set(driver, "m_Player", player); Set(screen, "m_Visual", driver);
			ViewBindStep bind = root.AddComponent<ViewBindStep>(); Set(bind, "m_View", view);
			SerializedObject data = new SerializedObject(screen); Array(data, "m_Preparation", bind); data.ApplyModifiedPropertiesWithoutUndo();
			root.SetActive(true); return root;
		}
		private static GameObject Host(GameObject dialog)
		{
			GameObject root = Child("QueuedDialogs", null, Vector2.zero, Vector2.zero); root.SetActive(false);
			root.AddComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay; root.AddComponent<GraphicRaycaster>();
			CanvasScaler scaler = root.AddComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(1280, 720);
			GameObject home = Child("Home", root.transform, Vector2.zero, Vector2.zero); Stretch(home); home.AddComponent<CanvasGroup>(); home.AddComponent<Image>().color = new Color(0.035f, 0.055f, 0.095f);
			Label("Title", home.transform, "UIControl  /  QUEUED DIALOGS", 36, new Vector2(1100, 70), new Vector2(0, 200));
			Label("Hint", home.transform, "Three requests. Nine bound rewards each. Prefab-authored transitions.", 21, new Vector2(1100, 55), new Vector2(0, 125));
			Button launch = Button("Launch", home.transform, "START REWARD FLOW", new Vector2(0, 15));
			Text result = Label("Result", home.transform, "Mouse / arrows / gamepad. Escape or Cancel returns through the Cancel button.", 18, new Vector2(1120, 80), new Vector2(0, -120));
			FocusScope focus = home.AddComponent<FocusScope>(); Set(focus, "m_Initial", launch);
			GameObject layer = Child("Dialog", root.transform, Vector2.zero, Vector2.zero); Stretch(layer);
			UIStack navigator = root.AddComponent<UIStack>();
			Set(navigator, "m_DisplayRoot", layer.transform);
			QueuedDialogsSample sample = root.AddComponent<QueuedDialogsSample>(); Set(sample, "m_Navigator", navigator); Set(sample, "m_DialogPrefab", dialog); Set(sample, "m_HomeFocus", focus); Set(sample, "m_Result", result);
			UnityEventTools.AddPersistentListener(launch.onClick, sample.StartFlow);
			GameObject events = Child("Events", root.transform, Vector2.zero, Vector2.zero); events.AddComponent<EventSystem>();
			InputSystemUIInputModule module = events.AddComponent<InputSystemUIInputModule>(); module.AssignDefaultActions();
			events.AddComponent<InputSystemFocusInput>();
			root.SetActive(true); return root;
		}
		private static void States(NamedStatePlayer player, GameObject target, string channel, string[] names, float[] scales, float[] alphas)
		{
			SerializedObject data = new SerializedObject(player); SerializedProperty states = data.FindProperty("m_States"); states.arraySize = names.Length;
			for (int i = 0; i < names.Length; i++)
			{
				GameObject host = Child(channel + " " + names[i], player.transform, Vector2.zero, Vector2.zero);
				AnimationAdapter adapter;
				if (channel == "Page")
				{
					if (target.GetComponent<Animator>() == null) { target.AddComponent<Animator>(); }
					Directory.CreateDirectory("Assets/Adoption/Animations");
					string path = "Assets/Adoption/Animations/" + names[i] + ".anim";
					AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
					if (clip == null) { clip = new AnimationClip { name = names[i], frameRate = 60 }; AssetDatabase.CreateAsset(clip, path); }
					float startScale = names[i] == "Open" ? 0.94f : 1f;
					float startAlpha = names[i] == "Open" ? 0f : 1f;
					foreach (string axis in new[] { "x", "y", "z" })
					{
						AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("", typeof(Transform), "m_LocalScale." + axis), AnimationCurve.Linear(0, startScale, 0.12f, scales[i]));
					}
					AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("", typeof(CanvasGroup), "m_Alpha"), AnimationCurve.Linear(0, startAlpha, 0.12f, alphas[i]));
					ClipAnimationAdapter clipAdapter = host.AddComponent<ClipAnimationAdapter>();
					clipAdapter.Clip = clip; clipAdapter.Root = target; adapter = clipAdapter;
				}
				else
				{
					LitMotionAnimationAdapter tween = host.AddComponent<LitMotionAnimationAdapter>();
					SerializedObject backend = new SerializedObject(tween);
					SerializedProperty items = backend.FindProperty("m_Tweens"); items.arraySize = 2;
					for (int item = 0; item < 2; item++)
					{
						SerializedProperty step = items.GetArrayElementAtIndex(item);
						step.FindPropertyRelative("m_Property").enumValueIndex = (int)(item == 0 ? TweenProperty.LocalScale : TweenProperty.Alpha);
						step.FindPropertyRelative("m_Rect").objectReferenceValue = target.transform;
						step.FindPropertyRelative("m_Group").objectReferenceValue = target.GetComponent<CanvasGroup>();
						step.FindPropertyRelative("m_To").vector3Value = Vector3.one * scales[i];
						step.FindPropertyRelative("m_ToAlpha").floatValue = alphas[i];
						step.FindPropertyRelative("m_FromCurrent").boolValue = true;
						step.FindPropertyRelative("m_Duration").floatValue = 0.12f;
						step.FindPropertyRelative("m_Ease").enumValueIndex = 0;
					}
					backend.ApplyModifiedPropertiesWithoutUndo(); adapter = tween;
				}
				SerializedProperty state = states.GetArrayElementAtIndex(i); state.FindPropertyRelative("m_Name").stringValue = names[i]; state.FindPropertyRelative("m_Channel").stringValue = channel; state.FindPropertyRelative("m_Animation").objectReferenceValue = adapter;
			}
			data.ApplyModifiedPropertiesWithoutUndo();
		}
		private static void Event(Button button, string name)
		{
			EventButton binding = button.gameObject.AddComponent<EventButton>(); SerializedObject data = new SerializedObject(binding); data.FindProperty("m_EventName").stringValue = name; data.ApplyModifiedPropertiesWithoutUndo();
		}
		private static void Set(UnityEngine.Object target, string name, UnityEngine.Object value)
		{
			SerializedObject data = new SerializedObject(target); data.FindProperty(name).objectReferenceValue = value; data.ApplyModifiedPropertiesWithoutUndo();
		}
		private static void Array(SerializedObject target, string name, UnityEngine.Object value)
		{
			SerializedProperty array = target.FindProperty(name); array.arraySize = 1; array.GetArrayElementAtIndex(0).objectReferenceValue = value;
		}

	}
}
