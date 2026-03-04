using NodeDesigner.Services.Designer;

namespace NodeDesigner.Tests;

public sealed class UndoRedoServiceTests
{
    [Fact]
    public void TryUndo_WhenStackIsEmpty_ReturnsFalseAndLeavesState()
    {
        var service = new UndoRedoService<int>(value => value);

        var changed = service.TryUndo(currentState: 5, out var previousState);

        Assert.False(changed);
        Assert.Equal(5, previousState);
        Assert.False(service.CanUndo);
        Assert.False(service.CanRedo);
    }

    [Fact]
    public void PushUndoRedo_RestoresExpectedStateSequence()
    {
        var service = new UndoRedoService<int>(value => value);

        var currentState = 0;
        service.Push(currentState);
        currentState = 1;
        service.Push(currentState);
        currentState = 2;

        var undone = service.TryUndo(currentState, out var previousState);

        Assert.True(undone);
        Assert.Equal(1, previousState);
        Assert.True(service.CanUndo);
        Assert.True(service.CanRedo);

        currentState = previousState;
        var redone = service.TryRedo(currentState, out var nextState);

        Assert.True(redone);
        Assert.Equal(2, nextState);
        Assert.True(service.CanUndo);
    }

    [Fact]
    public void Push_AfterUndo_ClearsRedoStack()
    {
        var service = new UndoRedoService<int>(value => value);

        var currentState = 0;
        service.Push(currentState);
        currentState = 1;
        service.Push(currentState);
        currentState = 2;

        var undone = service.TryUndo(currentState, out var previousState);
        Assert.True(undone);

        currentState = previousState;
        service.Push(currentState);
        currentState = 42;

        Assert.False(service.CanRedo);
        Assert.True(service.CanUndo);

        var redoApplied = service.TryRedo(currentState, out var redoState);
        Assert.False(redoApplied);
        Assert.Equal(currentState, redoState);
    }
}
