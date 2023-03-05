using System;
using System.Collections;
using System.Reflection;
using JetBrains.Annotations;
using UnityEditor;
using UnityEngine;

namespace Lyuma.Av3Emulator.Editor
{
    // based on https://github.com/lilxyzw/lilToon/blob/36ff295b41cc4c398b95419d513adeecb0b322d4/Assets/lilToon/Editor/lilStartup.cs
    // Copyright (c) 2020-2021 lilxyzw originally published under MIT License

    [InitializeOnLoad]
    public static class WarningOnImportingUnityPackage
    {
        static WarningOnImportingUnityPackage()
        {
            AssetDatabase.importPackageStarted -= OnImportPackageStarted;
            AssetDatabase.importPackageStarted += OnImportPackageStarted;
        }

        private static void OnImportPackageStarted(string packageName)
        {
            var indexlil = packageName.IndexOf("LyumaAv3Emulator", StringComparison.Ordinal);
            if (indexlil < 0) return;
            var packageVerString = packageName.Substring(indexlil + "LyumaAv3Emulator_v".Length);

            packageVerString = FindVersionName(packageVerString);

            var semPackage = ReadSemVer(packageVerString);
            var semCurrent = ReadSemVer(CurrentAv3EmulatorVersion.Value);
            if (semPackage == null || semCurrent == null) return;

            if (
                semPackage[0] < semCurrent[0] ||
                semPackage[0] == semCurrent[0] && semPackage[1] < semCurrent[1] ||
                semPackage[0] == semCurrent[0] && semPackage[1] == semCurrent[1] && semPackage[2] < semCurrent[2]
            )
            {
                if (EditorUtility.DisplayDialog("Av3Emulator",
                        "The package you are importing is an older version than the already imported LyumaAv3Emulator. " +
                        "Do you want to continue?",
                        "Yes", "No")) return;
                CoroutineHandler.StartStaticCoroutine(ClosePackageImportWindow());
            }
        }

        // '2.9.11(4).unitypackage' -> '2.9.11'
        private static string FindVersionName(string name)
        {
            for (var i = 0; i < name.Length; i++)
            {
                if ("0123456789.".IndexOf(name[i]) == -1)
                    return name.Substring(0, i);
            }

            return name;
        }

        private static IEnumerator ClosePackageImportWindow()
        {
            var type = typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.PackageImport");
            var method = typeof(EditorWindow).GetMethod("HasOpenInstances", BindingFlags.Static | BindingFlags.Public);
            if (method != null)
            {
                var genmethod = method.MakeGenericMethod(type);
                while (!(bool)genmethod.Invoke(null, null))
                {
                    yield return null;
                }

                EditorWindow.GetWindow(type).Close();
            }
        }

        private static int[] ReadSemVer([NotNull] string sem)
        {
            var parts = sem.Split('.');
            if (parts.Length < 3) return null;
            int major, minor, patch;
            try
            {
                major = int.Parse(parts[0]);
                minor = int.Parse(parts[1]);
                patch = int.Parse(parts[2]);
            }
            catch
            {
                return null;
            }

            return new[] { major, minor, patch };
        }

        //------------------------------------------------------------------------------------------------------------------------------
        // based on CoroutineHandler.cs
        // https://github.com/Unity-Technologies/EndlessRunnerSampleGame/blob/master/Assets/Scripts/CoroutineHandler.cs
        public class CoroutineHandler : MonoBehaviour
        {
            private static CoroutineHandler _instance;

            public static CoroutineHandler Instance
            {
                get
                {
                    if (_instance == null)
                    {
                        GameObject o = new GameObject("CoroutineHandler")
                        {
                            hideFlags = HideFlags.HideAndDontSave
                        };
                        _instance = o.AddComponent<CoroutineHandler>();
                    }

                    return _instance;
                }
            }

            public void OnDisable()
            {
                if (_instance) Destroy(_instance.gameObject);
            }

            public static Coroutine StartStaticCoroutine(IEnumerator coroutine)
            {
                return Instance.StartCoroutine(coroutine);
            }
        }

        // separate class for lazy initialization
        public static class CurrentAv3EmulatorVersion
        {
            public static string Value = ComputeAv3EmulatorVersion();

            private static string ComputeAv3EmulatorVersion()
            {
                try
                {
                    var packageJsonPath = AssetDatabase.GUIDToAssetPath("8609c13ba26b8cb4aa554ecf7d38efbe");
                    var packageJsonAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(packageJsonPath);
                    var packageJson = JsonUtility.FromJson<PackageJson>(packageJsonAsset.text);
                    return packageJson.version ?? "";
                }
                catch
                {
                    return "";
                }
            }

            [Serializable]
            private class PackageJson
            {
                public string version;
            }
        }
    }
}
