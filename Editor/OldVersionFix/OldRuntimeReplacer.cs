using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
namespace Lyuma.Av3Emulator.Editor.OldVersionFix
{
	public class LyumaAv3RuntimeReplacer
	{
		static LyumaAv3RuntimeReplacer()
		{
			SceneManager.sceneLoaded += ScanAndReplace;
		}

		[InitializeOnLoadMethod]
		private static void ReplaceObjectsInCurrentScene()
		{
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
				Type emulatorType = Type.GetType("LyumaAv3Emulator, Assembly-CSharp, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null");
				if (emulatorType != null)
				{
					Component[] oldEmulators = root.GetComponentsInChildren(emulatorType);
					foreach (var emulator in oldEmulators)
					{
						GameObject emulatorObject = emulator.gameObject;
						Debug.Log("Replacing old Av3Emulator on GameObject: " + emulatorObject.name);
						GameObject.DestroyImmediate(emulator);
						emulatorObject.AddComponent<Runtime.LyumaAv3Emulator>();
					}
				}
				
				Type oscType = Type.GetType("LyumaAv3Osc, Assembly-CSharp, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null");
				if (oscType != null)
				{
					Component[] oldOscs = root.GetComponentsInChildren(oscType);
					foreach (var oldOsc in oldOscs)
					{
						GameObject oscObject = oldOsc.gameObject;
						Debug.Log("Replacing old Av3Osc on GameObject: " + oscObject.name);
						GameObject.DestroyImmediate(oldOsc);
						oscObject.AddComponent<Runtime.LyumaAv3Osc>();
					}
				}
			}
		}
	}
}