using System;
using System.IO;
using System.Linq;
using Lyuma.Av3Emulator.Runtime;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using static Lyuma.Av3Emulator.Runtime.LyumaAv3Emulator;
using Directory = UnityEngine.Windows.Directory;
using Object = System.Object;

namespace Lyuma.Av3Emulator.Editor
{
	public class OldVersionFix
	{
		static OldVersionFix()
		{
			EditorSceneManager.sceneOpened += (a, b) => ReplaceEmulator();
		}

		[InitializeOnLoadMethod]
		private static void ScanForOldEmulator()
		{
			string emulatorDir = GetEmulatorPath();
			if (emulatorDir != null)
			{
				if (EditorUtility.DisplayDialog("Av3Emulator",
					    "You still have an old version of the Av3Emulator installed.\n"
					    + "This could cause unwanted behaviour. Remove it?",
					    "Yes", "No"))
				{
					Directory.Delete(emulatorDir);
					AssetDatabase.Refresh();
				}
			}

			
			Type emulatorType = Type.GetType("LyumaAv3Emulator, Assembly-CSharp, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null");
			if (emulatorType != null && emulatorDir == null)
			{
				foreach (var assetPath in AssetDatabase.FindAssets("LyumaAv3Emulator t:MonoScript", new[] { "Assets" })
					         .Select(AssetDatabase.GUIDToAssetPath)
					         .Where(x => AssetDatabase.LoadAssetAtPath<MonoScript>(x)?.GetClass() != null))
				{
					var pathComponents = assetPath.Split('/');
					var parentPath = string.Join("/", pathComponents.Take(pathComponents.Length - 2));
					EditorUtility.DisplayDialog("Av3Emulator",
						"You still have an old version of the Av3Emulator installed.\n"
						+ "This could cause unwanted behaviour.\n"
						+ "Please remove the old Av3Emulator install\n" 
						+ "This install seems to be located at: " + parentPath,
						"Ok");
				}
			}
			ReplaceEmulator();
		}

		private static string GetEmulatorPath()
		{
			var foundByGuid = AssetDatabase.GUIDToAssetPath("e42c5d0b3e2b3f64e8a88c225b3cef62");

			if (!string.IsNullOrEmpty(foundByGuid))
				return Directory.Exists(foundByGuid) ? foundByGuid : null;

			if (Directory.Exists("Assets/Lyuma/Av3Emulator"))
				return "Assets/Lyuma/Av3Emulator";
			return null;
		}
		
		private static void ReplaceEmulator()
		{
			GameObject go = GameObject.Find("/Avatars 3.0 Emulator Control");
			if (go != null)
			{
				GameObjectUtility.RemoveMonoBehavioursWithMissingScript(go);
				GetOrAddComponent<LyumaAv3Emulator>(go);
				GetOrAddComponent<LyumaAv3Osc>(go);
			}
		}
	}
}