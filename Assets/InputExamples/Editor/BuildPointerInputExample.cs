using System.IO;
using System.Linq;
using UIControl.InputSystem;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;

namespace UIControl.Examples.Editor
{
	public static class BuildPointerInputExample
	{
		private const string Folder = "Assets/InputExamples";
		[MenuItem("UIControl/Samples/Build Pointer Input Example")]
		public static void Build()
		{
			// バッチでは未保存のUntitledシーンがあるとAdditiveで開けない。作業シーンを単独で開く。
			bool batch = Application.isBatchMode;
			Scene previous = SceneManager.GetActiveScene();
			Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, batch ? NewSceneMode.Single : NewSceneMode.Additive);
			if (!batch) { SceneManager.SetActiveScene(scene); }
			try
			{
				GameObject host = new GameObject("Pointer Input Example");
				var app = host.AddComponent<PointerInputExample>();
				var canvasObject = Rect("Canvas", host.transform, Vector2.zero, new Vector2(1280, 720));
				var canvas = canvasObject.AddComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay;
				var scaler = canvasObject.AddComponent<UnityEngine.UI.CanvasScaler>(); scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(1280, 720);
				canvasObject.AddComponent<UnityEngine.UI.GraphicRaycaster>();
				var cameraObject = new GameObject("Camera", typeof(Camera)); cameraObject.transform.SetParent(host.transform); cameraObject.GetComponent<Camera>().backgroundColor = new Color(0.04f, 0.06f, 0.1f); cameraObject.GetComponent<Camera>().clearFlags = CameraClearFlags.SolidColor;
				var events = new GameObject("EventSystem", typeof(EventSystem)); events.transform.SetParent(host.transform);
				var module = events.AddComponent<InputSystemUIInputModule>(); module.AssignDefaultActions();
				InputActionAsset asset = CreateActions(module);
				module.point = null; module.leftClick = null; module.rightClick = null; module.middleClick = null; module.scrollWheel = null;
				module.move = null; module.submit = null; module.cancel = null; module.trackedDevicePosition = null; module.trackedDeviceOrientation = null;
				module.actionsAsset = asset;
				var refs = AssetDatabase.LoadAllAssetsAtPath(Folder + "/PointerUI.inputactions").OfType<InputActionReference>().ToArray();
				InputActionReference Ref(string name) => refs.First(r => r.action.id == asset.FindAction(name, true).id);
				module.point = Ref("UI/Point"); module.leftClick = Ref("UI/Click"); module.rightClick = Ref("UI/RightClick"); module.middleClick = Ref("UI/MiddleClick"); module.scrollWheel = Ref("UI/ScrollWheel");
				module.move = Ref("UI/Navigate"); module.submit = Ref("UI/Submit"); module.cancel = Ref("UI/Cancel"); module.trackedDevicePosition = Ref("UI/TrackedDevicePosition"); module.trackedDeviceOrientation = Ref("UI/TrackedDeviceOrientation");
				var router = events.AddComponent<InputSystemUIInputRouter>();
				var selection = events.AddComponent<PointerSelection>(); Set(selection, "m_Source", router); Bool(selection, "m_SelectNearestOnNavigation", true); Set(router, "m_Selection", selection);
				var serialized = new SerializedObject(router);
				// Routerは自前のActionだけを読む。Input Moduleの参照は触らない。
				var navigationProperty = serialized.FindProperty("m_NavigationMove"); navigationProperty.FindPropertyRelative("m_UseReference").boolValue = true; navigationProperty.FindPropertyRelative("m_Reference").objectReferenceValue = Ref("UI/Navigate");
				serialized.ApplyModifiedPropertiesWithoutUndo();
				// 仮想Mouseデバイス。スティックで動き、決定が左クリックになる。UIへの配送はInput Moduleの標準経路。
				var virtualMouse = events.AddComponent<VirtualMouseCursor>();
				var mouseSerialized = new SerializedObject(virtualMouse);
				foreach (var wiring in new[] { ("m_Move", "Cursor/Move"), ("m_Click", "Cursor/Click") })
				{
					var property = mouseSerialized.FindProperty(wiring.Item1); property.FindPropertyRelative("m_UseReference").boolValue = true; property.FindPropertyRelative("m_Reference").objectReferenceValue = Ref(wiring.Item2);
				}
				mouseSerialized.ApplyModifiedPropertiesWithoutUndo();
				Set(router, "m_VirtualMouse", virtualMouse);
				Text("Title", canvasObject.transform, new Vector2(0, 300), new Vector2(1180, 70), "POINTER INPUT / uGUI", 32);
				Text("Instructions", canvasObject.transform, new Vector2(0, 227), new Vector2(1180, 70), "Mouse / left stick: pointer    |    A: click / drag\nD-pad: select toward input    |    Yellow marker: navigation selection", 21);
				var scopeObject = Rect("Buttons", canvasObject.transform, new Vector2(0, 80), new Vector2(1120, 100));
				var scope = scopeObject.AddComponent<FocusScope>();
				foreach (var item in new[] { ("One", -350f), ("Two", 0f), ("Three", 350f) })
				{
					var buttonObject = Rect(item.Item1, scopeObject.transform, new Vector2(item.Item2, 0), new Vector2(250, 80));
					var graphic = buttonObject.AddComponent<UnityEngine.UI.Image>(); graphic.color = new Color(0.22f, 0.35f, 0.65f);
					var button = buttonObject.AddComponent<UnityEngine.UI.Button>(); button.targetGraphic = graphic;
					var colors = button.colors; colors.selectedColor = new Color(1, 0.8f, 0.2f); button.colors = colors;
					Text("Label", buttonObject.transform, Vector2.zero, new Vector2(240, 70), item.Item1, 26);
					UnityEventTools.AddStringPersistentListener(button.onClick, app.SelectButton, item.Item1);
					var node = buttonObject.AddComponent<NavigationNode>(); var data = new SerializedObject(node);
					foreach (string field in new[] { "m_Up", "m_Down", "m_Left", "m_Right" }) { var list = data.FindProperty(field); list.arraySize = 1; list.GetArrayElementAtIndex(0).FindPropertyRelative("m_Kind").enumValueIndex = (int)CandidateKind.Automatic; }
					data.ApplyModifiedPropertiesWithoutUndo();
				}
				var dropObject = Rect("Drop Area", canvasObject.transform, new Vector2(310, -90), new Vector2(340, 170)); dropObject.AddComponent<UnityEngine.UI.Image>().color = new Color(0.1f, 0.45f, 0.3f);
				Text("Label", dropObject.transform, Vector2.zero, new Vector2(330, 150), "DROP HERE", 25);
				var drop = dropObject.AddComponent<UIDropTarget>(); UnityEventTools.AddPersistentListener(drop.Dropped, app.Dropped);
				var card = Rect("Blue Card", canvasObject.transform, new Vector2(-330, -90), new Vector2(180, 110));
				var cardGraphic = card.AddComponent<UnityEngine.UI.Image>(); cardGraphic.color = new Color(0.1f, 0.6f, 0.9f);
				Text("Label", card.transform, Vector2.zero, new Vector2(175, 100), "DRAG ME", 22);
				// 仮想マウスは標準のPointer経路で届くので、掴む対象をSelectableにする必要はない。
				var drag = card.AddComponent<UIDraggable>(); UnityEventTools.AddPersistentListener(drag.EndDrag, app.DragEnded);
				var status = Text("Status", canvasObject.transform, new Vector2(0, -270), new Vector2(1180, 135), "", 20);
				var follow = Rect("Pointer Label", canvasObject.transform, Vector2.zero, new Vector2(145, 35)); follow.AddComponent<UnityEngine.UI.Image>().color = new Color(0.15f, 0.15f, 0.2f, 0.9f); follow.GetComponent<UnityEngine.UI.Image>().raycastTarget = false;
				Text("Text", follow.transform, Vector2.zero, new Vector2(145, 35), "Pointer follower", 16);
				var follower = follow.AddComponent<PointerFollower>(); Set(follower, "m_Source", router); Set(follower, "m_Bounds", canvasObject.transform); var f = new SerializedObject(follower); f.FindProperty("m_Offset").vector2Value = new Vector2(85, -30); f.ApplyModifiedPropertiesWithoutUndo();
				var cursor = Rect("Virtual Cursor", canvasObject.transform, Vector2.zero, new Vector2(15, 15)); var image = cursor.AddComponent<UnityEngine.UI.Image>(); image.color = Color.yellow; image.raycastTarget = false;
				var cursorFollower = cursor.AddComponent<PointerFollower>(); Set(cursorFollower, "m_Source", virtualMouse);
				Set(router, "m_VirtualMouseGraphic", image);
				Set(app, "m_Scope", scope); Set(app, "m_Router", router); Set(app, "m_Status", status);
				var prefab = PrefabUtility.SaveAsPrefabAsset(host, Folder + "/PointerInput.prefab"); Object.DestroyImmediate(host); PrefabUtility.InstantiatePrefab(prefab, scene);
				EditorSceneManager.SaveScene(scene, Folder + "/PointerInput.unity"); AssetDatabase.SaveAssets();
			}
			finally
			{
				// Singleで開いた場合は戻す先が無い。閉じるとシーンが一つも無くなる。
				if (!batch) { EditorSceneManager.CloseScene(scene, true); SceneManager.SetActiveScene(previous); }
			}
		}
		private static InputActionAsset CreateActions(InputSystemUIInputModule module)
		{
			var copy = Object.Instantiate(module.actionsAsset);
			var move = copy.FindAction(module.move.action.id.ToString(), true); while (move.bindings.Count > 0) { move.ChangeBinding(0).Erase(); }
			move.AddBinding("<Gamepad>/dpad"); move.AddCompositeBinding("2DVector").With("Up", "<Keyboard>/upArrow").With("Down", "<Keyboard>/downArrow").With("Left", "<Keyboard>/leftArrow").With("Right", "<Keyboard>/rightArrow");
			var cursor = copy.AddActionMap("Cursor"); cursor.AddAction("Move", InputActionType.Value, "<Gamepad>/leftStick", expectedControlLayout: "Vector2"); cursor.AddAction("Click", InputActionType.Button, "<Gamepad>/buttonSouth"); cursor.AddAction("Scroll", InputActionType.Value, "<Gamepad>/rightStick", expectedControlLayout: "Vector2");
			string path = Folder + "/PointerUI.inputactions"; File.WriteAllText(path, copy.ToJson()); Object.DestroyImmediate(copy); AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport); return AssetDatabase.LoadAssetAtPath<InputActionAsset>(path);
		}
		private static GameObject Rect(string name, Transform parent, Vector2 position, Vector2 size) { var go = new GameObject(name, typeof(RectTransform)); go.transform.SetParent(parent, false); var rect = (RectTransform)go.transform; rect.sizeDelta = size; rect.anchoredPosition = position; return go; }
		private static UnityEngine.UI.Text Text(string name, Transform parent, Vector2 position, Vector2 size, string value, int fontSize) { var go = Rect(name, parent, position, size); var text = go.AddComponent<UnityEngine.UI.Text>(); text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); text.text = value; text.fontSize = fontSize; text.alignment = TextAnchor.MiddleCenter; text.raycastTarget = false; return text; }
		private static void Set(Object target, string field, Object value) { var data = new SerializedObject(target); data.FindProperty(field).objectReferenceValue = value; data.ApplyModifiedPropertiesWithoutUndo(); }
		private static void Objects(Object target, string field, params Object[] values)
		{
			var data = new SerializedObject(target); var array = data.FindProperty(field); array.arraySize = values.Length;
			for (int i = 0; i < values.Length; i++) { array.GetArrayElementAtIndex(i).objectReferenceValue = values[i]; }
			data.ApplyModifiedPropertiesWithoutUndo();
		}
		private static void Bool(Object target, string field, bool value) { var data = new SerializedObject(target); data.FindProperty(field).boolValue = value; data.ApplyModifiedPropertiesWithoutUndo(); }
	}
}
