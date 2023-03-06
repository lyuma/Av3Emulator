using System;
using System.Collections.Generic;
using System.Reflection;
using JetBrains.Annotations;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Lyuma.Av3Emulator.Editor.OldVersionFix
{
    public class ScriptGuidMigrator
    {
        private static ActiveEditorTracker _tracker;

        public static bool DoMigration(GameObject gameObject)
        {
            // Because we cannot get instance of MonoBehaviour of missing script using GameObject.GetComponents<Component>()
            // I get MonoBehaviour instance from Editor created by ActiveEditorTracker.
            // (Unity unnecessarily replaces invalid object to actual null in mono side.)
            //
            // We have way to get instanceID in "m_Component" property of GameObject so if we have way to
            // create ScriptableObject from instanceId, we may use that way.
            // I couldn't find that so I'm using this weird way.

            // according to https://github.com/Unity-Technologies/UnityCsReference/blob/b22fe1bb369565ac8ba0f6dd1ae49aee56d63070/Editor/Mono/ActiveEditorTracker.bindings.cs#L14-L15,
            // ActiveEditorTracker is big overhead for native side so I cache this instance.
            var tracker = _tracker ?? (_tracker = NewActiveEditorTracker());
            tracker.ForceRebuild();

            // update tracker to 
            SetObjectsLockedByThisTracker.Invoke(tracker, new object[] { new List<Object> { gameObject } });
            tracker.ForceRebuild();

            var modified = false;

            var editors = tracker.activeEditors;

            foreach (var editor in editors)
            {
                if (!IsMissingScriptMonoBehaviour(editor.target)) continue;
                var componentSerialized = editor.serializedObject;

                var script = componentSerialized.FindProperty("m_Script");
                if (script == null) continue;

                if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(script.objectReferenceInstanceIDValue,
                        out var scriptGuid, out long scriptFileId)) continue;

                // fileID of .cs file as a MonoScript asset is always 11500000.
                if (scriptFileId == 11500000)
                {
                    if (_typeMapping.TryGetValue(scriptGuid, out var newMonoScript) && newMonoScript)
                    {
                        // we found new script file.
                        // replacing m_Script is not allowed in most case but if the script is missing, it's allowed.
                        // replacing m_Script is is better than re-creating behaviour because it keeps all configuration.
                        script.objectReferenceValue = newMonoScript;
                        componentSerialized.ApplyModifiedPropertiesWithoutUndo();
                        modified = true;
                    }
                }
            }

            return modified;
        }

        private static ActiveEditorTracker NewActiveEditorTracker()
        {
            var tracker = new ActiveEditorTracker();
            tracker.ForceRebuild();
            return tracker;
        }

        private static bool IsMissingScriptMonoBehaviour(Object obj) =>
            obj ? obj.GetType() == typeof(MonoBehaviour) : obj is MonoBehaviour;

        // this method live in 2019.4-2023.1
        [NotNull] private static readonly MethodInfo SetObjectsLockedByThisTracker =
            typeof(ActiveEditorTracker).GetMethod(
                "SetObjectsLockedByThisTracker",
                BindingFlags.Instance | BindingFlags.NonPublic,
                null,
                new[] { typeof(List<Object>) },
                null)
            ?? throw new InvalidOperationException("ActiveEditorTracker.SetObjectsLockedByThisTracker not found");

        // based on https://github.com/lyuma/Av3Emulator/tree/v2.9.11/Scripts
        // and https://github.com/lyuma/Av3Emulator/tree/83a9a4eeb0580ad223dc961075887a71c46b21a5/Runtime/Scripts
        private static Dictionary<string, MonoScript> _typeMapping = new Dictionary<string, MonoScript>
        {
            // GestureManagerAv3Menu
            ["4cc9dba3b86035b43b8baaca33369f11"] = MonoScript("90a58aec4b5146a6939684f0adc75ba3"),
            // LyumaAv3Emulator
            ["226ca8e52c3922d4a85b20831b97caf3"] = MonoScript("70803509c1e54a23bf0f4a227c4edf8c"),
            // LyumaAv3Menu
            ["3865e5f6001a4a9286e8c3f33314c306"] = MonoScript("8e742198ba224ba39857612167f4eed7"),
            // LyumaAv3Osc
            ["784aa748a7308b94a8f15dcd76531956"] = MonoScript("754c8aa70fe54f1cb0c3a5cd881838cd"),
            // LyumaAv3Runtime
            ["da29383b5c207b04585f808c1caad277"] = MonoScript("1e3772f42e08485895a8b85e0eefc62e"),
        };

        private static MonoScript MonoScript(string guid) =>
            AssetDatabase.LoadAssetAtPath<MonoScript>(AssetDatabase.GUIDToAssetPath(guid));
    }
}
