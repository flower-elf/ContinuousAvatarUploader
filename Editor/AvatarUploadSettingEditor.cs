using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine.SceneManagement;
using JetBrains.Annotations;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.UIElements;
using VRC.SDK3.Avatars.Components;
using Object = UnityEngine.Object;

namespace Anatawa12.ContinuousAvatarUploader.Editor
{
    [CustomEditor(typeof(AvatarUploadSetting))]
    [CanEditMultipleObjects]
    public class AvatarUploadSettingEditor : UnityEditor.Editor
    {
        private VRCAvatarDescriptor _cachedAvatar;
        private bool _settingAvatar;
        [CanBeNull] private static PreviewCameraManager _previewCameraManager;

        private SerializedProperty _name = null!;
        private SerializedProperty _avatarName = null!;
        private SerializedProperty _avatarDescriptor = null!;
        private SerializedProperty _windows = null!;
        private SerializedProperty _quest = null!;
        private SerializedProperty _ios = null!;

        private void OnEnable()
        {
            _name = serializedObject.FindProperty("m_Name");
            _avatarName = serializedObject.FindProperty(nameof(AvatarUploadSetting.avatarName));
            _avatarDescriptor = serializedObject.FindProperty(nameof(AvatarUploadSetting.avatarDescriptor));

            _windows = serializedObject.FindProperty(nameof(AvatarUploadSetting.windows));
            _quest = serializedObject.FindProperty(nameof(AvatarUploadSetting.quest));
            _ios = serializedObject.FindProperty(nameof(AvatarUploadSetting.ios));
        }

        public override VisualElement CreateInspectorGUI()
        {
            var root = new VisualElement { name = "AvatarUploadSettingEditor" };

            // Avatar descriptor section.
            // MaySceneReference resolution relies on IMGUI object fields with scene references,
            // so this part stays an IMGUIContainer.
            root.Add(new IMGUIContainer(DrawAvatarDescriptorSection));

            root.Add(DrawPlatformWarningAndUploadButton());

            // Platform-specific settings: UI Toolkit replicating the original
            // OnGUI nesting (enabled toggle > update image / versioning scopes).
            root.Add(CreatePlatformInfoSection(Labels.PCWindows, _windows));
            root.Add(CreatePlatformInfoSection(Labels.QuestAndroid, _quest));
            root.Add(CreatePlatformInfoSection(Labels.IOS, _ios));

            // Camera preview section (IMGUI-only rendering).
            root.Add(new IMGUIContainer(DrawCameraSection));

            return root;
        }

        private VisualElement CreatePlatformInfoSection(GUIContent name, SerializedProperty infoProp)
        {
            var root = new VisualElement { name = "platformInfo_" + name.text };

            var enabled = infoProp.FindPropertyRelative(nameof(PlatformSpecificInfo.enabled));
            var updateImage = infoProp.FindPropertyRelative(nameof(PlatformSpecificInfo.updateImage));
            var imageTakeEditorMode = infoProp.FindPropertyRelative(nameof(PlatformSpecificInfo.imageTakeEditorMode));
            var versioningEnabled = infoProp.FindPropertyRelative(nameof(PlatformSpecificInfo.versioningEnabled));
            var versionNamePrefix = infoProp.FindPropertyRelative(nameof(PlatformSpecificInfo.versionNamePrefix));
            var gitEnabled = infoProp.FindPropertyRelative(nameof(PlatformSpecificInfo.gitEnabled));
            var tagPrefix = infoProp.FindPropertyRelative(nameof(PlatformSpecificInfo.tagPrefix));
            var tagSuffix = infoProp.FindPropertyRelative(nameof(PlatformSpecificInfo.tagSuffix));

            // [enabled] "PC Windows"
            var enabledToggle = new Toggle(name.text) { tooltip = name.tooltip };
            enabledToggle.BindProperty(enabled);
            enabledToggle.style.marginBottom = 2;
            ContinuousAvatarUploader.MakeCheckboxLeft(enabledToggle);
            root.Add(enabledToggle);

            // indented scope shown while enabled
            var enabledScope = new VisualElement
            {
                name = "enabledScope",
                style = { marginLeft = 14 },
            };
            root.Add(enabledScope);

            // [updateImage] "Update Image on Upload"
            var updateImageToggle = new Toggle(Labels.UpdateImage.text) { tooltip = Labels.UpdateImage.tooltip };
            updateImageToggle.BindProperty(updateImage);
            updateImageToggle.style.marginBottom = 2;
            ContinuousAvatarUploader.MakeCheckboxLeft(updateImageToggle);
            enabledScope.Add(updateImageToggle);

            // "Take Image In" enum field, shown while updateImage
            var takeImageInField = new EnumField(Labels.TakeImageIn.text);
            takeImageInField.Init((ImageTakeEditorMode)imageTakeEditorMode.intValue);
            takeImageInField.BindProperty(imageTakeEditorMode);
            takeImageInField.AddToClassList("unity-base-field__aligned");
            takeImageInField.style.marginLeft = 14;
            takeImageInField.style.marginBottom = 2;
            enabledScope.Add(takeImageInField);

            // [versioningEnabled] "Versioning System"
            var versioningToggle = new Toggle(Labels.VersioningSystem.text) { tooltip = Labels.VersioningSystem.tooltip };
            versioningToggle.BindProperty(versioningEnabled);
            versioningToggle.style.marginBottom = 2;
            ContinuousAvatarUploader.MakeCheckboxLeft(versioningToggle);
            enabledScope.Add(versioningToggle);

            // versioning scope shown while versioningEnabled
            var versioningScope = new VisualElement { name = "versioningScope", style = { marginLeft = 14 } };
            enabledScope.Add(versioningScope);

            var versionPrefixField = new TextField(Labels.VersionNamePrefix.text);
            versionPrefixField.BindProperty(versionNamePrefix);
            versionPrefixField.AddToClassList("unity-base-field__aligned");
            versionPrefixField.style.marginBottom = 2;
            versioningScope.Add(versionPrefixField);

            var versionPreview = new Label { name = "versionPreview" };
            versioningScope.Add(versionPreview);

            // [gitEnabled] "git tagging"
            var gitToggle = new Toggle(Labels.GitTagging.text) { tooltip = Labels.GitTagging.tooltip };
            gitToggle.BindProperty(gitEnabled);
            gitToggle.style.marginBottom = 2;
            ContinuousAvatarUploader.MakeCheckboxLeft(gitToggle);
            versioningScope.Add(gitToggle);

            // git scope shown while gitEnabled
            var gitScope = new VisualElement { name = "gitScope", style = { marginLeft = 14 } };
            versioningScope.Add(gitScope);

            var tagPrefixField = new TextField(Labels.TagPrefix.text);
            tagPrefixField.BindProperty(tagPrefix);
            tagPrefixField.AddToClassList("unity-base-field__aligned");
            tagPrefixField.style.marginBottom = 2;
            gitScope.Add(tagPrefixField);

            var tagSuffixField = new TextField(Labels.TagSuffix.text);
            tagSuffixField.BindProperty(tagSuffix);
            tagSuffixField.AddToClassList("unity-base-field__aligned");
            tagSuffixField.style.marginBottom = 2;
            gitScope.Add(tagSuffixField);

            var tagPreview = new Label { name = "tagPreview" };
            gitScope.Add(tagPreview);

            void UpdateScopeVisibility()
            {
                var enabledValue = enabled.boolValue || enabled.hasMultipleDifferentValues;
                enabledScope.SetEnabled(!enabled.hasMultipleDifferentValues);
                enabledScope.style.display = enabledValue ? DisplayStyle.Flex : DisplayStyle.None;

                var updateImageValue = updateImage.boolValue || updateImage.hasMultipleDifferentValues;
                takeImageInField.SetEnabled(!updateImage.hasMultipleDifferentValues);
                takeImageInField.style.display = updateImageValue ? DisplayStyle.Flex : DisplayStyle.None;

                var versioningValue = versioningEnabled.boolValue || versioningEnabled.hasMultipleDifferentValues;
                var versioningEnabledValue = enabledValue && versioningValue;
                versioningScope.SetEnabled(!versioningEnabled.hasMultipleDifferentValues);
                versioningScope.style.display = versioningEnabledValue ? DisplayStyle.Flex : DisplayStyle.None;

                var gitValue = gitEnabled.boolValue || gitEnabled.hasMultipleDifferentValues;
                var gitEnabledValue = versioningEnabledValue && gitValue;
                gitScope.SetEnabled(!gitEnabled.hasMultipleDifferentValues);
                gitScope.style.display = gitEnabledValue ? DisplayStyle.Flex : DisplayStyle.None;

                if (!versionNamePrefix.hasMultipleDifferentValues)
                {
                    versionPreview.text = $"'({versionNamePrefix.stringValue}<version>)'will be added in avatar description";
                    versionPreview.style.display = versioningEnabledValue ? DisplayStyle.Flex : DisplayStyle.None;
                }
                else
                {
                    versionPreview.style.display = DisplayStyle.None;
                }

                if (!tagPrefix.hasMultipleDifferentValues && !tagSuffix.hasMultipleDifferentValues)
                {
                    tagPreview.text = $"tag name will be '{tagPrefix.stringValue}<version>{tagSuffix.stringValue}'";
                    tagPreview.style.display = gitEnabledValue ? DisplayStyle.Flex : DisplayStyle.None;
                }
                else
                {
                    tagPreview.style.display = DisplayStyle.None;
                }
            }

            root.RegisterCallback<AttachToPanelEvent>(_ =>
            {
                enabledToggle.RegisterValueChangedCallback(_ => UpdateScopeVisibility());
                updateImageToggle.RegisterValueChangedCallback(_ => UpdateScopeVisibility());
                versioningToggle.RegisterValueChangedCallback(_ => UpdateScopeVisibility());
                gitToggle.RegisterValueChangedCallback(_ => UpdateScopeVisibility());
                versionPrefixField.RegisterValueChangedCallback(_ => UpdateScopeVisibility());
                tagPrefixField.RegisterValueChangedCallback(_ => UpdateScopeVisibility());
                tagSuffixField.RegisterValueChangedCallback(_ => UpdateScopeVisibility());
                UpdateScopeVisibility();
            });

            return root;
        }

        private void DrawAvatarDescriptorSection()
        {
            serializedObject.Update();
            var avatars = targets.Cast<AvatarUploadSetting>().ToArray();

            if (serializedObject.isEditingMultipleObjects)
            {
                MultipleAvatarDescriptor(avatars);
            }
            else
            {
                SingleAvatarDescriptor(_avatarDescriptor);
            }
        }

        private VisualElement DrawPlatformWarningAndUploadButton()
        {
            var container = new VisualElement { name = "PlatformWarningAndUploadButton" };
            var avatars = targets.Cast<AvatarUploadSetting>().ToArray();

            if (!serializedObject.isEditingMultipleObjects)
            {
                var avatar = avatars.First();
                if (!avatar.ios.enabled && !avatar.quest.enabled && !avatar.windows.enabled)
                    container.Add(new HelpBox(
                        "This avatar has all platforms disabled. This is fine if intentional.",
                        HelpBoxMessageType.Warning));
            }

            var uploadButton = ContinuousAvatarUploader.UploadButtonGui(avatars, Repaint);
            uploadButton.SetEnabled(avatars.All(avatar => avatar.GetCurrentPlatformInfo().enabled));
            container.Add(uploadButton);
            return container;
        }

        private void DrawCameraSection()
        {
            serializedObject.Update();

            if (serializedObject.isEditingMultipleObjects)
            {
                EditorGUILayout.LabelField("Editing Camera Position is not supported in multi-editing");
            }
            else
            {
                EditorGUI.BeginDisabledGroup(
                    !_cachedAvatar
                    // previewing other avatar
                    || _previewCameraManager != null && _previewCameraManager.Target != _cachedAvatar
                );
                if (_previewCameraManager != null && _previewCameraManager.Target == _cachedAvatar)
                {
                    _previewCameraManager.AddEditor(this);
                    _previewCameraManager.DrawPreview();
                    if (IndentedButton("Finish Setting Camera Position"))
                    {
                        _previewCameraManager?.Finish();
                        _previewCameraManager = null;
                    }
                }
                else
                {
                    if (IndentedButton("Configure Camera Position"))
                    {
                        _previewCameraManager = new PreviewCameraManager(this, _cachedAvatar!);
                    }
                }

                EditorGUI.EndDisabledGroup();
            }

            serializedObject.ApplyModifiedProperties();
        }

        private void MultipleAvatarDescriptor(AvatarUploadSetting[] avatars)
        {
            EditorGUI.BeginDisabledGroup(true);
            if (_avatarDescriptor.hasMultipleDifferentValues)
            {
                var position = EditorGUILayout.GetControlRect(true, 18f);
                position = EditorGUI.PrefixLabel(position, Labels.Avatar);
                GUI.Label(position, Labels.MixedValueContent, EditorStyles.objectField);
                GUIStyle buttonStyle = "ObjectFieldButton";
                Rect position1 = new Rect(position.xMax - 19f, position.y, 19f, position.height);
                GUI.Label(position1, GUIContent.none, buttonStyle);
            }
            else
            {
                var descriptor = avatars[0].avatarDescriptor;
                var avatar = descriptor.TryResolve();
                if (avatar != null || descriptor.IsNull())
                {
                    EditorGUILayout.ObjectField(Labels.Avatar, avatar, typeof(VRCAvatarDescriptor), true);
                }
                else
                {
                    EditorGUILayout.LabelField(Labels.Avatar, new GUIContent(_avatarName.stringValue));
                    EditorGUILayout.ObjectField("In scene", descriptor.asset, typeof(SceneAsset),
                        false);
                }
            }
            EditorGUI.EndDisabledGroup();
        }

        private void SingleAvatarDescriptor(SerializedProperty avatarDescriptor)
        {
            var descriptor = (MaySceneReference)avatarDescriptor.boxedValue;
            if (descriptor.IsNull())
                _settingAvatar = true;
            if (_settingAvatar)
            {
                _cachedAvatar = (VRCAvatarDescriptor)EditorGUILayout.ObjectField("Set Avatar: ",
                    null, typeof(VRCAvatarDescriptor), true);

                if (_cachedAvatar)
                {
                    avatarDescriptor.boxedValue = new MaySceneReference(_cachedAvatar);
                    // might be reverted if it's individual asset but
                    // this is good for DescriptorSet
                    _name.stringValue = _avatarName.stringValue = _cachedAvatar.name;
                    _settingAvatar = false;
                }

                EditorGUI.BeginDisabledGroup(descriptor.IsNull());
                if (GUILayout.Button("Cancel Change Avatar"))
                    _settingAvatar = false;
                EditorGUI.EndDisabledGroup();
            }
            else
            {
                if (!_cachedAvatar)
                    _cachedAvatar = descriptor.TryResolve() as VRCAvatarDescriptor;
                if (_cachedAvatar)
                {
                    EditorGUILayout.ObjectField(Labels.Avatar, _cachedAvatar, typeof(VRCAvatarDescriptor), true);
                    _avatarName.stringValue = _cachedAvatar.name;
                }
                else
                {
                    EditorGUILayout.LabelField(Labels.Avatar,  new GUIContent(_avatarName.stringValue));
                    EditorGUILayout.ObjectField("In scene", descriptor.asset, typeof(SceneAsset),
                        false);
                }

                if (GUILayout.Button("Change Avatar"))
                    _settingAvatar = true;
            }
        }

        private void OnDisable()
        {
            if (_previewCameraManager != null && _previewCameraManager.Target == _cachedAvatar) 
            {
                _previewCameraManager.RemoveEditor(this);
            }
        }

        public override Texture2D RenderStaticPreview(string assetPath, Object[] subAssets, int width, int height)
        {
            var overlayTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(AssetDatabase.GUIDToAssetPath("235dc340dbeca4d2d84f557bf52e93b4"));
            if (overlayTexture == null) return null;
            var target = this.target as AvatarUploadSetting;
            if (target == null) return null;
            if (!target.avatarDescriptor.IsAssetReference()) return null;
            var targetAvatar = target.avatarDescriptor.TryResolve() as VRCAvatarDescriptor;
            if (targetAvatar == null) return null;
            var targetGameObject = targetAvatar.gameObject;

            var editor = UnityEditor.Editor.CreateEditor(targetGameObject);
            var previewTexture = editor.RenderStaticPreview("", Array.Empty<Object>(), width, height);
            DestroyImmediate(editor);
            if (previewTexture == null) return null;
 
            // overlay the "CAU Settings" text
            var renderTexture = RenderTexture.GetTemporary(width, height, 0, GraphicsFormat.R8G8B8A8_SRGB);
            try
            {
                // copy the overlay texture
                RenderTexture.active = renderTexture;

                // draw the overlay texture
                GL.PushMatrix();
                GL.LoadPixelMatrix(0, width, height, 0);
                Graphics.DrawTexture(new Rect(0, 0, width, height), previewTexture);
                Graphics.DrawTexture(new Rect(0, 0, width, height), overlayTexture);
                GL.PopMatrix();

                var texture2D = new Texture2D(width, height, TextureFormat.RGB24, false, false);
                texture2D.ReadPixels(new Rect(0.0f, 0.0f, width, height), 0, 0);
                texture2D.Apply();

                return texture2D;
            }
            finally
            {
                RenderTexture.ReleaseTemporary(renderTexture);
            }
        }

        static class Labels
        {
            public static readonly GUIContent Avatar = new("Avatar");

            public static readonly GUIContent PCWindows = new("PC Windows");
            public static readonly GUIContent QuestAndroid = new("Quest / Android");
            public static readonly GUIContent IOS = new("iOS");

            public static readonly GUIContent UpdateImage = new("Update Image on Upload");
            public static readonly GUIContent TakeImageIn = new("Take Image In");
            public static readonly GUIContent VersioningSystem = new("Versioning System");
            public static readonly GUIContent VersionNamePrefix = new("Version Prefix");
            public static readonly GUIContent GitTagging = new("git tagging");
            public static readonly GUIContent TagPrefix = new("Tag Prefix");
            public static readonly GUIContent TagSuffix = new("Tag Suffix");

            public static readonly GUIContent MixedValueContent = EditorGUIUtility.TrTextContent("—", "Mixed Values");
        }

        private static bool IndentedButton(string text, params GUILayoutOption[] options)
        {
            var content = new GUIContent(text);
            var rect = EditorGUI.IndentedRect(GUILayoutUtility.GetRect(content, GUI.skin.button, options));
            return GUI.Button(rect, content);
        }
    }

    sealed class PreviewCameraManager
    {
        private Camera _camera;
        private bool _prevLocked;
        private Object[] _prevSelection;
        private readonly Object[] _trackerTargets;

        private readonly VRCAvatarDescriptor _cachedAvatar;
        public Object Target => _cachedAvatar;

        private readonly HashSet<UnityEditor.Editor> _editors;
        private readonly Scene _previewScene;

        public PreviewCameraManager([NotNull] UnityEditor.Editor editor,
            [NotNull] VRCAvatarDescriptor cachedAvatar)
        {
            if (editor == null) throw new ArgumentNullException(nameof(editor));
            if (cachedAvatar == null) throw new ArgumentNullException(nameof(cachedAvatar));
            _editors = new HashSet<UnityEditor.Editor> { editor };
            _cachedAvatar = cachedAvatar;

            if (EditorUtility.IsPersistent(cachedAvatar))
            {
                _previewScene = EditorSceneManager.NewPreviewScene();
                PrefabUtility.LoadPrefabContentsIntoPreviewScene(
                    AssetDatabase.GetAssetPath(cachedAvatar), _previewScene);
            }

            _prevLocked = ActiveEditorTracker.sharedTracker.isLocked;
            ActiveEditorTracker.sharedTracker.isLocked = true;
            _trackerTargets = ActiveEditorTracker.sharedTracker.activeEditors.Select(x => x.target).ToArray();

            _camera = EditorUtility.CreateGameObjectWithHideFlags("VRCCam Shim Camera", HideFlags.DontSave,
                    typeof(Camera))
                .GetComponent<Camera>();
            _camera.enabled = false;
            _camera.cullingMask = unchecked((int)0xFFFFFFDF);
            _camera.nearClipPlane = 0.01f;
            _camera.farClipPlane = 100f;
            _camera.allowHDR = false;
            _camera.scene = _previewScene.IsValid() ? _previewScene : cachedAvatar.gameObject.scene;
            cachedAvatar.PositionPortraitCamera(_camera.transform);
            EditorApplication.update += OnUpdate;
            _prevSelection = Selection.objects;
            Selection.objects = new Object[] { _camera.gameObject };
        }

        private Vector3 _cameraPositionOld;
        private Quaternion _cameraRotationOld;
        private void OnUpdate()
        {
            if (_cachedAvatar == null || _editors.All(x => x == null))
            {
                Finish();
                return;
            }
            var transform = _camera.transform;
            if (_cameraPositionOld != transform.position || _cameraRotationOld != transform.rotation)
                foreach (var editor in _editors)
                    editor.Repaint();

            _cameraPositionOld = transform.position;
            _cameraRotationOld = transform.rotation;
        }

        public void DrawPreview(params GUILayoutOption[] options)
        {
            var cameraRect = GUILayoutUtility.GetAspectRect(16.0f / 9f, options);
            if (Event.current.type == EventType.Repaint)
            {
                var previewTexture = GetRenderTexture((int)cameraRect.width, (int)cameraRect.height);
                _camera.targetTexture = previewTexture;
                _camera.pixelRect = new Rect(0, 0, cameraRect.width, cameraRect.height);
                _camera.Render();
                Graphics.DrawTexture(cameraRect, previewTexture, new Rect(0, 0, 1, 1), 
                    0, 0, 0, 0, GUI.color, _guiTextureBlit2SrgbMaterial);
            }
        }

        readonly Material _guiTextureBlit2SrgbMaterial = typeof(EditorGUIUtility)
                .GetProperty("GUITextureBlit2SRGBMaterial", BindingFlags.Static | BindingFlags.NonPublic)
                ?.GetValue(typeof(EditorGUIUtility), null) as Material;

        private RenderTexture _previewTexture;

        private RenderTexture GetRenderTexture(int width, int height)
        {
            int antiAliasing = Mathf.Max(1, QualitySettings.antiAliasing);
            if (_previewTexture == null || _previewTexture.width != width || _previewTexture.height != height || _previewTexture.antiAliasing != antiAliasing)
            {
                _previewTexture = new RenderTexture(width, height, 24, SystemInfo.GetGraphicsFormat(DefaultFormat.LDR));
                _previewTexture.antiAliasing = antiAliasing;
            }
            return _previewTexture;
        }

        public void Finish()
        {
            if (!_camera) return;

            EditorApplication.update -= OnUpdate;
            if (_cachedAvatar)
            {
                var transform = _cachedAvatar.transform;
                _cachedAvatar.portraitCameraPositionOffset =
                    transform.InverseTransformPoint(_camera.transform.position);
                _cachedAvatar.portraitCameraRotationOffset =
                    Quaternion.Inverse(transform.rotation) * _camera.transform.rotation;
                EditorUtility.SetDirty(_cachedAvatar);
            }

            Object.DestroyImmediate(_camera.gameObject);
            _camera = null;
            Selection.objects = _prevSelection;
            ActiveEditorTracker.sharedTracker.isLocked = _prevLocked;

            // ReSharper disable once PossiblyImpureMethodCallOnReadOnlyVariable // IsValid doesn't change
            if (_previewScene.IsValid())
                EditorSceneManager.ClosePreviewScene(_previewScene);
        }

        public void AddEditor(UnityEditor.Editor editor) => _editors.Add(editor);
        public void RemoveEditor(UnityEditor.Editor editor) => _editors.Remove(editor);
    }
}
