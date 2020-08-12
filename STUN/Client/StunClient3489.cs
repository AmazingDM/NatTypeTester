﻿using STUN.Enums;
using STUN.Interfaces;
using STUN.Message;
using STUN.StunResult;
using STUN.Utils;
using System;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reactive.Linq;
using System.Reactive.Subjects;

namespace STUN.Client
{
    /// <summary>
    /// https://tools.ietf.org/html/rfc3489#section-10.1
    /// https://upload.wikimedia.org/wikipedia/commons/6/63/STUN_Algorithm3.svg
    /// </summary>
    public class StunClient3489 : IDisposable
    {
        #region Subject

        private readonly Subject<NatType> _natTypeSubj = new Subject<NatType>();
        public IObservable<NatType> NatTypeChanged => _natTypeSubj.AsObservable();

        private readonly Subject<IPEndPoint> _pubSubj = new Subject<IPEndPoint>();
        public IObservable<IPEndPoint> PubChanged => _pubSubj.AsObservable();

        private readonly Subject<IPEndPoint> _localSubj = new Subject<IPEndPoint>();
        public IObservable<IPEndPoint> LocalChanged => _localSubj.AsObservable();

        #endregion

        public IPEndPoint LocalEndPoint => (IPEndPoint)UdpClient.Client.LocalEndPoint;

        public TimeSpan Timeout
        {
            get => TimeSpan.FromMilliseconds(UdpClient.Client.ReceiveTimeout);
            set => UdpClient.Client.ReceiveTimeout = Convert.ToInt32(value.TotalMilliseconds);
        }

        protected readonly UdpClient UdpClient;

        protected readonly IPAddress Server;
        protected readonly ushort Port;

        public IPEndPoint RemoteEndPoint => Server == null ? null : new IPEndPoint(Server, Port);

        public StunClient3489(string server, ushort port = 3478, IPEndPoint local = null, IDnsQuery dnsQuery = null)
        {
            Func<string, IPAddress> dnsQuery1;
            if (string.IsNullOrEmpty(server))
            {
                throw new ArgumentException(@"Please specify STUN server !");
            }

            if (port < 1)
            {
                throw new ArgumentException(@"Port value must be >= 1 !");
            }

            if (dnsQuery != null)
            {
                dnsQuery1 = dnsQuery.Query;
            }
            else
            {
                dnsQuery1 = new DefaultDnsQuery().Query;
            }

            Server = dnsQuery1(server);
            if (Server == null)
            {
                throw new ArgumentException(@"Wrong STUN server !");
            }
            Port = port;

            UdpClient = local == null ? new UdpClient() : new UdpClient(local);

            Timeout = TimeSpan.FromSeconds(1.6);
        }

        public ClassicStunResult Query()
        {
            var res = new ClassicStunResult();
            _natTypeSubj.OnNext(res.NatType);
            _pubSubj.OnNext(res.PublicEndPoint);

            try
            {
                // test I
                var test1 = new StunMessage5389 { StunMessageType = StunMessageType.BindingRequest, MagicCookie = 0 };

                var (response1, remote1, local1) = Test(test1, RemoteEndPoint, RemoteEndPoint);
                if (response1 == null)
                {
                    res.NatType = NatType.UdpBlocked;
                    return res;
                }

                if (local1 != null)
                {
                    _localSubj.OnNext(LocalEndPoint);
                }

                var mappedAddress1 = AttributeExtensions.GetMappedAddressAttribute(response1);
                var changedAddress1 = AttributeExtensions.GetChangedAddressAttribute(response1);

                // 某些单 IP 服务器的迷惑操作
                if (mappedAddress1 == null
                || changedAddress1 == null
                || Equals(changedAddress1.Address, remote1.Address)
                || changedAddress1.Port == remote1.Port)
                {
                    res.NatType = NatType.UnsupportedServer;
                    return res;
                }

                _pubSubj.OnNext(mappedAddress1); // 显示 test I 得到的映射地址

                var test2 = new StunMessage5389
                {
                    StunMessageType = StunMessageType.BindingRequest,
                    MagicCookie = 0,
                    Attributes = new[] { AttributeExtensions.BuildChangeRequest(true, true) }
                };

                // test II
                var (response2, remote2, _) = Test(test2, RemoteEndPoint, changedAddress1);
                var mappedAddress2 = AttributeExtensions.GetMappedAddressAttribute(response2);

                if (Equals(mappedAddress1.Address, local1) && mappedAddress1.Port == LocalEndPoint.Port)
                {
                    // No NAT
                    if (response2 == null)
                    {
                        res.NatType = NatType.SymmetricUdpFirewall;
                        res.PublicEndPoint = mappedAddress1;
                        return res;
                    }
                    res.NatType = NatType.OpenInternet;
                    res.PublicEndPoint = mappedAddress2;
                    return res;
                }

                // NAT
                if (response2 != null)
                {
                    // 有些单 IP 服务器并不能测 NAT 类型，比如 Google 的
                    var type = Equals(remote1.Address, remote2.Address) || remote1.Port == remote2.Port ? NatType.UnsupportedServer : NatType.FullCone;
                    res.NatType = type;
                    res.PublicEndPoint = mappedAddress2;
                    return res;
                }

                // Test I(#2)
                var test12 = new StunMessage5389 { StunMessageType = StunMessageType.BindingRequest, MagicCookie = 0 };
                var (response12, _, _) = Test(test12, changedAddress1, changedAddress1);
                var mappedAddress12 = AttributeExtensions.GetMappedAddressAttribute(response12);

                if (mappedAddress12 == null)
                {
                    res.NatType = NatType.Unknown;
                    return res;
                }

                if (!Equals(mappedAddress12, mappedAddress1))
                {
                    res.NatType = NatType.Symmetric;
                    res.PublicEndPoint = mappedAddress12;
                    return res;
                }

                // Test III
                var test3 = new StunMessage5389
                {
                    StunMessageType = StunMessageType.BindingRequest,
                    MagicCookie = 0,
                    Attributes = new[] { AttributeExtensions.BuildChangeRequest(false, true) }
                };
                var (response3, _, _) = Test(test3, changedAddress1, changedAddress1);
                var mappedAddress3 = AttributeExtensions.GetMappedAddressAttribute(response3);
                if (mappedAddress3 != null)
                {
                    res.NatType = NatType.RestrictedCone;
                    res.PublicEndPoint = mappedAddress3;
                    return res;
                }
                res.NatType = NatType.PortRestrictedCone;
                res.PublicEndPoint = mappedAddress12;
                return res;
            }
            finally
            {
                _natTypeSubj.OnNext(res.NatType);
                _pubSubj.OnNext(res.PublicEndPoint);
            }
        }

        /// <returns>
        /// (StunMessage, Remote, Local)
        /// </returns>
        private (StunMessage5389, IPEndPoint, IPAddress) Test(StunMessage5389 sendMessage, IPEndPoint remote, IPEndPoint receive)
        {
            try
            {
                var b1 = sendMessage.Bytes.ToArray();
                //var t = DateTime.Now;

                // Simple retransmissions
                //https://tools.ietf.org/html/rfc3489#section-9.3
                //while (t + TimeSpan.FromSeconds(3) > DateTime.Now)
                {
                    try
                    {
                        var (receive1, ipe, local) = UdpClient.UdpReceive(b1, remote, receive);

                        var message = new StunMessage5389();
                        if (message.TryParse(receive1) &&
                            message.ClassicTransactionId.IsEqual(sendMessage.ClassicTransactionId))
                        {
                            return (message, ipe, local);
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine(ex);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
            }
            return (null, null, null);
        }

        public void Dispose()
        {
            UdpClient?.Dispose();
            _natTypeSubj.OnCompleted();
            _pubSubj.OnCompleted();
        }
    }
}
