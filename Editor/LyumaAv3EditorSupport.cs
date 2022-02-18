/* Copyright (c) 2020-2022 Lyuma <xn.lyuma@gmail.com>

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE. */
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEngine.Animations;
using UnityEditor.Animations;
using UnityEditor.Playables;
using UnityEngine.Playables;
using VRC.SDK3.Avatars.Components;

[InitializeOnLoadAttribute]
public static class LyumaAv3EditorSupport
{
    static Dictionary<VRCAvatarDescriptor.AnimLayerType, string> animLayerToDefaultFile = new Dictionary<VRCAvatarDescriptor.AnimLayerType, string> {
        {VRCAvatarDescriptor.AnimLayerType.TPose, "vrc_AvatarV3UtilityTPose"},
        {VRCAvatarDescriptor.AnimLayerType.IKPose, "vrc_AvatarV3UtilityIKPose"},
        {VRCAvatarDescriptor.AnimLayerType.Base, "vrc_AvatarV3LocomotionLayer"},
        {VRCAvatarDescriptor.AnimLayerType.Sitting, "vrc_AvatarV3SittingLayer"},
        {VRCAvatarDescriptor.AnimLayerType.Additive, "vrc_AvatarV3IdleLayer"},
        {VRCAvatarDescriptor.AnimLayerType.FX, "vrc_AvatarV3FaceLayer"},
        {VRCAvatarDescriptor.AnimLayerType.Action, "vrc_AvatarV3ActionLayer"},
        {VRCAvatarDescriptor.AnimLayerType.Gesture, "vrc_AvatarV3HandsLayer"},
    };
    static Dictionary<VRCAvatarDescriptor.AnimLayerType, string> animLayerToDefaultAvaMaskFile = new Dictionary<VRCAvatarDescriptor.AnimLayerType, string>
    {
        {VRCAvatarDescriptor.AnimLayerType.TPose, "vrc_MusclesOnly"},
        {VRCAvatarDescriptor.AnimLayerType.IKPose, "vrc_MusclesOnly"},
        {VRCAvatarDescriptor.AnimLayerType.Base, null},//"LyumaFullMask"},
        {VRCAvatarDescriptor.AnimLayerType.Sitting, null},//"LyumaFullMask"},
        {VRCAvatarDescriptor.AnimLayerType.Additive, null},//"LyumaFullMask"},
        {VRCAvatarDescriptor.AnimLayerType.FX, "LyumaEmptyMask"}, // TODO
        {VRCAvatarDescriptor.AnimLayerType.Action, null},//"vrc_MusclesOnly"},
        {VRCAvatarDescriptor.AnimLayerType.Gesture, "vrc_HandsOnly"},
    };

    static void InitDefaults() {
        foreach (var kv in animLayerToDefaultFile) {
            if (kv.Value == null) {
                LyumaAv3Runtime.animLayerToDefaultController[kv.Key] = null;
            } else
            {
                AnimatorController ac = AssetDatabase.LoadAssetAtPath<AnimatorController>("Assets/VRCSDK/Examples3/Animation/Controllers/" + kv.Value + ".controller");
                if (ac == null)
                {
                    Debug.LogWarning("Failed to resolve animator controller " + kv.Value + " for " + kv.Key);
                    ac = null;
                }
                LyumaAv3Runtime.animLayerToDefaultController[kv.Key] = ac;
            }
        }
        foreach (var kv in animLayerToDefaultAvaMaskFile) {
            if (kv.Value == null) {
                LyumaAv3Runtime.animLayerToDefaultAvaMask[kv.Key] = null;
            } else
            {
                AvatarMask mask = null;
                foreach (var guid in AssetDatabase.FindAssets(kv.Value))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    mask = AssetDatabase.LoadAssetAtPath<AvatarMask>(path);
                }
                if (mask == null)
                {
                    Debug.LogWarning("Failed to resolve avatar mask " + kv.Value + " for " + kv.Key);
                    mask = new AvatarMask();
                }
                LyumaAv3Runtime.animLayerToDefaultAvaMask[kv.Key] = mask;
            }
        }
        foreach (string guid in AssetDatabase.FindAssets("EmptyController")) {
            LyumaAv3Emulator.EmptyController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(AssetDatabase.GUIDToAssetPath(guid));
        }

        LyumaAv3Runtime.updateSelectionDelegate = (go) => {
            if (go == null && LyumaAv3Emulator.emulatorInstance != null) {
                Debug.Log("Resetting selected object: " + LyumaAv3Emulator.emulatorInstance);
                go = LyumaAv3Emulator.emulatorInstance.gameObject;
            }
            Debug.Log("Setting selected object: " + go);
            Selection.SetActiveObjectWithContext(go, go);
            // Highlighter.Highlight("Inspector", "Animator To Debug");
        };

        LyumaAv3Runtime.updateSceneLayersDelegate = (layers) => {
            Debug.Log("Setting selected layers: " + layers);
            Tools.visibleLayers = layers;
            Camera c = Camera.main;
            if (c != null) {
                c.cullingMask = layers;
            }
            // Highlighter.Highlight("Inspector", "Animator To Debug");
        };

        LyumaAv3Runtime.addRuntimeDelegate = (runtime) => {
            MoveComponentToTop(runtime);
        };

        LyumaAv3Osc.DrawDebugRectDelegate = (Rect pos, Color col, Color outlineCol) => {
            Color origColor = GUI.color;
            GUI.color = col;
            UnityEditor.Handles.BeginGUI();
            UnityEditor.Handles.DrawSolidRectangleWithOutline(pos, col, outlineCol);
            UnityEditor.Handles.EndGUI();
            GUI.color = origColor;
        };
        LyumaAv3Osc.DrawDebugTextDelegate = (ref Vector3 pos, Color backgroundCol, Color outlineCol, Color textCol, string str) => {
            Color origColor = GUI.color;
            GUI.color = backgroundCol;
            var view = UnityEditor.SceneView.currentDrawingSceneView;
            Vector2 size = GUI.skin.label.CalcSize(new GUIContent(str));
            Rect contentRect = new Rect(pos.x, pos.y, size.x, size.y);
            UnityEditor.Handles.BeginGUI();
            UnityEditor.Handles.DrawSolidRectangleWithOutline(contentRect, backgroundCol, outlineCol);
            GUI.color = textCol;
            contentRect.x += 1;
            contentRect.y += 1;
            GUI.Label(contentRect, str);
            UnityEditor.Handles.EndGUI();
            GUI.color = origColor;
            pos.x += contentRect.width + 2;
            if (pos.x + contentRect.width * 2 > view.position.width) {
                pos.x = 0;
            }
            pos.y += contentRect.height + 2;
        };
    }

    static void MoveComponentToTop(Component c) {
        GameObject go = c.gameObject;
        Component[] components = go.GetComponents<Component>();
        for (int i = 0; i < components.Length; i++) {
            if (components[i].GetType().Name.Contains("PipelineSaver")) {
                return;
            }
        }
        try {
            if (PrefabUtility.IsPartOfAnyPrefab(go)) {
                PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            }
        } catch (System.Exception) {}
        int moveUpCalls = components.Length - 2;
        if (!PrefabUtility.IsPartOfAnyPrefab(go.GetComponents<Component>()[1])) {
            for (int i = 0; i < moveUpCalls; i++) {
                UnityEditorInternal.ComponentUtility.MoveComponentUp(c);
            }
        }
    }

    // register an event handler when the class is initialized
    static LyumaAv3EditorSupport()
    {
        InitDefaults();
    }

    [MenuItem("Tools/Enable Avatars 3.0 Emulator")]
    public static void EnableAv3Testing() {
        GameObject go = GameObject.Find("/Avatars 3.0 Emulator Control");
        if (go != null) {
            go.SetActive(true);
        } else {
            go = new GameObject("Avatars 3.0 Emulator Control");
        }
        Selection.SetActiveObjectWithContext(go, go);
        go.GetOrAddComponent<LyumaAv3Emulator>();
        go.GetOrAddComponent<LyumaAv3Osc>();
    }
}
