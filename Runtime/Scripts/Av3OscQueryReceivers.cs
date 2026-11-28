using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using MeaMod.DNS.Model;
using MeaMod.DNS.Multicast;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Lyuma.Av3Emulator.Runtime
{
    internal sealed class Av3OscQueryReceivers : IDisposable
    {
        private const string ServiceSuffix = "._oscjson._tcp.local";
        private const int RefreshMilliseconds = 4000;
        private const int ExpirationMilliseconds = 16000;
        internal const string UserAgent = "AV3-Emulator-OSCQuery";
        private readonly ServiceDiscovery discovery;
        private readonly Dictionary<string, Candidate> candidates = new Dictionary<string, Candidate>(StringComparer.OrdinalIgnoreCase);
        private readonly int ownUdpPort;
        private int nextSearch = Environment.TickCount;
        private bool disposed;
        private volatile Route[] routes = Array.Empty<Route>();

        internal Av3OscQueryReceivers(ServiceDiscovery discovery, int ownUdpPort)
        {
            this.discovery = discovery;
            this.ownUdpPort = ownUdpPort;
            discovery.ServiceInstanceDiscovered += OnDiscovered;
            discovery.ServiceInstanceShutdown += OnShutdown;
        }

        internal Route[] Routes => routes;

        internal void Refresh()
        {
            int now = Environment.TickCount;
            if (unchecked(now - nextSearch) < 0)
                return;
            nextSearch = unchecked(now + RefreshMilliseconds);
            lock (candidates)
            {
                if (disposed)
                    return;
                var expired = candidates.Values.Where(candidate => unchecked(now - candidate.Seen) >= ExpirationMilliseconds).ToArray();
                foreach (var candidate in expired)
                    candidates.Remove(candidate.Name);
                if (expired.Length != 0)
                    RefreshRoutes();
                // A recent DNS-SD answer still identifies the endpoint if this multicast scan misses a reply.
                foreach (var candidate in candidates.Values)
                    QueueResolution(candidate);
            }
            try
            {
                discovery.QueryServiceInstances("_oscjson._tcp");
            }
            catch (SocketException)
            {
                // Discovery will try again when a multicast interface becomes available.
            }
        }

        private void OnDiscovered(object sender, ServiceInstanceDiscoveryEventArgs eventArgs)
        {
            string fullName = eventArgs.ServiceInstanceName.ToString().TrimEnd('.');
            if (!fullName.EndsWith(ServiceSuffix, StringComparison.OrdinalIgnoreCase))
                return;
            string name = fullName.Substring(0, fullName.Length - ServiceSuffix.Length);
            if (name.StartsWith("AV3-Emulator-", StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith("VRChat-Client-", StringComparison.OrdinalIgnoreCase))
                return;

            var records = eventArgs.Message.Answers.Concat(eventArgs.Message.AdditionalRecords).ToArray();
            var service = records.OfType<SRVRecord>().FirstOrDefault(record => record.Name == eventArgs.ServiceInstanceName);
            if (service == null || service.Port == 0 ||
                !records.OfType<ARecord>().Any(record => record.Name == service.Target && record.Address.Equals(IPAddress.Loopback)))
                return;

            int now = Environment.TickCount;
            lock (candidates)
            {
                if (disposed)
                    return;
                if (candidates.TryGetValue(name, out var candidate) && candidate.QueryPort == service.Port)
                {
                    candidate.Seen = now;
                    return;
                }
                if (candidate == null && candidates.Count >= 32)
                    return;
                candidate = new Candidate(name, service.Port, now);
                candidates[name] = candidate;
                QueueResolution(candidate);
            }
        }

        private void QueueResolution(Candidate candidate)
        {
            if (candidate.Resolving)
                return;
            candidate.Resolving = true;
            _ = Task.Run(() => Resolve(candidate));
        }

        private void OnShutdown(object sender, ServiceInstanceShutdownEventArgs eventArgs)
        {
            string fullName = eventArgs.ServiceInstanceName.ToString().TrimEnd('.');
            if (!fullName.EndsWith(ServiceSuffix, StringComparison.OrdinalIgnoreCase))
                return;
            lock (candidates)
                if (candidates.Remove(fullName.Substring(0, fullName.Length - ServiceSuffix.Length)))
                    RefreshRoutes();
        }

        private void Resolve(Candidate candidate)
        {
            int udpPort = 0;
            var paths = new HashSet<string>(StringComparer.Ordinal);
            try
            {
                var info = ReadJson(candidate.QueryPort, "/?HOST_INFO");
                udpPort = (int?)info["OSC_PORT"] ?? 0;
                string ip = (string)info["OSC_IP"];
                if (udpPort > 0 && udpPort <= 65535 && udpPort != ownUdpPort && (string)info["OSC_TRANSPORT"] == "UDP" &&
                    string.Equals((string)info["NAME"], candidate.Name, StringComparison.OrdinalIgnoreCase) &&
                    (ip == null || ip == "127.0.0.1"))
                    CollectWritableAvatarPaths(ReadJson(candidate.QueryPort, "/"), paths);
            }
            catch (Exception error) when (error is IOException || error is WebException || error is JsonException ||
                error is InvalidCastException || error is FormatException || error is OverflowException || error is ArgumentException)
            {
                paths.Clear();
            }
            lock (candidates)
            {
                if (!candidates.TryGetValue(candidate.Name, out var current) || !ReferenceEquals(candidate, current))
                    return;
                candidate.Resolving = false;
                candidate.Port = udpPort;
                candidate.Paths = paths;
                RefreshRoutes();
            }
        }

        private static JObject ReadJson(int port, string path)
        {
            var request = (HttpWebRequest)WebRequest.Create($"http://127.0.0.1:{port}{path}");
            request.Proxy = null;
            request.AllowAutoRedirect = false;
            request.UserAgent = UserAgent;
            request.Timeout = 1200;
            request.ReadWriteTimeout = 1200;
            using (var response = (HttpWebResponse)request.GetResponse())
            using (var reader = new StreamReader(response.GetResponseStream()))
            {
                if (response.StatusCode != HttpStatusCode.OK)
                    throw new IOException("Expected an OSCQuery JSON response");
                var buffer = new char[128 * 1024 + 1];
                int length = reader.ReadBlock(buffer, 0, buffer.Length);
                if (length == buffer.Length)
                    throw new IOException("The OSCQuery response exceeded the local receiver limit");
                using (var json = new JsonTextReader(new StringReader(new string(buffer, 0, length))) { MaxDepth = 32 })
                    return JObject.Load(json);
            }
        }

        private static void CollectWritableAvatarPaths(JObject node, HashSet<string> paths)
        {
            string path = (string)node["FULL_PATH"];
            if (path != null && (path == "/avatar" || path.StartsWith("/avatar/", StringComparison.Ordinal)) &&
                (((int?)node["ACCESS"] ?? 0) & 2) != 0)
                paths.Add(path);
            if (node["CONTENTS"] is JObject contents)
                foreach (var property in contents.Properties())
                    if (property.Value is JObject child)
                        CollectWritableAvatarPaths(child, paths);
        }

        private void RefreshRoutes()
        {
            var previous = routes;
            routes = candidates.Values.Where(candidate => candidate.Paths.Count != 0).GroupBy(candidate => candidate.Port)
                .Select(group =>
                {
                    var paths = new HashSet<string>(group.SelectMany(candidate => candidate.Paths), StringComparer.Ordinal);
                    return previous.FirstOrDefault(route => route.Endpoint.Port == group.Key && route.Paths.SetEquals(paths))
                        ?? new Route(group.Key, paths);
                }).ToArray();
        }

        public void Dispose()
        {
            discovery.ServiceInstanceDiscovered -= OnDiscovered;
            discovery.ServiceInstanceShutdown -= OnShutdown;
            lock (candidates)
            {
                disposed = true;
                candidates.Clear();
                routes = Array.Empty<Route>();
            }
        }

        private sealed class Candidate
        {
            internal readonly string Name;
            internal readonly int QueryPort;
            internal int Seen;
            internal bool Resolving;
            internal int Port;
            internal HashSet<string> Paths = new HashSet<string>(StringComparer.Ordinal);

            internal Candidate(string name, int queryPort, int now)
            {
                Name = name;
                QueryPort = queryPort;
                Seen = now;
            }
        }

        internal sealed class Route
        {
            internal readonly IPEndPoint Endpoint;
            internal readonly HashSet<string> Paths;
            internal readonly Dictionary<string, A3ESimpleOSC.OSCMessage> Sent = new Dictionary<string, A3ESimpleOSC.OSCMessage>();

            internal Route(int port, HashSet<string> paths)
            {
                Endpoint = new IPEndPoint(IPAddress.Loopback, port);
                Paths = paths;
            }

            internal bool Accepts(string path) => Paths.Any(prefix => path == prefix || path.StartsWith(prefix + "/", StringComparison.Ordinal));
        }
    }
}
