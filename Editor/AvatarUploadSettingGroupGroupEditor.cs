using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using VRC.SDK3.Avatars.Components;

namespace Anatawa12.ContinuousAvatarUploader.Editor
{
    [CustomEditor(typeof(AvatarUploadSettingGroupGroup))]
    public class AvatarUploadSettingGroupGroupEditor : UnityEditor.Editor
    {
        private SerializedProperty _groups;

        private void OnEnable()
        {
            _groups = serializedObject.FindProperty(nameof(AvatarUploadSettingGroupGroup.groups));
        }

        public override VisualElement CreateInspectorGUI()
        {
            var root = new VisualElement { name = "AvatarUploadSettingGroupGroupEditor" };

            root.Add(new Label("Avatar Upload Settings")
            {
                style = { unityFontStyleAndWeight = FontStyle.Bold }
            });

            root.Add(ContinuousAvatarUploader.UploadButtonGui(new[] { (AvatarUploadSettingGroupGroup)target }, Repaint));

            root.Add(new VisualElement { style = { height = 6 } });

            var groupsField = new PropertyField(_groups, "Groups") { name = "groupsField" };
            groupsField.Bind(serializedObject);
            root.Add(groupsField);

            return root;
        }
    }
}
