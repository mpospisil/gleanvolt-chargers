using System.Net;
using System.Net.Sockets;
using Gleanvolt.Chargers;

namespace Gleanvolt.Plugin.Huawei.Tests;

/// <summary>
/// A charger that serves one Modbus client, as the real one does (#3): the first connection is relayed to the fake
/// charger behind it, and any other made while it is open is closed at once and counted.
/// </summary>
internal sealed class OneClientRelay : IAsyncDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly int _target;
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _accepting;
    private int _open;
    private int _dropped;

    public OneClientRelay(int target)
    {
        _target = target;
        _listener.Start();
        _accepting = AcceptAsync();
    }

    public DeviceAddress Address => new("127.0.0.1", ((IPEndPoint)_listener.LocalEndpoint).Port);

    /// <summary>The connections closed because one was already open.</summary>
    public int Dropped => Volatile.Read(ref _dropped);

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        _listener.Stop();
        try
        {
            await _accepting;
        }
        catch (Exception ex) when (ex is OperationCanceledException or SocketException or ObjectDisposedException)
        {
        }

        _stop.Dispose();
    }

    private async Task AcceptAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            var client = await _listener.AcceptTcpClientAsync(_stop.Token);
            if (Interlocked.CompareExchange(ref _open, 1, 0) != 0)
            {
                Interlocked.Increment(ref _dropped);
                client.Dispose();
                continue;
            }

            _ = RelayAsync(client);
        }
    }

    private async Task RelayAsync(TcpClient client)
    {
        try
        {
            using (client)
            using (var charger = new TcpClient())
            {
                await charger.ConnectAsync(IPAddress.Loopback, _target, _stop.Token);
                var up = client.GetStream().CopyToAsync(charger.GetStream(), _stop.Token);
                var down = charger.GetStream().CopyToAsync(client.GetStream(), _stop.Token);
                await Task.WhenAny(up, down);
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException or SocketException or ObjectDisposedException)
        {
        }
        finally
        {
            Volatile.Write(ref _open, 0);
        }
    }
}
