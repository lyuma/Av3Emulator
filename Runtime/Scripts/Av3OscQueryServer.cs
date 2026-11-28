using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Lyuma.Av3Emulator.Runtime
{
    internal sealed class Av3OscQueryServer : IDisposable
    {
        private const int Read = 1;
        private const int Write = 2;
        private const string AvatarNamePath = "/avatar/name";
        private const string ParameterPrefix = "/avatar/parameters/";
        private static readonly string[] InputAxes = { "Vertical", "Horizontal", "LookHorizontal" };
        private static readonly string[] InputButtons =
        {
            "MoveForward", "MoveBackward", "MoveLeft", "MoveRight",
            "LookLeft", "LookRight", "Jump", "Run", "Voice"
        };
        private readonly TcpListener listener;
        private readonly object connectionLock = new object();
        private readonly JObject hostInfo;
        private TcpClient connection;
        private bool disposed;
        private LyumaAv3Runtime lastAvatar;
        private A3EOSCConfiguration.InnerJson[] lastMappings;
        private bool lastAllParameters;
        private volatile QueryTree tree = new QueryTree();

        internal Av3OscQueryServer(int udpPort)
        {
            listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            Port = ((IPEndPoint)listener.LocalEndpoint).Port;
            // VRChat excludes its client namespace from outbound receiver discovery.
            ServiceName = "VRChat-Client-" + Guid.NewGuid().ToString("N").Substring(0, 6).ToUpperInvariant();
            hostInfo = new JObject
            {
                ["NAME"] = ServiceName,
                ["OSC_IP"] = IPAddress.Loopback.ToString(),
                ["OSC_PORT"] = udpPort,
                ["OSC_TRANSPORT"] = "UDP",
                ["EXTENSIONS"] = new JObject
                {
                    ["ACCESS"] = true,
                    ["TYPE"] = true,
                    ["VALUE"] = true
                }
            };
            new Thread(Serve) { IsBackground = true, Name = "AV3 OSCQuery" }.Start();
        }

        internal int Port { get; }
        internal string ServiceName { get; }

        internal void Publish(LyumaAv3Runtime avatar, List<A3ESimpleOSC.OSCMessage> outgoing)
        {
            if (avatar == null || avatar.AvatarSyncSource != avatar || !avatar.IsLocal ||
                avatar.IsMirrorClone || avatar.IsShadowClone)
                avatar = null;

            var mappings = avatar == null ? null : avatar.OSCConfigurationFile.OSCJsonConfig?.parameters;
            bool allParameters = avatar != null && avatar.OSCConfigurationFile.SendRecvAllParamsNotInJSON;
            var current = tree;
            bool rebuild = !ReferenceEquals(avatar, lastAvatar) || mappings != lastMappings || allParameters != lastAllParameters;
            if (rebuild)
            {
                current = new QueryTree();
                if (avatar != null)
                {
                    foreach (var axis in InputAxes)
                        current.Register("/input/" + axis, "f", Write, 0f);
                    foreach (var button in InputButtons)
                        current.Register("/input/" + button, "T", Write, false);
                    current.Register(AvatarNamePath, "s", Read);
                    if (!allParameters && mappings != null)
                    {
                        foreach (var mapping in mappings)
                        {
                            string type = mapping.input.type == "Bool" ? "T" :
                                mapping.input.type == "Int" ? "i" : mapping.input.type == "Float" ? "f" : null;
                            if (type != null && mapping.input.address != AvatarNamePath)
                                current.Register(mapping.input.address, type, Write);
                        }
                    }
                    foreach (var message in outgoing)
                    {
                        if (message.arguments?.Length != 1 || message.path == AvatarNamePath)
                            continue;
                        object value = message.arguments[0];
                        bool avatarParameter = allParameters && message.path.StartsWith(ParameterPrefix, StringComparison.Ordinal);
                        string type = avatarParameter && avatar.BoolToIndex.ContainsKey(message.path.Substring(ParameterPrefix.Length))
                            ? "T" : TypeOf(value);
                        if (type != null)
                            current.Register(message.path, type, avatarParameter ? Read | Write : Read);
                    }
                }
                lastAvatar = avatar;
                lastMappings = mappings;
                lastAllParameters = allParameters;
            }
            if (avatar != null)
            {
                current.Nodes[AvatarNamePath].Value = avatar.avadesc.gameObject.name;
                foreach (var message in outgoing)
                {
                    if (message.arguments?.Length == 1 && message.path != AvatarNamePath &&
                        current.Nodes.TryGetValue(message.path, out var node))
                        node.Value = node.Type == "T" && message.arguments[0] is int number ? number != 0 : message.arguments[0];
                }
            }
            if (rebuild)
                tree = current;
        }

        private static string TypeOf(object value)
        {
            if (value is bool) return "T";
            if (value is int) return "i";
            if (value is float) return "f";
            if (value is string) return "s";
            return null;
        }

        private void Serve()
        {
            try
            {
                while (true)
                {
                    using (var client = listener.AcceptTcpClient())
                    {
                        lock (connectionLock)
                        {
                            if (disposed)
                                return;
                            connection = client;
                        }
                        try
                        {
                            Answer(client);
                        }
                        catch (IOException) { }
                        catch (SocketException) { }
                        catch (ObjectDisposedException) { }
                        finally
                        {
                            lock (connectionLock)
                                connection = null;
                        }
                    }
                }
            }
            catch (SocketException) { }
            catch (ObjectDisposedException) { }
            catch (InvalidOperationException) { }
        }

        private void Answer(TcpClient client)
        {
            using (NetworkStream stream = client.GetStream())
            {
                stream.WriteTimeout = 1000;
                string request = ReadRequest(stream);
                if (request == null)
                    return;
                string[] line = request.Split(' ');
                string status = "400 Bad Request";
                JObject body = null;
                if (line.Length == 3 && line[1].StartsWith("/", StringComparison.Ordinal))
                {
                    if (line[0] != "GET")
                        status = "405 Method Not Allowed";
                    else
                    {
                        var target = line[1].Split(new[] { '?', '#' }, 2);
                        if (target[0] == "/" && target.Length == 2 && target[1] == "HOST_INFO")
                            body = hostInfo;
                        else if (tree.Nodes.TryGetValue(Uri.UnescapeDataString(target[0]), out var node))
                            body = node.ToJson();
                        status = body == null ? "404 Not Found" : "200 OK";
                    }
                }
                byte[] payload = body == null ? Array.Empty<byte>() : Encoding.UTF8.GetBytes(body.ToString(Formatting.None));
                byte[] header = Encoding.ASCII.GetBytes($"HTTP/1.1 {status}\r\nContent-Type: application/json; charset=utf-8\r\n" +
                    $"Content-Length: {payload.Length}\r\nConnection: close\r\n\r\n");
                stream.Write(header, 0, header.Length);
                stream.Write(payload, 0, payload.Length);
            }
        }

        private static string ReadRequest(NetworkStream stream)
        {
            byte[] buffer = new byte[4096];
            int length = 0;
            var elapsed = Stopwatch.StartNew();
            while (length < buffer.Length && elapsed.ElapsedMilliseconds < 1000)
            {
                stream.ReadTimeout = Math.Max(1, 1000 - (int)elapsed.ElapsedMilliseconds);
                int received = stream.Read(buffer, length, buffer.Length - length);
                if (received == 0)
                    break;
                int searchStart = Math.Max(3, length);
                length += received;
                for (int i = searchStart; i < length; i++)
                {
                    if (buffer[i - 3] == '\r' && buffer[i - 2] == '\n' && buffer[i - 1] == '\r' && buffer[i] == '\n')
                        return Encoding.ASCII.GetString(buffer, 0, Array.IndexOf(buffer, (byte)'\r', 0, length));
                }
            }
            return null;
        }

        public void Dispose()
        {
            lock (connectionLock)
            {
                disposed = true;
                listener.Stop();
                connection?.Close();
            }
        }

        private sealed class QueryTree
        {
            internal readonly Dictionary<string, Node> Nodes = new Dictionary<string, Node>(StringComparer.Ordinal);

            internal QueryTree()
            {
                EnsureNode("/avatar/parameters");
            }

            internal void Register(string path, string type, int access, object value = null)
            {
                if (string.IsNullOrEmpty(path) || path[0] != '/' || path.Length < 2 || path.EndsWith("/", StringComparison.Ordinal))
                    return;
                var node = EnsureNode(path);
                node.Access |= access;
                node.Type = type;
                node.Value = value;
            }

            private Node EnsureNode(string path)
            {
                if (Nodes.TryGetValue(path, out var node))
                    return node;
                node = new Node(path);
                Nodes.Add(path, node);
                if (path != "/")
                {
                    int split = path.LastIndexOf('/');
                    var parent = EnsureNode(split == 0 ? "/" : path.Substring(0, split));
                    parent.Children.Add(path.Substring(split + 1), node);
                }
                return node;
            }
        }

        private sealed class Node
        {
            internal readonly string Path;
            internal readonly Dictionary<string, Node> Children = new Dictionary<string, Node>(StringComparer.Ordinal);
            internal int Access;
            internal string Type;
            internal volatile object Value;

            internal Node(string path) => Path = path;

            internal JObject ToJson()
            {
                var json = new JObject { ["FULL_PATH"] = Path, ["ACCESS"] = Access };
                if (Type != null)
                    json["TYPE"] = Type;
                object value = Value;
                if (value != null && (!(value is float scalar) || (!float.IsNaN(scalar) && !float.IsInfinity(scalar))))
                    json["VALUE"] = new JArray(JToken.FromObject(value));
                if (Children.Count != 0)
                {
                    var contents = new JObject();
                    foreach (var child in Children)
                        contents.Add(child.Key, child.Value.ToJson());
                    json["CONTENTS"] = contents;
                }
                return json;
            }
        }
    }
}
