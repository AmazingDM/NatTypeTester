﻿using STUN.Enums;
using STUN.Interfaces;
using STUN.Message;
using STUN.StunResult;
using STUN.Utils;
using System;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Threading.Tasks;

namespace STUN.Client
{
    /// <summary>
    /// https://tools.ietf.org/html/rfc5389#section-7.2.1
    /// https://tools.ietf.org/html/rfc5780#section-4.2
    /// </summary>
    public class StunClient5389UDP : StunClient3489
    {
        public StunClient5389UDP(string server, ushort port = 3478, IPEndPoint local = null, IDnsQuery dnsQuery = null)
        : base(server, port, local, dnsQuery)
        {
            Timeout = TimeSpan.FromSeconds(3);
        }

        public async Task<StunResult5389> QueryAsync()
        {
            var result = await FilteringBehaviorTestAsync();
            if (result.BindingTestResult != BindingTestResult.Success
            || result.FilteringBehavior == FilteringBehavior.UnsupportedServer
            )
            {
                return result;
            }

            if (Equals(result.PublicEndPoint, result.LocalEndPoint))
            {
                result.MappingBehavior = MappingBehavior.Direct;
                return result;
            }

            // MappingBehaviorTest test II
            var result2 = await BindingTestBaseAsync(new IPEndPoint(result.OtherEndPoint.Address, RemoteEndPoint.Port));
            if (result2.BindingTestResult != BindingTestResult.Success)
            {
                result.MappingBehavior = MappingBehavior.Fail;
                return result;
            }

            if (Equals(result2.PublicEndPoint, result.PublicEndPoint))
            {
                result.MappingBehavior = MappingBehavior.EndpointIndependent;
                return result;
            }

            // MappingBehaviorTest test III
            var result3 = await BindingTestBaseAsync(result.OtherEndPoint);
            if (result3.BindingTestResult != BindingTestResult.Success)
            {
                result.MappingBehavior = MappingBehavior.Fail;
                return result;
            }

            result.MappingBehavior = Equals(result3.PublicEndPoint, result2.PublicEndPoint) ? MappingBehavior.AddressDependent : MappingBehavior.AddressAndPortDependent;

            return result;
        }

        public async Task<StunResult5389> BindingTestAsync()
        {
            var result = await BindingTestBaseAsync(RemoteEndPoint);
            return result;
        }

        private async Task<StunResult5389> BindingTestBaseAsync(IPEndPoint remote)
        {
            BindingTestResult res;

            var test = new StunMessage5389 { StunMessageType = StunMessageType.BindingRequest };
            var (response1, _, local1) = await TestAsync(test, remote, remote);
            var mappedAddress1 = AttributeExtensions.GetXorMappedAddressAttribute(response1);
            var otherAddress = AttributeExtensions.GetOtherAddressAttribute(response1);

            if (response1 == null)
            {
                res = BindingTestResult.Fail;
            }
            else if (mappedAddress1 == null)
            {
                res = BindingTestResult.UnsupportedServer;
            }
            else
            {
                res = BindingTestResult.Success;
            }

            return new StunResult5389
            {
                BindingTestResult = res,
                LocalEndPoint = local1 == null ? null : new IPEndPoint(local1, LocalEndPoint.Port),
                PublicEndPoint = mappedAddress1,
                OtherEndPoint = otherAddress
            };
        }

        public async Task<StunResult5389> MappingBehaviorTestAsync()
        {
            // test I
            var result1 = await BindingTestBaseAsync(RemoteEndPoint);

            if (result1.BindingTestResult != BindingTestResult.Success)
            {
                return result1;
            }

            if (result1.OtherEndPoint == null
                || Equals(result1.OtherEndPoint.Address, RemoteEndPoint.Address)
                || result1.OtherEndPoint.Port == RemoteEndPoint.Port)
            {
                result1.MappingBehavior = MappingBehavior.UnsupportedServer;
                return result1;
            }

            if (Equals(result1.PublicEndPoint, result1.LocalEndPoint))
            {
                result1.MappingBehavior = MappingBehavior.Direct;
                return result1;
            }

            // test II
            var result2 = await BindingTestBaseAsync(new IPEndPoint(result1.OtherEndPoint.Address, RemoteEndPoint.Port));
            if (result2.BindingTestResult != BindingTestResult.Success)
            {
                result1.MappingBehavior = MappingBehavior.Fail;
                return result1;
            }

            if (Equals(result2.PublicEndPoint, result1.PublicEndPoint))
            {
                result1.MappingBehavior = MappingBehavior.EndpointIndependent;
                return result1;
            }

            // test III
            var result3 = await BindingTestBaseAsync(result1.OtherEndPoint);
            if (result3.BindingTestResult != BindingTestResult.Success)
            {
                result1.MappingBehavior = MappingBehavior.Fail;
                return result1;
            }

            result1.MappingBehavior = Equals(result3.PublicEndPoint, result2.PublicEndPoint) ? MappingBehavior.AddressDependent : MappingBehavior.AddressAndPortDependent;

            return result1;
        }

        public async Task<StunResult5389> FilteringBehaviorTestAsync()
        {
            // test I
            var result1 = await BindingTestBaseAsync(RemoteEndPoint);

            if (result1.BindingTestResult != BindingTestResult.Success)
            {
                return result1;
            }

            if (result1.OtherEndPoint == null
                || Equals(result1.OtherEndPoint.Address, RemoteEndPoint.Address)
                || result1.OtherEndPoint.Port == RemoteEndPoint.Port)
            {
                result1.FilteringBehavior = FilteringBehavior.UnsupportedServer;
                return result1;
            }

            // test II
            var test2 = new StunMessage5389
            {
                StunMessageType = StunMessageType.BindingRequest,
                Attributes = new[] { AttributeExtensions.BuildChangeRequest(true, true) }
            };
            var (response2, _, _) = await TestAsync(test2, RemoteEndPoint, result1.OtherEndPoint);

            if (response2 != null)
            {
                result1.FilteringBehavior = FilteringBehavior.EndpointIndependent;
                return result1;
            }

            // test III
            var test3 = new StunMessage5389
            {
                StunMessageType = StunMessageType.BindingRequest,
                Attributes = new[] { AttributeExtensions.BuildChangeRequest(false, true) }
            };
            var (response3, remote3, _) = await TestAsync(test3, RemoteEndPoint, RemoteEndPoint);

            if (response3 == null)
            {
                result1.FilteringBehavior = FilteringBehavior.AddressAndPortDependent;
                return result1;
            }

            if (Equals(remote3.Address, RemoteEndPoint.Address) && remote3.Port != RemoteEndPoint.Port)
            {
                result1.FilteringBehavior = FilteringBehavior.AddressAndPortDependent;
            }
            else
            {
                result1.FilteringBehavior = FilteringBehavior.UnsupportedServer;
            }
            return result1;
        }

        private async Task<(StunMessage5389, IPEndPoint, IPAddress)> TestAsync(StunMessage5389 sendMessage, IPEndPoint remote, IPEndPoint receive)
        {
            try
            {
                var b1 = sendMessage.Bytes.ToArray();
                //var t = DateTime.Now;

                // Simple retransmissions
                //https://tools.ietf.org/html/rfc5389#section-7.2.1
                //while (t + TimeSpan.FromSeconds(6) > DateTime.Now)
                {
                    try
                    {

                        var (receive1, ipe, local) = await UdpClient.UdpReceiveAsync(b1, remote, receive);

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
    }
}
