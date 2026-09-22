namespace SayAll.Core.Input;

public sealed class RemoteMicrophonePressGate
{
    private readonly object _syncRoot = new();
    private readonly TimeSpan _duplicateWindow;
    private DateTimeOffset? _lastAcceptedAt;

    public RemoteMicrophonePressGate(TimeSpan duplicateWindow)
    {
        if (duplicateWindow <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(duplicateWindow));
        }

        _duplicateWindow = duplicateWindow;
    }

    public bool TryAccept(DateTimeOffset timestamp)
    {
        lock (_syncRoot)
        {
            if (_lastAcceptedAt is DateTimeOffset lastAcceptedAt &&
                timestamp - lastAcceptedAt < _duplicateWindow)
            {
                return false;
            }

            _lastAcceptedAt = timestamp;
            return true;
        }
    }
}

public sealed record RemoteButtonGesture(
    RemoteButton Button,
    RemoteButtonTrigger Trigger);

public sealed class RemoteButtonGestureRecognizer
{
    private readonly TimeSpan _doubleClickWindow;
    private readonly TimeSpan _longPressThreshold;
    private readonly HashSet<RemoteButton>? _doubleClickButtons;
    private readonly HashSet<RemoteButton>? _longPressButtons;
    private RemoteButton? _pressedButton;
    private DateTimeOffset _pressedAt;
    private bool _longPressEmitted;
    private RemoteButton? _pendingClickButton;
    private DateTimeOffset _pendingClickReleasedAt;

    public RemoteButtonGestureRecognizer(
        TimeSpan? doubleClickWindow = null,
        TimeSpan? longPressThreshold = null,
        IReadOnlyCollection<RemoteButton>? doubleClickButtons = null,
        IReadOnlyCollection<RemoteButton>? longPressButtons = null)
    {
        _doubleClickWindow = doubleClickWindow ?? TimeSpan.FromMilliseconds(320);
        _longPressThreshold = longPressThreshold ?? TimeSpan.FromMilliseconds(650);
        _doubleClickButtons = doubleClickButtons is null
            ? null
            : new HashSet<RemoteButton>(doubleClickButtons);
        _longPressButtons = longPressButtons is null ? null : new(longPressButtons);
        if (_doubleClickWindow <= TimeSpan.Zero || _longPressThreshold <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(doubleClickWindow),
                "Gesture timing thresholds must be positive.");
        }
    }

    public IReadOnlyList<RemoteButtonGesture> Process(
        IReadOnlyCollection<RemoteButton> pressedButtons,
        DateTimeOffset timestamp)
    {
        ArgumentNullException.ThrowIfNull(pressedButtons);
        var emitted = new List<RemoteButtonGesture>();
        var currentButton = pressedButtons.Count == 0
            ? (RemoteButton?)null
            : pressedButtons.First();

        if (_pressedButton is not null)
        {
            if (currentButton == _pressedButton)
            {
                if (!_longPressEmitted && RecognizesLongPress(currentButton.Value) && timestamp - _pressedAt >= _longPressThreshold)
                {
                    emitted.Add(new RemoteButtonGesture(
                        _pressedButton.Value,
                        RemoteButtonTrigger.LongPress));
                    _longPressEmitted = true;
                    _pendingClickButton = null;
                }

                return emitted;
            }

            FinishPress(timestamp, emitted);
        }

        if (currentButton is not null)
        {
            if (_pendingClickButton is not null &&
                (_pendingClickButton != currentButton ||
                 timestamp - _pendingClickReleasedAt > _doubleClickWindow))
            {
                EmitPendingSingle(emitted);
            }

            _pressedButton = currentButton;
            _pressedAt = timestamp;
            _longPressEmitted = false;
        }
        else
        {
            FlushPending(timestamp, emitted);
        }

        return emitted;
    }

    public IReadOnlyList<RemoteButtonGesture> Flush(DateTimeOffset timestamp)
    {
        var emitted = new List<RemoteButtonGesture>();
        if (_pressedButton is { } pressed && !_longPressEmitted && RecognizesLongPress(pressed) &&
            timestamp - _pressedAt >= _longPressThreshold)
        {
            emitted.Add(new(pressed, RemoteButtonTrigger.LongPress));
            _longPressEmitted = true;
            _pendingClickButton = null;
        }
        FlushPending(timestamp, emitted);
        return emitted;
    }

    public void Reset()
    {
        _pressedButton = null;
        _longPressEmitted = false;
        _pendingClickButton = null;
    }

    private void FinishPress(
        DateTimeOffset timestamp,
        ICollection<RemoteButtonGesture> emitted)
    {
        var finishedButton = _pressedButton!.Value;
        var wasLongPress = _longPressEmitted ||
            (RecognizesLongPress(finishedButton) && timestamp - _pressedAt >= _longPressThreshold);
        _pressedButton = null;

        if (wasLongPress)
        {
            if (!_longPressEmitted)
            {
                emitted.Add(new RemoteButtonGesture(
                    finishedButton,
                    RemoteButtonTrigger.LongPress));
            }

            _pendingClickButton = null;
            _longPressEmitted = false;
            return;
        }

        if (_doubleClickButtons is not null &&
            !_doubleClickButtons.Contains(finishedButton))
        {
            emitted.Add(new RemoteButtonGesture(
                finishedButton,
                RemoteButtonTrigger.SingleClick));
            return;
        }

        if (_pendingClickButton == finishedButton &&
            timestamp - _pendingClickReleasedAt <= _doubleClickWindow)
        {
            emitted.Add(new RemoteButtonGesture(
                finishedButton,
                RemoteButtonTrigger.DoubleClick));
            _pendingClickButton = null;
        }
        else
        {
            if (_pendingClickButton is not null)
            {
                EmitPendingSingle(emitted);
            }

            _pendingClickButton = finishedButton;
            _pendingClickReleasedAt = timestamp;
        }
    }

    private void FlushPending(
        DateTimeOffset timestamp,
        ICollection<RemoteButtonGesture> emitted)
    {
        if (_pendingClickButton is not null &&
            timestamp - _pendingClickReleasedAt >= _doubleClickWindow)
        {
            EmitPendingSingle(emitted);
        }
    }

    private bool RecognizesLongPress(RemoteButton button) => _longPressButtons is null || _longPressButtons.Contains(button);

    private void EmitPendingSingle(ICollection<RemoteButtonGesture> emitted)
    {
        emitted.Add(new RemoteButtonGesture(
            _pendingClickButton!.Value,
            RemoteButtonTrigger.SingleClick));
        _pendingClickButton = null;
    }
}
