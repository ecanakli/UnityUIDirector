using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Ecanakli.UIDirector.EditorTools
{
    // Shows one running root: layers, shield, queues, kept instances, preload leases and recent events.
    internal sealed class UIDirectorWindow : EditorWindow
    {
        private const string MenuPath = "Window/UIDirector";
        private const string WindowTitle = "UIDirector";
        private const long RefreshIntervalMilliseconds = 250;
        private const float EventListHeight = 240f;
        private const float DropdownMinWidth = 180f;
        private const float HelpButtonWidth = 24f;
        private const float CopyButtonWidth = 80f;
        private const float ContentIndent = 15f;
        private const int SmallFontSize = 10;
        private const string OutsidePlayModeMessage =
            "The window shows a running root. Enter Play Mode to see its layers, shield, queues and events.";

        private readonly List<ViewRoot> _roots = new List<ViewRoot>();
        private readonly List<string> _rootLabels = new List<string>();
        private readonly Dictionary<string, bool> _foldoutOpen = new Dictionary<string, bool>();
        private readonly List<Section> _layerSections = new List<Section>();
        private readonly List<Section> _queueSections = new List<Section>();

        private UIDirectorWindowModel _model;
        private ViewRoot _selected;

        private DropdownField _rootDropdown;
        private Button _copyButton;
        private HelpBox _help;
        private ScrollView _page;
        private Label _status;
        private VisualElement _layersHost;
        private VisualElement _queuesHost;
        private Label _noQueues;
        private Section _shield;
        private Section _kept;
        private Section _preload;
        private Section _events;

        [MenuItem(MenuPath)]
        private static void Open()
        {
            GetWindow<UIDirectorWindow>(WindowTitle);
        }

        private void OnEnable()
        {
            titleContent = new GUIContent(WindowTitle);
            _model = new UIDirectorWindowModel();
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        private void OnDisable()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        }

        private void CreateGUI()
        {
            var root = rootVisualElement;
            root.Add(BuildToolbar());

            _help = new HelpBox(string.Empty, HelpBoxMessageType.Info);
            root.Add(_help);

            _page = new ScrollView(ScrollViewMode.Vertical);
            _page.style.flexGrow = 1f;
            root.Add(_page);
            BuildPage();

            Tick();

            // Runs for the life of the window; outside Play Mode a tick only keeps the message in place.
            root.schedule.Execute(Tick).Every(RefreshIntervalMilliseconds);
        }

        private VisualElement BuildToolbar()
        {
            var toolbar = new Toolbar();

            _rootDropdown = new DropdownField(new List<string>(), -1);
            _rootDropdown.style.minWidth = DropdownMinWidth;
            _rootDropdown.RegisterValueChangedCallback(OnRootPicked);
            toolbar.Add(_rootDropdown);

            var spacer = new VisualElement();
            spacer.style.flexGrow = 1f;
            toolbar.Add(spacer);

            _copyButton = new ToolbarButton(CopyState) { text = "Copy state" };
            _copyButton.style.width = CopyButtonWidth;
            toolbar.Add(_copyButton);

            var help = new ToolbarButton(OpenDocumentation) { text = "?" };
            help.style.width = HelpButtonWidth;
            toolbar.Add(help);
            return toolbar;
        }

        private void BuildPage()
        {
            _status = new Label();
            _status.style.unityFontStyleAndWeight = FontStyle.Bold;
            _page.Add(_status);

            _layersHost = new VisualElement();
            _page.Add(_layersHost);

            _shield = CreateSection("section:shield", "Shield holders", 0);
            _page.Add(_shield.Foldout);

            _queuesHost = new VisualElement();
            _page.Add(_queuesHost);
            _noQueues = new Label("Queues: none");
            _noQueues.style.fontSize = SmallFontSize;
            _page.Add(_noQueues);

            _kept = CreateSection("section:kept", "Kept instances", 0);
            _page.Add(_kept.Foldout);

            _preload = CreateSection("section:preload", "Preload leases", 0);
            _page.Add(_preload.Foldout);

            // The events sit in a nested scroll view of fixed height.
            _events = CreateSection("section:events", "Events", SmallFontSize);
            var eventScroll = new ScrollView(ScrollViewMode.Vertical);
            eventScroll.style.height = EventListHeight;
            eventScroll.style.flexShrink = 0f;
            _events.Foldout.Add(eventScroll);
            _events.Lines = new LineList(eventScroll.contentContainer, SmallFontSize);
            _page.Add(_events.Foldout);
        }

        // Callbacks

        private void OnRootPicked(ChangeEvent<string> change)
        {
            var index = _rootLabels.IndexOf(change.newValue);
            if (index < 0 || index >= _roots.Count)
                return;

            _selected = _roots[index];
            Tick();
        }

        private void OnPlayModeStateChanged(PlayModeStateChange change)
        {
            // Nothing from the finished session is kept.
            if (change == PlayModeStateChange.ExitingPlayMode || change == PlayModeStateChange.EnteredEditMode)
            {
                _roots.Clear();
                _rootLabels.Clear();
                _selected = null;
                _model.Clear();
            }

            if (change == PlayModeStateChange.EnteredPlayMode || change == PlayModeStateChange.EnteredEditMode)
                Tick();
        }

        private void CopyState()
        {
            EditorGUIUtility.systemCopyBuffer = _model.Describe();
        }

        private static void OpenDocumentation()
        {
            Application.OpenURL(DocLinks.DocumentationRoot + "index.md");
        }

        // Refresh

        private void Tick()
        {
            if (_page == null || _model == null)
                return;

            if (!Application.isPlaying)
            {
                if (_model.HasNavigator || _roots.Count > 0)
                {
                    _roots.Clear();
                    _rootLabels.Clear();
                    _selected = null;
                    _model.Clear();
                }

                UpdateDropdown();
                ShowMessage(OutsidePlayModeMessage);
                return;
            }

            RefreshRoots();
            _model.Refresh(_selected);
            UpdateDropdown();

            if (!_model.HasNavigator)
            {
                ShowMessage(_model.StatusLine());
                return;
            }

            ShowContent();
            Apply();
        }

        private void ShowMessage(string message)
        {
            SetText(_help, message);
            _help.style.display = DisplayStyle.Flex;
            _page.style.display = DisplayStyle.None;
            _copyButton.SetEnabled(false);
        }

        private void ShowContent()
        {
            _help.style.display = DisplayStyle.None;
            _page.style.display = DisplayStyle.Flex;
            _copyButton.SetEnabled(true);
        }

        // Lists the roots of the loaded scenes; the selection follows the object, so it survives the list changing.
        private void RefreshRoots()
        {
            var found = FindObjectsByType<ViewRoot>(FindObjectsInactive.Include, FindObjectsSortMode.None);

            // Known roots keep their place, gone ones drop out and new ones are appended.
            var changed = false;
            for (var i = _roots.Count - 1; i >= 0; i--)
            {
                if (Array.IndexOf(found, _roots[i]) < 0)
                {
                    _roots.RemoveAt(i);
                    changed = true;
                }
            }

            for (var i = 0; i < found.Length; i++)
            {
                if (!_roots.Contains(found[i]))
                {
                    _roots.Add(found[i]);
                    changed = true;
                }
            }

            if (changed || _rootLabels.Count != _roots.Count)
            {
                _rootLabels.Clear();
                for (var i = 0; i < _roots.Count; i++)
                    _rootLabels.Add((i + 1) + ": " + _roots[i].name + " (" + _roots[i].gameObject.scene.name + ")");
            }

            if (_selected == null || !_roots.Contains(_selected))
                _selected = _roots.Count > 0 ? _roots[0] : null;
        }

        private void UpdateDropdown()
        {
            _rootDropdown.SetEnabled(Application.isPlaying && _roots.Count > 0);

            if (!SameChoices(_rootDropdown.choices, _rootLabels))
                _rootDropdown.choices = new List<string>(_rootLabels);

            var index = _roots.IndexOf(_selected);
            var label = index >= 0 ? _rootLabels[index] : null;
            if (_rootDropdown.value != label)
                _rootDropdown.SetValueWithoutNotify(label);
        }

        private void Apply()
        {
            var state = _model.State;
            SetText(_status, _model.StatusLine());
            ApplyLayers(state);
            ApplyShield(state);
            ApplyQueues();
            ApplyNames(_kept, "Kept instances", state.KeptViewIds);
            ApplyNames(_preload, "Preload leases", state.PreloadViewIds);
            ApplyEvents();
        }

        private void ApplyLayers(NavigatorState state)
        {
            var layers = state.Layers;
            SyncSections(_layerSections, _layersHost, layers.Count, 0);

            for (var i = 0; i < layers.Count; i++)
            {
                var layer = layers[i];
                var section = _layerSections[i];
                Retarget(section, "layer:" + layer.LayerId);
                SetText(section.Foldout, UIDirectorWindowModel.FormatLayerHeader(layer));

                section.Lines.Begin();
                if (layer.Requests.Count == 0)
                    section.Lines.Add("(no requests)");

                for (var j = 0; j < layer.Requests.Count; j++)
                    section.Lines.Add(UIDirectorWindowModel.FormatRequest(layer.Requests[j]));

                section.Lines.End();
            }
        }

        private void ApplyShield(NavigatorState state)
        {
            var holders = state.ShieldHolders;
            SetText(_shield.Foldout, "Shield holders (" + state.ShieldCount + ")");

            _shield.Lines.Begin();
            if (holders.Count == 0)
                _shield.Lines.Add("(none)");

            for (var i = 0; i < holders.Count; i++)
                _shield.Lines.Add(UIDirectorWindowModel.FormatShieldHolder(holders[i]));

            _shield.Lines.End();
        }

        private void ApplyQueues()
        {
            var queues = _model.Queues;
            _noQueues.style.display = queues.Count == 0 ? DisplayStyle.Flex : DisplayStyle.None;
            SyncSections(_queueSections, _queuesHost, queues.Count, 0);

            for (var i = 0; i < queues.Count; i++)
            {
                var queue = queues[i];
                var section = _queueSections[i];
                Retarget(section, "queue:" + (string.IsNullOrEmpty(queue.Name) ? queue.LayerId : queue.Name));
                SetText(section.Foldout, "Queue " + UIDirectorWindowModel.FormatQueueHeader(queue));

                var lines = section.Lines;
                lines.Begin();
                lines.Add("Held: " + (queue.Held ? "yes" : "no"));
                lines.Add("Gates: " + queue.GateCount);

                lines.Add("Holds (" + queue.Holds.Count + ")", true);
                for (var j = 0; j < queue.Holds.Count; j++)
                    lines.Add(UIDirectorWindowModel.FormatQueueHold(queue.Holds[j]));

                lines.Add(
                    "Presenting: " + (queue.HasPresenting ? UIDirectorWindowModel.FormatQueueItem(queue.Presenting) : "(none)"));

                lines.Add("Waiting (" + queue.Waiting.Count + ")", true);
                for (var j = 0; j < queue.Waiting.Count; j++)
                    lines.Add((j + 1) + ". " + UIDirectorWindowModel.FormatQueueItem(queue.Waiting[j]));

                lines.End();
            }
        }

        private static void ApplyNames(Section section, string title, List<string> names)
        {
            SetText(section.Foldout, title + " (" + names.Count + ")");

            section.Lines.Begin();
            if (names.Count == 0)
                section.Lines.Add("(none)");

            for (var i = 0; i < names.Count; i++)
                section.Lines.Add(names[i]);

            section.Lines.End();
        }

        private void ApplyEvents()
        {
            var events = _model.Events;
            SetText(_events.Foldout, "Events (" + events.Count + ", newest first)");

            _events.Lines.Begin();
            if (events.Count == 0)
                _events.Lines.Add("(none)");

            for (var i = 0; i < events.Count; i++)
                _events.Lines.Add(_model.GetEventLine(i));

            _events.Lines.End();
        }

        // Sections

        private Section CreateSection(string key, string title, int fontSize)
        {
            var section = new Section { Key = key, Foldout = new Foldout { text = title } };
            section.Foldout.SetValueWithoutNotify(IsOpen(key));
            section.Foldout.RegisterValueChangedCallback(change =>
            {
                // Foldout events bubble; only this foldout's own toggle counts.
                if (change.target == section.Foldout)
                    _foldoutOpen[section.Key] = change.newValue;
            });

            var host = new VisualElement();
            host.style.marginLeft = ContentIndent;
            section.Foldout.Add(host);
            section.Lines = new LineList(host, fontSize);
            return section;
        }

        // Grows or shrinks the sections to count; removed foldouts leave the hierarchy.
        private void SyncSections(List<Section> sections, VisualElement host, int count, int fontSize)
        {
            while (sections.Count > count)
            {
                var last = sections.Count - 1;
                sections[last].Foldout.RemoveFromHierarchy();
                sections.RemoveAt(last);
            }

            while (sections.Count < count)
            {
                var section = CreateSection(string.Empty, string.Empty, fontSize);
                host.Add(section.Foldout);
                sections.Add(section);
            }
        }

        // A reused section may now show another layer or queue; its open state follows the key.
        private void Retarget(Section section, string key)
        {
            if (section.Key == key)
                return;

            section.Key = key;
            section.Foldout.SetValueWithoutNotify(IsOpen(key));
        }

        private bool IsOpen(string key)
        {
            bool open;
            return !_foldoutOpen.TryGetValue(key, out open) || open;
        }

        private static void SetText(TextElement element, string text)
        {
            if (element.text != text)
                element.text = text;
        }

        private static void SetText(HelpBox helpBox, string text)
        {
            if (helpBox.text != text)
                helpBox.text = text;
        }

        private static void SetText(Foldout foldout, string text)
        {
            if (foldout.text != text)
                foldout.text = text;
        }

        private static bool SameChoices(List<string> current, List<string> wanted)
        {
            if (current == null || current.Count != wanted.Count)
                return false;

            for (var i = 0; i < wanted.Count; i++)
            {
                if (current[i] != wanted[i])
                    return false;
            }

            return true;
        }

        private sealed class Section
        {
            public string Key;
            public Foldout Foldout;
            public LineList Lines;
        }

        // Label rows filled in place: labels are reused, added when needed and removed when in excess.
        private sealed class LineList
        {
            private readonly VisualElement _container;
            private readonly int _fontSize;
            private int _count;

            public LineList(VisualElement container, int fontSize)
            {
                _container = container;
                _fontSize = fontSize;
            }

            public void Begin()
            {
                _count = 0;
            }

            public void Add(string text, bool bold = false)
            {
                Label label;
                if (_count < _container.childCount)
                {
                    label = (Label)_container[_count];
                }
                else
                {
                    label = new Label();
                    if (_fontSize > 0)
                        label.style.fontSize = _fontSize;

                    _container.Add(label);
                }

                if (label.text != text)
                    label.text = text;

                label.style.unityFontStyleAndWeight = bold ? FontStyle.Bold : FontStyle.Normal;
                _count++;
            }

            public void End()
            {
                while (_container.childCount > _count)
                    _container.RemoveAt(_container.childCount - 1);
            }
        }
    }
}
