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
    public delegate Rect GetEditorViewport();
    public static GetEditorViewport GetEditorViewportDelegate;

    public delegate void DrawDebugRect(Rect pos, Color col, Color outlineCol);
    public static DrawDebugRect DrawDebugRectDelegate;
    public delegate void DrawDebugText(Rect contentRect, Color backgroundCol, Color outlineCol, Color textCol, string str);
    public static DrawDebugText DrawDebugTextDelegate;

    private LyumaAv3Emulator emulator;
    A3ESimpleOSC receiver;
    [Header("OSC Connection")]
    public bool openSocket = false;
    byte[] oscBuffer = new byte[65535];
    public int udpPort = 9000;
    public string outgoingUdpIp = "127.0.0.1";
    public int outgoingUdpPort = 9001;
    [SerializeField] private string commandLine = "";
    private int oldPort = 9000;
    [Header("OSC Status")]
    [SerializeField] private int localPort;
    [SerializeField] private string localIp;
    [SerializeField] private int numberOfOSCMessages;
    [Header("Target Avatar")]
    public VRC.SDK3.Avatars.Components.VRCAvatarDescriptor avatarDescriptor;
    public bool forwardToAllAvatarsInScene;
    [Header("Debug options")]
    public bool showOSCWithSceneGizmos = true;
    public bool sendLoopbackOSCReplies;
    public bool debugPrintReceivedMessages;
    public Dictionary<string, A3ESimpleOSC.OSCMessage> knownPaths = new Dictionary<string, A3ESimpleOSC.OSCMessage>();
    private List<A3ESimpleOSC.OSCMessage> messages = new List<A3ESimpleOSC.OSCMessage>();

    public void Start() {
        LyumaAv3Emulator[] emulators = FindObjectsOfType<LyumaAv3Emulator>();
        if (emulators == null || emulators.Length == 0) {
            return;
        }
        emulator = emulators[0];
        if (emulator != null && emulator.runtimes != null) {
            if (emulator.runtimes.Count > 0) {
                avatarDescriptor = emulator.runtimes[0].GetComponent<VRC.SDK3.Avatars.Components.VRCAvatarDescriptor>();
            }
        }
    }
    public void Update() {
        LyumaAv3Runtime runtime = avatarDescriptor != null ?avatarDescriptor.GetComponent<LyumaAv3Runtime>() : null;
        commandLine = "--osc=" + udpPort + ":" + outgoingUdpIp + ":" + outgoingUdpPort;
        if (openSocket && receiver != null && oldPort != udpPort) {
            receiver.StopClient();
        }
        if (openSocket && receiver == null) {
            oldPort = udpPort;
            receiver = new A3ESimpleOSC();
            var localEndpoint = receiver.OpenClient(udpPort);
            localIp = localEndpoint.Address.ToString();
            localPort = localEndpoint.Port;
            if (localEndpoint.Port != udpPort && udpPort != 0) {
                openSocket = false;
            }
        }
        if (!openSocket && receiver != null) {
            receiver.StopClient();
            receiver = null;
        }
        messages.Clear();
        if (receiver != null) {
            receiver.GetIncomingOSC(messages);
            if (sendLoopbackOSCReplies && messages.Count > 0) {
                receiver.SetUnconnectedEndpoint(messages[0].sender);
                var tt = new A3ESimpleOSC.TimeTag();
                for (int i = 0; i < messages.Count; i++) {
                    if (messages[i].bundleId != 0) {
                        tt = messages[i].time;
                        break;
                    }
                }
                receiver.SendOSCBundle(messages, tt);
            }
            foreach (var msg in messages) {
                numberOfOSCMessages += 1;
                if (debugPrintReceivedMessages) {
                    Debug.Log("Got OSC message: " + msg.ToString());
                }
                knownPaths[msg.path] = msg;
                if (forwardToAllAvatarsInScene) {
                    if (emulator == null || emulator.runtimes == null) {
                        continue;
                    }
                    foreach (var instRuntime in emulator.runtimes) {
                        instRuntime.HandleOSCMessage(msg);
                    }
                } else if (runtime != null) {
                    runtime.HandleOSCMessage(msg);
                }
            }
        }
        if (runtime != null && receiver != null) {
            messages.Clear();
            runtime.GetOSCDataInto(messages);
            if (messages.Count > 0) {
                receiver.SetUnconnectedEndpoint(new System.Net.IPEndPoint(System.Net.IPAddress.Parse(outgoingUdpIp), outgoingUdpPort));
                receiver.SendOSCBundle(messages, new A3ESimpleOSC.TimeTag { secs=-1, nsecs=-1 }, oscBuffer);
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
        if (showOSCWithSceneGizmos) {
            OnDrawGizmosSelected();
        }
    }
    static Color BACKGROUND_COLOR = new Color(0.8f,0.6f,0.7f,0.3f);
    static Color OUTLINE_COLOR = new Color(0.8f,0.3f,1.0f,0.7f);
    static Color TEXT_COLOR = Color.black; //new Color(0.2f,1.0f,0.5f,1.0f);
    static float BG_ALPHA = 0.5f;
    void RenderBoxType(Rect r, Vector2 v2) {
        Rect r2 = (Rect)r;
        float f = v2.x;
        float g = v2.y;
        float bgAlpha = BG_ALPHA;
        float widOffset = r.width * Mathf.Clamp01(v2.x * 0.5f + 0.5f);
        float heiOffset = r.height * Mathf.Clamp01(0.5f - v2.y * 0.5f);
        r.y += heiOffset;
        r.height -= heiOffset;
        DrawDebugRectDelegate(r, new Color(1.0f,1.0f,1.0f,bgAlpha), new Color(1.0f,1.0f,1.0f,bgAlpha));
        r2.width = widOffset;
        DrawDebugRectDelegate(r2, new Color(1.0f,1.0f,1.0f,bgAlpha), new Color(1.0f,1.0f,1.0f,bgAlpha));
    }
    void RenderBoxType(Rect r, float f2) {
        float bgAlpha = BG_ALPHA;
        float widOffset = r.width * Mathf.Clamp01(0.5f - f2 * 0.5f);
        r.width -= widOffset;
        DrawDebugRectDelegate(r, new Color(1.0f,1.0f,1.0f,bgAlpha), new Color(1.0f,1.0f,1.0f,bgAlpha));
    }
    void RenderBoxType(Rect r, int i2) {
        float bgAlpha = BG_ALPHA * (i2 == 0 ? 0.3f : 1.0f);
        float heiOffset = r.height * Mathf.Clamp01((255 - i2) / 255.0f);
        r.y += heiOffset;
        r.height -= heiOffset;
        DrawDebugRectDelegate(r, new Color(1.0f,1.0f,1.0f,bgAlpha), new Color(1.0f,1.0f,1.0f,bgAlpha));
    }
    void RenderBoxType(Rect r, bool b2) {
        float bgAlpha = BG_ALPHA;
        if (b2) {
            DrawDebugRectDelegate(r, new Color(1.0f,1.0f,1.0f,bgAlpha), new Color(1.0f,1.0f,1.0f,bgAlpha));
        }
    }

    
    HashSet<string> usedPartners = new HashSet<string>();
    Dictionary<string, string> replacePairs = new Dictionary<string, string> {
        {"Vertical", "Horizontal"},
        {"Z", "X"},
        {"Y", "X"},
        {"z", "x"},
        {"y", "x"},
    };
    void OnDrawGizmosSelected() {
        Camera camera = Camera.current;
        Matrix4x4 origMatrix = Gizmos.matrix;
        Gizmos.matrix = camera.projectionMatrix * camera.transform.localToWorldMatrix;
        Rect pos = new Rect(5,5,190,190);
        Rect viewportSize = GetEditorViewportDelegate();
        usedPartners.Clear();
        // float maxy = 0;
        int numBoxes = 0;
        foreach (var pathPair in knownPaths) {
            if (usedPartners.Contains(pathPair.Key)) {
                // Already got output
                continue;
            }
            A3ESimpleOSC.OSCMessage msg;
            foreach (var replacePair in replacePairs) {
                if (pathPair.Key.EndsWith(replacePair.Key)) {
                    if (pathPair.Value.arguments.Length >= 1 && pathPair.Value.arguments[0].GetType() == typeof(float)) {
                        string key = pathPair.Key.Substring(0, pathPair.Key.Length - replacePair.Key.Length) + replacePair.Value;
                        if (knownPaths.TryGetValue(key, out msg) && msg.arguments.Length >= 1 && msg.arguments[0].GetType() == typeof(float)) {
                            usedPartners.Add(key);
                            break;
                        }
                    }
                }
            }
            numBoxes++;
            pos.x += pos.width + 10;
            if (pos.x + pos.width > viewportSize.width) {
                pos.x = 5;
                pos.y += pos.height + 10;
            }
        }
        if (pos.y + pos.height > viewportSize.height) {
            pos = new Rect(5,5,190,pos.height * viewportSize.height / (pos.y + pos.height + 100));
        } else {
            pos = new Rect(5,5,190,190);
        }
        foreach (var pathPair in knownPaths) {
            A3ESimpleOSC.OSCMessage msg;
            bool isVec2 = false;
            Vector2 vecVal = new Vector2();
            if (usedPartners.Contains(pathPair.Key)) {
                // Already got output
                continue;
            }
            msg = pathPair.Value;
            System.Text.StringBuilder str = new System.Text.StringBuilder();
            msg.DebugInto(str, false);
            foreach (var replacePair in replacePairs) {
                if (pathPair.Key.EndsWith(replacePair.Key)) {
                    if (pathPair.Value.arguments.Length >= 1 && pathPair.Value.arguments[0].GetType() == typeof(float)) {
                        string key = pathPair.Key.Substring(0, pathPair.Key.Length - replacePair.Key.Length) + replacePair.Value;
                        if (knownPaths.TryGetValue(key, out msg) && msg.arguments.Length >= 1 && msg.arguments[0].GetType() == typeof(float)) {
                            msg.DebugInto(str, false);
                            vecVal.x = (float)msg.arguments[0];
                            vecVal.y = (float)pathPair.Value.arguments[0];
                            isVec2 = true;
                            break;
                        }
                    }
                }
            }
            Rect subPos = new Rect(pos);

            Color bgc = (Color)(BACKGROUND_COLOR);
            if (isVec2) {
                RenderBoxType(subPos, vecVal);
            } else if (msg.arguments != null && msg.arguments.Length >= 1) {
                switch (msg.arguments[0]) {
                    case float f:
                        RenderBoxType(subPos, f);
                        break;
                    case int i:
                        bgc.a *= (i == 0 ? 0.3f : 1.0f);
                        RenderBoxType(subPos, i);
                        break;
                    case bool b:
                        bgc.a *= (b ? 1.0f : 0.3f);
                        // RenderBoxType(subPos, b);
                        break;
                }
            }
            // Color c0 = new Color(OUTLINE_COLOR);
            DrawDebugRectDelegate(pos, bgc, OUTLINE_COLOR);
            DrawDebugTextDelegate(pos, new Color(0.0f,0.0f,1.0f,0.02f), OUTLINE_COLOR, TEXT_COLOR, str.ToString().Replace("; ", "\n"));
            pos.x += pos.width + 10;
            if (pos.x + pos.width > viewportSize.width) {
                pos.x = 5;
                pos.y += pos.height + 10;
            }
            // pos = new Vector3(pos.x + 105, pos.y, pos.z);
        }
        Gizmos.matrix = origMatrix;
    }
}
