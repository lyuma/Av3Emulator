/* Copyright (c) 2022 Lyuma <xn.lyuma@gmail.com>

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

using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using UnityEngine;

public class SimpleOSCReceiver
{
    public enum Impulse {IMPULSE}
    public struct OSCMessage {
        public string path;
        public long time;
        public string typeTag;
        public object[] arguments;
    }
    public class UDPThread {
        UdpClient udpServer;
        public bool shutdown;
        public int udp_port;
        public ConcurrentQueue<OSCMessage> receivedMessageQueue = new ConcurrentQueue<OSCMessage>();

        public void mythread(){
            udpServer = new UdpClient(udp_port);
            while (!shutdown) {
                var incomingIP = new IPEndPoint(IPAddress.Any, 0);
                try {
                    var data = udpServer.Receive(ref incomingIP);
                    try {
                        processOSC(data, 0, 0, data.Length);
                    } catch (Exception e) {
                        UnityEngine.Debug.LogError(e.ToString());
                    }
                } catch (SocketException) {
                    if (!shutdown) {
                        throw;
                    }
                }
            }
        }
        public void interrupt() {
            shutdown = true;
            udpServer.Close();
        }

        void processOSC(byte[] data, long timetag, int offset, int length) {
            if (offset == 0 && length > 20 && data[offset] == '#' && data[offset + 1] == 'b' && data[offset + 2] == 'u' && data[offset + 3] == 'n' &&
                data[offset + 4] == 'd' && data[offset + 5] == 'l' && data[offset + 6] == 'e' && data[offset + 7] == 0) {
                timetag = IPAddress.NetworkToHostOrder(BitConverter.ToInt64(data, offset + 8));
                offset += 16;
                while (offset < length) {
                    int msglen = IPAddress.NetworkToHostOrder(BitConverter.ToInt32(data, offset));
                    offset += 4;
                    processOSC(data, timetag, offset, msglen);
                    offset += msglen;
                }
                return;
            }
            OSCMessage msg = new OSCMessage();
            msg.time = timetag;
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
            offset++;
            int typetags = offset;
            while (data[offset] != 0) {
                offset++;
            }
            msg.typeTag = System.Text.Encoding.ASCII.GetString(data, typetags, offset - typetags);
            offset = (offset + 4) & ~3;
            msg.arguments = new object[msg.typeTag.Length];
            for (int i = 0; i < msg.typeTag.Length; i++) {
                // Debug.Log("doing type tag " + msg.typeTag[i] + " offset: " + offset);
                object obj = null;
                byte[] tmp;
                switch (msg.typeTag[i]) {
                case 'T':
                    obj = true;
                    break;
                case 'F':
                    obj = false;
                    break;
                case 'N':
                    obj = null;
                    break;
                case 'I':
                    obj = Impulse.IMPULSE;
                    break;
                case 'i':
                    obj = IPAddress.NetworkToHostOrder(BitConverter.ToInt32(data, offset));
                    offset += 4;
                    break;
                case 't':
                    obj = IPAddress.NetworkToHostOrder(BitConverter.ToInt64(data, offset));
                    offset += 8;
                    break;
                case 'f':
                    tmp = new byte[4];
                    Array.Copy(data, offset, tmp, 0, 4);
                    if (BitConverter.IsLittleEndian) {
                        Array.Reverse(tmp);
                    }
                    obj = BitConverter.ToSingle(tmp, 0);
                    offset += 4;
                    break;
                case 's':
                    int strend = offset;
                    while (data[offset] != 0) {
                        strend++;
                    }
                    tmp = new byte[strend - offset];
                    Array.Copy(data, offset, tmp, 0, strend - offset);
                    offset = strend + 1;
                    offset = (offset + 4) & ~3;
                    obj = System.Text.Encoding.UTF8.GetString(tmp);
                    break;
                case 'b':
                    int len = IPAddress.NetworkToHostOrder(BitConverter.ToInt32(data, offset));
                    offset += 4;
                    tmp = new byte[len];
                    Array.Copy(data, offset, tmp, 0, len);
                    offset += len;
                    offset = (offset + 4) & ~3;
                    obj = tmp;
                    break;
                default:
                    UnityEngine.Debug.LogError("Unknown type tag " + msg.typeTag + " argument " + i + " offset " + offset);
                    break;
                }
                msg.arguments[i] = obj;
            }
            // UnityEngine.Debug.Log("Adding msg to queue: " + msg.path);
            receivedMessageQueue.Enqueue(msg);
        }
    }

    Thread runningThread;
    UDPThread udpThreadState;
    
    public void OpenClient(int udp_port) {
        if (udpThreadState != null) {
            StopClient();
            udpThreadState = null;
        }
        udpThreadState = new UDPThread();
        udpThreadState.udp_port = udp_port;
        runningThread = new Thread(new ThreadStart(udpThreadState.mythread));
        runningThread.Start();
    }
    public void StopClient() {
        if (udpThreadState != null && !udpThreadState.shutdown) {
            udpThreadState.interrupt();
            UnityEngine.Debug.Log("joining OSC thread...");
            runningThread.Join(5000);
        }
    }

    public void GetIncomingOSC(List<OSCMessage> incomingMessages) {
        OSCMessage msg;
        if (udpThreadState != null) {
            while (udpThreadState.receivedMessageQueue.TryDequeue(out msg)) {
                incomingMessages.Add(msg);
            }
        }
    }
    
    // udpServer.Send(new byte[] { 1 }, 1); // if data is received reply letting the client know that we got his data          
}