using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace BattleGame.Server;

public sealed record LanServerInfo(
    string MachineName,
    int ProtocolVersion,
    IReadOnlyList<string> WebSocketAddresses)
{
    public static LanServerInfo Create()
    {
        string[] addresses = NetworkInterface.GetAllNetworkInterfaces()
            .Where(network => network.OperationalStatus == OperationalStatus.Up
                && network.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .SelectMany(network => network.GetIPProperties().UnicastAddresses)
            .Where(address => address.Address.AddressFamily == AddressFamily.InterNetwork
                && !IPAddress.IsLoopback(address.Address))
            .Select(address => $"ws://{address.Address}:5088/ws")
            .Distinct(StringComparer.Ordinal)
            .OrderBy(address => address, StringComparer.Ordinal)
            .ToArray();

        return new LanServerInfo(Environment.MachineName, 1, addresses);
    }
}
