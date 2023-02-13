using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BestHTTP.JSON;
using Boo.Lang.Runtime;
using GestureManagerBridge;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace GestureManagerBridge
{
	[InitializeOnLoad, ExecuteInEditMode]
	public class GestureManagerDetector
	{
		static GestureManagerDetector()
		{
			DetectGestureManager();
		}

		public static void DetectGestureManager()
		{
			Type gestureManagerType = GetTypeFromName("BlackStartX.GestureManager.GestureManager");
			bool definesChanged = false;
			string existingDefines = PlayerSettings.GetScriptingDefineSymbolsForGroup(EditorUserBuildSettings.selectedBuildTargetGroup);
			HashSet<string> defineSet = new HashSet<string>();

			if(existingDefines.Length > 0)
				defineSet = new HashSet<string>(existingDefines.Split(';'));

			string[] dependencies = new[]
			{
				"vrchat.blackstartx.gesture-manager",
				"vrchat.blackstartx.gesture-manager.editor"
			};
			
			if (gestureManagerType == null)
			{
				if(defineSet.Remove("GESTURE_MANAGER")) 
					definesChanged = true;
				RemoveFromAssembly(dependencies);
			}
			else
			{
				if(defineSet.Add("GESTURE_MANAGER")) 
					definesChanged = true;
				AddToAssembly(dependencies);
			}
			
			if(definesChanged)
			{
				string finalDefineString = string.Join(";", defineSet.ToArray());
				PlayerSettings.SetScriptingDefineSymbolsForGroup(EditorUserBuildSettings.selectedBuildTargetGroup, finalDefineString);
				Debug.LogFormat("Set Scripting Define Symbols for selected build target ({0}) to: {1}", EditorUserBuildSettings.selectedBuildTargetGroup.ToString(), finalDefineString);
			}
		}

		public static void AddToAssembly(string[] references)
		{
			var files = Directory.GetFiles(Path.GetDirectoryName(Application.dataPath),
				"lyuma.av3emulator.Editor.asmdef", SearchOption.AllDirectories);
			if (files.Length == 0)
			{
				throw new RuntimeException("Can't find reference");
			}
			string text = File.ReadAllText(files[0]);
			AssemblyDefinitionJsonObject def = JsonUtility.FromJson<AssemblyDefinitionJsonObject>(text);
			string[] assemblyReferences = def.references.Concat(references).Distinct().ToArray();
			def.references = assemblyReferences;

			File.WriteAllText(files[0], JsonUtility.ToJson(def));
		}
		
		public static void RemoveFromAssembly(string[] references)
		{
			var files = Directory.GetFiles(Path.GetDirectoryName(Application.dataPath),
				"lyuma.av3emulator.Editor.asmdef", SearchOption.AllDirectories);
			if (files.Length == 0)
			{
				throw new RuntimeException("Can't find reference");
			}

			string text = File.ReadAllText(files[0]);
			AssemblyDefinitionJsonObject def = JsonUtility.FromJson<AssemblyDefinitionJsonObject>(text);
			List<String> assemblyReferences = def.references.ToList();
			foreach (var reference in references)
			{
				assemblyReferences.Remove(reference);
			}

			def.references = assemblyReferences.ToArray();
			File.WriteAllText(files[0], JsonUtility.ToJson(def));
		}

		public static Type GetTypeFromName(string typeName)
		{
			var type = Type.GetType(typeName);
			if (type != null)
				return type;
			foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
			{
				type = a.GetType(typeName);
				if (type != null)
					return type;
			}

			return null;
		}
	}
}