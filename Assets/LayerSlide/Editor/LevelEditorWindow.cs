using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using LayerSlide.Core;
using LayerSlide.Presentation;
using UnityEditor;
using UnityEngine;

namespace LayerSlide.Editor
{
    /// <summary>
    /// IMGUI level editor (GDD 06 §9): level list on the left, grid in the middle, fields of the selection on the
    /// right. Validate, replay the recorded solution, search a solution with the editor budget and export JSON.
    /// Export stays disabled until static validation and the recorded solution replay both pass.
    /// </summary>
    public sealed class LevelEditorWindow : EditorWindow
    {
        const string LevelsFolder = "Assets/LayerSlide/Resources/Levels";
        const string SolutionsFolder = "Assets/LayerSlide/Resources/Solutions";
        const float CellPixels = 64f;
        const float GateBand = 22f;
        const int EditorMaxStates = 250000;
        const double EditorMaxSeconds = 10;

        enum Tool { Select, AddBlock, AddWall, AddGate }
        enum SelectionKind { None, Block, Wall, Gate }

        static readonly string[] Colors = { "Red", "Blue", "Yellow" };
        static readonly string[] Sides = { "Left", "Right", "Bottom", "Top" };

        string[] _levelIds = Array.Empty<string>();
        int _levelIndex = -1;
        LevelDefinition _definition;
        SolutionRecord _solution;
        Tool _tool;
        SelectionKind _selectionKind;
        int _selectionIndex = -1;
        Vector2 _listScroll, _inspectorScroll;

        // Validation / replay / search results; any edit resets them.
        List<string> _errors = new List<string>();
        bool _validated, _replayed;
        string _status = string.Empty;

        // Preview of the recorded solution or a found search path.
        Level _previewLevel;
        Board _previewBoard;
        List<SolverAction> _previewActions;
        int _previewStep;

        // Search state, stepped from EditorApplication.update.
        HintSolver _solver;
        Board _searchBoard;
        Stopwatch _searchClock;
        string _searchResult = "—";

        [MenuItem("Tools/Layer Slide/Level Editor")]
        public static void Open() => GetWindow<LevelEditorWindow>("Level Editor").Show();

        void OnEnable()
        {
            RefreshList();
            if (_levelIds.Length > 0 && _definition == null) Load(0);
        }

        void OnDisable() => StopSearch(cancelled: true);

        void RefreshList()
        {
            _levelIds = Directory.Exists(LevelsFolder)
                ? Directory.GetFiles(LevelsFolder, "*.json").Select(Path.GetFileNameWithoutExtension).OrderBy(id => id, StringComparer.Ordinal).ToArray()
                : Array.Empty<string>();
        }

        void Load(int index)
        {
            StopSearch(cancelled: true);
            _levelIndex = index;
            string id = _levelIds[index];
            _definition = JsonUtility.FromJson<LevelDefinition>(File.ReadAllText(Path.Combine(LevelsFolder, id + ".json")));
            string solutionPath = Path.Combine(SolutionsFolder, id + ".json");
            _solution = File.Exists(solutionPath) ? JsonUtility.FromJson<SolutionRecord>(File.ReadAllText(solutionPath)) : null;
            _selectionKind = SelectionKind.None;
            MarkDirty();
        }

        void MarkDirty()
        {
            _validated = _replayed = false;
            _errors.Clear();
            _status = string.Empty;
            _searchResult = "—";
            ResetPreview();
        }

        void ResetPreview()
        {
            _previewBoard = null;
            _previewActions = null;
            _previewStep = 0;
        }

        // ================================================================ Layout

        void OnGUI()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                DrawLevelList();
                using (new EditorGUILayout.VerticalScope())
                {
                    DrawToolbar();
                    if (_definition != null) DrawGrid();
                    DrawStatus();
                }
                using (new EditorGUILayout.VerticalScope(GUILayout.Width(280)))
                {
                    _inspectorScroll = EditorGUILayout.BeginScrollView(_inspectorScroll);
                    if (_definition != null) DrawInspector();
                    EditorGUILayout.EndScrollView();
                }
            }

            if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Delete && _selectionKind != SelectionKind.None)
            {
                DeleteSelection();
                Event.current.Use();
            }
        }

        void DrawLevelList()
        {
            using (new EditorGUILayout.VerticalScope(GUILayout.Width(110)))
            {
                EditorGUILayout.LabelField("Bölümler", EditorStyles.boldLabel);
                _listScroll = EditorGUILayout.BeginScrollView(_listScroll);
                for (int i = 0; i < _levelIds.Length; i++)
                {
                    var style = i == _levelIndex ? EditorStyles.toolbarButton : EditorStyles.miniButton;
                    if (GUILayout.Toggle(i == _levelIndex, _levelIds[i], style) && i != _levelIndex) Load(i);
                }
                EditorGUILayout.EndScrollView();
                if (GUILayout.Button("Yenile")) RefreshList();
            }
        }

        void DrawToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                _tool = (Tool)GUILayout.Toolbar((int)_tool, new[] { "Select", "AddBlock", "AddWall", "AddGate" }, EditorStyles.toolbarButton);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Delete", EditorStyles.toolbarButton)) DeleteSelection();
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Validate")) Validate();
                if (GUILayout.Button("Play Recorded Solution")) PlayRecordedSolution();
                if (_solver == null)
                {
                    if (GUILayout.Button("Search Solution")) StartSearch();
                }
                else if (GUILayout.Button("Cancel Search")) StopSearch(cancelled: true);
                if (GUILayout.Button("Reset Preview")) ResetPreview();
                using (new EditorGUI.DisabledScope(!_validated || !_replayed))
                    if (GUILayout.Button("Export JSON")) Export();
            }

            if (_previewActions != null)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("◀", GUILayout.Width(30))) SetPreviewStep(_previewStep - 1);
                    int step = EditorGUILayout.IntSlider(_previewStep, 0, _previewActions.Count);
                    if (step != _previewStep) SetPreviewStep(step);
                    if (GUILayout.Button("▶", GUILayout.Width(30))) SetPreviewStep(_previewStep + 1);
                }
                if (_previewStep > 0 && _previewStep <= _previewActions.Count)
                    EditorGUILayout.LabelField("Son eylem: " + Describe(_previewActions[_previewStep - 1]));
            }
        }

        void DrawStatus()
        {
            var d = _definition;
            if (d != null)
            {
                int cells = d.width * d.height - d.walls.Count;
                int footprint = d.blocks.Sum(b => b.width * b.height);
                EditorGUILayout.LabelField(
                    $"{d.width}×{d.height} · blok {d.blocks.Count} · katman {d.blocks.Sum(b => b.layers.Count)} · " +
                    $"doluluk {(cells > 0 ? footprint / (float)cells : 0f):P0} · duvar {d.walls.Count} · " +
                    $"kayıtlı çözüm {(_solution?.actions.Count.ToString() ?? "yok")} · arama {_searchResult}");
            }
            if (!string.IsNullOrEmpty(_status)) EditorGUILayout.HelpBox(_status, _errors.Count > 0 ? MessageType.Error : MessageType.Info);
            foreach (var error in _errors) EditorGUILayout.LabelField("• " + error, EditorStyles.wordWrappedMiniLabel);
        }

        // ================================================================ Grid

        void DrawGrid()
        {
            var d = _definition;
            float width = d.width * CellPixels + 2 * GateBand, height = d.height * CellPixels + 2 * GateBand;
            var area = GUILayoutUtility.GetRect(width, height, GUILayout.ExpandWidth(false));
            var board = new Rect(area.x + GateBand, area.y + GateBand, d.width * CellPixels, d.height * CellPixels);

            EditorGUI.DrawRect(area, Palette.Environment);
            for (int y = 0; y < d.height; y++)
            for (int x = 0; x < d.width; x++)
            {
                var cell = CellRect(board, x, y, 1, 1);
                bool wall = d.walls.Any(w => w.x == x && w.y == y);
                EditorGUI.DrawRect(Inset(cell, 1), wall ? Palette.Wall : Palette.Board);
                if (wall && _selectionKind == SelectionKind.Wall && d.walls.FindIndex(w => w.x == x && w.y == y) == _selectionIndex)
                    DrawOutline(cell, Color.white, 3);
            }

            for (int i = 0; i < d.gates.Count; i++)
            {
                var rect = GateRect(board, d.gates[i]);
                EditorGUI.DrawRect(rect, ColorOf(d.gates[i].color));
                GUI.Label(rect, d.gates[i].id, CenteredLabel(Color.white));
                if (_selectionKind == SelectionKind.Gate && _selectionIndex == i) DrawOutline(rect, Color.white, 2);
            }

            for (int i = 0; i < d.blocks.Count; i++)
                DrawBlock(board, i);

            HandleGridInput(board);
        }

        void DrawBlock(Rect board, int index)
        {
            var spec = _definition.blocks[index];
            int x = spec.x, y = spec.y, removed = 0;
            if (_previewBoard != null)
            {
                int runtime = _previewLevel.IndexOfBlock(spec.id);
                if (runtime < 0 || _previewBoard.IsRemoved(runtime)) return;
                x = _previewBoard.GetX(runtime);
                y = _previewBoard.GetY(runtime);
                removed = _previewBoard.GetRemovedLayers(runtime);
            }

            var rect = Inset(CellRect(board, x, y, spec.width, spec.height), 5);
            if (removed + 1 < spec.layers.Count)
                EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 8, rect.width, 8), ColorOf(spec.layers[removed + 1]));
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, rect.height - (removed + 1 < spec.layers.Count ? 8 : 0)),
                removed < spec.layers.Count ? ColorOf(spec.layers[removed]) : Color.gray);
            GUI.Label(rect, spec.id, CenteredLabel(Color.white));
            if (_selectionKind == SelectionKind.Block && _selectionIndex == index) DrawOutline(rect, Color.white, 3);
        }

        void HandleGridInput(Rect board)
        {
            var e = Event.current;
            if (e.type != EventType.MouseDown || e.button != 0) return;
            var d = _definition;
            var p = e.mousePosition;
            int cx = Mathf.FloorToInt((p.x - board.x) / CellPixels);
            int cy = d.height - 1 - Mathf.FloorToInt((p.y - board.y) / CellPixels);
            bool inside = board.Contains(p);

            switch (_tool)
            {
                case Tool.Select:
                    Select(p, board, cx, cy, inside);
                    break;
                case Tool.AddBlock when inside && IsFree(cx, cy):
                    d.blocks.Add(new BlockDefinition { id = NextBlockId(), x = cx, y = cy, width = 1, height = 1, layers = new List<string> { "Red" } });
                    SetSelection(SelectionKind.Block, d.blocks.Count - 1);
                    MarkDirty();
                    break;
                case Tool.AddWall when inside:
                    int existing = d.walls.FindIndex(w => w.x == cx && w.y == cy);
                    if (existing >= 0) d.walls.RemoveAt(existing);
                    else if (IsFree(cx, cy)) d.walls.Add(new CellData { x = cx, y = cy });
                    MarkDirty();
                    break;
                case Tool.AddGate when !inside:
                    AddGateAt(p, board);
                    break;
            }
            e.Use();
            Repaint();
        }

        void Select(Vector2 p, Rect board, int cx, int cy, bool inside)
        {
            var d = _definition;
            for (int i = 0; i < d.gates.Count; i++)
                if (GateRect(board, d.gates[i]).Contains(p)) { SetSelection(SelectionKind.Gate, i); return; }
            if (!inside) { SetSelection(SelectionKind.None, -1); return; }

            int block = d.blocks.FindIndex(b => cx >= b.x && cx < b.x + b.width && cy >= b.y && cy < b.y + b.height);
            if (block >= 0) { SetSelection(SelectionKind.Block, block); return; }
            int wall = d.walls.FindIndex(w => w.x == cx && w.y == cy);
            SetSelection(wall >= 0 ? SelectionKind.Wall : SelectionKind.None, wall);
        }

        void AddGateAt(Vector2 p, Rect board)
        {
            var d = _definition;
            string side;
            int start;
            if (p.x < board.x) { side = "Left"; start = d.height - 1 - Mathf.FloorToInt((p.y - board.y) / CellPixels); }
            else if (p.x > board.xMax) { side = "Right"; start = d.height - 1 - Mathf.FloorToInt((p.y - board.y) / CellPixels); }
            else if (p.y > board.yMax) { side = "Bottom"; start = Mathf.FloorToInt((p.x - board.x) / CellPixels); }
            else { side = "Top"; start = Mathf.FloorToInt((p.x - board.x) / CellPixels); }

            int edge = side == "Left" || side == "Right" ? d.height : d.width;
            if (start < 0 || start >= edge) return;
            d.gates.Add(new GateDefinition { id = NextGateId(), side = side, start = start, length = 1, color = "Red" });
            SetSelection(SelectionKind.Gate, d.gates.Count - 1);
            MarkDirty();
        }

        void SetSelection(SelectionKind kind, int index)
        {
            _selectionKind = kind;
            _selectionIndex = index;
        }

        void DeleteSelection()
        {
            var d = _definition;
            switch (_selectionKind)
            {
                case SelectionKind.Block: d.blocks.RemoveAt(_selectionIndex); break;
                case SelectionKind.Wall: d.walls.RemoveAt(_selectionIndex); break;
                case SelectionKind.Gate: d.gates.RemoveAt(_selectionIndex); break;
                default: return;
            }
            _selectionKind = SelectionKind.None;
            MarkDirty();
            Repaint();
        }

        // ================================================================ Inspector

        void DrawInspector()
        {
            var d = _definition;
            EditorGUILayout.LabelField("Bölüm", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            d.levelId = EditorGUILayout.TextField("levelId", d.levelId);
            d.contentVersion = Mathf.Max(1, EditorGUILayout.IntField("contentVersion", d.contentVersion));
            d.width = EditorGUILayout.IntSlider("width", d.width, 4, 5);
            d.height = EditorGUILayout.IntSlider("height", d.height, 4, 5);
            string tags = EditorGUILayout.TextField("tutorialTags", string.Join(",", d.tutorialTags));
            if (EditorGUI.EndChangeCheck())
            {
                d.tutorialTags = tags.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(t => t.Trim()).ToList();
                MarkDirty();
            }

            EditorGUILayout.Space();
            switch (_selectionKind)
            {
                case SelectionKind.Block when _selectionIndex < d.blocks.Count: DrawBlockFields(d.blocks[_selectionIndex]); break;
                case SelectionKind.Gate when _selectionIndex < d.gates.Count: DrawGateFields(d.gates[_selectionIndex]); break;
                case SelectionKind.Wall when _selectionIndex < d.walls.Count:
                    EditorGUILayout.LabelField("Duvar", EditorStyles.boldLabel);
                    EditorGUILayout.LabelField($"({d.walls[_selectionIndex].x}, {d.walls[_selectionIndex].y})");
                    break;
                default:
                    EditorGUILayout.HelpBox("Select: blok/duvar/kapı seç. AddBlock: hücreye tıkla. AddWall: hücreyi aç/kapat. AddGate: tahta dışındaki kenar bandına tıkla. Delete: seçimi sil.", MessageType.None);
                    break;
            }
        }

        void DrawBlockFields(BlockDefinition block)
        {
            EditorGUILayout.LabelField("Blok", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            block.id = EditorGUILayout.TextField("id", block.id);
            block.x = EditorGUILayout.IntField("x", block.x);
            block.y = EditorGUILayout.IntField("y", block.y);
            int shape = block.width == 2 ? 1 : block.height == 2 ? 2 : 0;
            shape = EditorGUILayout.Popup("şekil", shape, new[] { "1×1", "2×1", "1×2" });
            block.width = shape == 1 ? 2 : 1;
            block.height = shape == 2 ? 2 : 1;

            int count = EditorGUILayout.IntSlider("katman", block.layers.Count, 1, RuleConstants.MaxLayers);
            while (block.layers.Count < count) block.layers.Add(block.layers[block.layers.Count - 1] == "Red" ? "Blue" : "Red");
            while (block.layers.Count > count) block.layers.RemoveAt(block.layers.Count - 1);
            for (int i = 0; i < block.layers.Count; i++)
                block.layers[i] = Colors[EditorGUILayout.Popup(i == 0 ? "üst (aktif)" : "alt", Math.Max(0, Array.IndexOf(Colors, block.layers[i])), Colors)];
            if (EditorGUI.EndChangeCheck()) MarkDirty();
        }

        void DrawGateFields(GateDefinition gate)
        {
            EditorGUILayout.LabelField("Kapı", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            gate.id = EditorGUILayout.TextField("id", gate.id);
            gate.side = Sides[EditorGUILayout.Popup("kenar", Math.Max(0, Array.IndexOf(Sides, gate.side)), Sides)];
            gate.start = EditorGUILayout.IntField("start", gate.start);
            gate.length = Mathf.Max(1, EditorGUILayout.IntField("length", gate.length));
            gate.color = Colors[EditorGUILayout.Popup("renk", Math.Max(0, Array.IndexOf(Colors, gate.color)), Colors)];
            if (EditorGUI.EndChangeCheck()) MarkDirty();
        }

        // ================================================================ Actions

        void Validate()
        {
            _errors = new List<string>();
            _validated = LevelValidator.Validate(_definition, _errors);
            _status = _validated ? "Statik doğrulama geçti." : $"{_errors.Count} doğrulama hatası.";
        }

        void PlayRecordedSolution()
        {
            Validate();
            if (!_validated) return;
            var level = Level.Create(_definition);
            if (_solution == null) { _status = "Kayıtlı çözüm yok."; _replayed = false; return; }

            _replayed = SolutionReplayer.Replay(level, _solution, out string error);
            _status = _replayed ? $"Kayıtlı çözüm geçerli: {_solution.actions.Count} eylem, tahta boş." : "Replay başarısız: " + error;
            if (!_replayed) _errors.Add(error);

            var actions = new List<SolverAction>();
            foreach (var step in _solution.actions)
                actions.Add(new SolverAction { Block = level.IndexOfBlock(step.block), X = step.x, Y = step.y, Gate = string.IsNullOrEmpty(step.exit) ? -1 : level.IndexOfGate(step.exit) });
            StartPreview(level, actions);
        }

        void StartPreview(Level level, List<SolverAction> actions)
        {
            _previewLevel = level;
            _previewActions = actions;
            _previewBoard = new Board(level);
            _previewStep = 0;
        }

        void SetPreviewStep(int step)
        {
            step = Mathf.Clamp(step, 0, _previewActions.Count);
            _previewBoard.ResetToStart();
            for (int i = 0; i < step; i++)
            {
                var a = _previewActions[i];
                if (!RuleEngine.TryCreateCommand(_previewBoard, a.Block, a.X, a.Y, a.Gate, out var command)) { step = i; break; }
                RuleEngine.Apply(_previewBoard, command);
            }
            _previewStep = step;
            Repaint();
        }

        void StartSearch()
        {
            Validate();
            if (!_validated) return;
            var level = Level.Create(_definition);
            _searchBoard = _previewBoard != null && _previewLevel != null && _previewLevel.LevelId == level.LevelId ? _previewBoard : new Board(level);
            _solver = new HintSolver { MaxStates = EditorMaxStates };
            _solver.Begin(_searchBoard);
            _searchClock = Stopwatch.StartNew();
            _searchResult = "aranıyor…";
            EditorApplication.update += StepSearch;
        }

        void StepSearch()
        {
            if (_solver == null) return;
            var status = _solver.Step(Stopwatch.Frequency / 50); // 20 ms per editor tick
            if (status == SolverStatus.Running && _searchClock.Elapsed.TotalSeconds < EditorMaxSeconds)
            {
                Repaint();
                return;
            }

            if (status == SolverStatus.Running) status = SolverStatus.BudgetExceeded;
            _searchResult = status == SolverStatus.Found
                ? $"Found ({_solver.Path.Count} eylem, {_solver.DiscoveredStates} durum, {_searchClock.Elapsed.TotalSeconds:F2} sn)"
                : $"{status} ({_solver.DiscoveredStates} durum, {_searchClock.Elapsed.TotalSeconds:F2} sn)";
            if (status == SolverStatus.Found)
                StartPreview(_searchBoard.Level, new List<SolverAction>(_solver.Path));
            StopSearch(cancelled: false);
        }

        void StopSearch(bool cancelled)
        {
            if (_solver == null) return;
            EditorApplication.update -= StepSearch;
            if (cancelled)
            {
                _solver.Cancel();
                _searchResult = "Cancelled";
            }
            _solver.Clear();
            _solver = null;
            Repaint();
        }

        void Export()
        {
            string path = Path.Combine(LevelsFolder, _definition.levelId + ".json");
            File.WriteAllText(path, ToJson(_definition));
            AssetDatabase.ImportAsset(path);
            _status = $"Yazıldı: {path}";
            RefreshList();
        }

        /// <summary>Same layout as the GDD 03 sources: one block / gate per line.</summary>
        static string ToJson(LevelDefinition d)
        {
            string Quote(string s) => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
            string Int(int v) => v.ToString(CultureInfo.InvariantCulture);

            var json = new StringBuilder();
            json.Append("{\n");
            json.Append($"  \"schemaVersion\": {Int(d.schemaVersion)},\n");
            json.Append($"  \"levelId\": {Quote(d.levelId)},\n");
            json.Append($"  \"contentVersion\": {Int(d.contentVersion)},\n");
            json.Append($"  \"ruleVersion\": {Int(d.ruleVersion)},\n");
            json.Append($"  \"width\": {Int(d.width)},\n");
            json.Append($"  \"height\": {Int(d.height)},\n");
            json.Append("  \"walls\": [" + string.Join(",", d.walls.Select(w => $"{{\"x\":{Int(w.x)},\"y\":{Int(w.y)}}}")) + "],\n");
            json.Append("  \"blocks\": [\n" + string.Join(",\n", d.blocks.Select(b =>
                $"    {{\"id\":{Quote(b.id)},\"x\":{Int(b.x)},\"y\":{Int(b.y)},\"width\":{Int(b.width)},\"height\":{Int(b.height)},\"layers\":[{string.Join(",", b.layers.Select(Quote))}]}}")) + "\n  ],\n");
            json.Append("  \"gates\": [\n" + string.Join(",\n", d.gates.Select(g =>
                $"    {{\"id\":{Quote(g.id)},\"side\":{Quote(g.side)},\"start\":{Int(g.start)},\"length\":{Int(g.length)},\"color\":{Quote(g.color)}}}")) + "\n  ],\n");
            json.Append("  \"tutorialTags\": [" + string.Join(", ", d.tutorialTags.Select(Quote)) + "]\n");
            json.Append("}\n");
            return json.ToString();
        }

        // ================================================================ Helpers

        bool IsFree(int x, int y) =>
            !_definition.walls.Any(w => w.x == x && w.y == y)
            && !_definition.blocks.Any(b => x >= b.x && x < b.x + b.width && y >= b.y && y < b.y + b.height);

        string NextBlockId()
        {
            for (char c = 'A'; c <= 'Z'; c++)
                if (_definition.blocks.All(b => b.id != c.ToString())) return c.ToString();
            return "B" + _definition.blocks.Count;
        }

        string NextGateId()
        {
            for (int i = 1; ; i++)
                if (_definition.gates.All(g => g.id != "G" + i)) return "G" + i;
        }

        Rect CellRect(Rect board, int x, int y, int w, int h) =>
            new Rect(board.x + x * CellPixels, board.y + (_definition.height - y - h) * CellPixels, w * CellPixels, h * CellPixels);

        Rect GateRect(Rect board, GateDefinition gate)
        {
            switch (gate.side)
            {
                case "Left": return new Rect(board.x - GateBand, board.y + (_definition.height - gate.start - gate.length) * CellPixels, GateBand - 2, gate.length * CellPixels);
                case "Right": return new Rect(board.xMax + 2, board.y + (_definition.height - gate.start - gate.length) * CellPixels, GateBand - 2, gate.length * CellPixels);
                case "Bottom": return new Rect(board.x + gate.start * CellPixels, board.yMax + 2, gate.length * CellPixels, GateBand - 2);
                default: return new Rect(board.x + gate.start * CellPixels, board.y - GateBand, gate.length * CellPixels, GateBand - 2);
            }
        }

        static Rect Inset(Rect r, float by) => new Rect(r.x + by, r.y + by, r.width - 2 * by, r.height - 2 * by);

        static void DrawOutline(Rect r, Color color, float thickness)
        {
            EditorGUI.DrawRect(new Rect(r.x, r.y, r.width, thickness), color);
            EditorGUI.DrawRect(new Rect(r.x, r.yMax - thickness, r.width, thickness), color);
            EditorGUI.DrawRect(new Rect(r.x, r.y, thickness, r.height), color);
            EditorGUI.DrawRect(new Rect(r.xMax - thickness, r.y, thickness, r.height), color);
        }

        static Color ColorOf(string color) =>
            Level.TryParseColor(color, out var parsed) ? Palette.Of(parsed) : Color.magenta;

        static GUIStyle CenteredLabel(Color color) =>
            new GUIStyle(EditorStyles.boldLabel) { alignment = TextAnchor.MiddleCenter, normal = { textColor = color } };

        string Describe(SolverAction a)
        {
            string block = _previewLevel.Blocks[a.Block].Id;
            return a.Gate >= 0 ? $"{block} → ({a.X},{a.Y}), {_previewLevel.Gates[a.Gate].Id} kapısından çıkar" : $"{block} → ({a.X},{a.Y}) kaydır";
        }
    }
}
