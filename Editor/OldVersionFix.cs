using System;
using System.IO;
using System.Linq;
using Lyuma.Av3Emulator.Runtime;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
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
		private static void ReplaceObjectsInCurrentScene()
		{
			bool EmulatorFound = Directory.Exists("Assets/Lyuma/Av3Emulator"); 
			if (EmulatorFound)
			{
				if (EditorUtility.DisplayDialog("Av3Emulator",
					    "You still have an old version of the Av3Emulator Installed.\n"
						+"This could cause unwanted behaviour. Remove it?",
					    "Yes", "No"))
				{
					Directory.Delete("Assets/Lyuma/Av3Emulator");
					AssetDatabase.Refresh();
				}
			}
			Type emulatorType = Type.GetType("LyumaAv3Emulator, Assembly-CSharp, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null");
			if (emulatorType != null && !EmulatorFound)
			{
				foreach (var assetPath in AssetDatabase.FindAssets("LyumaAv3Emulator t:MonoScript", new[] { "Assets" })
					         .Select(AssetDatabase.GUIDToAssetPath)
					         .Where(x => AssetDatabase.LoadAssetAtPath<MonoScript>(x)?.GetClass() != null))
				{
					var pathComponents = assetPath.Split('/');
					var parentPath = string.Join("/", pathComponents.Take(pathComponents.Length - 2));
					if (EditorUtility.DisplayDialog("Av3Emulator",
						    "You still have an old version of the Av3Emulator Installed.\n"
							+"This could cause unwanted behaviour. Remove it?\n"
						    + "This would delete the following folder:\n" + parentPath,
						    "Yes", "No"))
					{
						Directory.Delete(parentPath); 
						AssetDatabase.Refresh();
					}
				}
			}
			ReplaceEmulator();
		}
		
		private static void ReplaceEmulator()
		{
			GameObject go = GameObject.Find("/Avatars 3.0 Emulator Control");
			if (go != null)
			{
				GameObjectUtility.RemoveMonoBehavioursWithMissingScript(go);
				go.GetOrAddComponent<LyumaAv3Emulator>();
				go.GetOrAddComponent<LyumaAv3Osc>();
			}
		}
	}
}