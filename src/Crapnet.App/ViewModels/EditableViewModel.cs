using CommunityToolkit.Mvvm.ComponentModel;

namespace Crapnet.App.ViewModels;

/// <summary>
/// A mutable piece of a rule that announces every user edit to whoever owns it.
/// </summary>
/// <remarks>
/// The domain model is immutable, so editing happens here and is projected back into fresh records
/// on demand. Rather than have the window watch dozens of individual properties, each editable
/// piece bubbles a single <see cref="Changed"/> event upwards; the window debounces those and
/// pushes one rebuilt profile into the engine.
/// </remarks>
public abstract class EditableViewModel : ObservableObject
{
    private bool _raising;

    protected EditableViewModel() => PropertyChanged += (_, _) => RaiseChanged();

    /// <summary>Raised after any edit, including edits to nested fields.</summary>
    public event EventHandler? Changed;

    /// <summary>Bubbles a child's notifications up as edits of our own.</summary>
    protected void Track(ObservableObject child) => child.PropertyChanged += (_, _) => RaiseChanged();

    /// <summary>Hook for refreshing derived properties before the edit is announced.</summary>
    protected virtual void OnEdited()
    {
    }

    private void RaiseChanged()
    {
        // OnEdited normally writes derived properties, which come straight back through
        // PropertyChanged. Without this guard a summary refresh would recurse forever.
        if (_raising) return;

        _raising = true;
        try
        {
            OnEdited();
            Changed?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            _raising = false;
        }
    }
}
