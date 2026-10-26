using System.Net;
using System.Net.Sockets;
using EnshroudedManager.Shared;

namespace EnshroudedManager.Service;

/// <summary>Minimal Steam A2S_INFO client, used to read the player count from the query port.</summary>
public static class SteamQuery
{
    private static readonly byte[] InfoRequest =
        [0xFF, 0xFF, 0xFF, 0xFF, 0x54, .."Source Engine Query\0"u8];

    public static async Task<PlayersDto?> GetPlayersAsync(int port, CancellationToken ct = default)
    {
        try
        {
            using var udp = new UdpClient();
            var ep = new IPEndPoint(IPAddress.Loopback, port);
            var request = InfoRequest;
            for (var i = 0; i < 2; i++)
            {
                await udp.SendAsync(request, ep, ct);
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromSeconds(2));
                var data = (await udp.ReceiveAsync(cts.Token)).Buffer;
                if (data.Length < 5) return null;
                if (data[4] == 0x41 && data.Length >= 9) // challenge: resend with it appended
                {
                    request = [.. InfoRequest, .. data[5..9]];
                    continue;
                }
                return data[4] == 0x49 ? ParseInfo(data) : null;
            }
        }
        catch { }
        return null;
    }

    private static PlayersDto? ParseInfo(byte[] d)
    {
        var pos = 6; // header(4) + type(1) + protocol(1)
        for (var s = 0; s < 4; s++) // name, map, folder, game
        {
            while (pos < d.Length && d[pos] != 0) pos++;
            pos++;
        }
        pos += 2; // app id
        return pos + 1 < d.Length ? new PlayersDto(d[pos], d[pos + 1]) : null;
    }
}
