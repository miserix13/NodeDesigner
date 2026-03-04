namespace NodeDesigner.Services.Designer;

public interface IUndoRedoService<T>
    where T : notnull
{
    bool CanUndo { get; }

    bool CanRedo { get; }

    void Push(T state);

    bool TryUndo(T currentState, out T previousState);

    bool TryRedo(T currentState, out T nextState);

    void Clear();
}
