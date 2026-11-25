using Gleanvolt.Chargers;
using Gleanvolt.Modbus;

namespace Gleanvolt.Plugin.Solax;

/// <summary>
/// The charger serves one Modbus client, and the plugin's own connection is it: a second one is dropped (#3). An
/// exchange with the address in use goes through that connection; any other address gets a client of its own.
/// </summary>
internal static class OwnConnection
{
    public static async Task<T> AtAsync<T>(
        this ModbusDevice device,
        DeviceAddress address,
        byte unitId,
        Func<IModbusClient, Task<T>> exchange,
        CancellationToken cancellationToken)
    {
        if (address == device.Address && unitId == device.UnitId)
        {
            return await device.UseAsync(exchange, cancellationToken).ConfigureAwait(false);
        }

        await using var client = device.ClientFor(address, unitId);
        return await exchange(client).ConfigureAwait(false);
    }

    /// <summary>
    /// Searches <paramref name="hosts"/> with <paramref name="probe"/>: the host in use through the plugin's own
    /// connection, left out of the port scan, which would open a second one; the others as LanScan does.
    /// </summary>
    public static async Task<IReadOnlyList<FoundCharger>> FindAsync(
        this ModbusDevice device,
        IReadOnlyList<System.Net.IPAddress> hosts,
        int port,
        Func<IModbusClient, CancellationToken, Task<string?>> probe,
        CancellationToken cancellationToken)
    {
        var inUse = device.Address;
        var isInUse = (System.Net.IPAddress host) => inUse is not null && host.ToString() == inUse.Host;

        var found = (await LanScan.SearchAsync(
            [.. hosts.Where(host => !isInUse(host))],
            port,
            (address, token) => device.AtAsync(address, 1, client => probe(client, token), token),
            cancellationToken).ConfigureAwait(false)).ToList();

        if (inUse is not null && hosts.Any(isInUse))
        {
            try
            {
                if (await device.AtAsync(inUse, device.UnitId, client => probe(client, cancellationToken), cancellationToken).ConfigureAwait(false) is { } description)
                {
                    found.Add(new FoundCharger(inUse, description));
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Not answering now: the search finds nothing there, as for any other host.
            }
        }

        return [.. found.OrderBy(charger => charger.Address.Host, StringComparer.Ordinal)];
    }
}
