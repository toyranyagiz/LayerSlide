using System.IO;
using LayerSlide.Gameplay;
using LayerSlide.Presentation;
using LayerSlide.Services;
using LayerSlide.UI;
using TMPro;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.UI;

namespace LayerSlide.Editor
{
    /// <summary>
    /// Creates every generated asset and both scenes from code so a clean checkout can rebuild the project
    /// deterministically (GDD 06 §1–2, 04 §4–5). Safe to run repeatedly.
    /// Batch: Unity -batchmode -projectPath . -executeMethod LayerSlide.Editor.ProjectSetup.RunBatch -quit
    /// </summary>
    public static class ProjectSetup
    {
        const string Root = "Assets/LayerSlide";
        const string MaterialsFolder = Root + "/Art/Materials";
        const string RenderingFolder = Root + "/Data/Rendering";
        const string ScenesFolder = Root + "/Scenes";
        const string ThemePath = Root + "/Data/VisualTheme.asset";
        const string BootstrapScenePath = ScenesFolder + "/Bootstrap.unity";
        const string GameplayScenePath = ScenesFolder + "/Gameplay.unity";
        const string TmpEssentialsPackage = "Packages/com.unity.ugui/Package Resources/TMP Essential Resources.unitypackage";

        // Turkish letters and the punctuation used by the UI, pre-rendered into the font atlas.
        const string PrewarmCharacters =
            "ABCÇDEFGĞHIİJKLMNOÖPRSŞTUÜVYZQWXabcçdefgğhıijklmnoöprsştuüvyzqwx0123456789 .,:;!?'\"()-–—…·•→✓/%+";

        [MenuItem("Tools/Layer Slide/Setup Project")]
        public static void Run()
        {
            ImportTmpEssentials();
            ConfigureEditorAndPlayer();
            var pipeline = CreateRenderPipeline();
            AssetBaker.BakeAudio();
            AssetBaker.BakeIconAndSplash();
            CreateTheme();
            // Persist generated assets first: opening a new scene unloads unsaved in-memory assets.
            AssetDatabase.SaveAssets();
            CreateBootstrapScene();
            CreateGameplayScene();
            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene(BootstrapScenePath, true),
                new EditorBuildSettingsScene(GameplayScenePath, true)
            };
            AssetDatabase.SaveAssets();
            Debug.Log($"[ProjectSetup] Done. Pipeline: {AssetDatabase.GetAssetPath(pipeline)}");
        }

        public static void RunBatch()
        {
            try
            {
                Run();
                EditorApplication.Exit(0);
            }
            catch (System.Exception exception)
            {
                Debug.LogException(exception);
                EditorApplication.Exit(1);
            }
        }

        // ---------------------------------------------------------------- Packages and settings

        /// <summary>
        /// TMP Essential Resources are committed under Assets/TextMesh Pro. This only matters for a fresh project;
        /// note that ImportPackage is deferred in batch mode, so run the menu item once in the editor in that case.
        /// </summary>
        static void ImportTmpEssentials()
        {
            if (AssetDatabase.FindAssets("t:TMP_Settings").Length > 0) return;
            AssetDatabase.ImportPackage(TmpEssentialsPackage, false);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        }

        static void ConfigureEditorAndPlayer()
        {
            EditorSettings.serializationMode = SerializationMode.ForceText;
            VersionControlSettings.mode = "Visible Meta Files";

            PlayerSettings.companyName = "Mehmet Yagiz Toyran";
            PlayerSettings.productName = "Layer Slide";
            PlayerSettings.bundleVersion = "1.0.0";
            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.Portrait;
            PlayerSettings.runInBackground = false;

            var android = NamedBuildTarget.Android;
            PlayerSettings.SetApplicationIdentifier(android, "com.mehmetyagiztoyran.layerslide");
            PlayerSettings.SetScriptingBackend(android, ScriptingImplementation.IL2CPP);
            PlayerSettings.SetManagedStrippingLevel(android, ManagedStrippingLevel.Low);
            PlayerSettings.SetApiCompatibilityLevel(android, ApiCompatibilityLevel.NET_Standard);
            PlayerSettings.Android.bundleVersionCode = 1;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel26;
            PlayerSettings.Android.targetSdkVersion = (AndroidSdkVersions)36;
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsDeviceType.OpenGLES3 });

            // Active Input Handling = Input System Package only. There is no public API for this field.
            var playerSettings = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset");
            if (playerSettings.Length > 0)
            {
                var serialized = new SerializedObject(playerSettings[0]);
                var handler = serialized.FindProperty("activeInputHandler");
                if (handler != null)
                {
                    handler.intValue = 1;
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                }
            }
        }

        static UniversalRenderPipelineAsset CreateRenderPipeline()
        {
            EnsureFolder(RenderingFolder);
            string rendererPath = RenderingFolder + "/LayerSlide_Renderer.asset";
            string pipelinePath = RenderingFolder + "/LayerSlide_URP.asset";

            var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(rendererPath);
            if (renderer == null)
            {
                renderer = ScriptableObject.CreateInstance<UniversalRendererData>();
                AssetDatabase.CreateAsset(renderer, rendererPath);
            }

            var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(pipelinePath);
            if (pipeline == null)
            {
                pipeline = UniversalRenderPipelineAsset.Create(renderer);
                AssetDatabase.CreateAsset(pipeline, pipelinePath);
            }

            // Single quality profile from GDD 04 §5.
            var serialized = new SerializedObject(pipeline);
            SetInt(serialized, "m_MSAA", 2);
            SetBool(serialized, "m_SupportsHDR", false);
            SetFloat(serialized, "m_RenderScale", 1f);
            SetBool(serialized, "m_MainLightShadowsSupported", true);
            SetInt(serialized, "m_MainLightShadowmapResolution", 1024);
            SetFloat(serialized, "m_ShadowDistance", 15f);
            SetInt(serialized, "m_ShadowCascadeCount", 1);
            SetBool(serialized, "m_SoftShadowsSupported", true);
            SetInt(serialized, "m_AdditionalLightsRenderingMode", 0);
            SetBool(serialized, "m_AdditionalLightShadowsSupported", false);
            SetBool(serialized, "m_UseSRPBatcher", true);
            serialized.ApplyModifiedPropertiesWithoutUndo();

            GraphicsSettings.defaultRenderPipeline = pipeline;
            KeepSingleQualityLevel(pipeline);
            EditorUtility.SetDirty(pipeline);
            return pipeline;
        }

        static void KeepSingleQualityLevel(RenderPipelineAsset pipeline)
        {
            var assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/QualitySettings.asset");
            if (assets.Length == 0) return;

            var serialized = new SerializedObject(assets[0]);
            var levels = serialized.FindProperty("m_QualitySettings");
            if (levels != null && levels.arraySize > 0)
            {
                while (levels.arraySize > 1)
                    levels.DeleteArrayElementAtIndex(levels.arraySize - 1);
                var level = levels.GetArrayElementAtIndex(0);
                level.FindPropertyRelative("name").stringValue = "LayerSlide";
                var asset = level.FindPropertyRelative("customRenderPipeline");
                if (asset != null) asset.objectReferenceValue = pipeline;
                var vSync = level.FindPropertyRelative("vSyncCount");
                if (vSync != null) vSync.intValue = 0;
            }
            SetInt(serialized, "m_CurrentQuality", 0);

            var defaults = serialized.FindProperty("m_PerPlatformDefaultQuality");
            if (defaults != null)
                for (int i = 0; i < defaults.arraySize; i++)
                {
                    var entry = defaults.GetArrayElementAtIndex(i).FindPropertyRelative("second");
                    if (entry != null) entry.intValue = 0;
                }
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        // ---------------------------------------------------------------- Materials, fonts, theme

        static void CreateTheme()
        {
            EnsureFolder(MaterialsFolder);
            var theme = AssetDatabase.LoadAssetAtPath<VisualTheme>(ThemePath);
            if (theme == null)
            {
                theme = ScriptableObject.CreateInstance<VisualTheme>();
                AssetDatabase.CreateAsset(theme, ThemePath);
            }

            theme.red = LitMaterial("Red", Palette.Red, 0.30f);
            theme.blue = LitMaterial("Blue", Palette.Blue, 0.30f);
            theme.yellow = LitMaterial("Yellow", Palette.Yellow, 0.30f);
            theme.board = LitMaterial("Board", Palette.Board, 0.10f);
            theme.wall = LitMaterial("Wall", Palette.Wall, 0.10f);
            theme.perimeter = LitMaterial("Perimeter", new Color(0.79f, 0.83f, 0.87f), 0.10f);
            theme.white = UnlitMaterial("UnlitWhite", Color.white);
            theme.dark = UnlitMaterial("UnlitDark", Palette.Text);
            theme.whiteTransparent = TransparentUnlitMaterial("UnlitWhiteTransparent", new Color(1f, 1f, 1f, 0.55f));
            theme.meshBank = AssetBaker.BakeMeshes();
            theme.regularFont = FontAsset("NotoSans-Regular");
            theme.semiBoldFont = FontAsset("NotoSans-SemiBold");
            EditorUtility.SetDirty(theme);
        }

        static Material LitMaterial(string name, Color color, float smoothness)
        {
            var material = LoadOrCreateMaterial(name, "Universal Render Pipeline/Lit");
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Metallic", 0f);
            material.SetFloat("_Smoothness", smoothness);
            material.enableInstancing = true;
            EditorUtility.SetDirty(material);
            return material;
        }

        static Material UnlitMaterial(string name, Color color)
        {
            var material = LoadOrCreateMaterial(name, "Universal Render Pipeline/Unlit");
            material.SetColor("_BaseColor", color);
            material.enableInstancing = true;
            EditorUtility.SetDirty(material);
            return material;
        }

        /// <summary>URP Unlit set to alpha-blended transparent (no depth write, transparent queue).</summary>
        static Material TransparentUnlitMaterial(string name, Color color)
        {
            var material = LoadOrCreateMaterial(name, "Universal Render Pipeline/Unlit");
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Surface", 1f);
            material.SetFloat("_Blend", 0f);
            material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_SrcBlendAlpha", (float)UnityEngine.Rendering.BlendMode.One);
            material.SetFloat("_DstBlendAlpha", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_ZWrite", 0f);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.SetOverrideTag("RenderType", "Transparent");
            material.renderQueue = (int)RenderQueue.Transparent;
            EditorUtility.SetDirty(material);
            return material;
        }

        static Material LoadOrCreateMaterial(string name, string shaderName)
        {
            string path = $"{MaterialsFolder}/{name}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null) return material;

            var shader = Shader.Find(shaderName);
            if (shader == null) throw new System.InvalidOperationException($"Shader '{shaderName}' not found. Is URP installed?");
            material = new Material(shader) { name = name };
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        /// <summary>Dynamic SDF font asset backed by the bundled Noto Sans file, pre-warmed with Turkish glyphs.</summary>
        static TMP_FontAsset FontAsset(string fileName)
        {
            string assetPath = $"{Root}/Fonts/{fileName} SDF.asset";
            var existing = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(assetPath);
            if (existing != null) return existing;

            var font = AssetDatabase.LoadAssetAtPath<Font>($"{Root}/Fonts/{fileName}.ttf");
            if (font == null) throw new FileNotFoundException($"Font {fileName}.ttf is missing.");

            var asset = TMP_FontAsset.CreateFontAsset(font, 64, 6, GlyphRenderMode.SDFAA, 1024, 1024, AtlasPopulationMode.Dynamic, false);
            asset.name = fileName + " SDF";
            AssetDatabase.CreateAsset(asset, assetPath);
            asset.material.name = fileName + " SDF Material";
            asset.atlasTextures[0].name = fileName + " SDF Atlas";
            AssetDatabase.AddObjectToAsset(asset.material, asset);
            AssetDatabase.AddObjectToAsset(asset.atlasTextures[0], asset);
            asset.TryAddCharacters(PrewarmCharacters);
            EditorUtility.SetDirty(asset);
            return asset;
        }

        // ---------------------------------------------------------------- Scenes

        /// <summary>Bootstrap shows the in-app splash (icon + name) while Gameplay loads.</summary>
        static void CreateBootstrapScene()
        {
            EnsureFolder(ScenesFolder);
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            new GameObject("Bootstrap").AddComponent<Bootstrap>();

            var camera = new GameObject("Camera").AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Palette.Environment;
            camera.cullingMask = 0;

            var canvasObject = new GameObject("Splash", typeof(RectTransform));
            canvasObject.layer = LayerMask.NameToLayer("UI");
            canvasObject.AddComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(360f, 640f);
            scaler.matchWidthOrHeight = 0f;

            var icon = new GameObject("Icon", typeof(RectTransform)).AddComponent<Image>();
            icon.transform.SetParent(canvasObject.transform, false);
            icon.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(AssetBaker.IconPath);
            icon.rectTransform.sizeDelta = new Vector2(160f, 160f);
            icon.rectTransform.anchoredPosition = new Vector2(0f, 40f);

            var theme = AssetDatabase.LoadAssetAtPath<VisualTheme>(ThemePath);
            var title = new GameObject("Title", typeof(RectTransform)).AddComponent<TextMeshProUGUI>();
            title.transform.SetParent(canvasObject.transform, false);
            title.font = theme.semiBoldFont;
            title.fontSize = 32f;
            title.alignment = TextAlignmentOptions.Center;
            title.color = Color.white;
            title.text = "Layer Slide";
            title.rectTransform.sizeDelta = new Vector2(320f, 48f);
            title.rectTransform.anchoredPosition = new Vector2(0f, -80f);

            EditorSceneManager.SaveScene(scene, BootstrapScenePath);
        }

        static void CreateGameplayScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            SceneManager.SetActiveScene(scene);
            var theme = AssetDatabase.LoadAssetAtPath<VisualTheme>(ThemePath);
            if (theme == null) throw new FileNotFoundException("Visual theme asset is missing.", ThemePath);

            RenderSettings.skybox = null;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = Palette.Ambient * 0.65f;
            RenderSettings.fog = false;

            var light = new GameObject("DirectionalLight").AddComponent<Light>();
            light.type = LightType.Directional;
            light.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            light.intensity = 1.2f;
            light.color = Color.white;
            light.shadows = LightShadows.Soft;

            // Clears the whole screen behind the HUD; the board camera only draws inside the BoardRect.
            var background = new GameObject("BackgroundCamera").AddComponent<Camera>();
            background.clearFlags = CameraClearFlags.SolidColor;
            background.backgroundColor = Palette.Environment;
            background.cullingMask = 0;
            background.depth = -10;
            background.orthographic = true;
            background.gameObject.AddComponent<AudioListener>();
            background.gameObject.AddComponent<OutputLimiter>();

            var boardCameraObject = new GameObject("BoardCamera");
            var boardCamera = boardCameraObject.AddComponent<Camera>();
            boardCamera.depth = 0;
            boardCamera.allowHDR = false;
            boardCamera.allowMSAA = true;
            var framer = boardCameraObject.AddComponent<CameraFramer>();

            var boardRoot = new GameObject("BoardRoot");
            var presenter = boardRoot.AddComponent<BoardPresenter>();
            SetReference(presenter, "theme", theme);

            var canvasObject = new GameObject("Canvas", typeof(RectTransform));
            canvasObject.layer = LayerMask.NameToLayer("UI");
            canvasObject.AddComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            canvasObject.AddComponent<CanvasScaler>();
            canvasObject.AddComponent<GraphicRaycaster>();
            var ui = canvasObject.AddComponent<UIController>();
            SetReference(ui, "theme", theme);

            var controllerObject = new GameObject("GameController");
            var game = controllerObject.AddComponent<GameController>();
            var input = controllerObject.AddComponent<InputController>();
            SetReference(game, "presenter", presenter);
            SetReference(game, "input", input);
            SetReference(game, "ui", ui);
            SetReference(game, "boardCamera", framer);
            SetReference(input, "presenter", presenter);
            SetReference(input, "boardCamera", framer);
            SetReference(input, "ui", ui);

            EditorSceneManager.SaveScene(scene, GameplayScenePath);
        }

        // ---------------------------------------------------------------- Helpers

        static void SetReference(Object target, string field, Object value)
        {
            var serialized = new SerializedObject(target);
            var property = serialized.FindProperty(field);
            if (property == null) throw new System.MissingFieldException(target.GetType().Name, field);
            property.objectReferenceValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        static void SetInt(SerializedObject serialized, string name, int value)
        {
            var property = serialized.FindProperty(name);
            if (property != null) property.intValue = value;
            else Debug.LogWarning($"[ProjectSetup] Property {name} not found on {serialized.targetObject.GetType().Name}.");
        }

        static void SetBool(SerializedObject serialized, string name, bool value)
        {
            var property = serialized.FindProperty(name);
            if (property != null) property.boolValue = value;
            else Debug.LogWarning($"[ProjectSetup] Property {name} not found on {serialized.targetObject.GetType().Name}.");
        }

        static void SetFloat(SerializedObject serialized, string name, float value)
        {
            var property = serialized.FindProperty(name);
            if (property != null) property.floatValue = value;
            else Debug.LogWarning($"[ProjectSetup] Property {name} not found on {serialized.targetObject.GetType().Name}.");
        }

        public static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            if (!string.IsNullOrEmpty(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
