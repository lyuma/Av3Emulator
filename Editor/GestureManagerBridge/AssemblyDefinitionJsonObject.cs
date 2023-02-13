using System;
using Codice.CM.Client.Differences;

namespace GestureManagerBridge
{
	[Serializable]
	public class AssemblyDefinitionJsonObject
	{
		public string name;
		public string[] references;
		public string[] includePlatforms;
		public string[]	excludePlatforms;
		public bool allowUnsafeCode;
		public bool overrideReferences;
		public string[]	precompiledReferences;
		public bool autoReferenced;
		public string[]	defineConstraints;
		public string[]	versionDefines;
		public bool noEngineReferences;
	}
}