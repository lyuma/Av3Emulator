/* A3ESimpleOSC for C#, version 0.1
Copyright (c) 2022 Lyuma <xn.lyuma@gmail.com>

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

#if UNITY_5_3_OR_NEWER
#define UNITY
#endif

using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Threading;

public class A3ESimpleOSC
{
    public enum Impulse {IMPULSE}
    public struct TimeTag {
        public int secs;
        public int nsecs;
        public override string ToString() {
            return "" + secs + ":" + nsecs;
        }
    #if UNITY
        public static implicit operator UnityEngine.Vector2Int(TimeTag tt) {
            return new UnityEngine.Vector2Int { x = tt.secs, y = tt.nsecs };
        }
        public static implicit operator TimeTag(UnityEngine.Vector2Int v2) {
            return new TimeTag { secs = v2.x, nsecs = v2.y };
        }
    #endif
    }
    public struct OSCColor {
        public byte r;
        public byte g;
        public byte b;
        public byte a;
        public override string ToString() {
            return "OSCColor<" + r + "," + g + "," + b + "," + a + ">";
        }
    #if UNITY
        public static implicit operator UnityEngine.Color(OSCColor c) {
            return (UnityEngine.Color)new UnityEngine.Color32 { r = c.r, g = c.g, b = c.b, a = c.a };
        }
        public static implicit operator OSCColor(UnityEngine.Color c) {
            UnityEngine.Color32 c32 = (UnityEngine.Color32)c;
            return new OSCColor { r = c32.r, g = c32.g, b = c32.b, a = c32.a };
        }
        public static implicit operator OSCColor(UnityEngine.Color32 c) {
            return new OSCColor { r = c.r, g = c.g, b = c.b, a = c.a };
        }
        public static implicit operator UnityEngine.Color32(OSCColor c) {
            return new UnityEngine.Color32 { r = c.r, g = c.g, b = c.b, a = c.a };
        }
    #endif
    }
    public struct OSCMessage {
        public IPEndPoint sender;
        public uint bundleId; // 0 if not in a bundle; positive integer if part of a bundle.
        public string path;
        public TimeTag time;
        public string typeTag;
        public object[] arguments;
        public override string ToString() {
            System.Text.StringBuilder ret = new System.Text.StringBuilder();
            ret.Append("<OSCMessage ");
            DebugInto(ret);
            ret.Append(">");
            return ret.ToString();
        }
        public void DebugInto(System.Text.StringBuilder ret, bool dispIPTime=true) {
            ret.Append(path);
            if (dispIPTime && sender != null) {
                ret.Append("; from ");
                ret.Append(sender.ToString());
            }
            if (dispIPTime && (time.secs != 0 || time.nsecs != 0)) {
                ret.Append(" @");
                ret.Append(time);
            }
            if (dispIPTime) {
                ret.Append("; type ");
                ret.Append(typeTag);
            }
            ret.Append(":\n");
            DebugObjectArrayInto(ret, "  ", arguments);
        }
    }

    public static bool DebugLoggingEnabled = true; // Set to false to handle bad data without logspam.
    static void CryWolf(string logMsg) {
        if (DebugLoggingEnabled) {
            #if UNITY
                UnityEngine.Debug.LogWarning(logMsg);
            #else
                System.Console.WriteLine(logMsg);
            #endif
        }
    }

    public static void DebugObjectArrayInto(System.Text.StringBuilder sb, string indent, object[] args) {
        int idx = 0;
        foreach (var arg in args) {
            sb.Append(indent);
            sb.Append("[Arg");
            sb.Append(idx);
            sb.Append("] = ");
            switch (arg) {
                case null:
                    sb.Append("null");
                    break;
                case object[] subArgs:
                    sb.Append("[\n");
                    DebugObjectArrayInto(sb, indent + "    ", subArgs);
                    sb.Append(indent + "]");
                    break;
                case byte[] subBytes:
                    sb.Append("new byte[] {");
                    bool first = true;
                    foreach (byte b in subBytes) {
                        if (!first) {
                            sb.Append(",");
                        }
                        first = false;
                        sb.Append((int)b);
                    }
                    sb.Append("}");
                    break;
                case float f:
                    sb.Append(f.ToString("F4"));
                    break;
                case int i:
                    sb.Append(i.ToString());
                    break;
                case bool b:
                    sb.Append(b.ToString());
                    break;
                default:
                    sb.Append("(");
                    sb.Append(arg.GetType().Name);
                    sb.Append(")");
                    sb.Append(arg);
                    break;
            }
            sb.Append("\n");
            idx++;
        }
    }


    /// Parsing / decoding functions:
    static object ParseType(char typeTag, byte[] data, ref int offset) {
        switch(typeTag) {
            case 'T':
                return true;
            case 'F':
                return false;
            case 'N':
                return null;
            case 'I':
                return Impulse.IMPULSE;
            case 'i':
                int iret = IPAddress.NetworkToHostOrder(BitConverter.ToInt32(data, offset));
                offset += 4;
                return iret;
            case 'f':
                byte[] tmp = new byte[4];
                Array.Copy(data, offset, tmp, 0, 4);
                if (BitConverter.IsLittleEndian) {
                    Array.Reverse(tmp);
                }
                float fret = BitConverter.ToSingle(tmp, 0);
                offset += 4;
                return fret;
            case 't':
                int secs = IPAddress.NetworkToHostOrder(BitConverter.ToInt32(data, offset));
                int nanosecs = IPAddress.NetworkToHostOrder(BitConverter.ToInt32(data, offset + 4));
                offset += 8;
                return new TimeTag { secs = secs, nsecs = nanosecs };
            case 's':
                int strend = offset;
                while (data[strend] != 0) {
                    strend++;
                }
                tmp = new byte[strend - offset];
                Array.Copy(data, offset, tmp, 0, strend - offset);
                offset = strend;
                offset = (offset + 4) & ~3;
                return System.Text.Encoding.UTF8.GetString(tmp);
            case 'b':
                int len = IPAddress.NetworkToHostOrder(BitConverter.ToInt32(data, offset));
                offset += 4;
                tmp = new byte[len];
                Array.Copy(data, offset, tmp, 0, len);
                offset += len;
                offset = (offset + 3) & ~3;
                return tmp;
            // Non-standard types:
            case 'r':
                byte r = data[offset++];
                byte g = data[offset++];
                byte b = data[offset++];
                byte a = data[offset++];
                return new OSCColor { r = r, g = g, b = b, a = a };
            case 'h':
                long lret = IPAddress.NetworkToHostOrder(BitConverter.ToInt64(data, offset));
                offset += 8;
                return lret;
            case 'd':
                byte[] dtmp = new byte[8];
                Array.Copy(data, offset, dtmp, 0, 8);
                if (BitConverter.IsLittleEndian) {
                    Array.Reverse(dtmp);
                }
                double dret = BitConverter.ToDouble(dtmp, 0);
                offset += 8;
                return dret;
            case 'c':
                uint cret = (uint)IPAddress.NetworkToHostOrder(BitConverter.ToInt32(data, offset));
                offset += 4;
                return cret;
            default:
                CryWolf("Unknown type tag " + typeTag + " offset " + offset);
                break;
        }
        return null;
    }

    static void SerializeTypeInto(byte[] data, ref int offset, object value, char typeTag) {
    	// Debug.Log("Serialize " + value.GetType() + " " + (value) + " as " + typeTag);
        byte[]tmp;
        switch (typeTag) {
            case 'T':
            case 'F':
            case 'N':
            case 'I':
                break;
            case 'i':
                Array.Copy(BitConverter.GetBytes(IPAddress.HostToNetworkOrder((int)value)), 0, data, offset, 4);
                offset += 4;
                break;
            case 'f':
                tmp = BitConverter.GetBytes((float)value);
                if (BitConverter.IsLittleEndian) {
                    Array.Reverse(tmp);
                }
                Array.Copy(tmp, 0, data, offset, 4);
                offset += 4;
                break;
            case 't':
                TimeTag v2;
                switch (value) {
                #if UNITY
                    case UnityEngine.Vector2Int i2:
                        v2 = (TimeTag)i2;
                        break;
                #endif
                    default:
                        v2 = (TimeTag)value;
                        break;
                }
                Array.Copy(BitConverter.GetBytes(IPAddress.HostToNetworkOrder((int)v2.secs)), 0, data, offset, 4);
                Array.Copy(BitConverter.GetBytes(IPAddress.HostToNetworkOrder((int)v2.nsecs)), 0, data, offset + 4, 4);
                offset += 8;
                break;
            case 's':
                tmp = System.Text.Encoding.UTF8.GetBytes((string)value);
                Array.Copy(tmp, 0, data, offset, tmp.Length);
                data[tmp.Length + offset] = 0;
                offset += tmp.Length;
                for (int endOffset = (offset + 4) & ~3; offset < endOffset; offset++) {
                    data[offset] = 0;
                }
                break;
            case 'b':
                tmp = (byte[])value;
                Array.Copy(BitConverter.GetBytes(IPAddress.HostToNetworkOrder((int)tmp.Length)), 0, data, offset, 4);
                offset += 4;
                Array.Copy(tmp, 0, data, offset, tmp.Length);
                data[tmp.Length + offset] = 0;
                offset += tmp.Length;
                for (int endOffset = (offset + 3) & ~3; offset < endOffset; offset++) {
                    data[offset] = 0;
                }
                break;
            // Non-standard types:
            case 'r':
                OSCColor col;
                switch (value) {
            #if UNITY
                    case UnityEngine.Color32 unic32:
                        col = (OSCColor)unic32;
                        break;
                    case UnityEngine.Color unic:
                        col = (OSCColor)unic;
                        break;
            #endif
                    default:
                        col = (OSCColor)value;
                        break;
                }
                data[offset++] = col.r;
                data[offset++] = col.g;
                data[offset++] = col.b;
                data[offset++] = col.a;
                break;
            case 'h':
                Array.Copy(BitConverter.GetBytes(IPAddress.HostToNetworkOrder((long)value)), 0, data, offset, 8);
                offset += 8;
                break;
            case 'd':
                tmp = BitConverter.GetBytes((double)value);
                if (BitConverter.IsLittleEndian) {
                    Array.Reverse(tmp);
                }
                Array.Copy(tmp, 0, data, offset, 8);
                offset += 8;
                break;
            case 'c':
                Array.Copy(BitConverter.GetBytes(IPAddress.HostToNetworkOrder((int)(uint)value)), 0, data, offset, 4);
                offset += 4;
                break;
            default:
                CryWolf("Unexpected type tag to serialize " + typeTag + " offset " + offset);
                break;
        }
    }

    public static void DecodeOSCInto(ConcurrentQueue<OSCMessage> outQueue, byte[] data, int offset, int length, IPEndPoint senderIp=null, uint bundleId=0, TimeTag bundleTimetag=new TimeTag(), uint bundleIdNested=0) {
        if (offset == 0 && length > 20 && data[offset] == '#' && data[offset + 1] == 'b' && data[offset + 2] == 'u' && data[offset + 3] == 'n' &&
            data[offset + 4] == 'd' && data[offset + 5] == 'l' && data[offset + 6] == 'e' && data[offset + 7] == 0) {
            int secs = IPAddress.NetworkToHostOrder(BitConverter.ToInt32(data, offset + 8));
            int nanosecs = IPAddress.NetworkToHostOrder(BitConverter.ToInt32(data, offset + 12));
            bundleTimetag = new TimeTag { secs = secs, nsecs = nanosecs };
            offset += 16;
            while (offset < length) {
                int msglen = IPAddress.NetworkToHostOrder(BitConverter.ToInt32(data, offset));
                offset += 4;
                DecodeOSCInto(outQueue, data, offset, msglen, senderIp, bundleId, bundleTimetag, bundleId);
                offset += msglen;
            }
            return;
        }
        OSCMessage msg = new OSCMessage();
        msg.time = bundleTimetag;
        msg.sender = senderIp;
        msg.bundleId = bundleIdNested;
        int strlen = 0;
        while (data[offset + strlen] != 0) {
            strlen++;
        }
        msg.path = System.Text.Encoding.UTF8.GetString(data, offset, strlen);
        offset += strlen;
        offset = (offset + 4) & ~3;
        while (data[offset] != ',') {
            offset++;
        }
        int typetags = offset;
        while (data[offset] != 0) {
            offset++;
        }
        msg.typeTag = System.Text.Encoding.ASCII.GetString(data, typetags, offset - typetags);
        offset = (offset + 4) & ~3;
        //msg.arguments = new object[msg.typeTag.Length];
        List<object> topLevelArguments = new List<object>();
        List<List<object>> nested = new List<List<object>>();
        nested.Add(topLevelArguments);
        for (int i = 1; i < msg.typeTag.Length; i++) {
            // Debug.Log("doing type tag " + msg.typeTag[i] + " offset: " + offset);
            object obj;
            switch (msg.typeTag[i]) {
            case '[':
                nested.Add(new List<object>());
                break;
            case ']':
                if (nested.Count > 1) {
                    obj = nested[nested.Count - 1].ToArray();
                    nested.RemoveAt(nested.Count - 1);
                    nested[nested.Count - 1].Add(obj);
                }
                break;
            default:
                obj = ParseType(msg.typeTag[i], data, ref offset);
                nested[nested.Count - 1].Add(obj);
                break;
            }
        }
        if (nested.Count != 1) {
            CryWolf("Invalid nested count (mismatched start and end array in OSC message): " + msg.typeTag);
        }
        msg.arguments = topLevelArguments.ToArray();
        outQueue.Enqueue(msg);
    }

    static void GenerateOSCTypeTagInto(System.Text.StringBuilder typeTag, object[] packet) {
        if (typeTag.Length == 0) {
            typeTag.Append(',');
        }
        foreach (object po in packet) {
            switch(po) {
                case object[] subArray:
                    typeTag.Append('[');
                    GenerateOSCTypeTagInto(typeTag, subArray);
                    typeTag.Append(']');
                    break;
                case float f:
                    typeTag.Append('f');
                    break;
                case int i:
                    typeTag.Append('i');
                    break;
            #if UNITY
                case UnityEngine.Color32 ui32:
                case UnityEngine.Color ui:
            #endif
                case OSCColor i:
                    typeTag.Append('r');
                    break;
                case true:
                    typeTag.Append('T');
                    break;
                case false:
                    typeTag.Append('F');
                    break;
                case null:
                    typeTag.Append('N');
                    break;
                case Impulse.IMPULSE:
                    typeTag.Append('I');
                    break;
                case string s:
                    typeTag.Append('s');
                    break;
                case byte[] b:
                    typeTag.Append('b');
                    break;
            #if UNITY
                case UnityEngine.Vector2Int i2:
            #endif
                case TimeTag tt:
                    typeTag.Append('t');
                    break;
                case double d:
                    typeTag.Append('d');
                    break;
                case long l:
                    typeTag.Append('h');
                    break;
                case uint c: // represent OSC char as uint. confusing???
                    typeTag.Append('c');
                    break;
                default:
                    CryWolf("Invalid type " + po.GetType() + " at " + typeTag);
                    break;
            }
        }
    }

    public static void EncodeOSCInto(byte[] data, ref int offset, OSCMessage msg, string type_tag_override="") {
        if (msg.typeTag == null || msg.typeTag.Length == 0) {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            GenerateOSCTypeTagInto(sb, msg.arguments);
            msg.typeTag = sb.ToString();
        }
        byte[] tmp = System.Text.Encoding.UTF8.GetBytes((string)msg.path);
        Array.Copy(tmp, 0, data, offset, tmp.Length);
        data[tmp.Length + offset] = 0;
        offset += tmp.Length;
        for (int endOffset = (offset + 4) & ~3; offset < endOffset; offset++) {
            data[offset] = 0;
        }
        tmp = System.Text.Encoding.UTF8.GetBytes((string)msg.typeTag);
        Array.Copy(tmp, 0, data, offset, tmp.Length);
        data[tmp.Length + offset] = 0;
        offset += tmp.Length;
        for (int endOffset = (offset + 4) & ~3; offset < endOffset; offset++) {
            data[offset] = 0;
        }
        List<object[]> nested = new List<object[]>();
        nested.Add(msg.arguments);
        List<int> nestedIdx = new List<int>();
        nestedIdx.Add(0);
        foreach (char ch in msg.typeTag) {
            switch (ch) {
            case ',':
                continue;
            case '[':
                object[] newArr = (object[])nested[nested.Count-1][nestedIdx[nestedIdx.Count-1]];
                nested.Add(newArr);
                nestedIdx[nestedIdx.Count-1] += 1;
                nestedIdx.Add(0);
                break;
            case ']':
                nested.RemoveAt(nested.Count-1);
                nestedIdx.RemoveAt(nestedIdx.Count-1);
                break;
            default:
                SerializeTypeInto(data, ref offset, nested[nested.Count-1][nestedIdx[nestedIdx.Count-1]], ch);
                nestedIdx[nestedIdx.Count-1] += 1;
                break;
            }
        }
    }

    public static void EncodeOSCBundleInto(byte[] data, ref int offset, List<OSCMessage> packets, TimeTag tt) {
        Array.Copy(new byte[]{(byte)'#',(byte)'b',(byte)'u',(byte)'n',(byte)'d',(byte)'l',(byte)'e',0}, 0, data, offset, 8);
        offset += 8;
        Array.Copy(BitConverter.GetBytes(IPAddress.HostToNetworkOrder((int)tt.secs)), 0, data, offset, 4);
        Array.Copy(BitConverter.GetBytes(IPAddress.HostToNetworkOrder((int)tt.nsecs)), 0, data, offset + 4, 4);
        offset += 8;
        foreach (var msg in packets) {
            int startOffset = offset;
            offset += 4;
            EncodeOSCInto(data, ref offset, msg);
            int endOffset = offset;
            Array.Copy(BitConverter.GetBytes(IPAddress.HostToNetworkOrder((int)(endOffset - startOffset - 4))), 0, data, startOffset, 4);
        }
    }

    public class UDPThread {
        UdpClient udpServer;
        public bool shutdown;
        public int udp_port;
        public uint bundleCounter; // just so callers know if it came from a bundle.
        public ConcurrentQueue<OSCMessage> receivedMessageQueue = new ConcurrentQueue<OSCMessage>();

        public IPEndPoint Open(int udp_port) {
            udpServer = new UdpClient(udp_port);
            return (IPEndPoint)udpServer.Client.LocalEndPoint;
        }
        public IPEndPoint Open(IPEndPoint local_udp_endpoint) {
            udpServer = new UdpClient(local_udp_endpoint);
            return (IPEndPoint)udpServer.Client.LocalEndPoint;
        }
        public void Connect(IPEndPoint endPoint) {
            udpServer.Connect(endPoint);
        }

        public void mythread(){
            while (!shutdown) {
                var incomingIP = new IPEndPoint(IPAddress.Any, 0);
                try {
                    var data = udpServer.Receive(ref incomingIP);
                    try {
                        if (bundleCounter == 0) {
                            bundleCounter += 1;
                        }
                        DecodeOSCInto(receivedMessageQueue, data, 0, data.Length, incomingIP, bundleCounter);
                        bundleCounter += 1;
                    } catch (Exception e) {
                        CryWolf(e.ToString());
                    }
                } catch (SocketException) {
                    if (!shutdown) {
                        continue; //throw;
                    }
                }
            }
        }
        public void Close() {
            shutdown = true;
            if (udpServer != null) {
                udpServer.Close();
            }
        }
        public void SendBytes(byte[] buffer, int length, IPEndPoint endPoint) {
            if (endPoint == null) {
                udpServer.Send(buffer, length);
            } else {
                udpServer.Send(buffer, length, endPoint);
            }
        }
    }

    Thread runningThread;
    UDPThread udpThreadState;
    byte[]scratchSpace = new byte[8192];
    IPEndPoint unconnectedEndpoint = null;

    // Two methods for establishing send relationship:
    // I. A connected UDP socket cannot receive messages from other hosts.
    public void Connect(IPEndPoint endPoint) {
        udpThreadState.Connect(endPoint);
    }
    // II. This can be called freely for every datagram and only affects data sent.
    public void SetUnconnectedEndpoint(IPEndPoint endPoint) {
        unconnectedEndpoint = endPoint;
    }

    // Two methods for creating a socket (with or without bound local ip/port)
    // I. Open a socket only. Avoids creating a thread.
    public IPEndPoint OpenSendOnlyClient(int udp_port=0) {
        return OpenSendOnlyClient(new IPEndPoint(IPAddress.Any, udp_port));
    }
    public IPEndPoint OpenSendOnlyClient(IPEndPoint local_udp_endpoint) {
        runningThread = null;
        udpThreadState = new UDPThread();
        return udpThreadState.Open(local_udp_endpoint);
    }

    // II. Open a socket, and creates a thread for receiving.
    public IPEndPoint OpenClient(int udp_port=0) {
        return OpenClient(new IPEndPoint(IPAddress.Any, udp_port));
    }
    public IPEndPoint OpenClient(IPEndPoint local_udp_endpoint) {
        if (udpThreadState != null) {
            StopClient();
            udpThreadState = null;
        }
        udpThreadState = new UDPThread();
        IPEndPoint localEndPoint = udpThreadState.Open(local_udp_endpoint);
        runningThread = new Thread(new ThreadStart(udpThreadState.mythread));
        runningThread.Start();
        return localEndPoint;
    }

    // Call this to close the socket, and join the thread if any.
    public void StopClient() {
        if (udpThreadState != null && !udpThreadState.shutdown) {
            udpThreadState.Close();
            if (runningThread != null) {
                runningThread.Join(5000);
            }
        }
    }

    // Read data waiting in the buffer.
    public void GetIncomingOSC(List<OSCMessage> incomingMessages) {
        OSCMessage msg;
        if (udpThreadState != null && runningThread != null) {
            while (udpThreadState.receivedMessageQueue.TryDequeue(out msg)) {
                incomingMessages.Add(msg);
            }
        }
    }

    // Send a single OSCMessage, not bundled.
    public void SendOSCPacket(OSCMessage msg, byte[] buffer=null) {
        if (buffer == null) {
            buffer = scratchSpace;
        }
        int encodedLength = 0;
        EncodeOSCInto(buffer, ref encodedLength, msg);
        udpThreadState.SendBytes(buffer, encodedLength, unconnectedEndpoint);
    }

    // Send a single OSCMessage as a bundle.
    public void SendOSCBundle(List<OSCMessage> messages, TimeTag ts, byte[] buffer=null) {
        if (messages.Count == 0) {
            CryWolf("Attempt to send bundle with no messages!");
        }
        if (buffer == null) {
            buffer = scratchSpace;
        }
        int encodedLength = 0;
        EncodeOSCBundleInto(buffer, ref encodedLength, messages, ts);
        udpThreadState.SendBytes(buffer, encodedLength, unconnectedEndpoint);
    }
    public void SendRaw(byte[] buffer, int encodedLength) {
        udpThreadState.SendBytes(buffer, encodedLength, unconnectedEndpoint);
    }

    // udpServer.Send(new byte[] { 1 }, 1); // if data is received reply letting the client know that we got his data          
}