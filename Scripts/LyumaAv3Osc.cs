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
using UnityEngine;
using System.Collections.Generic;

public class LyumaAv3Osc : MonoBehaviour {
    public delegate void DrawDebugRect(Rect pos, Color col, Color outlineCol);
    public static DrawDebugRect DrawDebugRectDelegate;
    public delegate void DrawDebugText(ref Vector3 pos, Color backgroundCol, Color outlineCol, Color textCol, string str);
    public static DrawDebugText DrawDebugTextDelegate;

    SimpleOSCReceiver receiver;
    public bool enabled = false;
    public int port = 9000;
    public bool alwaysShowDebug;
    public Dictionary<string, SimpleOSCReceiver.OSCMessage> knownPaths = new Dictionary<string, SimpleOSCReceiver.OSCMessage>();
    private List<SimpleOSCReceiver.OSCMessage> messages = new List<SimpleOSCReceiver.OSCMessage>();

    public void Update() {
        if (enabled && receiver == null) {
            receiver = new SimpleOSCReceiver();
            receiver.OpenClient(port);
        }
        if (!enabled && receiver != null) {
            receiver.StopClient();
        }
        messages.Clear();
        if (receiver != null) {
            receiver.GetIncomingOSC(messages);
            foreach (var msg in messages) {
                Debug.Log("Got OSC message: " + msg.path + " args " + msg.typeTag);
                knownPaths[msg.path] = msg;
                foreach (var runtime in LyumaAv3Emulator.emulatorInstance.runtimes) {
                    if (msg.path.StartsWith("/avatar/parameters/")) {
                        string ParamName = msg.path.Split(new char[]{'/'}, 4)[3];
                        // TODO: I do not know if VRChat supports OSC bool.
                        if (msg.arguments.Length > 0 && msg.arguments[0].GetType() == typeof(bool)) {
                            int idx;
                            if (runtime.BoolToIndex.TryGetValue(ParamName, out idx)) {
                                runtime.Bools[idx].value = (bool)(msg.arguments[0]);
                            }
                        }
                        if (msg.arguments.Length > 0 && msg.arguments[0].GetType() == typeof(int)) {
                            int idx;
                            if (runtime.BoolToIndex.TryGetValue(ParamName, out idx)) {
                                runtime.Bools[idx].value = ((int)(msg.arguments[0])) != 0;
                            }
                            if (runtime.IntToIndex.TryGetValue(ParamName, out idx)) {
                                runtime.Ints[idx].value = (int)(msg.arguments[0]);
                            }
                        }
                        if (msg.arguments.Length > 0 && msg.arguments[0].GetType() == typeof(float)) {
                            int idx;
                            if (runtime.FloatToIndex.TryGetValue(ParamName, out idx)) {
                                runtime.Floats[idx].value = (float)(msg.arguments[0]);
                            }
                        }
                    }
                }
            }
        }
        if (!enabled && receiver != null) {
            receiver = null;
        }
    }
    public void OnDestroy() {
        if (receiver != null) {
            receiver.StopClient();
        }
    }
    
    Vector3 ScreenToWorld(float x, float y) {
        Camera camera = Camera.current;
        Vector3 s = camera.WorldToScreenPoint(transform.position);
        return camera.ScreenToWorldPoint(new Vector3(x, camera.pixelHeight - y, s.z));
    }

    Rect ScreenRect(int x, int y, int w, int h) {
        Vector3 tl = ScreenToWorld(x, y);
        Vector3 br = ScreenToWorld(x + w, y + h);
        return new Rect(tl.x, tl.y, br.x - tl.x, br.y - tl.y);
    }
    void OnDrawGizmos() {
        if (alwaysShowDebug) {
            OnDrawGizmosSelected();
        }
    }
    void OnDrawGizmosSelected() {
        Camera camera = Camera.current;
        Matrix4x4 origMatrix = Gizmos.matrix;
        Gizmos.matrix = camera.projectionMatrix * camera.transform.localToWorldMatrix;
        Vector3 pos = new Vector3(5,5,0);
        float maxy = 0;
        foreach (var pathPair in knownPaths) {
            var msg = pathPair.Value;
            string str = " " + msg.path + "  \n\n";
            int idx = 0;
            foreach (var arg in msg.arguments) {
                str += " [Arg" + idx + msg.typeTag.Substring(idx, 1) + "] = " + arg + "\n";
            }
            float bgAlpha = 0.3f;
            float r1 = 0.8f, r2 = 1.0f, b1=0.7f, b2 = 0.3f;
            if (msg.arguments.Length >= 1 && msg.arguments[0].GetType() == typeof(float)) {
                bgAlpha += (float)(msg.arguments[0]) * 0.2f;
            } else if (msg.arguments.Length >= 1 && msg.arguments[0].GetType() == typeof(int)) {
                bgAlpha += (int)(msg.arguments[0]) > 0 ? 0.6f : 0.0f;
                b1 = 0.8f;
                b2 = 1.0f;
                r1=0.7f;
                r2 = 0.3f;
            } else {
                b1 = r1;
                b2 = r2;
            }
            // Rect fullRect = new Rect(pos.x, pos.y, 100, 100);
            float oldy = pos.y;
            DrawDebugTextDelegate(ref pos, new Color(r1,0.6f,b1,bgAlpha), new Color(r2,0.3f,b2,0.7f), new Color(1.0f,1.0f,1.0f,1.0f), str);
            maxy = pos.y > maxy ? pos.y : maxy;
            if (pos.x == 0) {
                pos.y = maxy + 5;
            } else {
                pos.y = oldy;
            }
            pos.x += 5;
            // pos = new Vector3(pos.x + 105, pos.y, pos.z);
        }
        Gizmos.matrix = origMatrix;
    }
}
