namespace NodeDesigner.Services.Designer;

public sealed class UndoRedoService<T>(Func<T, T> clone)
    : IUndoRedoService<T>
    where T : notnull
{
    private readonly Stack<T> _undoStack = new();
    private readonly Stack<T> _redoStack = new();
    private readonly Func<T, T> _clone = clone;

    public bool CanUndo => _undoStack.Count > 0;

    public bool CanRedo => _redoStack.Count > 0;

    public void Push(T state)
    {
        _undoStack.Push(_clone(state));
        _redoStack.Clear();
    }

    public bool TryUndo(T currentState, out T previousState)
    {
        if (_undoStack.Count == 0)
        {
            previousState = currentState;
            return false;
        }

        _redoStack.Push(_clone(currentState));
        previousState = _undoStack.Pop();
        return true;
    }

    public bool TryRedo(T currentState, out T nextState)
    {
        if (_redoStack.Count == 0)
        {
            nextState = currentState;
            return false;
        }

        _undoStack.Push(_clone(currentState));
        nextState = _redoStack.Pop();
        return true;
    }

    public void Clear()
    {
        _undoStack.Clear();
        _redoStack.Clear();
    }
}
