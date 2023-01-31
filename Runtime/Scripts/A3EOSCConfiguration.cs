using System;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;

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
    readonly static string [][] OSC_BUILTIN_PARAMETERS = {
        new string[]{"VelocityZ","Float"},
        new string[]{"VelocityY","Float"},
        new string[]{"VelocityX","Float"},
        new string[]{"InStation","Bool"},
        new string[]{"Seated","Bool"},
        new string[]{"AFK","Bool"},
        new string[]{"Upright","Float"},
        new string[]{"AngularY","Float"},
        new string[]{"Grounded","Bool"},
        new string[]{"MuteSelf","Bool"},
        new string[]{"VRMode","Int"},
        new string[]{"TrackingType","Int"},
        new string[]{"GestureRightWeight","Float"},
        new string[]{"GestureRight","Int"},
        new string[]{"GestureLeftWeight","Float"},
        new string[]{"GestureLeft","Int"},
        new string[]{"Voice","Float"},
        new string[]{"Viseme","Int"}
    };
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
            var bname = OSC_BUILTIN_PARAMETERS[i][0];
            var btype = OSC_BUILTIN_PARAMETERS[i][1];
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
    public void EnsureOSCJSONConfig(VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionParameters expparams, string avatarid, string name) {
        try {
            string localLowPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (localLowPath.EndsWith("Local")) {
                localLowPath = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(localLowPath), "LocalLow");
            }
            string userid = null;
            string vrcOSCPath = System.IO.Path.Combine(localLowPath, "VRChat", "vrchat", "OSC");

            System.Type apiusertype = System.Type.GetType("VRC.Core.APIUser, VRCCore-Editor");
            if (apiusertype != null) {
                var idprop = apiusertype.GetProperty("id", System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.Public);
                var prop = apiusertype.GetProperty("CurrentUser", System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.Public);
                // Debug.Log("idprop " + idprop);
                if (idprop != null && prop != null) {
                    var apiuserinst = prop.GetValue(null);
                    if (apiuserinst != null) {
                        // Debug.Log("apiuser " + apiuserinst);
                        userid = (string)idprop.GetValue(apiuserinst);
                    }
                }
            }
            try {
                System.IO.Directory.CreateDirectory(vrcOSCPath);
            } catch (System.IO.IOException) {
            }
            if (userid == null || userid.Length == 0) {
                // do not have a known user account.
                // find the most recent user folder.
                DateTime dt = DateTime.MinValue;
                // Debug.Log("lets-a look at " + vrcOSCPath);
                foreach (string file in System.IO.Directory.GetDirectories(vrcOSCPath, "*", System.IO.SearchOption.TopDirectoryOnly)) {
                    // Debug.Log("enumerate a file " + file);
                    DateTime thisdt = System.IO.File.GetLastWriteTime(file);
                    if (thisdt > dt) {
                        userid = System.IO.Path.GetFileName(file);
                        dt = thisdt;
                    }
                }
            }
            if (userid == null || userid.Length == 0) {
                OSCAvatarID = "not_logged_in";
                OSCFilePath = "No User folder was found. Please play VRC or login.";
                OSCJsonConfig = GenerateOuterJSON(expparams, "not_logged_in", name);
                return;
            }
            string avatarDirectory = System.IO.Path.Combine(vrcOSCPath, userid, "Avatars");
            try {
                System.IO.Directory.CreateDirectory(avatarDirectory);
            } catch (System.IO.IOException) {
            }
            if (avatarid != null && UseRealPipelineIdJSONFile) {
                OSCAvatarID = avatarid; // json file already exists: let's use it.
                OSCFilePath = System.IO.Path.Combine(avatarDirectory, avatarid + ".json");
            } else {
                avatarid = AVTR_EMULATOR_PREFIX + (whichtest ? "A" : "B");
                OSCFilePath = System.IO.Path.Combine(avatarDirectory, avatarid + ".json");
                whichtest = !whichtest;
                OSCAvatarID = avatarid;
                WriteJSON(OSCFilePath, GenerateOuterJSON(expparams, avatarid, name));
            }
            if (System.IO.File.Exists(OSCFilePath)) {
                try {
                    OSCJsonConfig = ReadJSON(OSCFilePath);
                } catch (Exception e) {
                    Debug.LogException(e);
                    Debug.Log("File failed to load. Generating new JSON for " + OSCAvatarID);
                    OSCJsonConfig = GenerateOuterJSON(expparams, OSCAvatarID, name);
                }
            } else {
                Debug.Log("File does not exist. Generating new JSON for " + OSCAvatarID);
                OSCJsonConfig = GenerateOuterJSON(expparams, OSCAvatarID, name);
            }
        } catch (Exception e) {
            Debug.LogException(e);
            OSCAvatarID = "exception_generating_json";
            OSCFilePath = e.Message;
            Debug.Log("Unable to determine Avatar ID or JSON file path. Generating config.");
            OSCJsonConfig = GenerateOuterJSON(expparams, "exception_generating_json", name);
        }
    }

}
