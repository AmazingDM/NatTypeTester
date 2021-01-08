using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace STUN.Utils
{
	public static class NetUtils
	{
		public const string DefaultLocalEnd = @"0.0.0.0:0";

		public static IPEndPoint? ParseEndpoint(string? str)
		{
			if (str is null)
			{
				return null;
			}

			var ipPort = str.Trim().Split(':');
			if (ipPort.Length < 2)
			{
				return null;
			}

			IPAddress? ip = null;
			if (ipPort.Length == 2 && IPAddress.TryParse(ipPort[0], out ip))
			{
				if (!IPAddress.TryParse(ipPort[0], out ip))
				{
					return null;
				}
			}
			else if (ipPort.Length > 2)
			{
				var ipStr = string.Join(@":", ipPort, 0, ipPort.Length - 1);
				if (!ipStr.StartsWith(@"[") || !ipStr.EndsWith(@"]") || !IPAddress.TryParse(ipStr, out ip))
				{
					return null;
				}
			}

			if (ip != null && ushort.TryParse(ipPort.Last(), out var port))
			{
				return new IPEndPoint(ip, port);
			}

			return null;
		}

		public static TcpState GetState(this TcpClient tcpClient)
		{
			var foo = IPGlobalProperties
				.GetIPGlobalProperties()
				.GetActiveTcpConnections()
				.SingleOrDefault(x => x.LocalEndPoint.Equals(tcpClient.Client.LocalEndPoint));
			return foo?.State ?? TcpState.Unknown;
		}
	}
}
