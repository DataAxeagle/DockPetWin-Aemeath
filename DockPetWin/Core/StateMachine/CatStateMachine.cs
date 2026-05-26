namespace DockPetWin.Core.StateMachine;

public sealed class CatStateMachine
{
    private readonly Random random = new();
    private TimeSpan walkMinimum = TimeSpan.FromMinutes(2);
    private TimeSpan walkMaximum = TimeSpan.FromMinutes(5);
    private TimeSpan restMinimum = TimeSpan.FromMinutes(2);
    private TimeSpan restMaximum = TimeSpan.FromMinutes(5);

    public CatState State { get; private set; } = CatState.Resting;

    public event Action<CatState, CatState>? Transitioned;
    public event Action<CatState, TimeSpan>? DurationScheduled;

    public void UpdateDurations(
        TimeSpan walkingMinimum,
        TimeSpan walkingMaximum,
        TimeSpan restingMinimum,
        TimeSpan restingMaximum)
    {
        walkMinimum = walkingMinimum;
        walkMaximum = Max(walkingMaximum, walkMinimum);
        restMinimum = restingMinimum;
        restMaximum = Max(restingMaximum, restMinimum);
    }

    public void Start()
    {
        EnterTransitioning();
    }

    public void BeginDrag()
    {
        if (!State.CanBeginDrag)
        {
            return;
        }

        TransitionTo(CatState.Dragged);
    }

    public void EndDrag()
    {
        if (State.Kind != CatStateKind.Dragged)
        {
            return;
        }

        EnterRandomLongDurationStateWithOptionalTransition();
    }

    public void Pet()
    {
        EnterTransitioning();
    }

    public void FinishScheduledState(CatState scheduledState)
    {
        if (State != scheduledState)
        {
            return;
        }

        if (State.Kind == CatStateKind.Transitioning)
        {
            EnterRandomLongDurationState();
            return;
        }

        if (State.IsLongDuration)
        {
            EnterRandomLongDurationStateWithOptionalTransition();
        }
    }

    public void ToggleLongDurationState()
    {
        if (State.Kind == CatStateKind.Walking)
        {
            TransitionTo(CatState.Resting);
            ScheduleCurrentState();
            return;
        }

        TransitionTo(CatState.Walking);
        ScheduleCurrentState();
    }

    public void Rest()
    {
        TransitionTo(CatState.Resting);
        ScheduleCurrentState();
    }

    private void EnterRandomLongDurationStateWithOptionalTransition()
    {
        if (random.Next(2) == 0)
        {
            EnterTransitioning();
        }
        else
        {
            EnterRandomLongDurationState();
        }
    }

    private void EnterRandomLongDurationState()
    {
        TransitionTo(random.Next(2) == 0 ? CatState.Walking : CatState.Resting);
        ScheduleCurrentState();
    }

    private void EnterTransitioning()
    {
        TransitionTo(CatState.Transitioning);
        DurationScheduled?.Invoke(State, TimeSpan.FromSeconds(2));
    }

    private void ScheduleCurrentState()
    {
        var duration = State.Kind switch
        {
            CatStateKind.Walking => RandomDuration(walkMinimum, walkMaximum),
            CatStateKind.Resting => RandomDuration(restMinimum, restMaximum),
            _ => TimeSpan.Zero
        };

        if (duration > TimeSpan.Zero)
        {
            DurationScheduled?.Invoke(State, duration);
        }
    }

    private TimeSpan RandomDuration(TimeSpan minimum, TimeSpan maximum)
    {
        if (maximum <= minimum)
        {
            return minimum;
        }

        var range = maximum - minimum;
        return minimum + TimeSpan.FromMilliseconds(random.NextDouble() * range.TotalMilliseconds);
    }

    private void TransitionTo(CatState newState)
    {
        var oldState = State;
        State = newState;
        Transitioned?.Invoke(oldState, newState);
    }

    private static TimeSpan Max(TimeSpan first, TimeSpan second)
    {
        return first >= second ? first : second;
    }
}
