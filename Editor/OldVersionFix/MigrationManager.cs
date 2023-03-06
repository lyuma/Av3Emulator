using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Directory = UnityEngine.Windows.Directory;
using Object = System.Object;

namespace Lyuma.Av3Emulator.Editor.OldVersionFix
{
	public static class MigrationManager
	{
		static MigrationManager()
		{
			SceneManager.sceneLoaded += ScanAndReplace;
		}

		[InitializeOnLoadMethod]
		private static void ReplaceObjectsInCurrentScene()
		{
			bool EmulatorFound = Directory.Exists("Assets/Lyuma/Av3Emulator"); 
			if (EmulatorFound)
			{
				if (EditorUtility.DisplayDialog("Av3Emulator",
					    "You still have an old version of the Av3Emulator Installed.\nThis could cause unwanted behaviour. Remove it?",
					    "Yes", "No"))
				{
					Directory.Delete("Assets/Lyuma/Av3Emulator");
					AssetDatabase.Refresh();
				}
			}
			Type emulatorType = Type.GetType("LyumaAv3Emulator, Assembly-CSharp, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null");
			if (emulatorType != null && !EmulatorFound)
			{
				string[] guids = AssetDatabase.FindAssets("LyumaAv3Emulator", new[] {"Assets"});
				string[] assetPaths = guids.Select(AssetDatabase.GUIDToAssetPath).ToArray();
				foreach (var assetPath in assetPaths)
				{
					string absolutePath = Application.dataPath + "/" + assetPath.Substring(7);
					string absoluteParentPath = new System.IO.DirectoryInfo(absolutePath).Parent.Parent.FullName;
					string parentPath = "Assets" + Path.DirectorySeparatorChar + absoluteParentPath.Substring(Application.dataPath.Length + 1);
					if (EditorUtility.DisplayDialog("Av3Emulator",
						    "You still have an old version of the Av3Emulator Installed.\nThis could cause unwanted behaviour. Remove it?\n"
						    + "This would delete the following folder:\n" + parentPath,
						    "Yes", "No"))
					{
						Directory.Delete(parentPath); 
						AssetDatabase.Refresh();
					}
				}
			}
			GameObject[] objects = SceneManager.GetActiveScene().GetRootGameObjects();
			ReplaceEmulatorOnObjects(objects);
		}
		
		private static void ScanAndReplace(Scene scene, LoadSceneMode mode)
		{
			GameObject[] objects = scene.GetRootGameObjects();
			ReplaceEmulatorOnObjects(objects);
		}

		private static void ReplaceEmulatorOnObjects(GameObject[] objects)
		{
			foreach (var root in objects)
			{
				ReplaceEmulatorRecursive(root.transform);
			}
		}
		private static void ReplaceEmulatorRecursive(Transform parent)
		{
			ReplaceEmulator(parent);
			foreach (Transform child in parent)
			{
				ReplaceEmulator(child);
				ReplaceEmulatorRecursive(child);
			}
		}

		private static void ReplaceEmulator(Transform transform)
		{
			if (transform.name.ToLower().Contains("emulator"))
			{
				Component[] components = transform.GetComponents<Component>();
				bool found = false;
				foreach (var component in components)
				{
					if (component == null)
					{
						GameObjectUtility.RemoveMonoBehavioursWithMissingScript(transform.gameObject); 
						found = true;
					}
				}

				if (found)
				{
					transform.gameObject.AddComponent<Runtime.LyumaAv3Emulator>();
					transform.gameObject.AddComponent<Runtime.LyumaAv3Osc>();
				}
			};
		}
	}
}
