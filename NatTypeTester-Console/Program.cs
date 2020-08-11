using STUN.StunResult;
using STUN.Utils;
using System;
using System.Net;
using System.Threading.Tasks;

namespace NatTypeTester_Console
{
    internal static class Program
    {
        /// <summary>
        /// stun.qq.com 3478 0.0.0.0:0
        /// </summary>
        private static async Task Main(string[] args)
        {
            var server = @"stun.qq.com";
            ushort port = 3478;
            IPEndPoint local = null;
            if (args.Length > 0 && (Uri.CheckHostName(args[0]) == UriHostNameType.Dns || IPAddress.TryParse(args[0], out _)))
            {
                server = args[0];
            }
            if (args.Length > 1)
            {
                ushort.TryParse(args[1], out port);
            }
            if (args.Length > 2)
            {
                local = NetUtils.ParseEndpoint(args[2]);
            }

            var res = await NetUtils.NatBehaviorDiscovery(server, port, local);
            Console.WriteLine($@"Other address is {res.OtherEndPoint}");
            Console.WriteLine($@"Binding test: {res.BindingTestResult}");
            Console.WriteLine($@"Local address: {res.LocalEndPoint}");
            Console.WriteLine($@"Mapped address: {res.PublicEndPoint}");
            Console.WriteLine($@"Nat mapping behavior: {res.MappingBehavior}");
            Console.WriteLine($@"Nat filtering behavior: {res.FilteringBehavior}");

            var result = 0;
            switch (res.MappingBehavior)
            {
                case STUN.Enums.MappingBehavior.Unknown:
                    result = -1;
                    break;
                case STUN.Enums.MappingBehavior.UnsupportedServer:
                    result = -2;
                    break;
                case STUN.Enums.MappingBehavior.Fail:
                    result = -3;
                    break;
                case STUN.Enums.MappingBehavior.Direct:
                case STUN.Enums.MappingBehavior.EndpointIndependent:
                    if (res.FilteringBehavior == STUN.Enums.FilteringBehavior.EndpointIndependent)
                        result = 1;
                    if (res.FilteringBehavior == STUN.Enums.FilteringBehavior.AddressDependent)
                        result = 2;
                    if (res.FilteringBehavior == STUN.Enums.FilteringBehavior.AddressAndPortDependent)
                        result = 2;
                    break;
                case STUN.Enums.MappingBehavior.AddressDependent:
                    result = 3;
                    break;
                case STUN.Enums.MappingBehavior.AddressAndPortDependent:
                    result = 4;
                    break;
                default:
                    break;
            }
            Console.WriteLine($@"[result]: {result}");
        }
    }
}
