using System;
using MXEngine.MVP;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace MXEngine.Samples.Editor
{
    public static class NavigationDemoBuilder
    {
        private const string BasePath = "Assets/MXEngine/Samples/NavigationDemo";
        private const string PrefabPath = BasePath + "/Prefabs";
        private const string ScenePath = "Assets/Scenes/NavigationDemo.unity";
        private static readonly Font Font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        [MenuItem("MXEngine/Build Navigation Demo Scene")]
        public static void Build()
        {
            EnsureFolder(PrefabPath);

            var lobby = CreateScreenPrefab("LobbyScreen", "LOBBY SCREEN", "Press Gameplay to push a second screen.",
                new Color(0.10f, 0.18f, 0.27f));
            var gameplay = CreateScreenPrefab("GameplayScreen", "GAMEPLAY SCREEN", "Press Back to restore Lobby.",
                new Color(0.12f, 0.25f, 0.20f));
            var settings = CreateModalPrefab();
            var loading = CreateLoadingPrefab();

            Register(GameViewKey.Lobby, lobby);
            Register(GameViewKey.Gameplay, gameplay);
            Register(GameViewKey.Settings, settings);
            Register(GameViewKey.Loading, loading);
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var canvasObject = new GameObject("Navigation Demo Canvas", typeof(RectTransform), typeof(Canvas),
                typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(UIRoot), typeof(NavigationDemo));
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            var canvasTransform = canvasObject.GetComponent<RectTransform>();
            var screenRoot = CreateFullStretch("ScreenRoot", canvasTransform);
            var modalRoot = CreateFullStretch("ModalRoot", canvasTransform);
            var overlayRoot = CreateFullStretch("OverlayRoot", canvasTransform);
            var uiRoot = canvasObject.GetComponent<UIRoot>();
            var rootProperties = new SerializedObject(uiRoot);
            rootProperties.FindProperty("<ScreenRoot>k__BackingField").objectReferenceValue = screenRoot;
            rootProperties.FindProperty("<ModalRoot>k__BackingField").objectReferenceValue = modalRoot;
            rootProperties.FindProperty("<OverlayRoot>k__BackingField").objectReferenceValue = overlayRoot;
            rootProperties.ApplyModifiedPropertiesWithoutUndo();

            var controllerObject = new GameObject("GameController", typeof(GameController));
            var controllerProperties = new SerializedObject(controllerObject.GetComponent<GameController>());
            controllerProperties.FindProperty("canvas").objectReferenceValue = canvas;
            controllerProperties.FindProperty("uiRoot").objectReferenceValue = uiRoot;
            controllerProperties.ApplyModifiedPropertiesWithoutUndo();

            var header = CreateFullStretch("Status Bar", canvasTransform);
            header.anchorMin = new Vector2(0, 1);
            header.anchorMax = Vector2.one;
            header.pivot = new Vector2(0.5f, 1);
            header.sizeDelta = new Vector2(0, 72);
            header.anchoredPosition = Vector2.zero;
            var headerImage = header.gameObject.AddComponent<Image>();
            headerImage.color = new Color(0.02f, 0.04f, 0.07f, 0.86f);
            var status = CreateText("Status", header, "Starting...", 30, Color.white);
            SetFullStretch(status.rectTransform);

            var toolbar = CreateFullStretch("Toolbar", canvasTransform);
            toolbar.anchorMin = Vector2.zero;
            toolbar.anchorMax = new Vector2(1, 0);
            toolbar.pivot = new Vector2(0.5f, 0);
            toolbar.sizeDelta = new Vector2(0, 120);
            toolbar.anchoredPosition = Vector2.zero;
            var toolbarImage = toolbar.gameObject.AddComponent<Image>();
            toolbarImage.color = new Color(0.02f, 0.04f, 0.07f, 0.96f);
            var layout = toolbar.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.padding = new RectOffset(20, 20, 18, 18);
            layout.spacing = 14;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = true;

            var buttons = new[]
            {
                CreateButton("Lobby", toolbar),
                CreateButton("Gameplay", toolbar),
                CreateButton("Settings", toolbar),
                CreateButton("Back", toolbar),
                CreateButton("Close Modal", toolbar),
                CreateButton("Close All", toolbar),
                CreateButton("Loading", toolbar)
            };

            var demo = canvasObject.GetComponent<NavigationDemo>();
            var demoProperties = new SerializedObject(demo);
            demoProperties.FindProperty("uiRoot").objectReferenceValue = uiRoot;
            demoProperties.FindProperty("lobbyButton").objectReferenceValue = buttons[0];
            demoProperties.FindProperty("gameplayButton").objectReferenceValue = buttons[1];
            demoProperties.FindProperty("settingsButton").objectReferenceValue = buttons[2];
            demoProperties.FindProperty("backButton").objectReferenceValue = buttons[3];
            demoProperties.FindProperty("closeModalButton").objectReferenceValue = buttons[4];
            demoProperties.FindProperty("closeAllModalsButton").objectReferenceValue = buttons[5];
            demoProperties.FindProperty("loadingButton").objectReferenceValue = buttons[6];
            demoProperties.FindProperty("statusText").objectReferenceValue = status;
            demoProperties.ApplyModifiedPropertiesWithoutUndo();

            var eventSystemObject = new GameObject("EventSystem", typeof(EventSystem),
                typeof(InputSystemUIInputModule));
            eventSystemObject.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
            Debug.Log($"Navigation demo ready: {ScenePath}");
        }

        private static string CreateScreenPrefab(string name, string title, string description, Color color)
        {
            var root = NewUIObject(name, null);
            SetFullStretch(root);
            root.gameObject.AddComponent<Image>().color = color;
            root.gameObject.AddComponent<NavigationDemoView>();
            var titleText = CreateText("Title", root, title, 86, Color.white);
            titleText.rectTransform.sizeDelta = new Vector2(1600, 170);
            titleText.rectTransform.anchoredPosition = new Vector2(0, 110);
            var descriptionText = CreateText("Description", root, description, 42,
                new Color(0.8f, 0.9f, 1f));
            descriptionText.rectTransform.sizeDelta = new Vector2(1600, 130);
            descriptionText.rectTransform.anchoredPosition = new Vector2(0, -35);
            return SavePrefab(root.gameObject, name);
        }

        private static string CreateModalPrefab()
        {
            var root = NewUIObject("SettingsModal", null);
            SetFullStretch(root);
            root.gameObject.AddComponent<Image>().color = new Color(0, 0, 0, 0.62f);
            root.gameObject.AddComponent<NavigationDemoView>();
            var panel = NewUIObject("Panel", root);
            panel.sizeDelta = new Vector2(820, 440);
            panel.gameObject.AddComponent<Image>().color = new Color(0.20f, 0.21f, 0.33f);
            var title = CreateText("Title", panel, "SETTINGS MODAL", 62, Color.white);
            title.rectTransform.sizeDelta = new Vector2(760, 120);
            title.rectTransform.anchoredPosition = new Vector2(0, 65);
            var hint = CreateText("Hint", panel, "Open again to test the modal stack.\nUse Close Modal or Close All below.",
                32, Color.white);
            hint.rectTransform.sizeDelta = new Vector2(740, 180);
            hint.rectTransform.anchoredPosition = new Vector2(0, -55);
            return SavePrefab(root.gameObject, "SettingsModal");
        }

        private static string CreateLoadingPrefab()
        {
            var root = NewUIObject("LoadingOverlay", null);
            SetFullStretch(root);
            root.gameObject.AddComponent<Image>().color = new Color(0.02f, 0.04f, 0.12f, 0.78f);
            var label = CreateText("Label", root, "LOADING OVERLAY", 68, Color.white);
            label.rectTransform.sizeDelta = new Vector2(1500, 180);
            return SavePrefab(root.gameObject, "LoadingOverlay");
        }

        private static void Register(string key, string path)
        {
            var settings = AddressableAssetSettingsDefaultObject.GetSettings(true);
            if (settings == null || settings.DefaultGroup == null)
                throw new InvalidOperationException("Addressables needs a default group.");

            var guid = AssetDatabase.AssetPathToGUID(path);
            var addressableEntry = settings.CreateOrMoveEntry(guid, settings.DefaultGroup);
            addressableEntry.address = key;
            EditorUtility.SetDirty(settings);
        }

        private static string SavePrefab(GameObject root, string name)
        {
            var path = PrefabPath + "/" + name + ".prefab";
            PrefabUtility.SaveAsPrefabAsset(root, path);
            UnityEngine.Object.DestroyImmediate(root);
            return path;
        }

        private static RectTransform CreateFullStretch(string name, RectTransform parent)
        {
            var rect = NewUIObject(name, parent);
            SetFullStretch(rect);
            return rect;
        }

        private static RectTransform NewUIObject(string name, RectTransform parent)
        {
            var gameObject = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer));
            var rect = gameObject.GetComponent<RectTransform>();
            if (parent != null)
                rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            return rect;
        }

        private static void SetFullStretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static Text CreateText(string name, RectTransform parent, string value, int size, Color color)
        {
            var rect = NewUIObject(name, parent);
            var label = rect.gameObject.AddComponent<Text>();
            label.font = Font;
            label.text = value;
            label.fontSize = size;
            label.color = color;
            label.alignment = TextAnchor.MiddleCenter;
            label.raycastTarget = false;
            return label;
        }

        private static Button CreateButton(string name, RectTransform parent)
        {
            var rect = NewUIObject(name + " Button", parent);
            rect.gameObject.AddComponent<Image>().color = new Color(0.20f, 0.42f, 0.68f);
            var button = rect.gameObject.AddComponent<Button>();
            var element = rect.gameObject.AddComponent<LayoutElement>();
            element.minWidth = 150;
            element.minHeight = 75;
            var label = CreateText("Label", rect, name, 28, Color.white);
            SetFullStretch(label.rectTransform);
            return button;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
                return;
            var parent = path.Substring(0, path.LastIndexOf('/'));
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, path.Substring(path.LastIndexOf('/') + 1));
        }
    }
}
