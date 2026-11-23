using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Gleanvolt.Plugin.GoE;
using Gleanvolt.Providers.Contracts;
using Gleanvolt.Providers.Contracts.Charger;
using Microsoft.Extensions.Logging.Abstractions;

namespace Gleanvolt.Chargers.Tests;

/// <summary>The go-e plugin against a fake go-e's HTTP API v2: states, the pause through the force state, the API off.</summary>
public sealed class GoeTests : IAsyncLifetime
{
    private static readonly PluginInfo Plugin = new("goe-charger", "go-e Charger", "Goe", 8100, 80);

    private readonly FakeGoe _goe = new();
    private Harness _harness = null!;
    private GoeCharger _charger = null!;

    public Task InitializeAsync()
    {
        _harness = new Harness(Plugin, new Dictionary<string, string?> { ["Goe:Host"] = "192.168.1.30" });
        _charger = new GoeCharger(new FakeClients(_goe, _harness.Recorder), _harness.Settings, TimeProvider.System, NullLogger<GoeCharger>.Instance);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _harness.DisposeAsync();

    [Fact]
    public async Task A_charging_go_e_reads_with_its_ceiling_and_says_it_can_switch_phases()
    {
        _goe.Car = 2;
        _goe.PowerWatts = 6900;
        var hub = await _harness.ConnectAsync(_charger, new ChargerLimits(6, 16, 3, 230));

        var reading = await hub.ReadAsync();

        Assert.Equal(ChargerState.Charging, reading.Status);
        Assert.Equal(6900, reading.PowerWatts);
        Assert.Equal(16, reading.MaxCurrentAmps);   // an 11 kW unit
        Assert.True(reading.ChargingAllowed);
        Assert.True(hub.Capabilities!.CanSwitchPhases);
        Assert.Equal("http://192.168.1.30/" + GoeCharger.StatusRequest, _goe.Requests[^1]);
    }

    [Fact]
    public async Task A_pause_is_the_force_state_off_and_the_next_setpoint_resumes_it_in_one_request()
    {
        var hub = await _harness.ConnectAsync(_charger, new ChargerLimits(6, 16, 3, 230));

        await hub.SetChargingAllowedAsync(true);
        await hub.SetCurrentAsync(0, "a cloud");
        var paused = await hub.ReadAsync();
        await hub.SetCurrentAsync(8, "the sun");

        Assert.Equal(["frc=2", "frc=1", "amp=8&frc=2"], _goe.Sets);
        Assert.True(paused.ChargingAllowed);
        Assert.Equal(0, paused.CurrentAmps);
    }

    [Fact]
    public async Task A_stop_while_paused_stays_stopped()
    {
        var hub = await _harness.ConnectAsync(_charger, new ChargerLimits(6, 16, 3, 230));

        await hub.SetChargingAllowedAsync(false);
        await hub.SetCurrentAsync(0);
        await hub.SetCurrentAsync(10);

        Assert.Equal(["frc=1", "amp=10"], _goe.Sets);
    }

    [Fact]
    public async Task With_the_API_off_the_owner_is_asked_to_switch_it_on()
    {
        _goe.ApiOff = true;
        var hub = await _harness.ConnectAsync(_charger, new ChargerLimits(6, 16, 3, 230));

        await Assert.ThrowsAnyAsync<Exception>(() => hub.ReadAsync());

        var status = await hub.WaitForStatusAsync(s => !s.Healthy);
        Assert.True(status.NeedsOwner);
        Assert.Contains("HTTP API v2", status.Message);
    }

    [Theory]
    [InlineData(1, true, ChargerState.Available)]
    [InlineData(3, true, ChargerState.Preparing)]
    [InlineData(3, false, ChargerState.SuspendedEvse)]
    [InlineData(4, true, ChargerState.SuspendedEv)]
    [InlineData(4, false, ChargerState.SuspendedEvse)]
    [InlineData(5, true, ChargerState.Faulted)]
    [InlineData(0, true, ChargerState.Unknown)]
    public void Car_and_alw_map_as_the_built_in_driver_mapped_them(int car, bool allowed, ChargerState expected)
    {
        Assert.Equal(expected, GoeCharger.StateOf(car, allowed));
    }

    /// <summary>A go-e's local HTTP API v2, from go-e's documentation.</summary>
    private sealed class FakeGoe : HttpMessageHandler
    {
        public int Car { get; set; } = 1;

        public double PowerWatts { get; set; }

        public int Force { get; set; } = 2;

        public int Amp { get; set; } = 16;

        public bool ApiOff { get; set; }

        public List<string> Requests { get; } = [];

        public List<string> Sets { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!.ToString());

            if (ApiOff)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            }

            JsonObject answer;
            if (request.RequestUri.AbsolutePath == "/api/set")
            {
                var query = request.RequestUri.Query.TrimStart('?');
                Sets.Add(query);
                answer = [];
                foreach (var pair in query.Split('&'))
                {
                    var (key, value) = (pair.Split('=')[0], int.Parse(pair.Split('=')[1], System.Globalization.CultureInfo.InvariantCulture));
                    if (key == "amp") Amp = value; else Force = value;
                    answer[key] = true;
                }
            }
            else
            {
                var energy = new JsonArray();
                for (var i = 0; i < 16; i++)
                {
                    energy.Add(i == 11 ? PowerWatts : 0);
                }

                answer = new JsonObject
                {
                    ["car"] = Car, ["alw"] = Force != 1, ["frc"] = Force, ["amp"] = Amp, ["ama"] = 32, ["var"] = 11,
                    ["nrg"] = energy, ["err"] = 0, ["psm"] = 0, ["fwv"] = "56.1", ["typ"] = "go-eCharger_V3",
                };
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(answer.ToJsonString(), Encoding.UTF8, "application/json"),
            });
        }
    }
}
