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
using System.Linq;

public class LyumaAv3Osc : MonoBehaviour {
    public delegate Rect GetEditorViewport();
    public static GetEditorViewport GetEditorViewportDelegate;

    public delegate void DrawDebugRect(Rect pos, Color col, Color outlineCol);
    public static DrawDebugRect DrawDebugRectDelegate;
    public delegate void DrawDebugText(Rect contentRect, Color backgroundCol, Color outlineCol, Color textCol, string str, TextAnchor alignment);
    public static DrawDebugText DrawDebugTextDelegate;

    private LyumaAv3Emulator emulator;
    A3ESimpleOSC receiver;
    [Header("OSC Connection")]
    public bool openSocket = false;
    public bool disableOSC = false;
    public bool resendAllParameters = false;
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
    
    [Header("Gizmo settings")]
    public bool alwaysShowOSCGizmos = true;
    public bool clearGizmos = false;
    public Color GizmoFilledColor = new Color(1.0f,0.0f,1.0f,0.1f);
    public Color GizmoBackgroundColor = new Color(0.75f,0.0f,0.6f,0.05f);
    public Color GizmoOutlineColor = new Color(0.9f,0.7f,0.8f,0.5f);
    public Color GizmoTextColor = new Color(1.0f,0.8f,1.0f,0.9f); //new Color(0.2f,1.0f,0.5f,1.0f);
    protected Color GizmoBoundsColor = new Color(0.0f,0.0f,0.0f,0.6f); //new Color(0.2f,1.0f,0.5f,1.0f);
    public bool GizmoShowSenderIP;
    [Header("Debug options")]
    public bool sendLoopbackOSCReplies;
    public bool debugPrintReceivedMessages;

    

    public Dictionary<string, A3ESimpleOSC.OSCMessage> knownPaths = new Dictionary<string, A3ESimpleOSC.OSCMessage>();
    Dictionary<string, Vector2> minMaxByPath = new Dictionary<string, Vector2>();
    private List<A3ESimpleOSC.OSCMessage> messages = new List<A3ESimpleOSC.OSCMessage>();
    Dictionary<string, A3ESimpleOSC.OSCMessage> lastSent = new Dictionary<string, A3ESimpleOSC.OSCMessage>();

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
        if (clearGizmos) {
            clearGizmos = false;
            knownPaths.Clear();
            minMaxByPath.Clear();
        }
        if (openSocket && receiver != null && oldPort != udpPort) {
            receiver.StopClient();
            receiver = null;
        }
        if ((!disableOSC && openSocket) && receiver == null) {
            localIp = "";
            localPort = -1;
            oldPort = udpPort;
            receiver = new A3ESimpleOSC();
            bool success = false;
            try {
                var localEndpoint = receiver.OpenClient(udpPort);
                localIp = localEndpoint.Address.ToString();
                localPort = localEndpoint.Port;
                success = localEndpoint.Port == udpPort || (udpPort == 0 && localEndpoint.Port > 0);
            } catch (System.Exception  e) {
                localIp = e.Message;
                Debug.LogException(e);
            }
            if (!success) {
                Debug.LogError("Failed to bind socket to OSC");
                openSocket = false;
            } else {
                resendAllParameters = true;
            }
        }
        if ((disableOSC||!openSocket) && receiver != null) {
            receiver.StopClient();
            receiver = null;
        }
        if (resendAllParameters) {
            resendAllParameters = false;
            lastSent.Clear();
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
                if (!minMaxByPath.ContainsKey(msg.path)) {
                    minMaxByPath[msg.path] = new Vector2(0,0);
                }
            }
            if (forwardToAllAvatarsInScene) {
                if (emulator != null && emulator.runtimes != null) {
                    foreach (var instRuntime in emulator.runtimes) {
                        instRuntime.HandleOSCMessages(messages);
                    }
                }
            } else if (runtime != null) {
                runtime.HandleOSCMessages(messages);
            }
        }
        if (runtime != null && receiver != null) {
            messages.Clear();
            runtime.GetOSCDataInto(messages);
            if (messages.Count > 0) {
                receiver.SetUnconnectedEndpoint(new System.Net.IPEndPoint(System.Net.IPAddress.Parse(outgoingUdpIp), outgoingUdpPort));
                // receiver.SendOSCBundle(messages, new A3ESimpleOSC.TimeTag { secs=-1, nsecs=-1 }, oscBuffer);
                foreach (var message in messages) {
                    if (lastSent.ContainsKey(message.path) && Enumerable.SequenceEqual(message.arguments, lastSent[message.path].arguments)) {
                        continue;
                    }
                    lastSent[message.path] = message;
                    if (debugPrintReceivedMessages) {
                        Debug.Log("Sending " + message + " to " + outgoingUdpIp + ":" + outgoingUdpPort);
                    }
                    receiver.SendOSCPacket(message, oscBuffer);
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
        if (alwaysShowOSCGizmos) {
            ActuallyDrawGizmos();
        }
    }
    void RenderBoxType(Rect r, Vector2 v2, Vector2 minMaxHoriz, Vector2 minMaxVert) {
        float xrange = Mathf.Max(0.001f, minMaxHoriz.y - minMaxHoriz.x);
        float yrange = Mathf.Max(0.001f, minMaxVert.y - minMaxVert.x);
        Rect r2 = (Rect)r;
        Rect r3 = (Rect)r;
        float f = v2.x;
        float g = v2.y;
        float widOffset = r.width * Mathf.Clamp01((v2.x - minMaxHoriz.x) / xrange);
        float heiOffset = r.height * Mathf.Clamp01(1.0f - (v2.y - minMaxVert.x) / yrange);
        r.y += heiOffset;
        r.height -= heiOffset;
        DrawDebugRectDelegate(r, GizmoFilledColor, GizmoFilledColor);
        r2.width = widOffset;
        DrawDebugRectDelegate(r2, GizmoFilledColor, GizmoFilledColor);
        DrawDebugTextDelegate(r3, new Color(0,0,0,0), new Color(0,0,0,0), GizmoBoundsColor, "\n\n\n\n"+minMaxHoriz.x.ToString("F2"), TextAnchor.MiddleLeft);
        DrawDebugTextDelegate(r3, new Color(0,0,0,0), new Color(0,0,0,0), GizmoBoundsColor, minMaxHoriz.y.ToString("F2")+"\n\n\n\n", TextAnchor.MiddleRight);
        DrawDebugTextDelegate(r3, new Color(0,0,0,0), new Color(0,0,0,0), GizmoBoundsColor, minMaxVert.x.ToString("F2")+"\n", TextAnchor.LowerCenter);
        DrawDebugTextDelegate(r3, new Color(0,0,0,0), new Color(0,0,0,0), GizmoBoundsColor, "\n"+minMaxVert.y.ToString("F2"), TextAnchor.UpperCenter);
    }
    void RenderBoxType(Rect r, float f2, Vector2 minMax) {
        float xrange = Mathf.Max(0.001f, minMax.y - minMax.x);
        float widOffset = r.width * Mathf.Clamp01(1.0f - (f2 - minMax.x) / xrange);
        Rect r3 = (Rect)r;
        r.width -= widOffset;
        DrawDebugRectDelegate(r, GizmoFilledColor, GizmoFilledColor);
        DrawDebugTextDelegate(r3, new Color(0,0,0,0), new Color(0,0,0,0), GizmoBoundsColor, "\n\n\n\n"+minMax.x.ToString("F2"), TextAnchor.MiddleLeft);
        DrawDebugTextDelegate(r3, new Color(0,0,0,0), new Color(0,0,0,0), GizmoBoundsColor, minMax.y.ToString("F2")+"\n\n\n\n", TextAnchor.MiddleRight);
    }
    void RenderBoxType(Rect r, int i2, Vector2Int minMax) {
        float heiOffset = r.height * Mathf.Clamp01(1.0f - (i2 - minMax.x) / Mathf.Max(1.0f, minMax.y - minMax.x));
        Rect r3 = (Rect)r;
        r.y += heiOffset;
        r.height -= heiOffset;
        DrawDebugRectDelegate(r, GizmoFilledColor, GizmoFilledColor);
        DrawDebugTextDelegate(r3, new Color(0,0,0,0), new Color(0,0,0,0), GizmoBoundsColor, minMax.x.ToString()+"\n", TextAnchor.LowerCenter);
        DrawDebugTextDelegate(r3, new Color(0,0,0,0), new Color(0,0,0,0), GizmoBoundsColor, "\n"+minMax.y.ToString(), TextAnchor.UpperCenter);
    }
    void RenderBoxType(Rect r, bool b2) {
        if (b2) {
            DrawDebugRectDelegate(r, GizmoFilledColor, GizmoFilledColor);
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
        if (!alwaysShowOSCGizmos) {
            ActuallyDrawGizmos();
        }
    }
    void ActuallyDrawGizmos() {
        Camera camera = Camera.current;
        Matrix4x4 origMatrix = Gizmos.matrix;
        Gizmos.matrix = camera.projectionMatrix * camera.transform.localToWorldMatrix;
        Rect viewportSize = GetEditorViewportDelegate();
        Rect pos = new Rect(5 + viewportSize.x,5 + viewportSize.y,190,190);
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
                pos.x = 5 + viewportSize.x;
                pos.y += pos.height + 10;
            }
        }
        if (pos.y + pos.height > viewportSize.height) {
            pos = new Rect(5 + viewportSize.x,5 + viewportSize.y,190,pos.height * viewportSize.height / (pos.y + pos.height + 100));
        } else {
            pos = new Rect(5 + viewportSize.x,5 + viewportSize.y,190,190);
        }
        foreach (var pathPair in knownPaths) {
            bool isVec2 = false;
            Vector2 vecVal = new Vector2();
            if (usedPartners.Contains(pathPair.Key)) {
                // Already got output
                continue;
            }
            Vector2 minmaxVert = minMaxByPath[pathPair.Key];
            Vector2 minmaxHoriz = new Vector2();
            System.Text.StringBuilder str = new System.Text.StringBuilder();
            foreach (var replacePair in replacePairs) {
                if (pathPair.Key.EndsWith(replacePair.Key)) {
                    if (pathPair.Value.arguments.Length >= 1 && pathPair.Value.arguments[0].GetType() == typeof(float)) {
                        string key = pathPair.Key.Substring(0, pathPair.Key.Length - replacePair.Key.Length) + replacePair.Value;
                        A3ESimpleOSC.OSCMessage msg;
                        if (knownPaths.TryGetValue(key, out msg) && msg.arguments.Length >= 1 && msg.arguments[0].GetType() == typeof(float)) {
                            msg.DebugInto(str, GizmoShowSenderIP);
                            vecVal.y = (float)pathPair.Value.arguments[0];
                            minmaxVert = new Vector2(Mathf.Min(minmaxVert.x, vecVal.y), Mathf.Max(minmaxVert.y, vecVal.y));
                            minMaxByPath[pathPair.Key] = minmaxVert;

                            vecVal.x = (float)msg.arguments[0];
                            minmaxHoriz = minMaxByPath[key];
                            minmaxHoriz = new Vector2(Mathf.Min(minmaxHoriz.x, vecVal.x), Mathf.Max(minmaxHoriz.y, vecVal.x));
                            minMaxByPath[key] = minmaxHoriz;
                            isVec2 = true;
                            break;
                        }
                    }
                }
            }
            pathPair.Value.DebugInto(str, GizmoShowSenderIP);
            Rect subPos = new Rect(pos);

            Color bgc = (Color)(GizmoBackgroundColor);
            if (isVec2) {
                RenderBoxType(subPos, vecVal, minmaxHoriz, minmaxVert);
            } else if (pathPair.Value.arguments != null && pathPair.Value.arguments.Length >= 1) {
                A3ESimpleOSC.OSCMessage msg = pathPair.Value;
                switch (msg.arguments[0]) {
                    case float f:
                        minmaxVert = new Vector2(Mathf.Min(minmaxVert.x, f), Mathf.Max(minmaxVert.y, f));
                        minMaxByPath[pathPair.Key] = minmaxVert;
                        RenderBoxType(subPos, f, minmaxVert);
                        break;
                    case int i:
                        minmaxVert = new Vector2(Mathf.Min(minmaxVert.x, (float)i), Mathf.Max(minmaxVert.y, (float)i));
                        minMaxByPath[pathPair.Key] = minmaxVert;
                        if (i != 0) {
                            DrawDebugRectDelegate(subPos, GizmoFilledColor, GizmoFilledColor);
                        }
                        RenderBoxType(subPos, i, new Vector2Int(0,255)); // Hardcode bounds. new Vector2Int((int)minmaxVert.x, (int)minmaxVert.y));
                        break;
                    case bool b:
                        if (b) {
                            DrawDebugRectDelegate(subPos, GizmoFilledColor, GizmoFilledColor);
                        }
                        RenderBoxType(subPos, b ? 1 : 0, new Vector2Int(0,1));
                        // RenderBoxType(subPos, b);
                        break;
                }
            }
            // Color c0 = new Color(OUTLINE_COLOR);
            DrawDebugRectDelegate(pos, bgc, GizmoOutlineColor);
            DrawDebugTextDelegate(pos, new Color(0.0f,0.0f,1.0f,0.02f), GizmoOutlineColor, GizmoTextColor, str.ToString().Replace(";", "\n").Replace(" @", "\n@" )
                .Replace("(Single)", "(Float)").Replace("/avatar/parameters/", ""), TextAnchor.MiddleCenter);
            pos.x += pos.width + 10;
            if (pos.x + pos.width > viewportSize.width) {
                pos.x = 5 + viewportSize.x;
                pos.y += pos.height + 10;
            }
            // pos = new Vector3(pos.x + 105, pos.y, pos.z);
        }
        Gizmos.matrix = origMatrix;
    }
}
