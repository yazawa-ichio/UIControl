using System;
using System.IO;
using System.Linq;
using GameStateStructure;
using TMPro;
using UIControl.InputSystem;
using UIControl.LitMotion;
using UIControl.UVMBinding;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using UVMBinding;
using UVMBinding.Binders;
using UVMBinding.Events;

namespace UIControl.Showcase
{
	// Showcase専用のPrefab / Scene生成。Unity APIだけで組み立て、外部の素材を使わない。
	public static class BuildShowcase
	{
		const string Folder = "Assets/Showcase";
		public const string ScenePath = Folder + "/Scenes/Showcase.unity";
		static TMP_FontAsset s_Font;
		static readonly Color Ink = new Color(0.045f, 0.065f, 0.11f);
		static readonly Color PanelColor = new Color(0.085f, 0.12f, 0.19f);
		static readonly Color Teal = new Color(0.05f, 0.41f, 0.43f);

		[MenuItem("UIControl/Samples/Create Showcase")]
		public static void Create()
		{
			if (EditorApplication.isPlaying) { throw new InvalidOperationException("Stop Play before regenerating sample Prefabs."); }
			var active = EditorSceneManager.GetActiveScene();
			if (active.isDirty) { throw new InvalidOperationException("Save the active scene before generating this sample."); }
			foreach (var part in new[] { "Prefabs", "Animations", "Scenes" }) { Directory.CreateDirectory(Folder + "/" + part); }
			AssetDatabase.Refresh();
			CreateFont();
			var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
			GameObject row = Save(Row(), "ActionRow");
			GameObject[] pages = new[]
			{
				Save(PagePrefab("MenuPage", row), "MenuPage"), Save(PagePrefab("HUDPage", row), "HUDPage"),
				Save(PagePrefab("ListPage", row), "ListPage"), Save(PagePrefab("SettingsPage", row), "SettingsPage"),
				Save(PagePrefab("DialogPage", row), "DialogPage"), Save(PagePrefab("CardsPage", row), "CardsPage")
			};
			GameObject host = Save(Host(pages), "Showcase");
			PrefabUtility.InstantiatePrefab(host);
			EditorSceneManager.SaveScene(scene, ScenePath);
			AssetDatabase.SaveAssets();
			Debug.Log("SHOWCASE_READY " + ScenePath);
		}

		static void CreateFont()
		{
			s_Font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset");
			if (s_Font == null)
			{
				TMP_PackageResourceImporter.ImportResources(true, false, false);
				throw new InvalidOperationException("TMP Essentials import started. Run Create again after the import completes.");
			}
		}

		static GameObject Save(GameObject root, string name)
		{
			root.name = name;
			try { return PrefabUtility.SaveAsPrefabAsset(root, Folder + "/Prefabs/" + name + ".prefab"); }
			finally { UnityEngine.Object.DestroyImmediate(root); }
		}
		static GameObject Rect(string name, Transform parent, float width, float height, float x = 0, float y = 0)
		{
			var obj = new GameObject(name, typeof(RectTransform));
			obj.transform.SetParent(parent, false);
			var rect = (RectTransform)obj.transform;
			rect.sizeDelta = new Vector2(width, height);
			rect.anchoredPosition = new Vector2(x, y);
			return obj;
		}
		static void Stretch(GameObject obj)
		{
			var rect = (RectTransform)obj.transform;
			rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
			rect.offsetMin = rect.offsetMax = Vector2.zero;
		}
		static UnityEngine.UI.Image ColorPanel(GameObject obj, Color color, bool raycast = true)
		{
			var image = obj.AddComponent<UnityEngine.UI.Image>(); image.color = color; image.raycastTarget = raycast; return image;
		}
		static TMP_Text Text(string name, Transform parent, string value, int size, float width, float height, float x, float y, TextAlignmentOptions alignment = TextAlignmentOptions.MidlineLeft)
		{
			var text = Rect(name, parent, width, height, x, y).AddComponent<TextMeshProUGUI>();
			text.font = s_Font; text.text = value; text.fontSize = size; text.color = new Color(0.92f, 0.95f, 0.99f);
			text.alignment = alignment; text.raycastTarget = false; text.textWrappingMode = TextWrappingModes.Normal;
			return text;
		}
		static TMP_Text BoundText(string path, Transform parent, int size, float width, float height, float x, float y)
		{
			var label = Text(path, parent, path, size, width, height, x, y);
			label.gameObject.AddComponent<TextMeshProBinder>().ChangePath(path);
			return label;
		}

		static UnityEngine.UI.Button Button(string name, Transform parent, string label, float width, float height, float x, float y)
		{
			var obj = Rect(name, parent, width, height, x, y);
			var image = ColorPanel(obj, Teal);
			var button = obj.AddComponent<UnityEngine.UI.Button>(); button.targetGraphic = image;
			var colors = button.colors;
			colors.normalColor = Color.white; colors.highlightedColor = new Color(1.15f, 1.2f, 1.2f); colors.selectedColor = new Color(1.35f, 1.4f, 1.4f);
			colors.pressedColor = new Color(0.65f, 0.75f, 0.8f); colors.disabledColor = new Color(0.4f, 0.45f, 0.5f); button.colors = colors;
			Text("Label", obj.transform, label, 18, width - 24, height - 10, 0, 0, TextAlignmentOptions.Center);
			var node = obj.AddComponent<NavigationNode>();
			foreach (var direction in new[] { "m_Up", "m_Down", "m_Left", "m_Right" }) { Candidates(node, direction); }
			ButtonAnimation(obj);
			return button;
		}
		static void ButtonAnimation(GameObject obj)
		{
			var player = obj.AddComponent<NamedStatePlayer>();
			string[] names = { "Normal", "Highlighted", "Selected", "Pressed", "Disabled" };
			float[] scale = { 1f, 1.02f, 1.035f, 0.96f, 1f };
			var data = new SerializedObject(player); var states = data.FindProperty("m_States"); states.arraySize = names.Length;
			for (int i = 0; i < names.Length; i++)
			{
				var host = Rect(names[i], obj.transform, 0, 0);
				var adapter = host.AddComponent<LitMotionAnimationAdapter>();
				var serialized = new SerializedObject(adapter); var items = serialized.FindProperty("m_Tweens"); items.arraySize = 1;
				var tween = items.GetArrayElementAtIndex(0);
				tween.FindPropertyRelative("m_Property").enumValueIndex = (int)TweenProperty.LocalScale;
				tween.FindPropertyRelative("m_Rect").objectReferenceValue = obj.transform;
				tween.FindPropertyRelative("m_To").vector3Value = Vector3.one * scale[i];
				tween.FindPropertyRelative("m_FromCurrent").boolValue = true;
				tween.FindPropertyRelative("m_Duration").floatValue = 0.09f;
				serialized.ApplyModifiedPropertiesWithoutUndo();
				var state = states.GetArrayElementAtIndex(i);
				state.FindPropertyRelative("m_Channel").stringValue = "Selection";
				state.FindPropertyRelative("m_Name").stringValue = names[i];
				state.FindPropertyRelative("m_Animation").objectReferenceValue = adapter;
			}
			data.ApplyModifiedPropertiesWithoutUndo();
			Set(obj.AddComponent<SelectableStateDriver>(), "m_Player", player);
		}
		static void PageAnimation(Page page, GameObject panel)
		{
			if (panel.GetComponent<Animator>() == null) { panel.AddComponent<Animator>(); }
			var player = page.gameObject.AddComponent<NamedStatePlayer>();
			var serialized = new SerializedObject(player); var states = serialized.FindProperty("m_States"); states.arraySize = 4;
			string[] names = { "Open", "Close", "Covered", "Hidden" };
			for (int i = 0; i < names.Length; i++)
			{
				string path = Folder + "/Animations/" + names[i] + ".anim";
				var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
				if (clip == null) { clip = new AnimationClip { name = names[i], frameRate = 60 }; AssetDatabase.CreateAsset(clip, path); }
				float from = i == 0 ? 0 : 1;
				float to = i == 1 || i == 3 ? 0 : i == 2 ? 0.65f : 1;
				AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("", typeof(CanvasGroup), "m_Alpha"), AnimationCurve.EaseInOut(0, from, 0.14f, to));
				var adapter = Rect("Page " + names[i], page.transform, 0, 0).AddComponent<ClipAnimationAdapter>();
				adapter.Clip = clip; adapter.Root = panel;
				var state = states.GetArrayElementAtIndex(i);
				state.FindPropertyRelative("m_Channel").stringValue = "Page";
				state.FindPropertyRelative("m_Name").stringValue = names[i];
				state.FindPropertyRelative("m_Animation").objectReferenceValue = adapter;
			}
			serialized.ApplyModifiedPropertiesWithoutUndo();
			var driver = page.gameObject.AddComponent<PageStateDriver>(); Set(driver, "m_Player", player); Set(page, "m_Visual", driver);
		}

		static GameObject Row()
		{
			var button = Button("ActionRow", null, "ACTION", 272, 56, 0, 0);
			button.gameObject.SetActive(false);
			button.gameObject.AddComponent<View>();
			button.GetComponentInChildren<TMP_Text>().gameObject.AddComponent<TextMeshProBinder>().ChangePath("Label");
			button.gameObject.AddComponent<InteractableBinder>().ChangePath("Enabled");
			BindEvent(button, "Click");
			var layout = button.gameObject.AddComponent<LayoutElement>(); layout.preferredHeight = 56; layout.minHeight = 56;
			button.gameObject.SetActive(true);
			return button.gameObject;
		}
		static void BindEvent(UnityEngine.UI.Button button, string name)
		{
			var data = new SerializedObject(button.gameObject.AddComponent<EventButton>());
			data.FindProperty("m_EventName").stringValue = name; data.ApplyModifiedPropertiesWithoutUndo();
		}
		static void Input<T>(UnityEngine.UI.Button button, int priority = 0) where T : InputButton
		{
			var data = new SerializedObject(button.gameObject.AddComponent<T>());
			data.FindProperty("m_Priority").intValue = priority; data.ApplyModifiedPropertiesWithoutUndo();
		}
		static void Collection(GameObject root, GameObject row, string path)
		{
			var binder = root.AddComponent<CollectionBinder>(); Set(binder, "m_Preafb", row); binder.ChangePath(path);
		}
		static void Grid(GameObject root, int columns, float width, float height = 56)
		{
			var grid = root.AddComponent<GridLayoutGroup>(); grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount; grid.constraintCount = columns;
			grid.cellSize = new Vector2(width, height); grid.spacing = new Vector2(12, 14); grid.childAlignment = TextAnchor.UpperCenter;
		}

		static GameObject PagePrefab(string kind, GameObject row)
		{
			var root = Rect(kind, null, 0, 0); root.SetActive(false); Stretch(root);
			var input = root.AddComponent<CanvasGroup>();
			var page = root.AddComponent<Page>();
			var scope = root.AddComponent<FocusScope>();
			// Header 100 > Page 0、Dialog 200。登録順には依存させない。
			var scopeData = new SerializedObject(scope); scopeData.FindProperty("m_Priority").intValue = kind == "DialogPage" ? 200 : 0; scopeData.ApplyModifiedPropertiesWithoutUndo();
			var view = root.AddComponent<View>();
			var bind = root.AddComponent<ViewBindStep>(); Set(bind, "m_View", view);
			Set(page, "m_InputGroup", input); Set(page, "m_Focus", scope); Objects(page, "m_Preparation", bind);
			bool dialog = kind == "DialogPage";
			if (dialog) { var mask = Rect("Dim", root.transform, 0, 0); Stretch(mask); ColorPanel(mask, new Color(0, 0, 0, 0.65f)); }
			float width = dialog ? 810 : 1020;
			float height = dialog ? 580 : 720;
			var panel = Rect("Panel", root.transform, width, height);
			panel.AddComponent<CanvasGroup>(); ColorPanel(panel, kind == "HUDPage" ? new Color(0.07f, 0.16f, 0.2f) : PanelColor);
			ColorPanel(Rect("Accent", panel.transform, width, 5, 0, height / 2 - 2.5f), new Color(0.26f, 0.86f, 0.78f), false);
			BoundText("Title", panel.transform, 30, width - 80, 55, 0, height / 2 - 56);
			BoundText("Description", panel.transform, 17, width - 80, 84, 0, height / 2 - 124);
			var back = Button("Back", panel.transform, "BACK", 230, 52, -width / 2 + 155, -height / 2 + 46);
			back.GetComponentInChildren<TMP_Text>().gameObject.AddComponent<TextMeshProBinder>().ChangePath("BackLabel");
			back.gameObject.AddComponent<ActiveBinder>().ChangePath("BackVisible"); BindEvent(back, "Back"); Input<BackButton>(back, dialog ? 200 : 0);
			if (kind == "ListPage")
			{
				CreateList(panel.transform, row);
				BoundText("Detail", panel.transform, 20, 430, 180, 246, 52);
				var actions = Rect("Actions", panel.transform, 436, 225, 246, -169); Grid(actions, 2, 212); Collection(actions, row, "Actions");
			}
			else if (kind == "SettingsPage")
			{
				CreateTabs(panel.transform, scope);
				BoundText("Detail", panel.transform, 18, 660, 64, 120, -294);
			}
			else if (kind == "CardsPage")
			{
				var cards = Rect("Cards", panel.transform, 580, 330, -180, -35); Grid(cards, 3, 178, 86); Collection(cards, row, "Items");
				BoundText("Detail", panel.transform, 19, 275, 170, 320, 40);
				var actions = Rect("Actions", panel.transform, 278, 160, 320, -145); Grid(actions, 1, 278); Collection(actions, row, "Actions");
			}
			else
			{
				float y = dialog ? -8 : -24;
				var actions = Rect("Actions", panel.transform, width - 80, dialog ? 180 : 340, 0, y);
				Grid(actions, dialog ? 2 : 3, dialog ? 350 : 300); Collection(actions, row, "Actions");
				BoundText("Detail", panel.transform, 17, width - 100, dialog ? 76 : 60, 0, dialog ? -140 : -249);
				if (dialog) { CreateInput(panel.transform); }
			}
			back.transform.SetAsLastSibling();
			if (kind != "HUDPage") { PageAnimation(page, panel); }
			root.SetActive(true);
			return root;
		}

		static void CreateList(Transform panel, GameObject row)
		{
			var host = Rect("List", panel, 458, 470, -240, -61);
			ColorPanel(host, Ink);
			var scroll = host.AddComponent<ScrollRect>(); scroll.horizontal = false; scroll.vertical = true; scroll.movementType = ScrollRect.MovementType.Clamped;
			var viewport = Rect("Viewport", host.transform, 0, 0); Stretch(viewport); viewport.AddComponent<RectMask2D>();
			var content = Rect("Content", viewport.transform, 0, 0);
			var rect = (RectTransform)content.transform; rect.anchorMin = new Vector2(0, 1); rect.anchorMax = Vector2.one; rect.pivot = new Vector2(0.5f, 1); rect.sizeDelta = new Vector2(-20, 0);
			var layout = content.AddComponent<VerticalLayoutGroup>(); layout.spacing = 10; layout.padding = new RectOffset(8, 8, 8, 8); layout.childControlWidth = layout.childControlHeight = true; layout.childForceExpandHeight = false;
			var fit = content.AddComponent<ContentSizeFitter>(); fit.horizontalFit = ContentSizeFitter.FitMode.Unconstrained; fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
			scroll.content = rect; scroll.viewport = (RectTransform)viewport.transform; scroll.scrollSensitivity = 32;
			host.AddComponent<SelectionScroll>();
			Collection(content, row, "Items");
		}

		static void CreateTabs(Transform panel, FocusScope anchors)
		{
			var host = Rect("Tabs", panel, 940, 400, 0, -40);
			var tabs = host.AddComponent<TabController>(); var navigation = host.AddComponent<TabShortcuts>(); Set(navigation, "m_Tabs", tabs);
			string[] labels = { "AUDIO", "DISPLAY", "CONTROLS", "ACCOUNT" };
			string[][] events = { new[] { "Volume", "Defaults" }, new[] { "Mirror", "Quality", "Help" }, new[] { "Rebind", "Help" }, new[] { "Rename", "Help" } };
			var data = new SerializedObject(tabs); var definitions = data.FindProperty("m_Tabs"); definitions.arraySize = 4;
			var anchorNodes = new NavigationNode[4]; var scopes = new FocusScope[4];
			for (int i = 0; i < 4; i++)
			{
				var button = Button(labels[i], host.transform, labels[i], 220, 54, -348 + i * 232, 175);
				UnityEventTools.AddIntPersistentListener(button.onClick, tabs.Select, i);
				anchorNodes[i] = button.GetComponent<NavigationNode>();
				var content = Rect("Content " + labels[i], host.transform, 920, 290, 0, -20);
				scopes[i] = content.AddComponent<FocusScope>();
				Text("Section", content.transform, labels[i] + " / placeholder values", 23, 840, 50, 0, 108);
				NavigationNode first = null;
				for (int j = 0; j < events[i].Length; j++)
				{
					var item = Button(events[i][j], content.transform, events[i][j].ToUpperInvariant(), 375, 62, -202 + (j % 2) * 404, 32 - (j / 2) * 86);
					BindEvent(item, events[i][j]);
					var node = item.GetComponent<NavigationNode>(); first = first != null ? first : node;
					Candidates(node, "m_Up", anchorNodes[i]);
				}
				Set(scopes[i], "m_Initial", first != null ? first.GetComponent<UnityEngine.UI.Selectable>() : null);
				var definition = definitions.GetArrayElementAtIndex(i);
				definition.FindPropertyRelative("m_Anchor").objectReferenceValue = anchorNodes[i].Selectable; definition.FindPropertyRelative("m_Content").objectReferenceValue = scopes[i];
				Candidates(anchorNodes[i], "m_Down", scope: scopes[i]);
				content.SetActive(i == 0);
			}
			data.ApplyModifiedPropertiesWithoutUndo();
			for (int i = 0; i < 4; i++)
			{
				if (i > 0) { Candidates(anchorNodes[i], "m_Left", anchorNodes[i - 1]); }
				if (i < 3) { Candidates(anchorNodes[i], "m_Right", anchorNodes[i + 1]); }
			}
			Set(anchors, "m_Initial", anchorNodes[0].Selectable);
			var previous = Button("PreviousTab", panel, "< TAB  Q / LB", 200, 48, 130, -220);
			var next = Button("NextTab", panel, "TAB >  E / RB", 200, 48, 350, -220);
			UnityEventTools.AddPersistentListener(previous.onClick, navigation.Previous); UnityEventTools.AddPersistentListener(next.onClick, navigation.Next);
			Input<PreviousButton>(previous); Input<NextButton>(next);
			// 固定候補 2 件 + Automatic。タブが無効・非表示でも次候補へ進める。
			Candidates(previous.GetComponent<NavigationNode>(), "m_Up", anchorNodes[1], anchorNodes[2]);
		}

		static void CreateInput(Transform panel)
		{
			var obj = Rect("NameInput", panel, 510, 48, 0, -198); ColorPanel(obj, new Color(0.15f, 0.21f, 0.29f));
			obj.AddComponent<ActiveBinder>().ChangePath("InputVisible");
			var input = obj.AddComponent<TMP_InputField>();
			var viewport = Rect("Text Area", obj.transform, 480, 42); viewport.AddComponent<RectMask2D>();
			var value = Text("Text", viewport.transform, "", 20, 475, 38, 0, 0);
			input.textViewport = (RectTransform)viewport.transform; input.textComponent = (TextMeshProUGUI)value; input.fontAsset = s_Font;
			input.text = ""; input.targetGraphic = obj.GetComponent<UnityEngine.UI.Image>();
			var placeholder = Text("Placeholder", viewport.transform, "Player name", 18, 475, 38, 0, 0); placeholder.color = new Color(0.65f, 0.72f, 0.8f); input.placeholder = placeholder;
			var node = obj.AddComponent<NavigationNode>(); foreach (var direction in new[] { "m_Up", "m_Down", "m_Left", "m_Right" }) { Candidates(node, direction); }
		}

		static GameObject Host(GameObject[] prefabs)
		{
			var root = Rect("Showcase", null, 0, 0); root.SetActive(false);
			root.AddComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay; root.AddComponent<GraphicRaycaster>();
			var scaler = root.AddComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(1440, 900); scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
			var background = Rect("Background", root.transform, 0, 0); Stretch(background); ColorPanel(background, Ink, false);
			var events = Rect("Events", root.transform, 0, 0); events.AddComponent<EventSystem>();
			var module = events.AddComponent<InputSystemUIInputModule>(); module.AssignDefaultActions();
			var camera = new GameObject("Camera", typeof(Camera));
			camera.transform.SetParent(root.transform, false); camera.GetComponent<Camera>().clearFlags = CameraClearFlags.SolidColor; camera.GetComponent<Camera>().backgroundColor = Ink;
			var manager = Rect("GSS", root.transform, 0, 0).AddComponent<GameStateManager>();
			var app = root.AddComponent<ShowcaseApp>(); Set(app, "m_Manager", manager); Objects(app, "m_PagePrefabs", prefabs);
			var main = Rect("Main", root.transform, 1050, 750, -180, -12).AddComponent<UIStack>();
			Set(main, "m_DisplayRoot", main.transform); Set(app, "m_Main", main);
			var header = Rect("Header", root.transform, 1410, 72, 0, 402); ColorPanel(header, new Color(0.10f, 0.15f, 0.22f));
			var headerGroup = header.AddComponent<CanvasGroup>(); var scope = header.AddComponent<FocusScope>();
			var focusData = new SerializedObject(scope); focusData.FindProperty("m_Priority").intValue = 100; focusData.ApplyModifiedPropertiesWithoutUndo();
			Text("Brand", header.transform, "UICONTROL SHOWCASE", 20, 290, 54, -532, 0);
			string[] labels = { "Settings", "Help", "Title" };
			for (int i = 0; i < labels.Length; i++)
			{
				var button = Button(labels[i], header.transform, labels[i].ToUpperInvariant(), 160, 48, 152 + 170 * i, 0);
				UnityEventTools.AddStringPersistentListener(button.onClick, app.OpenHeader, labels[i]);
				if (i == 0) { Set(scope, "m_Initial", button); }
			}
			Set(app, "m_Header", scope); Set(app, "m_HeaderGroup", headerGroup);
			var inspector = Rect("Observation", root.transform, 335, 734, 538, -12); ColorPanel(inspector, new Color(0.06f, 0.08f, 0.13f), false);
			var status = Text("Status", inspector.transform, "Waiting for Play", 16, 307, 612, 0, 43, TextAlignmentOptions.TopLeft); Set(app, "m_Status", status);
			var focusCheck = Button("RecreateHome", inspector.transform, "REOPEN HOME (FOCUS CHECK)", 303, 50, 0, -307);
			UnityEventTools.AddPersistentListener(focusCheck.onClick, app.RecreateHome);
			var front = Rect("Front", root.transform, 0, 0).AddComponent<UIStack>(); Stretch(front.gameObject);
			Set(front, "m_DisplayRoot", front.transform); Set(app, "m_Front", front);
			Text("Controls", root.transform, "Mouse / Left stick: cursor   D-pad / Arrows: navigation   South: click / submit   East: back   Start: header   LB / RB: tabs", 16, 1400, 46, 0, -412, TextAlignmentOptions.Center);
			ConfigureInput(root, app, module);
			ConfigureFade(root, app);
			root.SetActive(true);
			return root;
		}

		static void ConfigureFade(GameObject root, ShowcaseApp app)
		{
			var existing = root.transform.Find("ScreenFade");
			var obj = existing != null ? existing.gameObject : Rect("ScreenFade", root.transform, 0, 0);
			Stretch(obj); obj.transform.SetAsLastSibling();
			var group = obj.GetComponent<CanvasGroup>();
			if (group == null) { group = obj.AddComponent<CanvasGroup>(); }
			group.alpha = 0; group.blocksRaycasts = false;
			var graphic = obj.GetComponent<UnityEngine.UI.Image>();
			if (graphic == null) { graphic = ColorPanel(obj, Color.black); }
			var player = obj.GetComponent<NamedStatePlayer>();
			if (player == null) { player = obj.AddComponent<NamedStatePlayer>(); }
			player.TimeMode = AnimationTimeMode.Unscaled;
			var serialized = new SerializedObject(player); var states = serialized.FindProperty("m_States");
			if (obj.GetComponent<Animator>() == null) { obj.AddComponent<Animator>(); }
			string[] names = { "Out", "OutQuick", "OutImmediate", "In" };
			float[] times = { 0.5f, 0.3f, 0, 0.5f };
			states.arraySize = names.Length;
			for (int i = 0; i < names.Length; i++)
			{
				string path = Folder + "/Animations/ScreenFade" + names[i] + ".anim";
				var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
				if (clip == null) { clip = new AnimationClip { name = "ScreenFade" + names[i], frameRate = 60 }; AssetDatabase.CreateAsset(clip, path); }
				var curve = times[i] == 0 ? new AnimationCurve(new Keyframe(0, 1)) : AnimationCurve.Linear(0, i == 3 ? 1 : 0, times[i], i == 3 ? 0 : 1);
				AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("", typeof(CanvasGroup), "m_Alpha"), curve);
				var child = obj.transform.Find(names[i]);
				var host = child != null ? child.gameObject : Rect(names[i], obj.transform, 0, 0);
				var adapter = host.GetComponent<ClipAnimationAdapter>();
				if (adapter == null) { adapter = host.AddComponent<ClipAnimationAdapter>(); }
				adapter.Clip = clip; adapter.Root = obj;
				var state = states.GetArrayElementAtIndex(i);
				state.FindPropertyRelative("m_Channel").stringValue = "Default";
				state.FindPropertyRelative("m_Name").stringValue = names[i];
				state.FindPropertyRelative("m_Animation").objectReferenceValue = adapter;
			}
			serialized.ApplyModifiedPropertiesWithoutUndo();
			var fade = obj.GetComponent<ScreenFade>();
			if (fade == null) { fade = obj.AddComponent<ScreenFade>(); }
			Set(fade, "m_App", app); Set(fade, "m_Player", player); Set(fade, "m_Group", group); Set(fade, "m_Graphic", graphic);
			Set(app, "m_ScreenFade", fade);
		}

		static void ConfigureInput(GameObject root, ShowcaseApp app, InputSystemUIInputModule module)
		{
			if (module.move?.action == null) { module.AssignDefaultActions(); }
			var old = module.GetComponent<InputSystemFocusInput>();
			if (old != null) { UnityEngine.Object.DestroyImmediate(old); }
			string path = Folder + "/ShowcaseUI.inputactions";
			InputActionAsset asset = AssetDatabase.LoadAssetAtPath<InputActionAsset>(path);
			if (asset == null)
			{
				if (module.actionsAsset == null) { module.AssignDefaultActions(); }
				var copy = UnityEngine.Object.Instantiate(module.actionsAsset);
				var move = copy.FindAction(module.move.action.id.ToString(), true);
				while (move.bindings.Count > 0) { move.ChangeBinding(0).Erase(); }
				move.AddBinding("<Gamepad>/dpad");
				move.AddCompositeBinding("2DVector").With("Up", "<Keyboard>/upArrow").With("Down", "<Keyboard>/downArrow").With("Left", "<Keyboard>/leftArrow").With("Right", "<Keyboard>/rightArrow");
				var cursor = copy.AddActionMap("Cursor");
				cursor.AddAction("Move", InputActionType.Value, "<Gamepad>/leftStick", expectedControlLayout: "Vector2");
				cursor.AddAction("Click", InputActionType.Button, "<Gamepad>/buttonSouth");
				cursor.AddAction("Scroll", InputActionType.Value, "<Gamepad>/rightStick", expectedControlLayout: "Vector2");
				File.WriteAllText(path, copy.ToJson()); UnityEngine.Object.DestroyImmediate(copy);
				AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
				asset = AssetDatabase.LoadAssetAtPath<InputActionAsset>(path);
			}
			// 同じ action を指す Reference の setter は差し替えを省略するため、先に外す。
			module.point = null; module.leftClick = null; module.rightClick = null; module.middleClick = null; module.scrollWheel = null;
			module.move = null; module.submit = null; module.cancel = null; module.trackedDevicePosition = null; module.trackedDeviceOrientation = null;
			module.actionsAsset = asset;
			var refs = AssetDatabase.LoadAllAssetsAtPath(path).OfType<InputActionReference>().ToArray();
			InputActionReference Ref(string name) { return refs.First(x => x.action.id == asset.FindAction("UI/" + name, true).id); }
			// actionsAsset の setter が作る一時 Reference ではなく、import 済み sub-asset を保存する。
			module.point = Ref("Point"); module.leftClick = Ref("Click"); module.rightClick = Ref("RightClick"); module.middleClick = Ref("MiddleClick");
			module.scrollWheel = Ref("ScrollWheel"); module.move = Ref("Navigate"); module.submit = Ref("Submit"); module.cancel = Ref("Cancel");
			module.trackedDevicePosition = Ref("TrackedDevicePosition"); module.trackedDeviceOrientation = Ref("TrackedDeviceOrientation");
			var router = module.GetComponent<InputSystemUIInputRouter>();
			if (router == null) { router = module.gameObject.AddComponent<InputSystemUIInputRouter>(); }
			Set(app, "m_InputRouter", router);
			var selection = module.GetComponent<PointerSelection>(); if (selection == null) { selection = module.gameObject.AddComponent<PointerSelection>(); }
			Set(selection, "m_Source", router); Set(router, "m_Selection", selection);
			var existing = root.transform.Find("VirtualCursor");
			GameObject cursorObject = existing != null ? existing.gameObject : Rect("VirtualCursor", root.transform, 18, 18);
			var graphic = cursorObject.GetComponent<UnityEngine.UI.Image>();
			if (graphic == null) { graphic = ColorPanel(cursorObject, new Color(1, 0.85f, 0.25f), false); }
			graphic.raycastTarget = false; graphic.enabled = false;
			cursorObject.transform.localRotation = Quaternion.Euler(0, 0, 45);
			cursorObject.transform.SetAsLastSibling();
			// 仮想Mouseデバイス。UIへの配送はInput Moduleの標準経路。表示はPointerFollowerで追従し、RouterがGraphicの表示を切り替える。
			var virtualMouse = module.GetComponent<VirtualMouseCursor>(); if (virtualMouse == null) { virtualMouse = module.gameObject.AddComponent<VirtualMouseCursor>(); }
			var follower = cursorObject.GetComponent<PointerFollower>(); if (follower == null) { follower = cursorObject.AddComponent<PointerFollower>(); }
			Set(follower, "m_Source", virtualMouse);
			Set(router, "m_VirtualMouse", virtualMouse); Set(router, "m_VirtualMouseGraphic", graphic);
			var data = new SerializedObject(router);
			var navigation = data.FindProperty("m_NavigationMove"); navigation.FindPropertyRelative("m_UseReference").boolValue = true;
			navigation.FindPropertyRelative("m_Reference").objectReferenceValue = refs.First(x => x.action.id == asset.FindAction("UI/Navigate", true).id);
			data.ApplyModifiedPropertiesWithoutUndo();
			var mouseData = new SerializedObject(virtualMouse);
			foreach (var wiring in new[] { ("m_Move", "Cursor/Move"), ("m_Click", "Cursor/Click") })
			{
				var property = mouseData.FindProperty(wiring.Item1); property.FindPropertyRelative("m_UseReference").boolValue = true;
				property.FindPropertyRelative("m_Reference").objectReferenceValue = refs.First(x => x.action.id == asset.FindAction(wiring.Item2, true).id);
			}
			mouseData.ApplyModifiedPropertiesWithoutUndo();
		}

		static void Candidates(NavigationNode node, string direction, NavigationNode first = null, NavigationNode second = null, FocusScope scope = null)
		{
			var data = new SerializedObject(node); var list = data.FindProperty(direction);
			int count = 1 + (first != null ? 1 : 0) + (second != null ? 1 : 0) + (scope != null ? 1 : 0); list.arraySize = count;
			int index = 0;
			foreach (var target in new[] { first, second })
			{
				if (target == null) { continue; }
				var candidate = list.GetArrayElementAtIndex(index++); candidate.FindPropertyRelative("m_Kind").enumValueIndex = (int)CandidateKind.Node; candidate.FindPropertyRelative("m_Target").objectReferenceValue = target.Selectable;
			}
			if (scope != null) { var candidate = list.GetArrayElementAtIndex(index++); candidate.FindPropertyRelative("m_Kind").enumValueIndex = (int)CandidateKind.Scope; candidate.FindPropertyRelative("m_Scope").objectReferenceValue = scope; }
			list.GetArrayElementAtIndex(index).FindPropertyRelative("m_Kind").enumValueIndex = (int)CandidateKind.Automatic;
			data.ApplyModifiedPropertiesWithoutUndo();
		}
		static void Set(UnityEngine.Object target, string field, UnityEngine.Object value)
		{
			var data = new SerializedObject(target); data.FindProperty(field).objectReferenceValue = value; data.ApplyModifiedPropertiesWithoutUndo();
		}
		static void Objects(UnityEngine.Object target, string field, params UnityEngine.Object[] values)
		{
			var data = new SerializedObject(target); var array = data.FindProperty(field); array.arraySize = values.Length;
			for (int i = 0; i < values.Length; i++) { array.GetArrayElementAtIndex(i).objectReferenceValue = values[i]; }
			data.ApplyModifiedPropertiesWithoutUndo();
		}
	}
}
