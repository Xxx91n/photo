using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace PhotoPrivacy.Ui;

public enum ConnectionState
{
    Connected,
    Reconnecting,
    Disconnected
}

public sealed class ConnectionStateService : INotifyPropertyChanged, IDisposable
{
    private readonly Func<string, CancellationToken, Task<bool>> _isAliveAsync;
    private readonly string _endpointName;
    private readonly CancellationTokenSource _cts = new();
    private bool _disposed;
    private readonly Timer _timer;
    private ConnectionState _state = ConnectionState.Disconnected;
    private int _backoffMs = 1000;
    private static readonly int[] BackoffSteps = { 1000, 2000, 4000, 8000, 16000, 32000, 60000 };

    public event PropertyChangedEventHandler? PropertyChanged;

    public ConnectionState State
    {
        get => _state;
        private set
        {
            if (_state != value)
            {
                _state = value;
                OnPropertyChanged();
            }
        }
    }

    public ConnectionStateService(WorkerIpcClient client, string endpointName)
        : this(client.IsAliveAsync, endpointName)
    {
    }

    public ConnectionStateService(Func<string, CancellationToken, Task<bool>> isAliveAsync, string endpointName)
    {
        _isAliveAsync = isAliveAsync;
        _endpointName = endpointName;
        _timer = new Timer(HeartbeatCallback, null, TimeSpan.Zero, TimeSpan.FromMilliseconds(1000));
    }

    private async void HeartbeatCallback(object? state)
    {
        if (_disposed) return;
        CancellationToken token;
        try { token = _cts.Token; } catch (ObjectDisposedException) { return; }
        try
        {
            var alive = await _isAliveAsync(_endpointName, token).ConfigureAwait(false);
            if (alive)
            {
                State = ConnectionState.Connected;
                _backoffMs = 1000;
            }
            else
            {
                State = ConnectionState.Reconnecting;
                _backoffMs = NextBackoff(_backoffMs);
            }
        }
        catch (OperationCanceledException)
        {
            State = ConnectionState.Disconnected;
        }
        catch
        {
            State = ConnectionState.Reconnecting;
            _backoffMs = NextBackoff(_backoffMs);
        }

        try
        {
            _timer.Change(TimeSpan.FromMilliseconds(_backoffMs), TimeSpan.FromMilliseconds(_backoffMs));
        }
        catch (ObjectDisposedException)
        {
            // disposing
        }
    }

    private static int NextBackoff(int current)
    {
        for (var i = 0; i < BackoffSteps.Length; i++)
        {
            if (current < BackoffSteps[i])
            {
                return BackoffSteps[i];
            }
        }
        return 60000;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _cts.Cancel(); } catch (ObjectDisposedException) { }
        _timer.Dispose();
        _cts.Dispose();
    }
}
