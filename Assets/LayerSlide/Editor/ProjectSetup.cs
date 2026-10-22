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
        const string ToyShader = "LayerSlide/Toy";

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
            AssetBaker.BakeBackgroundTextures();
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

            // Molded "candy plastic" blocks (bright cap, deeper sides, raised panel); the board is a matte toy tray.
            theme.red = ToyMaterial("Red", Palette.Red, Palette.Darken(Palette.Red, 0.30f), 0.07f, 0.5f, 0.35f);
            theme.blue = ToyMaterial("Blue", Palette.Blue, Palette.Darken(Palette.Blue, 0.30f), 0.07f, 0.5f, 0.35f);
            theme.yellow = ToyMaterial("Yellow", Palette.Yellow, Palette.Darken(Palette.Yellow, 0.24f), 0.06f, 0.5f, 0.35f);
            theme.board = ToyMaterial("Board", Palette.Tray, Palette.PanelLip, 0f, 0.15f, 0f);
            theme.boardCell = ToyMaterial("BoardCell", Palette.BoardCell, Palette.Darken(Palette.BoardCell, 0.12f), 0.025f, 0.08f, 0f);
            theme.boardCellAlt = ToyMaterial("BoardCellAlt", Palette.BoardCellAlt, Palette.Darken(Palette.BoardCellAlt, 0.12f), 0.02f, 0.08f, 0f);
            theme.boardWell = ToyMaterial("BoardWell", Palette.BoardWell, Palette.Darken(Palette.BoardWell, 0.15f), 0f, 0f, 0f);
            theme.wall = ToyMaterial("Wall", Palette.Wall, Palette.Darken(Palette.Wall, 0.22f), 0.06f, 0.3f, 0.25f);
            theme.perimeter = ToyMaterial("Perimeter", Palette.PanelInset, Palette.PanelLip, 0.03f, 0.25f, 0.15f);
            theme.white = UnlitMaterial("UnlitWhite", Color.white);
            theme.dark = UnlitMaterial("UnlitDark", Palette.Text);
            theme.whiteTransparent = TransparentUnlitMaterial("UnlitWhiteTransparent", new Color(1f, 1f, 1f, 0.55f));
            theme.background = UnlitMaterial("SkyBackground", Color.white);
            theme.background.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(AssetBaker.SkyGradientPath));
            theme.blobShadow = TransparentUnlitMaterial("BlobShadow", new Color(0.16f, 0.08f, 0.42f, 0.32f));
            theme.blobShadow.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(AssetBaker.BlobShadowPath));
            theme.gloss = TransparentUnlitMaterial("Gloss", new Color(1f, 1f, 1f, 0.34f));
            theme.meshBank = AssetBaker.BakeMeshes();

            var fallback = FontAsset("NotoSans-SemiBold");
            theme.bodyFont = FontAsset("Baloo2-SemiBold", fallback);
            theme.displayFont = FontAsset("Baloo2-ExtraBold", fallback);
            // Titles: thick dark outline plus a hard drop shadow. Buttons: a softer shadow only.
            theme.titleTextMaterial = TextPreset(theme.displayFont, "Baloo2-ExtraBold Title", 0.24f, Palette.TextOutline,
                new Color(0.10f, 0.05f, 0.30f, 0.55f), -0.9f, 0.05f, 0.25f);
            theme.buttonTextMaterial = TextPreset(theme.displayFont, "Baloo2-ExtraBold Button", 0f, Color.clear,
                new Color(0.10f, 0.05f, 0.30f, 0.30f), -0.75f, 0.1f, 0f);
            EditorUtility.SetDirty(theme);
        }

        /// <summary>SDF material preset (outline and/or underlay) sharing the font's atlas.</summary>
        static Material TextPreset(TMP_FontAsset font, string name, float outlineWidth, Color outlineColor, Color underlayColor,
            float underlayOffsetY, float underlaySoftness, float underlayDilate)
        {
            string path = $"{Root}/Fonts/{name}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(font.material) { name = name };
                AssetDatabase.CreateAsset(material, path);
            }
            material.shader = font.material.shader;
            material.SetTexture(ShaderUtilities.ID_MainTex, font.atlasTexture);
            material.SetFloat(ShaderUtilities.ID_GradientScale, font.atlasPadding + 1);
            material.SetFloat(ShaderUtilities.ID_TextureWidth, font.atlasWidth);
            material.SetFloat(ShaderUtilities.ID_TextureHeight, font.atlasHeight);

            material.SetColor(ShaderUtilities.ID_OutlineColor, outlineColor);
            material.SetFloat(ShaderUtilities.ID_OutlineWidth, outlineWidth);
            if (outlineWidth > 0f) material.EnableKeyword(ShaderUtilities.Keyword_Outline);
            else material.DisableKeyword(ShaderUtilities.Keyword_Outline);
            // Dilate by the outline so the outline grows outward and letter counters stay open.
            material.SetFloat(ShaderUtilities.ID_FaceDilate, outlineWidth * 0.6f);

            material.EnableKeyword(ShaderUtilities.Keyword_Underlay);
            material.SetColor(ShaderUtilities.ID_UnderlayColor, underlayColor);
            material.SetFloat(ShaderUtilities.ID_UnderlayOffsetX, 0f);
            material.SetFloat(ShaderUtilities.ID_UnderlayOffsetY, underlayOffsetY);
            material.SetFloat(ShaderUtilities.ID_UnderlaySoftness, underlaySoftness);
            material.SetFloat(ShaderUtilities.ID_UnderlayDilate, underlayDilate + outlineWidth);
            EditorUtility.SetDirty(material);
            return material;
        }

        /// <summary>LayerSlide/Toy material: top and side colors, panel lift, plastic highlight and contact darkening.</summary>
        static Material ToyMaterial(string name, Color top, Color side, float panel, float highlight, float contact)
        {
            var material = LoadOrCreateMaterial(name, ToyShader);
            material.shader = Shader.Find(ToyShader); // older materials were URP Lit
            material.SetColor("_BaseColor", top);
            material.SetColor("_SideColor", side);
            material.SetColor("_ShadowTint", new Color(0.66f, 0.58f, 0.95f));
            material.SetFloat("_PanelLight", panel);
            material.SetFloat("_SpecStrength", highlight);
            material.SetFloat("_SpecSize", 6f);
            material.SetFloat("_RimStrength", 0.14f);
            material.SetFloat("_ContactAO", contact);
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

        /// <summary>
        /// Dynamic SDF font asset backed by a bundled font file, pre-warmed with Turkish glyphs. The 10 px padding
        /// leaves room for the thick title outline.
        /// </summary>
        static TMP_FontAsset FontAsset(string fileName, TMP_FontAsset fallback = null)
        {
            string assetPath = $"{Root}/Fonts/{fileName} SDF.asset";
            var existing = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(assetPath);
            if (existing != null)
            {
                SetFallback(existing, fallback);
                return existing;
            }

            var font = AssetDatabase.LoadAssetAtPath<Font>($"{Root}/Fonts/{fileName}.ttf");
            if (font == null) throw new FileNotFoundException($"Font {fileName}.ttf is missing.");

            var asset = TMP_FontAsset.CreateFontAsset(font, 72, 10, GlyphRenderMode.SDFAA, 1024, 1024, AtlasPopulationMode.Dynamic, false);
            asset.name = fileName + " SDF";
            AssetDatabase.CreateAsset(asset, assetPath);
            asset.material.name = fileName + " SDF Material";
            asset.atlasTextures[0].name = fileName + " SDF Atlas";
            AssetDatabase.AddObjectToAsset(asset.material, asset);
            AssetDatabase.AddObjectToAsset(asset.atlasTextures[0], asset);
            asset.TryAddCharacters(PrewarmCharacters);
            SetFallback(asset, fallback);
            EditorUtility.SetDirty(asset);
            return asset;
        }

        static void SetFallback(TMP_FontAsset asset, TMP_FontAsset fallback)
        {
            if (fallback == null || asset == fallback) return;
            asset.fallbackFontAssetTable ??= new System.Collections.Generic.List<TMP_FontAsset>();
            if (asset.fallbackFontAssetTable.Contains(fallback)) return;
            asset.fallbackFontAssetTable.Add(fallback);
            EditorUtility.SetDirty(asset);
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
            title.font = theme.displayFont;
            if (theme.titleTextMaterial != null) title.fontSharedMaterial = theme.titleTextMaterial;
            title.fontSize = 40f;
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

            // Soft three-color ambient (lavender sky, cooler sides, violet bounce) under one warm key light.
            RenderSettings.skybox = null;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.86f, 0.83f, 1.00f) * 0.85f;
            RenderSettings.ambientEquatorColor = Palette.Ambient * 0.62f;
            RenderSettings.ambientGroundColor = new Color(0.45f, 0.38f, 0.78f) * 0.5f;
            RenderSettings.fog = false;

            var light = new GameObject("DirectionalLight").AddComponent<Light>();
            light.type = LightType.Directional;
            light.transform.rotation = Quaternion.Euler(58f, -28f, 0f);
            light.intensity = 1.05f;
            light.color = new Color(1f, 0.97f, 0.92f);
            light.shadows = LightShadows.Soft;
            light.shadowStrength = 0.55f;

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
            SetReference(framer, "background", theme.background);

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
