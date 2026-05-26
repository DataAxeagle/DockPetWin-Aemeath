namespace DockPetWin.Core.StateMachine;

public enum CatStateKind
{
    Transitioning,
    Walking,
    Resting,
    Dragged
}

public readonly record struct CatState(CatStateKind Kind)
{
    public static CatState Transitioning { get; } = new(CatStateKind.Transitioning);
    public static CatState Walking { get; } = new(CatStateKind.Walking);
    public static CatState Resting { get; } = new(CatStateKind.Resting);
    public static CatState Dragged { get; } = new(CatStateKind.Dragged);

    public bool IsLongDuration => Kind is CatStateKind.Walking or CatStateKind.Resting;

    public bool CanBeginDrag => Kind is CatStateKind.Walking or CatStateKind.Resting or CatStateKind.Transitioning;
}
