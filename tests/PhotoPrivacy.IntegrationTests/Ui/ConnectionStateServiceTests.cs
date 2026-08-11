using System.ComponentModel;
using PhotoPrivacy.Ui;
using Xunit;

namespace PhotoPrivacy.IntegrationTests.Ui;

public class ConnectionStateServiceTests
{
    [Fact]
    public void Initial_State_Should_Be_Disconnected()
    {
        using var svc = new ConnectionStateService((_, _) => Task.FromResult(true), "test-endpoint");
        Assert.Equal(ConnectionState.Disconnected, svc.State);
        svc.Dispose();
    }

    [Fact]
    public async Task State_Should_Transition_To_Connected_When_Alive()
    {
        var tcs = new TaskCompletionSource<bool>();
        tcs.SetResult(true);
        using var svc = new ConnectionStateService((_, ct) =>
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(true);
        }, "test-ep");

        ConnectionState? observed = null;
        svc.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ConnectionStateService.State))
                observed = svc.State;
        };

        await Task.Delay(200);
        Assert.Equal(ConnectionState.Connected, observed);
    }

    [Fact]
    public async Task State_Should_Transition_To_Reconnecting_When_Not_Alive()
    {
        using var svc = new ConnectionStateService((_, ct) =>
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(false);
        }, "test-ep");

        ConnectionState? observed = null;
        svc.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ConnectionStateService.State))
                observed = svc.State;
        };

        await Task.Delay(200);
        Assert.Equal(ConnectionState.Reconnecting, observed);
    }

    [Fact]
    public void Dispose_Should_Not_Throw()
    {
        var svc = new ConnectionStateService((_, _) => Task.FromResult(true), "ep");
        svc.Dispose();
        // Double dispose should not throw
        svc.Dispose();
    }
}