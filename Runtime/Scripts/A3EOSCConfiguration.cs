using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;

namespace Lyuma.Av3Emulator.Runtime
{
	[Serializable]
	public struct A3EOSCConfiguration {
		public bool UseRealPipelineIdJSONFile;
		public bool SendRecvAllParamsNotInJSON;
		public bool GenerateOSCConfig;
		public bool LoadOSCConfig;
		public bool SaveOSCConfig;
		public string OSCAvatarID;
		public string OSCFilePath;
		public OuterJson OSCJsonConfig;

		static bool whichtest;
		[Serializable]
		public struct InputOutputPath {
			public string address;
			public string type;
		}
		[Serializable]
		public struct InnerJson {
			public string name;
			public InputOutputPath input;
			public InputOutputPath output;
		}
		[Serializable]
		public class OuterJson {
			public string id;
			public string name;
			public InnerJson[] parameters;
		}

		private readonly static LyumaAv3Runtime.BuiltinParameterDefinition[] OSC_BUILTIN_PARAMETERS = LyumaAv3Runtime.BUILTIN_PARAMETERS.Where(x => x.name != "IsLocal").ToArray();
		public static OuterJson GenerateOuterJSON(VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionParameters expparams, string id, string name) {
			OuterJson oj = new OuterJson();
			oj.id = id;
			oj.name = name;
			const string ADDRESS_PREFIX = "/avatar/parameters/";
			int nonempty;
			if (expparams == null) {
				expparams = ScriptableObject.CreateInstance<VRCExpressionParameters>();
				expparams.parameters = new VRCExpressionParameters.Parameter[] {
						new VRCExpressionParameters.Parameter {
							defaultValue = 0,
							saved = false,
							name = "VRCEmote",
							valueType = VRCExpressionParameters.ValueType.Int
						},
						new VRCExpressionParameters.Parameter {
							defaultValue = 0,
							saved = false,
							name = "VRCFaceBlendH",
							valueType = VRCExpressionParameters.ValueType.Float
						},
						new VRCExpressionParameters.Parameter {
							defaultValue = 0,
							saved = false,
							name = "VRCFaceBlendV",
							valueType = VRCExpressionParameters.ValueType.Float
						}
				};
			}
			nonempty = expparams.parameters.Length;
			foreach (var p in expparams.parameters) {
				if (p.name.Length == 0) {
					nonempty--;
				}
			}
			oj.parameters = new InnerJson[OSC_BUILTIN_PARAMETERS.Length + nonempty];
			int idx = 0;
			// VRC writes these in reverse order. No idea why.
			foreach (var p in expparams.parameters) {
				if (p.name.Length != 0) {
					oj.parameters[nonempty - idx - 1] = new InnerJson {
						name = p.name,
						input = new InputOutputPath {
							address = ADDRESS_PREFIX + p.name,
							type = (p.valueType == VRCExpressionParameters.ValueType.Int ? "Int" :
								(p.valueType == VRCExpressionParameters.ValueType.Float ? "Float" : "Bool"))
						},
						output = new InputOutputPath {
							address = ADDRESS_PREFIX + p.name,
							type = (p.valueType == VRCExpressionParameters.ValueType.Int ? "Int" :
								(p.valueType == VRCExpressionParameters.ValueType.Float ? "Float" : "Bool"))
						}
					};
					idx++;
				}
			}
			for (int i = 0; i < OSC_BUILTIN_PARAMETERS.Length; i++) {
				var bname = OSC_BUILTIN_PARAMETERS[i].name;
				var btype = OSC_BUILTIN_PARAMETERS[i].GetTypeString();
				oj.parameters[idx] = new InnerJson {
					name = bname,
					output = new InputOutputPath {
						address = ADDRESS_PREFIX + bname,
						type = btype
					}
				};
				idx++;
			}
			return oj;
		}
		public static OuterJson ReadJSON(string full_file_path) {
			return JsonUtility.FromJson<OuterJson>(System.IO.File.ReadAllText(full_file_path));
		}
		public static void WriteJSON(string filename, OuterJson oj) {
			// pretty print, remove empty {"input":{"address":"","type":""}} junk that unity dumps in, and match VRChat's whitespace.
			System.IO.File.WriteAllLines(filename, System.Text.RegularExpressions.Regex.Replace("\ufeff" + 
				System.Text.RegularExpressions.Regex.Replace(
					JsonUtility.ToJson(oj, true), ",\\s*\"input\"\\s*:\\s*{\\s*\"address\"\\s*:\\s*\"\"\\s*,\\s*\"type\"\\s*:\\s*\"\"\\s*}\\s*",""),
					"\n(\\s*)\\1(\\S)", "\n$1$2").Split('\n'));
		}

		public const string AVTR_EMULATOR_PREFIX = "avtr_LyumaAv3Emulator_";
		public void EnsureOSCJSONConfig(VRCExpressionParameters expparams, string avatarid, string name)
		{
			try
			{
				bool useRealId = UseRealPipelineIdJSONFile && !string.IsNullOrEmpty(avatarid);
				if (useRealId && !IsProfileIdentifier(avatarid))
					throw new ArgumentException("The avatar ID is not a valid OSC profile identifier.");

				string avatarDirectory = ResolveAvatarDirectory();
				if (avatarDirectory == null)
				{
					SetInMemoryProfile(expparams, name, "not_logged_in",
						"No User folder was found. Please play VRC or login.");
					return;
				}

				LoadOrCreateProfile(avatarDirectory, expparams, avatarid, name, useRealId);
			}
			catch (Exception e)
			{
				Debug.LogWarning("Could not prepare the OSC profile. " + e.Message);
				SetInMemoryProfile(expparams, name, "exception_generating_json", e.Message);
			}
		}

		private void SetInMemoryProfile(VRCExpressionParameters expparams, string name, string fallbackId, string message)
		{
			OSCAvatarID = UseRealPipelineIdJSONFile ? fallbackId : AVTR_EMULATOR_PREFIX + "Default";
			OSCFilePath = message;
			OSCJsonConfig = GenerateOuterJSON(expparams, OSCAvatarID, name);
		}

		private void LoadOrCreateProfile(string avatarDirectory, VRCExpressionParameters expparams,
			string avatarid, string name, bool useRealId)
		{
			if (!useRealId)
			{
				avatarid = AVTR_EMULATOR_PREFIX + (whichtest ? "A" : "B");
				whichtest = !whichtest;
			}

			OSCAvatarID = avatarid;
			OSCFilePath = ResolveConfigPath(avatarDirectory, Path.Combine(avatarDirectory, avatarid + ".json"));
			Directory.CreateDirectory(avatarDirectory);
			if (!useRealId)
				WriteJSON(OSCFilePath, GenerateOuterJSON(expparams, avatarid, name));

			OSCJsonConfig = null;
			if (File.Exists(OSCFilePath))
			{
				try
				{
					OSCJsonConfig = ReadJSON(OSCFilePath);
				}
				catch (Exception e) when (e is ArgumentException || e is IOException || e is UnauthorizedAccessException)
				{
					Debug.LogWarning("Could not read the saved OSC profile. " + e.Message);
				}
			}
			if (OSCJsonConfig == null)
				OSCJsonConfig = GenerateOuterJSON(expparams, OSCAvatarID, name);
		}

		internal bool AccessSelectedProfile(bool save)
		{
			try
			{
				AccessProfile(ResolveAvatarDirectory(), save);
				return true;
			}
			catch (Exception e) when (e is ArgumentException || e is IOException ||
				e is UnauthorizedAccessException || e is NotSupportedException)
			{
				Debug.LogWarning("Could not " + (save ? "save" : "load") + " the selected OSC profile. " + e.Message);
				return false;
			}
		}

		private void AccessProfile(string avatarDirectory, bool save)
		{
			string path = ResolveConfigPath(avatarDirectory, OSCFilePath);
			if (save)
			{
				WriteJSON(path, OSCJsonConfig);
			}
			else
			{
				OSCJsonConfig = ReadJSON(path) ?? throw new ArgumentException("The selected file does not contain an OSC profile.");
			}
		}

		private static bool IsProfileIdentifier(string value)
		{
			return !string.IsNullOrEmpty(value) && value.All(c => char.IsLetterOrDigit(c) || c == '_' || c == '-');
		}

		private static string ResolveConfigPath(string avatarDirectory, string selectedPath)
		{
			if (avatarDirectory == null || string.IsNullOrEmpty(selectedPath) || !Path.IsPathRooted(selectedPath))
				throw new ArgumentException("Select a JSON file inside the active OSC avatar directory.");

			string path = Path.GetFullPath(selectedPath);
			var comparison = Path.DirectorySeparatorChar == '\\' ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
			if (!path.StartsWith(avatarDirectory + Path.DirectorySeparatorChar, comparison) ||
				!string.Equals(Path.GetExtension(path), ".json", StringComparison.OrdinalIgnoreCase))
				throw new ArgumentException("Select a JSON file inside the active OSC avatar directory.");

			RequireOrdinaryPath(avatarDirectory);
			for (string current = path; !string.Equals(current, avatarDirectory, comparison); current = Path.GetDirectoryName(current))
			{
				if (Path.GetFileName(current).IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
					throw new ArgumentException("The selected OSC profile has an invalid file or directory name.");
				RequireOrdinaryPath(current);
			}
			return path;
		}

		private static void RequireOrdinaryPath(string path)
		{
			try
			{
				if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
					throw new ArgumentException("OSC profiles require ordinary files and directories.");
			}
			catch (FileNotFoundException) { }
			catch (DirectoryNotFoundException) { }
		}

		private static string ResolveAvatarDirectory()
		{
			string localData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
			if (string.IsNullOrEmpty(localData))
				return null;
			if (string.Equals(Path.GetFileName(localData), "Local", StringComparison.OrdinalIgnoreCase))
				localData = Path.Combine(Path.GetDirectoryName(localData), "LocalLow");

			string oscRoot = Path.GetFullPath(localData);
			foreach (string segment in new[] { "VRChat", "vrchat", "OSC" })
			{
				oscRoot = Path.Combine(oscRoot, segment);
				RequireOrdinaryPath(oscRoot);
			}

			var accountType = Type.GetType("VRC.Core.APIUser, VRCCore-Editor");
			object account = accountType?.GetProperty("CurrentUser",
				System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public)?.GetValue(null);
			string user = account == null ? null : accountType.GetProperty("id")?.GetValue(account) as string;
			if (string.IsNullOrEmpty(user))
			{
				if (!Directory.Exists(oscRoot))
					return null;
				user = new DirectoryInfo(oscRoot).EnumerateDirectories()
					.Where(directory => IsProfileIdentifier(directory.Name) &&
						(directory.Attributes & FileAttributes.ReparsePoint) == 0)
					.OrderByDescending(directory => directory.LastWriteTimeUtc)
					.Select(directory => directory.Name)
					.FirstOrDefault();
			}

			if (user == null)
				return null;
			if (!IsProfileIdentifier(user))
				throw new ArgumentException("The account ID is not a valid OSC profile identifier.");
			string accountDirectory = Path.Combine(oscRoot, user);
			RequireOrdinaryPath(accountDirectory);
			return Path.Combine(accountDirectory, "Avatars");
		}
	}
}
