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
using System.Net;
using System.Net.Sockets;

namespace Lyuma.Av3Emulator.Runtime
{
	public class LyumaAv3Osc : MonoBehaviour {
		public delegate Rect GetEditorViewport();
		public static GetEditorViewport GetEditorViewportDelegate;

		public delegate void DrawDebugRect(Rect pos, Color col, Color outlineCol);
		public static DrawDebugRect DrawDebugRectDelegate;
		public delegate void DrawDebugText(Rect contentRect, Color backgroundCol, Color outlineCol, Color textCol, string str, TextAnchor alignment);
		public static DrawDebugText DrawDebugTextDelegate;

		private LyumaAv3Emulator emulator;
		A3ESimpleOSC receiver;
		[HideInInspector] public bool openSocket = false;
		[HideInInspector] public bool disableOSC = false;
		byte[] oscBuffer = new byte[65535];  // 65535 being the max length of a UDP package
		private const int PreferredOscPort = 9000;
		public int udpPort = 0;
		public string outgoingUdpIp = "127.0.0.1";
		public int outgoingUdpPort = 9001;
		[SerializeField, HideInInspector] private string commandLine = "";
		private int oldPort;
		private float retryOscQueryAt;
		private Av3OscQueryServer oscQuery;
		private Av3OscQueryDiscovery oscQueryDiscovery;
		private Av3OscQueryReceivers oscQueryReceivers;
		[SerializeField, HideInInspector] private int localQueryPort;
		[SerializeField, HideInInspector] private int localPort;
		[SerializeField, HideInInspector] private string localIp;
		[SerializeField, HideInInspector] private int numberOfOSCMessages;
		[Header("Target Avatar")]
		public VRC.SDK3.Avatars.Components.VRCAvatarDescriptor avatarDescriptor;
		public bool forwardToAllAvatarsInScene;
		private LyumaAv3Runtime lastOscTarget;
		
		[Header("Debug Options")]
		public bool resendAllParameters = false;
		public bool sendLoopbackOSCReplies;
		public bool debugPrintReceivedMessages;

		[Header("Gizmo Options")]
		public bool alwaysShowOSCGizmos = true;
		public bool clearGizmos = false;
		public Color GizmoFilledColor = new Color(1.0f,0.0f,1.0f,0.1f);
		public Color GizmoBackgroundColor = new Color(0.75f,0.0f,0.6f,0.05f);
		public Color GizmoOutlineColor = new Color(0.9f,0.7f,0.8f,0.5f);
		public Color GizmoTextColor = new Color(1.0f,0.8f,1.0f,0.9f); //new Color(0.2f,1.0f,0.5f,1.0f);
		protected Color GizmoBoundsColor = new Color(0.0f,0.0f,0.0f,0.6f); //new Color(0.2f,1.0f,0.5f,1.0f);
		public bool GizmoShowSenderIP;

		

		public Dictionary<string, A3ESimpleOSC.OSCMessage> knownPaths = new Dictionary<string, A3ESimpleOSC.OSCMessage>();
		Dictionary<string, Vector2> minMaxByPath = new Dictionary<string, Vector2>();
		private List<A3ESimpleOSC.OSCMessage> messages = new List<A3ESimpleOSC.OSCMessage>();
		Dictionary<string, A3ESimpleOSC.OSCMessage> lastSent = new Dictionary<string, A3ESimpleOSC.OSCMessage>();

		private IPEndPoint lastManualEndpoint;

		public void Start() {
			LyumaAv3Emulator[] emulators = FindObjectsOfType<LyumaAv3Emulator>();
			if (emulators == null || emulators.Length == 0) {
				return;
			}
			emulator = emulators[0];
			if (avatarDescriptor == null && emulator.runtimes != null && emulator.runtimes.Count > 0) {
				avatarDescriptor = emulator.runtimes[0].avadesc;
			}
		}
		public void Update() {
			var animator = avatarDescriptor != null ? avatarDescriptor.GetComponent<Animator>() : null;
			var runtime = animator != null ? LyumaAv3Runtime.GetRuntimeForAnimator(animator) : null;
			if (!ReferenceEquals(lastOscTarget, runtime)) {
				lastOscTarget = runtime;
				ClearSentValues();
			}
			commandLine = "--osc=" + udpPort + ":" + outgoingUdpIp + ":" + outgoingUdpPort;
			if (clearGizmos) {
				clearGizmos = false;
				knownPaths.Clear();
				minMaxByPath.Clear();
			}
			if (openSocket && receiver != null && oldPort != udpPort) {
				CloseReceiver();
			}
			if ((!disableOSC && openSocket) && receiver == null) {
				localIp = "";
				localPort = -1;
				oldPort = udpPort;
				var socket = new A3ESimpleOSC();
				bool success = false;
				try {
					var localEndpoint = udpPort == 0
						? socket.OpenClientWithFallback(PreferredOscPort)
						: socket.OpenClient(udpPort);
					receiver = socket;
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
					CloseReceiver();
				} else {
					resendAllParameters = true;
				}
			}
			if ((disableOSC||!openSocket) && receiver != null) {
				CloseReceiver();
			}
			UpdateOscQuery();
			if (resendAllParameters) {
				resendAllParameters = false;
				ClearSentValues();
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
				oscQuery?.Publish(runtime, messages);
				if (messages.Count > 0) {
					var manualEndpoint = IPAddress.TryParse(outgoingUdpIp, out var ip) && outgoingUdpPort > 0 && outgoingUdpPort <= 65535
						? new IPEndPoint(ip, outgoingUdpPort) : null;
					if (!Equals(manualEndpoint, lastManualEndpoint)) {
						lastManualEndpoint = manualEndpoint;
						ClearSentValues();
					}
					if (manualEndpoint != null)
						SendOscChanges(manualEndpoint, lastSent);
					if (oscQueryReceivers != null)
						foreach (var route in oscQueryReceivers.Routes)
							if (!route.Endpoint.Equals(manualEndpoint))
								SendOscChanges(route.Endpoint, route.Sent, route);
				}
			} else {
				oscQuery?.Publish(null, messages);
			}
		}

		private void SendOscChanges(IPEndPoint endpoint, Dictionary<string, A3ESimpleOSC.OSCMessage> sent,
			Av3OscQueryReceivers.Route route = null)
		{
			receiver.SetUnconnectedEndpoint(endpoint);
			foreach (var message in messages)
			{
				if (route != null && !route.Accepts(message.path))
					continue;
				if (sent.TryGetValue(message.path, out var previous) && Enumerable.SequenceEqual(message.arguments, previous.arguments))
					continue;
				sent[message.path] = message;
				if (debugPrintReceivedMessages)
					Debug.Log($"Sending {message} to {endpoint}");
				receiver.SendOSCPacket(message, oscBuffer);
			}
		}

		private void ClearSentValues()
		{
			lastSent.Clear();
			if (oscQueryReceivers != null)
				foreach (var route in oscQueryReceivers.Routes)
					route.Sent.Clear();
		}

		private void UpdateOscQuery()
		{
			if (receiver == null)
				return;
			if (oscQueryReceivers != null)
			{
				oscQueryReceivers.Refresh();
				return;
			}
			if (Time.realtimeSinceStartup < retryOscQueryAt)
				return;

			try
			{
				if (oscQuery == null)
				{
					oscQuery = new Av3OscQueryServer(localPort);
					localQueryPort = oscQuery.Port;
				}
				oscQueryDiscovery = new Av3OscQueryDiscovery(oscQuery.ServiceName, localQueryPort, localPort);
				oscQueryReceivers = new Av3OscQueryReceivers(oscQueryDiscovery.Services, localPort);
				oscQueryReceivers.Refresh();
			}
			catch (SocketException error)
			{
				if (retryOscQueryAt == 0)
					Debug.LogWarning($"AV3 could not start OSCQuery. It will retry while OSC is open. {error.Message}", this);
				retryOscQueryAt = Time.realtimeSinceStartup + 1f;
			}
		}

		private void OnDisable() => CloseReceiver();
		public void OnDestroy() => CloseReceiver();

		private void CloseReceiver()
		{
			oscQueryReceivers?.Dispose();
			oscQueryReceivers = null;
			oscQueryDiscovery?.Dispose();
			oscQueryDiscovery = null;
			oscQuery?.Dispose();
			oscQuery = null;
			localQueryPort = 0;
			retryOscQueryAt = 0;
			receiver?.StopClient();
			receiver = null;
			localPort = -1;
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
			if (GetEditorViewportDelegate == null) {
				return; // Avoid logspam. Gizmos will not work if we do not know the editor viewport.
			}
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
							RenderBoxType(subPos, i, new Vector2Int(Mathf.Min(0, (int)minmaxVert.x), Mathf.Max(255, (int)minmaxVert.y)));
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
}