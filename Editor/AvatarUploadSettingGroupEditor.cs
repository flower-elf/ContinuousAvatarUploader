using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using VRC.SDK3.Avatars.Components;
using Object = UnityEngine.Object;

namespace Anatawa12.ContinuousAvatarUploader.Editor
{
    [CustomEditor(typeof(AvatarUploadSettingGroup))]
    public class AvatarUploadSettingGroupEditor : UnityEditor.Editor
    {
        private AvatarUploadSettingGroup _asset;
        private Dictionary<int, CreateDescriptorContainer> _inspectorsDoctionary = new Dictionary<int, CreateDescriptorContainer>();
        private List<CreateDescriptorContainer> _inspectors = new List<CreateDescriptorContainer>();
        private VisualElement _inspector;
        private ObjectField _avatarToAddField;
        private Button _addAvatarButton;
        private const int CreatePerFrame = 5;
        private const int CreateInitial = 20;

        public override VisualElement CreateInspectorGUI()
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            _asset = (AvatarUploadSettingGroup)target;

            var root = new VisualElement
            {
                name = "RootElement"
            };
            _inspector = new VisualElement
            {
                name = "Inspectors"
            };

            var header = new VisualElement { name = "Header", style = { marginBottom = 6 } };
            header.Add(new Label("Avatar Upload Settings")
            {
                style = { unityFontStyleAndWeight = FontStyle.Bold }
            });

            var hasAvatarWithAllPlatformsDisabled = _asset.avatars.Any(avatar => !avatar.ios.enabled && !avatar.quest.enabled && !avatar.windows.enabled);
            if (hasAvatarWithAllPlatformsDisabled)
            {
                header.Add(new HelpBox("Some avatars have all platforms disabled. This is fine if intentional.",
                    HelpBoxMessageType.Warning));
            }

            header.Add(ContinuousAvatarUploader.UploadButtonGui(new[] { _asset }, Repaint));

            RecreateInspectors(throttled: true);
            CreateInspectorElementsThrottled();

            var trailer = new VisualElement { name = "Trailer", style = { marginTop = 6 } };
            _avatarToAddField = new ObjectField("Avatar to Add")
            {
                objectType = typeof(VRCAvatarDescriptor),
                allowSceneObjects = true,
            };
            _avatarToAddField.AddToClassList("unity-base-field__aligned");
            _avatarToAddField.style.marginBottom = 2;
            _addAvatarButton = new Button(AddAvatar) { text = "Add Avatar" };
            _addAvatarButton.SetEnabled(false);
            _avatarToAddField.RegisterValueChangedCallback(evt => _addAvatarButton.SetEnabled(evt.newValue != null));
            trailer.Add(_avatarToAddField);
            trailer.Add(_addAvatarButton);

            root.Add(header);
            root.Add(_inspector);
            root.Add(trailer);

            UnityEngine.Debug.Log($"CreateInspectorGUI took {stopwatch.ElapsedMilliseconds}ms");

            return root;
        }

        private void AddAvatar()
        {
            var avatarDescriptor = _avatarToAddField.value as VRCAvatarDescriptor;
            if (!avatarDescriptor) return;

            var newObj = ScriptableObject.CreateInstance<AvatarUploadSetting>();
            newObj.avatarDescriptor = new MaySceneReference(avatarDescriptor);
            newObj.name = newObj.avatarName = avatarDescriptor.gameObject.name;

            ArrayUtility.Add(ref _asset.avatars, newObj);
            EditorUtility.SetDirty(_asset);
            AssetDatabase.AddObjectToAsset(newObj, _asset);
            AssetDatabase.SaveAssetIfDirty(newObj);
            _avatarToAddField.value = null;

            RecreateInspectors();
        }

        private void CreateInspectorElementsThrottled()
        {
            var index = 0;

            for (var i = 0; i < CreateInitial; i++)
            {
                if (index >= _inspectors.Count) return;
                _inspectors[index].CreateInspectorElement();
                index++;
            }

            void CreateFrame()
            {
                for (var i = 0; i < CreatePerFrame; i++)
                {
                    if (index >= _inspectors.Count) return;
                    _inspectors[index].CreateInspectorElement();
                    index++;
                }

                EditorApplication.delayCall += CreateFrame;
            }

            EditorApplication.delayCall += CreateFrame;
        }

        void RecreateInspectors() => RecreateInspectors(false);

        void RecreateInspectors(bool throttled)
        {
            _inspector.Clear();
            _inspectors.Clear();
            var instanceIds = new HashSet<int>();
            foreach (var assetAvatar in _asset.avatars)
            {
                var instanceId = assetAvatar.GetInstanceID();
                instanceIds.Add(instanceId);
                if (!_inspectorsDoctionary.TryGetValue(instanceId, out var container))
                {
                    _inspectorsDoctionary.Add(instanceId,
                        container = new CreateDescriptorContainer(_asset, assetAvatar));
                    container.OnReorder += RecreateInspectors;
                    if (!throttled) container.CreateInspectorElement();
                }

                _inspector.Add(container);
                _inspectors.Add(container);
            }

            foreach (var i in _inspectorsDoctionary.Keys.ToArray())
                if (!instanceIds.Contains(i))
                    _inspectorsDoctionary.Remove(i);
        }
    }

    class CreateDescriptorContainer : VisualElement
    {
        public event Action OnReorder;
        private readonly AvatarUploadSetting _setting;
        private readonly VisualElement _inspectorElementContainer;

        public CreateDescriptorContainer(AvatarUploadSettingGroup group, AvatarUploadSetting setting)
        {
            _setting = setting;

            var indexLabel = new Label();
            void UpdateIndexLabel()
            {
                int index = System.Array.IndexOf(group.avatars, setting);
                indexLabel.text = index < 0 ? "Avatar" : $"Avatar #{index}";
            }
            UpdateIndexLabel();
            Add(indexLabel);

            Add(_inspectorElementContainer = new VisualElement());

            var removeButton = new Button(() =>
            {
                ArrayUtility.Remove(ref group.avatars, setting);
                EditorUtility.SetDirty(group);
                Object.DestroyImmediate(setting, true);
                AssetDatabase.SaveAssetIfDirty(group);

                OnReorder?.Invoke();
            })
            {
                text = "Remove Avatar",
            };
            removeButton.style.flexGrow = 1;
            removeButton.style.marginTop = 2;
            Add(removeButton);

            // reorder buttons on one row: up on the left, down on the right
            var orderRow = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Row,
                    justifyContent = Justify.SpaceBetween,
                    marginTop = 2,
                    marginBottom = 0,
                }
            };
            var upButton = new Button(() =>
            {
                var index = System.Array.IndexOf(group.avatars, setting);
                Debug.Assert(index != -1, nameof(index) + " != -1");
                var temp = group.avatars[index - 1];
                group.avatars[index - 1] = setting;
                group.avatars[index] = temp;
                EditorUtility.SetDirty(group);

                OnReorder?.Invoke();
            })
            {
                text = "▲",
                tooltip = "Move up",
            };
            upButton.style.width = 30;
            var downButton = new Button(() =>
            {
                var index = System.Array.IndexOf(group.avatars, setting);
                Debug.Assert(index != -1, nameof(index) + " != -1");
                var temp = group.avatars[index + 1];
                group.avatars[index + 1] = setting;
                group.avatars[index] = temp;
                EditorUtility.SetDirty(group);

                OnReorder?.Invoke();
            })
            {
                text = "▼",
                tooltip = "Move down",
            };
            downButton.style.width = 30;

            void UpdateButtonsEnabled()
            {
                var index = System.Array.IndexOf(group.avatars, setting);
                upButton.SetEnabled(index > 0);
                downButton.SetEnabled(index >= 0 && index < group.avatars.Length - 1);
            }

            orderRow.Add(upButton);
            orderRow.Add(downButton);
            Add(orderRow);
            var separator = new VisualElement
            {
                name = "separator",
                style =
                {
                    height = 18,
                    justifyContent = Justify.Center,
                }
            };
            separator.Add(new VisualElement
            {
                style =
                {
                    height = 1,
                    backgroundColor = new Color(0.5f, 0.5f, 0.5f, 1),
                }
            });
            Add(separator);

            OnReorder += () =>
            {
                UpdateIndexLabel();
                UpdateButtonsEnabled();
            };

            UpdateButtonsEnabled();
            RegisterCallback<AttachToPanelEvent>(_ => UpdateButtonsEnabled());
        }

        public void CreateInspectorElement()
        {
            if (_inspectorElementContainer.childCount != 0) return;
            _inspectorElementContainer.Add(new InspectorElement(_setting));
        }
    }
}
