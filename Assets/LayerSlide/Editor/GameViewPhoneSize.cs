using System;
using System.Reflection;
using UnityEditor;

namespace LayerSlide.Editor
{
    /// <summary>
    /// Points the Game view at a real phone resolution (1080x2400, 20:9 portrait). Unity has no public API for
    /// Game view sizes, so this goes through the editor's internal GameViewSizes / GameView types. An existing
    /// custom size named "Phone" is resized in place; otherwise "Phone 1080x2400" is added.
    /// </summary>
    public static class GameViewPhoneSize
    {
        const int Width = 1080;
        const int Height = 2400;
        const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        [MenuItem("Tools/Layer Slide/Game View: Phone 1080x2400")]
        public static void Apply()
        {
            var assembly = typeof(EditorWindow).Assembly;
            var sizesType = assembly.GetType("UnityEditor.GameViewSizes");
            var sizes = typeof(ScriptableSingleton<>).MakeGenericType(sizesType).GetProperty("instance").GetValue(null);
            var group = sizesType.GetProperty("currentGroup", Any).GetValue(sizes);
            var groupType = group.GetType();
            var getSize = groupType.GetMethod("GetGameViewSize", Any);
            int total = (int)groupType.GetMethod("GetTotalCount", Any).Invoke(group, null);
            int builtIn = (int)groupType.GetMethod("GetBuiltinCount", Any).Invoke(group, null);

            int index = -1;
            for (int i = builtIn; i < total && index < 0; i++)
            {
                var size = getSize.Invoke(group, new object[] { i });
                var sizeType = size.GetType();
                string text = (string)sizeType.GetProperty("baseText", Any).GetValue(size);
                bool match = text == "Phone" || text == "Phone 1080x2400"
                    || (int)sizeType.GetProperty("width", Any).GetValue(size) == Width && (int)sizeType.GetProperty("height", Any).GetValue(size) == Height;
                if (!match) continue;

                // 1 = FixedResolution (0 would be an aspect ratio).
                sizeType.GetProperty("sizeType", Any).SetValue(size, Enum.ToObject(sizeType.GetProperty("sizeType", Any).PropertyType, 1));
                sizeType.GetProperty("width", Any).SetValue(size, Width);
                sizeType.GetProperty("height", Any).SetValue(size, Height);
                index = i;
            }

            if (index < 0)
            {
                var sizeClass = assembly.GetType("UnityEditor.GameViewSize");
                var kind = assembly.GetType("UnityEditor.GameViewSizeType");
                var created = sizeClass.GetConstructor(new[] { kind, typeof(int), typeof(int), typeof(string) })
                    .Invoke(new[] { Enum.ToObject(kind, 1), Width, Height, "Phone 1080x2400" });
                groupType.GetMethod("AddCustomSize", Any).Invoke(group, new[] { created });
                index = total;
            }

            sizesType.GetMethod("SaveToHDD", Any)?.Invoke(sizes, null);

            var gameViewType = assembly.GetType("UnityEditor.GameView");
            var gameView = EditorWindow.GetWindow(gameViewType);
            gameViewType.GetMethod("SizeSelectionCallback", Any).Invoke(gameView, new object[] { index, null });
            gameView.Repaint();
        }
    }
}
