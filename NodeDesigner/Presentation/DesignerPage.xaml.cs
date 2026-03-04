using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Windows.Foundation;
using Windows.System;
using Windows.UI.Core;

namespace NodeDesigner.Presentation;

public sealed partial class DesignerPage : Page
{
    private enum InteractionMode
    {
        None,
        DragSelection,
        PanSurface,
    }

    private InteractionMode _interactionMode;
    private uint? _activePointerId;
    private Point _lastWorldPosition;
    private Point _lastSurfacePosition;
    private bool _rendererStarted;

    public DesignerPage()
    {
        this.InitializeComponent();
        Loaded += DesignerPage_Loaded;
        Unloaded += DesignerPage_Unloaded;
    }

    private async void DesignerPage_Loaded(object sender, RoutedEventArgs e)
    {
        if (_rendererStarted || DataContext is not DesignerModel model)
        {
            return;
        }

        _rendererStarted = true;

        try
        {
            await model.StartRendererAsync(DesignerSurface.ActualWidth, DesignerSurface.ActualHeight);
        }
        catch
        {
            _rendererStarted = false;
        }
    }

    private async void DesignerPage_Unloaded(object sender, RoutedEventArgs e)
    {
        if (!_rendererStarted || DataContext is not DesignerModel model)
        {
            return;
        }

        _rendererStarted = false;

        try
        {
            await model.StopRendererAsync();
        }
        catch
        {
        }
    }

    private void DesignerSurface_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (DataContext is not DesignerModel model)
        {
            return;
        }

        var point = e.GetCurrentPoint(DesignerSurface);
        _lastSurfacePosition = point.Position;
        _lastWorldPosition = ToWorld(point.Position, model);

        model.ForwardPointerInput(
            _lastWorldPosition.X,
            _lastWorldPosition.Y,
            point.Properties.IsLeftButtonPressed,
            point.Properties.IsMiddleButtonPressed,
            point.Properties.IsRightButtonPressed);

        _activePointerId = e.Pointer.PointerId;
        DesignerSurface.CapturePointer(e.Pointer);

        if (point.Properties.IsMiddleButtonPressed || point.Properties.IsRightButtonPressed)
        {
            _interactionMode = InteractionMode.PanSurface;
            e.Handled = true;
            return;
        }

        if (!point.Properties.IsLeftButtonPressed)
        {
            return;
        }

        var nodeId = model.FindNodeAt(_lastWorldPosition.X, _lastWorldPosition.Y);

        if (!string.IsNullOrWhiteSpace(nodeId))
        {
            if (IsModifierDown(VirtualKey.Menu))
            {
                model.ConnectPrimarySelectionTo(nodeId);
                _interactionMode = InteractionMode.None;
            }
            else
            {
                model.SelectNode(nodeId, multiSelect: IsModifierDown(VirtualKey.Control));
                _interactionMode = InteractionMode.DragSelection;
            }
        }
        else
        {
            if (!IsModifierDown(VirtualKey.Control))
            {
                model.ClearSelectionImmediate();
            }

            _interactionMode = InteractionMode.PanSurface;
        }

        e.Handled = true;
    }

    private void DesignerSurface_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (DataContext is not DesignerModel model
            || _activePointerId is null
            || _activePointerId.Value != e.Pointer.PointerId)
        {
            return;
        }

        var point = e.GetCurrentPoint(DesignerSurface);
        var currentSurfacePosition = point.Position;

        if (_interactionMode == InteractionMode.PanSurface)
        {
            var deltaX = currentSurfacePosition.X - _lastSurfacePosition.X;
            var deltaY = currentSurfacePosition.Y - _lastSurfacePosition.Y;
            model.PanBy(deltaX, deltaY);

            var world = ToWorld(currentSurfacePosition, model);
            model.ForwardPointerInput(
                world.X,
                world.Y,
                point.Properties.IsLeftButtonPressed,
                point.Properties.IsMiddleButtonPressed,
                point.Properties.IsRightButtonPressed);

            _lastSurfacePosition = currentSurfacePosition;
            e.Handled = true;
            return;
        }

        if (_interactionMode != InteractionMode.DragSelection)
        {
            return;
        }

        var currentWorldPosition = ToWorld(currentSurfacePosition, model);
        var deltaWorldX = currentWorldPosition.X - _lastWorldPosition.X;
        var deltaWorldY = currentWorldPosition.Y - _lastWorldPosition.Y;

        model.MoveSelectedNodes(deltaWorldX, deltaWorldY);
        model.ForwardPointerInput(
            currentWorldPosition.X,
            currentWorldPosition.Y,
            point.Properties.IsLeftButtonPressed,
            point.Properties.IsMiddleButtonPressed,
            point.Properties.IsRightButtonPressed);

        _lastSurfacePosition = currentSurfacePosition;
        _lastWorldPosition = currentWorldPosition;
        e.Handled = true;
    }

    private void DesignerSurface_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_activePointerId is null || _activePointerId.Value != e.Pointer.PointerId)
        {
            return;
        }

        if (DataContext is DesignerModel model)
        {
            var point = e.GetCurrentPoint(DesignerSurface);
            var world = ToWorld(point.Position, model);
            model.ForwardPointerInput(world.X, world.Y, false, false, false);
        }

        _interactionMode = InteractionMode.None;
        _activePointerId = null;
        DesignerSurface.ReleasePointerCapture(e.Pointer);
        e.Handled = true;
    }

    private void DesignerSurface_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        if (DataContext is not DesignerModel model)
        {
            return;
        }

        var point = e.GetCurrentPoint(DesignerSurface);
        var delta = point.Properties.MouseWheelDelta;
        var world = ToWorld(point.Position, model);

        model.ZoomBy(delta > 0 ? 1.08 : 0.92);
        model.ForwardPointerInput(
            world.X,
            world.Y,
            point.Properties.IsLeftButtonPressed,
            point.Properties.IsMiddleButtonPressed,
            point.Properties.IsRightButtonPressed,
            delta);

        e.Handled = true;
    }

    private void DesignerSurface_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (DataContext is not DesignerModel model)
        {
            return;
        }

        var world = ToWorld(e.GetPosition(DesignerSurface), model);

        var nodeId = model.FindNodeAt(world.X, world.Y);

        if (!string.IsNullOrWhiteSpace(nodeId))
        {
            model.ConnectPrimarySelectionTo(nodeId);
        }
        else
        {
            model.AddNodeAt(world.X, world.Y);
        }

        e.Handled = true;
    }

    private static Point ToWorld(Point surfacePosition, DesignerModel model)
    {
        var zoom = model.ViewportZoom <= 0 ? 1 : model.ViewportZoom;

        return new Point(
            (surfacePosition.X - model.ViewportOffsetX) / zoom,
            (surfacePosition.Y - model.ViewportOffsetY) / zoom);
    }

    private static bool IsModifierDown(VirtualKey key)
    {
        var state = InputKeyboardSource.GetKeyStateForCurrentThread(key);

        return (state & CoreVirtualKeyStates.Down) == CoreVirtualKeyStates.Down;
    }
}
