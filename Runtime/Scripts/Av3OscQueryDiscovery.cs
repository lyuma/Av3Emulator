using System;
using System.Net;
using System.Net.Sockets;
using MeaMod.DNS.Model;
using MeaMod.DNS.Multicast;

namespace Lyuma.Av3Emulator.Runtime
{
    internal sealed class Av3OscQueryDiscovery : IDisposable
    {
        private readonly MulticastService multicast = new MulticastService { UseIpv6 = false };
        private readonly ServiceDiscovery discovery;
        internal ServiceDiscovery Services => discovery;

        internal Av3OscQueryDiscovery(string name, int tcpPort, int udpPort)
        {
            discovery = new ServiceDiscovery(multicast);
            try
            {
                var query = new ServiceProfile(name, "_oscjson._tcp", (ushort)tcpPort, new[] { IPAddress.Loopback });
                var osc = new ServiceProfile(name, "_osc._udp", (ushort)udpPort, new[] { IPAddress.Loopback });
                discovery.Advertise(query);
                discovery.Advertise(osc);
                multicast.Start();
                Announce(query);
                Announce(osc);
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        private void Announce(ServiceProfile profile)
        {
            var announcement = new Message { QR = true, AA = true };
            announcement.Answers.Add(new PTRRecord
            {
                Name = profile.QualifiedServiceName,
                DomainName = profile.FullyQualifiedName
            });
            announcement.AdditionalRecords.AddRange(profile.Resources);
            multicast.SendAnswer(announcement, checkDuplicate: false);
        }

        public void Dispose()
        {
            try
            {
                discovery.Unadvertise();
            }
            catch (SocketException)
            {
                // The interface may have disappeared before the goodbye could be sent.
            }
            finally
            {
                discovery.Dispose();
                multicast.Dispose();
            }
        }
    }
}
