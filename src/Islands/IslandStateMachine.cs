using IslasEscritorio.Core;

namespace IslasEscritorio.Islands;

public sealed class IslandStateMachine
{
    public string RuntimeId { get; }
    public IslandConfig Config { get; }
    public int ActivationDelayMs { get; }
    public int CloseDelayMs { get; }
    public IslandState State { get; private set; } = IslandState.Closed;
    public DateTime? NextDeadlineUtc { get; private set; }
    public bool IsPinnedOpen { get; set; }

    public IslandStateMachine(string runtimeId, IslandConfig config, int activationDelayMs, int closeDelayMs)
    {
        RuntimeId = runtimeId;
        Config = config;
        ActivationDelayMs = Math.Max(0, activationDelayMs);
        CloseDelayMs = Math.Max(0, closeDelayMs);
    }

    public bool Update(
        bool inActivationZone,
        bool inIslandBounds,
        DateTime nowUtc,
        Func<bool>? canActivate = null,
        int? dynamicDelayMs = null)
    {
        IslandState before = State;
        bool inside = inActivationZone || inIslandBounds || IsPinnedOpen;

        switch (State)
        {
            case IslandState.Closed when inActivationZone:
                if (canActivate is not null && !canActivate())
                    break;

                int effectiveDelay = Math.Max(0, dynamicDelayMs ?? ActivationDelayMs);
                if (effectiveDelay == 0)
                {
                    State = IslandState.Open;
                    NextDeadlineUtc = null;
                }
                else
                {
                    State = IslandState.Waiting;
                    NextDeadlineUtc = nowUtc.AddMilliseconds(effectiveDelay);
                }
                break;

            case IslandState.Waiting when !inActivationZone && !IsPinnedOpen:
                State = IslandState.Closed;
                NextDeadlineUtc = null;
                break;

            case IslandState.Waiting when NextDeadlineUtc.HasValue && nowUtc >= NextDeadlineUtc.Value:
                if (canActivate is not null && !canActivate())
                {
                    State = IslandState.Closed;
                    NextDeadlineUtc = null;
                }
                else
                {
                    State = IslandState.Open;
                    NextDeadlineUtc = null;
                }
                break;

            case IslandState.Open when !inside:
                if (CloseDelayMs == 0)
                {
                    State = IslandState.Closed;
                    NextDeadlineUtc = null;
                }
                else
                {
                    State = IslandState.Closing;
                    NextDeadlineUtc = nowUtc.AddMilliseconds(CloseDelayMs);
                }
                break;

            case IslandState.Closing when inside:
                State = IslandState.Open;
                NextDeadlineUtc = null;
                break;

            case IslandState.Closing when NextDeadlineUtc.HasValue && nowUtc >= NextDeadlineUtc.Value:
                State = IslandState.Closed;
                NextDeadlineUtc = null;
                break;
        }

        return State != before;
    }

    public void ForceOpen()
    {
        State = IslandState.Open;
        NextDeadlineUtc = null;
    }

    public void ForceClose()
    {
        IsPinnedOpen = false;
        State = IslandState.Closed;
        NextDeadlineUtc = null;
    }
}
