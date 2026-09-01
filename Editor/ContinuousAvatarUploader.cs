using System;
using System.Collections.Generic;
using System.Linq;
using JetBrains.Annotations;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using VRC.Core;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3A.Editor;
using Object = UnityEngine.Object;

namespace Anatawa12.ContinuousAvatarUploader.Editor
{
    public class ContinuousAvatarUploader : EditorWindow
    {
        [SerializeField] [ItemCanBeNull] [NotNull] internal AvatarUploadSettingOrGroup[] settingsOrGroups = Array.Empty<AvatarUploadSettingOrGroup>();
        [SerializeField] private List<MaySceneReference> temporarySettings = new List<MaySceneReference>();

        [CanBeNull]
        internal static ContinuousAvatarUploader Instance
        {
            get
            {
                var objects = Resources.FindObjectsOfTypeAll(typeof(ContinuousAvatarUploader));
                return objects.Length == 0 ? null : (ContinuousAvatarUploader)objects[0];
            }
        }

        // for uploading avatars

        [NonSerialized] private AvatarUploadSetting _currentUploadingAvatar;
        [SerializeField] private List<UploadErrorInfo> uploadErrors = new List<UploadErrorInfo>();
        [SerializeField] private bool dragDropFoldout = false;

        private UploaderProgressAsset progressAsset;

        private SerializedObject _serialized;
        private SerializedProperty _settingsOrGroups;

        // UI Toolkit elements
        private VisualElement _progressSection;
        private ProgressBar _platformProgressBar;
        private ProgressBar _avatarProgressBar;
        private ObjectField _uploadingAvatarField;
        private Label _sleepingLabel;
        private Button _abortButton;
        private VisualElement _settingsSection;
        private Foldout _dragDropFoldoutElement;
        private VisualElement _dropArea;
        private VisualElement _temporaryAvatarsContainer;
        private ScrollView _uploadsScroll;
        private PropertyField _settingsListField;
        private VisualElement _checkResultsContainer;
        private Button _startUploadButton;
        private ScrollView _errorsContainer;

        private double _lastCheckTime;
        private int _lastErrorCount = -1;
        private bool _updateRegistered;

        [MenuItem("Window/Continuous Avatar Uploader")]
        [MenuItem("Tools/Continuous Avatar Uploader")]
        private static void OpenWindowItem() => OpenWindow();
        public static ContinuousAvatarUploader OpenWindow() => GetWindow<ContinuousAvatarUploader>("ContinuousAvatarUploader");

        private void OnEnable()
        {
            _serialized = new SerializedObject(this);
            _settingsOrGroups = _serialized.FindProperty(nameof(settingsOrGroups));
            _settingsOrGroups.isExpanded = true;

            CleanupTempGroupAsset();

            VRCSdkControlPanel.OnSdkPanelEnable += OnSdkPanelEnableDisable;
            VRCSdkControlPanel.OnSdkPanelDisable += OnSdkPanelEnableDisable;
            UploadOrchestrator.OnUploadSingleAvatarStarted += OnUploadSingleAvatarStarted;
            UploadOrchestrator.OnUploadSingleAvatarFinished += OnUploadSingleAvatarFinished;
            UploadOrchestrator.OnUploadFinished += OnUploadFinished;
            UploadOrchestrator.OnLoginFailed += OnLoginFailed;
            UploadOrchestrator.OnRandomException += OnRandomException;

            EditorApplication.update += UpdateTick;
            _updateRegistered = true;
        }

        private void OnDisable()
        {
            VRCSdkControlPanel.OnSdkPanelEnable -= OnSdkPanelEnableDisable;
            VRCSdkControlPanel.OnSdkPanelDisable -= OnSdkPanelEnableDisable;
            UploadOrchestrator.OnUploadSingleAvatarStarted -= OnUploadSingleAvatarStarted;
            UploadOrchestrator.OnUploadSingleAvatarFinished -= OnUploadSingleAvatarFinished;
            UploadOrchestrator.OnUploadFinished -= OnUploadFinished;
            UploadOrchestrator.OnLoginFailed -= OnLoginFailed;
            UploadOrchestrator.OnRandomException -= OnRandomException;
            if (_updateRegistered)
            {
                EditorApplication.update -= UpdateTick;
                _updateRegistered = false;
            }
            CleanupTempGroupAsset();
        }

        private void OnSdkPanelEnableDisable(object sender, EventArgs e) => RefreshDynamicSections();

        private void OnUploadSingleAvatarStarted(UploaderProgressAsset progress, AvatarUploadSetting avatar)
        {
            _currentUploadingAvatar = avatar;
            RefreshDynamicSections();
        }

        private void OnUploadSingleAvatarFinished(UploaderProgressAsset progress, AvatarUploadSetting avatar)
        {
            _currentUploadingAvatar = null;
            RefreshDynamicSections();
        }

        private void OnUploadFinished(UploaderProgressAsset obj, bool successfully)
        {
            _currentUploadingAvatar = null;
            // if finished unsuccessfully, we should have shown error dialog already
            if (Preferences.ShowDialogWhenUploadFinished && successfully)
                EditorUtility.DisplayDialog("Continuous Avatar Uploader", "Finished Uploading Avatars!", "OK");

            CleanupTempGroupAsset();

            RefreshDynamicSections();
        }

        private void OnLoginFailed(Exception obj)
        {
            _currentUploadingAvatar = null;
            EditorUtility.DisplayDialog("Continuous Avatar Uploader", "Login Failed: " + obj.Message, "OK");
        }

        private void OnRandomException(Exception obj)
        {
            _currentUploadingAvatar = null;
            EditorUtility.DisplayDialog("Continuous Avatar Uploader", "An error occurred: " + obj.Message, "OK");
        }

        // ===== UI Toolkit =====

        private void CreateGUI()
        {
            rootVisualElement.Clear();

            // progress section
            _progressSection = new VisualElement { name = "progressSection" };
            _progressSection.Add(new Label("UPLOAD IN PROGRESS"));
            _platformProgressBar = new ProgressBar { title = "" };
            _platformProgressBar.style.height = 20;
            _progressSection.Add(_platformProgressBar);
            _avatarProgressBar = new ProgressBar { title = "" };
            _avatarProgressBar.style.height = 20;
            _progressSection.Add(_avatarProgressBar);
            _uploadingAvatarField = new ObjectField("Uploading")
            {
                objectType = typeof(AvatarUploadSetting),
                allowSceneObjects = true,
            };
            _uploadingAvatarField.AddToClassList("unity-base-field__aligned");
            _progressSection.Add(_uploadingAvatarField);
            _sleepingLabel = new Label("Sleeping a little") { style = { display = DisplayStyle.None } };
            _progressSection.Add(_sleepingLabel);
            _abortButton = new Button(UploadOrchestrator.CancelUpload) { text = "ABORT UPLOAD" };
            _progressSection.Add(_abortButton);

            // settings section
            _settingsSection = new VisualElement { name = "settingsSection" };
            _settingsSection.style.flexGrow = 1;
            _settingsSection.Add(CreatePreferencesFields());

            // drag & drop section (space before it, matching EditorGUILayout.Space)
            _dragDropFoldoutElement = new Foldout
            {
                text = "Drag & Drop Upload",
                value = dragDropFoldout,
            };
            _dragDropFoldoutElement.style.marginTop = 6;
            _dropArea = new VisualElement { name = "dropArea" };
            _dropArea.style.justifyContent = Justify.Center;
            _dropArea.Add(new Label("Drag Avatar Prefabs or GameObjects Here")
            {
                style =
                {
                    unityTextAlign = TextAnchor.MiddleCenter,
                    flexGrow = 1,
                }
            });
            _dropArea.style.minHeight = 50;
            _dropArea.style.borderTopWidth = 1;
            _dropArea.style.borderBottomWidth = 1;
            _dropArea.style.borderLeftWidth = 1;
            _dropArea.style.borderRightWidth = 1;
            _dropArea.style.borderTopColor = new Color(0.5f, 0.5f, 0.5f, 1);
            _dropArea.style.borderBottomColor = new Color(0.5f, 0.5f, 0.5f, 1);
            _dropArea.style.borderLeftColor = new Color(0.5f, 0.5f, 0.5f, 1);
            _dropArea.style.borderRightColor = new Color(0.5f, 0.5f, 0.5f, 1);
            _dropArea.style.marginTop = 4;
            _dropArea.style.marginBottom = 4;
            _dropArea.style.marginLeft = 4;
            _dropArea.style.marginRight = 4;
            _dropArea.RegisterCallback<DragUpdatedEvent>(OnDragUpdated);
            _dropArea.RegisterCallback<DragPerformEvent>(OnDragPerform);
            _temporaryAvatarsContainer = new VisualElement { name = "temporaryAvatarsContainer" };
            _dragDropFoldoutElement.Add(_dropArea);
            _dragDropFoldoutElement.Add(_temporaryAvatarsContainer);
            _dragDropFoldoutElement.RegisterValueChangedCallback(evt =>
            {
                dragDropFoldout = evt.newValue;
            });
            _settingsSection.Add(_dragDropFoldoutElement);

            // target platforms (space before, matching EditorGUILayout.Space)
            _settingsSection.Add(new Label("Target Platforms")
            {
                style = { unityFontStyleAndWeight = FontStyle.Bold, marginTop = 6 }
            });
            _settingsSection.Add(CreatePlatformToggles(RefreshCheckSection));

            // check results
            _checkResultsContainer = new VisualElement { name = "checkResultsContainer" };
            _settingsSection.Add(_checkResultsContainer);

            _startUploadButton = new Button(StartUploadWithCheck) { text = "Start Upload" };
            _startUploadButton.style.marginTop = 4;
            _settingsSection.Add(_startUploadButton);

            // upload settings list
            _uploadsScroll = new ScrollView { name = "uploadsScroll", verticalScrollerVisibility = ScrollerVisibility.Auto };
            _uploadsScroll.style.flexGrow = 1;
            _settingsListField = new PropertyField(_settingsOrGroups) { name = "settingsList" };
            _uploadsScroll.Add(_settingsListField);
            var clearSettingsButton = new Button(ClearSettings) { text = "Clear Settings" };
            _uploadsScroll.Add(clearSettingsButton);
            _settingsSection.Add(_uploadsScroll);

            // order matters: progress, then settings (both disabled during upload), then errors at the bottom
            rootVisualElement.Add(_progressSection);
            rootVisualElement.Add(_settingsSection);

            var errorsLabel = new Label("Errors from Previous Build:")
            {
                style = { unityFontStyleAndWeight = FontStyle.Bold, marginTop = 10 }
            };
            rootVisualElement.Add(errorsLabel);
            _errorsContainer = new ScrollView { name = "errorsContainer", verticalScrollerVisibility = ScrollerVisibility.Auto };
            _errorsContainer.style.flexGrow = 1;
            rootVisualElement.Add(_errorsContainer);

            _settingsListField.Bind(_serialized);

            RefreshDynamicSections();
        }

        private VisualElement CreatePreferencesFields()
        {
            var container = new VisualElement();

            var sleepField = new FloatField("Sleep Seconds")
            {
                tooltip = "The time sleeps between upload",
                value = Preferences.SleepSeconds,
            };
            sleepField.AddToClassList("unity-base-field__aligned");
            sleepField.style.marginBottom = 2;
            sleepField.RegisterValueChangedCallback(evt => Preferences.SleepSeconds = evt.newValue);
            container.Add(sleepField);

            container.Add(CreatePreferenceToggle(
                "Take Thumbnail In PlayMode by Default",
                "If this is enabled, CAU will take Thumbnail after entering PlayMode.",
                Preferences.TakeThumbnailInPlaymodeByDefault,
                value => Preferences.TakeThumbnailInPlaymodeByDefault = value));
            container.Add(CreatePreferenceToggle(
                "Show Dialog when Finished",
                "If this is enabled, CAU will tell you upload finished.",
                Preferences.ShowDialogWhenUploadFinished,
                value => Preferences.ShowDialogWhenUploadFinished = value));
            container.Add(CreatePreferenceToggle(
                "Rollback Build Platform",
                "If this is enabled, CAU will rollback the build platform to the one before upload after upload finished.",
                Preferences.RollbackBuildPlatform,
                value => Preferences.RollbackBuildPlatform = value));
            container.Add(CreatePreferenceToggle(
                "Continue upload other avatars on build or upload error",
                "If this is enabled, CAU will continue uploading other avatars even if some avatar build or upload (if reach retry count limit) fails.",
                Preferences.ContinueUploadOnError,
                value => Preferences.ContinueUploadOnError = value));

            var retryField = new IntegerField("Retry Count")
            {
                tooltip = "The number of retries to attempt for each upload. Zero means no retries, so only one attempt will be made.",
                value = Preferences.RetryCount,
            };
            retryField.AddToClassList("unity-base-field__aligned");
            retryField.style.marginBottom = 2;
            retryField.RegisterValueChangedCallback(evt => Preferences.RetryCount = evt.newValue);
            container.Add(retryField);

            return container;
        }

        private static Toggle CreatePreferenceToggle(string label, string tooltip, bool initialValue, Action<bool> setter)
        {
            var toggle = new Toggle(label)
            {
                tooltip = tooltip,
                value = initialValue,
            };
            toggle.style.marginBottom = 2;
            MakeCheckboxLeft(toggle);
            toggle.RegisterValueChangedCallback(evt => setter(evt.newValue));
            return toggle;
        }

        /// <summary>
        /// Arranges a toggle as "checkbox on the left, label right after it":
        /// reorders BaseField's [labelElement, visualInput] so the checkbox
        /// comes first, and prevents the input from stretching so the label
        /// hugs the checkbox instead of being pushed to the far right.
        /// </summary>
        internal static void MakeCheckboxLeft(Toggle toggle)
        {
            var children = toggle.Children().ToArray();
            if (children.Length < 2) return;
            var input = children[1];
            toggle.Remove(input);
            toggle.Insert(0, input);
            input.style.flexGrow = 0;
        }

        private void ClearSettings()
        {
            _settingsOrGroups.arraySize = 0;
            _serialized.ApplyModifiedProperties();
            _settingsListField.Bind(_serialized);
            RefreshCheckSection();
        }

        private void UpdateTick()
        {
            var loaded = UploaderProgressAsset.Load();
            progressAsset = loaded != null ? loaded : progressAsset;

            var errorCount = progressAsset?.uploadErrors.Count ?? 0;
            if (errorCount != _lastErrorCount)
            {
                _lastErrorCount = errorCount;
                uploadErrors = progressAsset?.uploadErrors ?? uploadErrors;
                RebuildErrorsSection();
            }

            if (EditorApplication.timeSinceStartup - _lastCheckTime > 0.25)
            {
                _lastCheckTime = EditorApplication.timeSinceStartup;
                RefreshCheckSection();
            }
        }

        private void RefreshDynamicSections()
        {
            RefreshProgressSection();
            RebuildErrorsSection();
            RefreshCheckSection();
            RebuildTemporaryAvatars();
        }

        private void RefreshProgressSection()
        {
            var uploadInProgress = progressAsset != null;
            _progressSection.style.display = uploadInProgress ? DisplayStyle.Flex : DisplayStyle.None;
            _settingsSection.SetEnabled(!uploadInProgress);

            if (!uploadInProgress) return;

            var totalCount = progressAsset.uploadSettings.Length;
            var uploadingIndex = progressAsset.uploadingAvatarIndex;
            var totalPlatforms = progressAsset.targetPlatforms.Length;
            var uploadingTargetCount = progressAsset.uploadFinishedPlatforms.Length;

            _platformProgressBar.value = totalPlatforms == 0 ? 0 : (uploadingTargetCount + 0.5f) / totalPlatforms * 100f;
            _platformProgressBar.title =
                $"Uploading for {progressAsset.uploadingTargetPlatform} ({uploadingTargetCount + 1} / {totalPlatforms} platforms)";
            _avatarProgressBar.value = totalCount == 0 ? 0 : (uploadingIndex + 0.5f) / totalCount * 100f;
            _avatarProgressBar.title =
                $"Uploading {uploadingIndex + 1} / {totalCount} for {progressAsset.uploadingTargetPlatform}";

            if (_currentUploadingAvatar)
            {
                _uploadingAvatarField.style.display = DisplayStyle.Flex;
                _sleepingLabel.style.display = DisplayStyle.None;
                _uploadingAvatarField.value = _currentUploadingAvatar;
            }
            else
            {
                _uploadingAvatarField.style.display = DisplayStyle.None;
                _sleepingLabel.style.display = DisplayStyle.Flex;
            }
        }

        private void RefreshCheckSection()
        {
            if (_checkResultsContainer == null) return;
            _checkResultsContainer.Clear();
            var checkResult = CheckUpload();
            AddCheckHelpBox(checkResult, UploadCheckResult.Uploading, "Uploading", MessageType.Info);
            AddCheckHelpBox(checkResult, UploadCheckResult.NoDescriptors, "No AvatarUploadSettings are specified", MessageType.Error);
            AddCheckHelpBox(checkResult, UploadCheckResult.AnyNull, "Some AvatarUploadSetting or Group are None", MessageType.Error);
            AddCheckHelpBox(checkResult, UploadCheckResult.PlayMode, "To upload avatars, exit Play mode", MessageType.Error);
            AddCheckHelpBox(checkResult, UploadCheckResult.NoCredentials, "Please login in control panel", MessageType.Error);
            AddCheckHelpBox(checkResult, UploadCheckResult.ControlPanelClosed, "Please open Control panel", MessageType.Error);
            AddCheckHelpBox(checkResult, UploadCheckResult.NoAvatarBuilder, "No Valid VRCSDK Avatars Found", MessageType.Error);
            AddCheckHelpBox(checkResult, UploadCheckResult.PlayModeSettingsNotGood,
                "Some avatars are going or taking thumbnail in PlayMode. " +
                "To take thumbnail in PlayMode, Please Disable 'Reload Domain' Option in " +
                "Enter Play Mode Settings in Editor in Project Settings", MessageType.Error);
            AddCheckHelpBox(checkResult, UploadCheckResult.UnsupportedPlatformSelected,
                "Some target platforms are selected, but not supported by current build. " +
                "Please install the build support for those platforms in Unity Hub, or " +
                "uncheck the target platforms in Continuous Avatar Uploader settings.", MessageType.Error);
            AddCheckHelpBox(checkResult, UploadCheckResult.NoPlatformsSelected,
                "No target platforms are selected. " +
                "Please select at least one target platform in Continuous Avatar Uploader settings.", MessageType.Error);
            _startUploadButton.SetEnabled(checkResult == UploadCheckResult.Ok);
        }

        private void AddCheckHelpBox(UploadCheckResult checkResult, UploadCheckResult flag, string text, MessageType messageType)
        {
            if ((checkResult & flag) != 0)
                _checkResultsContainer.Add(new HelpBox(text, (HelpBoxMessageType)messageType));
        }

        private void RebuildErrorsSection()
        {
            if (_errorsContainer == null) return;
            _errorsContainer.Clear();
            if (uploadErrors.Count == 0)
            {
                _errorsContainer.Add(new Label("No Errors"));
                return;
            }

            foreach (var previousUploadError in uploadErrors)
            {
                var row = new VisualElement();
                if (previousUploadError.uploadingAvatar != null)
                {
                    var uploadingField = new ObjectField("Uploading")
                    {
                        objectType = typeof(AvatarUploadSetting),
                        value = previousUploadError.uploadingAvatar,
                        allowSceneObjects = false,
                    };
                    uploadingField.AddToClassList("unity-base-field__aligned");
                    row.Add(uploadingField);
                }
                else if (previousUploadError.avatarDescriptor.asset != null
                         && previousUploadError.avatarDescriptor.GetCachedResolve() is VRCAvatarDescriptor descriptor)
                {
                    var uploadingField = new ObjectField("Uploading")
                    {
                        objectType = typeof(VRCAvatarDescriptor),
                        value = descriptor,
                        allowSceneObjects = false,
                    };
                    uploadingField.AddToClassList("unity-base-field__aligned");
                    row.Add(uploadingField);
                }
                else
                {
                    row.Add(CreateLabelField("Avatar", previousUploadError.avatarName));
                }

                var platformField = new EnumField("For", previousUploadError.targetPlatform);
                platformField.AddToClassList("unity-base-field__aligned");
                row.Add(platformField);
                row.Add(new TextField { value = previousUploadError.message, multiline = true });
                _errorsContainer.Add(row);
            }
        }

        private static VisualElement CreateLabelField(string label, string value)
        {
            var row = new VisualElement { style = { flexDirection = FlexDirection.Row } };
            row.AddToClassList("unity-base-field");
            row.AddToClassList("unity-base-field__aligned");
            var labelElement = new Label(label);
            labelElement.AddToClassList("unity-base-field__label");
            row.Add(labelElement);
            var valueElement = new Label(value);
            valueElement.AddToClassList("unity-base-field__input");
            valueElement.style.flexGrow = 1;
            row.Add(valueElement);
            return row;
        }

        private void RebuildTemporaryAvatars()
        {
            if (_temporaryAvatarsContainer == null) return;
            _temporaryAvatarsContainer.Clear();

            if (temporarySettings.Count == 0)
            {
                _dropArea.style.display = DisplayStyle.Flex;
                return;
            }

            _temporaryAvatarsContainer.Add(new Label("Avatar List:"));
            var avatarRows = new VisualElement { name = "temporaryAvatarRows" };

            for (int i = temporarySettings.Count - 1; i >= 0; i--)
            {
                var index = i;
                var maySceneRef = temporarySettings[i];
                if (maySceneRef.asset == null)
                {
                    temporarySettings.RemoveAt(i);
                    continue;
                }

                var descriptor = maySceneRef.GetCachedResolve() as VRCAvatarDescriptor;
                var avatarName = descriptor?.gameObject.name ?? "Missing Avatar";

                var row = new VisualElement { style = { flexDirection = FlexDirection.Row } };
                var objectField = new ObjectField(avatarName)
                {
                    objectType = typeof(VRCAvatarDescriptor),
                    value = descriptor,
                    allowSceneObjects = true,
                };
                objectField.AddToClassList("unity-base-field__aligned");
                objectField.style.flexGrow = 1;
                objectField.RegisterValueChangedCallback(evt =>
                {
                    if (evt.newValue is VRCAvatarDescriptor newDescriptor)
                        temporarySettings[index] = new MaySceneReference(newDescriptor);
                });
                var removeButton = new Button(() =>
                {
                    temporarySettings.RemoveAt(index);
                    RebuildTemporaryAvatars();
                }) { text = "Remove" };
                removeButton.style.width = 60;
                row.Add(objectField);
                row.Add(removeButton);
                avatarRows.Add(row);
            }

            if (temporarySettings.Count > 8)
            {
                var scroll = new ScrollView { name = "temporaryAvatarsScroll" };
                scroll.style.maxHeight = 160;
                scroll.Add(avatarRows);
                _temporaryAvatarsContainer.Add(scroll);
            }
            else
            {
                _temporaryAvatarsContainer.Add(avatarRows);
            }

            var clearAllButton = new Button(() =>
            {
                temporarySettings.Clear();
                RebuildTemporaryAvatars();
            }) { text = "Clear All D&D Avatars" };
            _temporaryAvatarsContainer.Add(clearAllButton);
        }

        private void OnDragUpdated(DragUpdatedEvent evt)
        {
            DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
            evt.StopPropagation();
        }

        private void OnDragPerform(DragPerformEvent evt)
        {
            DragAndDrop.AcceptDrag();

            foreach (var draggedObject in DragAndDrop.objectReferences)
            {
                if (!(draggedObject is GameObject go))
                    continue;

                var descriptor = go.GetComponent<VRCAvatarDescriptor>();
                if (descriptor == null)
                    continue;

                var maySceneRef = new MaySceneReference(descriptor);
                temporarySettings.Add(maySceneRef);
            }

            RebuildTemporaryAvatars();
            evt.StopPropagation();
        }

        [Flags]
        internal enum UploadCheckResult
        {
            Ok = 0,
            Uploading = 1 << 0,
            NoDescriptors = 1 << 1,
            AnyNull = 1 << 2,
            PlayMode = 1 << 3,
            NoCredentials = 1 << 4,
            ControlPanelClosed = 1 << 5,
            NoAvatarBuilder = 1 << 6,
            PlayModeSettingsNotGood = 1 << 7,
            UnsupportedPlatformSelected = 1 << 8,
            NoPlatformsSelected = 1 << 9,
        }

        // We do omit temp since play mode settings is always default
        private UploadCheckResult CheckUpload() => CheckUploadStatic(GetUploadingAvatars(includeTemp: false), RefreshDynamicSections);

        internal static UploadCheckResult CheckUploadStatic(IEnumerable<AvatarUploadSetting> avatars, [CanBeNull] Action repaint = null)
        {
            var result = UploadCheckResult.Ok;
            if (UploadOrchestrator.IsUploadInProgress()) result |= UploadCheckResult.Uploading;
            if (EditorApplication.isPlayingOrWillChangePlaymode) result |= UploadCheckResult.PlayMode;
            if (!Uploader.VerifyCredentials(repaint)) result |= UploadCheckResult.NoCredentials;
            if (!VRCSdkControlPanel.window) result |= UploadCheckResult.ControlPanelClosed;
            if (!VRCSdkControlPanel.TryGetBuilder<IVRCSdkAvatarBuilderApi>(out _)) result |= UploadCheckResult.NoAvatarBuilder;
            if (!CheckPlaymodeSettings(avatars)) result |= UploadCheckResult.PlayModeSettingsNotGood;

            foreach (var platform in Uploader.GetTargetPlatforms())
            {
                if (!Uploader.IsBuildSupportedInstalled(platform) && Preferences.UploadFor(platform))
                    result |= UploadCheckResult.UnsupportedPlatformSelected;
            }

            if (!Uploader.GetTargetPlatforms().Any(Preferences.UploadFor))
                result |= UploadCheckResult.NoPlatformsSelected;

            return result;
        }

        internal bool StartUpload()
        {
            if (CheckUpload() != UploadCheckResult.Ok) return false;
            DoStartUpload();
            return true;
        }

        private void StartUploadWithCheck()
        {
            if (!StartUpload())
                ShowUploadFailedDialog();
        }

        private static void ShowUploadFailedDialog()
        {
            EditorUtility.DisplayDialog("Failed to start upload",
                "Failed to start upload.\nPlease refer Uploader window for reason", "OK");
        }

        /// <summary>
        /// Creates a row of toggles for the target platforms, shared by the main window
        /// and the inspector upload buttons.
        /// </summary>
        internal static VisualElement CreatePlatformToggles(Action onChange)
        {
            var container = new VisualElement { name = "platformsRow" };
            foreach (var platform in Uploader.GetTargetPlatforms())
            {
                var platformName = platform;
                var isEnabled = Preferences.UploadFor(platform);
                var supported = Uploader.IsBuildSupportedInstalled(platform);
                var toggle = new Toggle($"Upload for {platformName}")
                {
                    value = isEnabled,
                    tooltip = supported
                        ? $"If this is enabled, CAU will upload avatars for {platformName} platform."
                        : $"Build support for {platformName} is not installed. ",
                };
                toggle.style.marginBottom = 2;
                MakeCheckboxLeft(toggle);
                toggle.SetEnabled(supported || isEnabled);
                toggle.RegisterValueChangedCallback(evt =>
                {
                    Preferences.SetUploadFor(platformName, evt.newValue);
                    onChange?.Invoke();
                });
                container.Add(toggle);
            }

            return container;
        }

        private static bool CheckPlaymodeSettings(IEnumerable<AvatarUploadSetting> avatars)
        {
            if (Utils.ReloadDomainDisabled())
                return true;

            if (Preferences.TakeThumbnailInPlaymodeByDefault)
                return false;

            foreach (var avatarUploadSetting in avatars)
            {
                var currentInfo = avatarUploadSetting.GetCurrentPlatformInfo();
                if (currentInfo.updateImage)
                {
                    bool enterPlaymode;
                    switch (currentInfo.imageTakeEditorMode)
                    {
                        case ImageTakeEditorMode.UseUploadGuiSetting:
                            enterPlaymode = Preferences.TakeThumbnailInPlaymodeByDefault;
                            break;
                        case ImageTakeEditorMode.InEditMode:
                            enterPlaymode = false;
                            break;
                        case ImageTakeEditorMode.InPlayMode:
                            enterPlaymode = true;
                            break;
                        default:
                            throw new ArgumentOutOfRangeException();
                    }

                    if (enterPlaymode)
                        return false;
                }
            }

            // there are need to disable reload domain
            return true;
        }

        private IEnumerable<AvatarUploadSetting> GetUploadingAvatars(bool includeTemp = true) =>
            settingsOrGroups
                .Where(x => x)
                .SelectMany(x => x.Settings)
                .Where(x => x)
                .Concat(includeTemp ? CreateTemporarySettings() : Array.Empty<AvatarUploadSetting>());

        private List<AvatarUploadSetting> CreateTemporarySettings()
        {
            var tempSettings = new List<AvatarUploadSetting>();
            foreach (var maySceneRef in temporarySettings)
            {
                if (maySceneRef.asset == null) continue;

                var tempSetting = CreateTemporarySetting(maySceneRef);
                if (tempSetting != null)
                {
                    tempSettings.Add(tempSetting);
                }
            }

            return tempSettings;
        }

        private void CleanupTempGroupAsset()
        {
        }

        private void DoStartUpload()
        {
            var progress = ScriptableObject.CreateInstance<UploaderProgressAsset>();
            progress.openedScenes = UploadOrchestrator.GetLastOpenedScenes();
            progress.uploadSettings = GetUploadingAvatars().ToArray();
            progress.targetPlatforms = Uploader.GetTargetPlatforms().Where(Preferences.UploadFor).ToArray();
            progress.sleepMilliseconds = (int)(Preferences.SleepSeconds * 1000);
            progress.rollbackPlatform = Preferences.RollbackBuildPlatform;
            progress.retryCount = Preferences.RetryCount;
            progress.continueUploadOnError = Preferences.ContinueUploadOnError;
            UploadOrchestrator.StartUpload(progress);
        }

        private AvatarUploadSetting CreateTemporarySetting(MaySceneReference maySceneRef)
        {
            var descriptor = maySceneRef.GetCachedResolve() as VRCAvatarDescriptor;
            if (descriptor == null) return null;

            var tempSetting = ScriptableObject.CreateInstance<AvatarUploadSetting>();
            tempSetting.name = descriptor.gameObject.name;
            tempSetting.avatarDescriptor = maySceneRef;
            tempSetting.avatarName = descriptor.gameObject.name;

            tempSetting.windows.enabled = true;
            tempSetting.quest.enabled = true;
            tempSetting.ios.enabled = true;

            tempSetting.hideFlags = HideFlags.DontUnloadUnusedAsset;

            return tempSetting;
        }

        /// <summary>
        /// Creates a UI Toolkit element for uploading the given avatars directly from an inspector.
        /// </summary>
        public static VisualElement UploadButtonGui(IEnumerable<AvatarUploadSettingOrGroup> avatarOrGroups, [CanBeNull] Action repaint = null)
        {
            var avatars = avatarOrGroups
                .Where(x => x)
                .SelectMany(x => x.Settings)
                .Where(x => x)
                .ToArray();

            var root = new VisualElement { name = "UploadButtonGui" };

            Button uploadButton = null!;

            // target platform selector: a pure UI Toolkit multi-select dropdown
            // (equivalent of the original EnumFlagsField)
            var platformField = new EnumFlagsDropdownField("Target Platforms",
                Uploader.GetTargetPlatforms().ToArray(),
                platform => Preferences.UploadFor(platform),
                (platform, value) => Preferences.SetUploadFor(platform, value));
            platformField.RegisterValueChangedCallback(_ =>
            {
                UpdateButton();
                repaint?.Invoke();
            });
            root.Add(platformField);

            uploadButton = new Button(OnUploadClick) { text = "Upload This Avatar" };
            uploadButton.tooltip = "Upload this avatar to the current target platform. " +
                                    "If you want to upload multiple avatars, use the Continuous Avatar Uploader window.";
            root.Add(uploadButton);
            UpdateButton();

            void OnUploadClick()
            {
                var uploader = OpenWindow();
                uploader.settingsOrGroups = avatars.ToArray<AvatarUploadSettingOrGroup>();
                if (!uploader.StartUpload())
                    ShowUploadFailedDialog();
            }

            void UpdateButton()
            {
                var check = CheckUploadStatic(avatars, repaint);
                uploadButton.text = avatars.Length == 1 ? "Upload This Avatar" : $"Upload {avatars.Length} Avatars";
                uploadButton.tooltip = check == UploadCheckResult.Ok
                    ? "Upload this avatar to the current target platform. " +
                      "If you want to upload multiple avatars, use the Continuous Avatar Uploader window."
                    : "Cannot upload avatars now. Check the Continuous Avatar Uploader window for details.";
                uploadButton.SetEnabled(check == UploadCheckResult.Ok);
            }

            return root;
        }
    }

    [Flags]
    internal enum TargetPlatformFlags
    {
        Windows = 1 << (int)TargetPlatform.Windows,
        Android = 1 << (int)TargetPlatform.Android,
        iOS = 1 << (int)TargetPlatform.iOS,
    }

    /// <summary>
    /// UI Toolkit equivalent of IMGUI's EnumFlagsField for upload platforms.
    /// </summary>
    internal sealed class EnumFlagsDropdownField : BaseField<TargetPlatformFlags>
    {
        private readonly TargetPlatform[] _platforms;
        private readonly Func<TargetPlatform, bool> _getValue;
        private readonly Action<TargetPlatform, bool> _setValue;
        private readonly VisualElement _input;
        private readonly Label _valueLabel;
        private readonly TargetPlatformFlags _allFlags;

        public EnumFlagsDropdownField(
            string label,
            TargetPlatform[] platforms,
            Func<TargetPlatform, bool> getValue,
            Action<TargetPlatform, bool> setValue)
            : this(label, platforms, getValue, setValue, new VisualElement())
        {
        }

        private EnumFlagsDropdownField(
            string label,
            TargetPlatform[] platforms,
            Func<TargetPlatform, bool> getValue,
            Action<TargetPlatform, bool> setValue,
            VisualElement input)
            : base(label, input)
        {
            _platforms = platforms;
            _getValue = getValue;
            _setValue = setValue;
            _input = input;
            _allFlags = _platforms.Aggregate((TargetPlatformFlags)0,
                (flags, platform) => flags | FlagFor(platform));

            AddToClassList("unity-base-field__aligned");
            AddToClassList("unity-popup-field");
            _input.AddToClassList("unity-popup-field__input");
            _input.focusable = true;
            _input.style.flexDirection = FlexDirection.Row;
            _input.style.alignItems = Align.Center;

            _valueLabel = new Label { name = "value" };
            _valueLabel.pickingMode = PickingMode.Ignore;
            _valueLabel.style.flexGrow = 1;
            _input.Add(_valueLabel);

            var arrow = new Label("▾") { name = "arrow", pickingMode = PickingMode.Ignore };
            _input.Add(arrow);

            _input.RegisterCallback<ClickEvent>(_ => ShowMenu());
            _input.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode != KeyCode.Return && evt.keyCode != KeyCode.Space) return;
                ShowMenu();
                evt.StopPropagation();
            });
            RegisterCallback<AttachToPanelEvent>(_ =>
            {
                Preferences.UploadPlatformsChanged -= RefreshFromSource;
                Preferences.UploadPlatformsChanged += RefreshFromSource;
                RefreshFromSource();
            });
            RegisterCallback<DetachFromPanelEvent>(_ => Preferences.UploadPlatformsChanged -= RefreshFromSource);

            SetValueWithoutNotify(ReadValue());
        }

        public override void SetValueWithoutNotify(TargetPlatformFlags newValue)
        {
            base.SetValueWithoutNotify(newValue);
            UpdateValueLabel();
        }

        private static TargetPlatformFlags FlagFor(TargetPlatform platform) =>
            (TargetPlatformFlags)(1 << (int)platform);

        private TargetPlatformFlags ReadValue()
        {
            TargetPlatformFlags flags = 0;
            foreach (var platform in _platforms)
                if (_getValue(platform))
                    flags |= FlagFor(platform);
            return flags;
        }

        private void RefreshFromSource()
        {
            var current = ReadValue();
            if (current != value)
                value = current;
        }

        private void UpdateValueLabel()
        {
            if (value == 0)
            {
                _valueLabel.text = "Nothing";
                return;
            }

            if (value == _allFlags)
            {
                _valueLabel.text = "Everything";
                return;
            }

            _valueLabel.text = string.Join(", ", _platforms
                .Where(platform => (value & FlagFor(platform)) != 0)
                .Select(platform => platform.ToString()));
        }

        private void ShowMenu()
        {
            RefreshFromSource();
            var menu = new GenericDropdownMenu();
            menu.AddItem("Nothing", value == 0, () => ApplyValue(0));
            menu.AddItem("Everything", value == _allFlags, () => ApplyValue(_allFlags));
            menu.AddSeparator("");
            foreach (var platform in _platforms)
            {
                var flag = FlagFor(platform);
                menu.AddItem(platform.ToString(), (value & flag) != 0,
                    () => ApplyValue(value ^ flag));
            }
            menu.DropDown(_input.worldBound, _input);
        }

        private void ApplyValue(TargetPlatformFlags newValue)
        {
            if (newValue == value) return;
            foreach (var platform in _platforms)
                _setValue(platform, (newValue & FlagFor(platform)) != 0);
            value = newValue;
        }
    }
}
