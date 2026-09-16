using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.VisualTree;

namespace Crapnet.App.Controls;

/// <summary>
/// A row of mutually exclusive buttons, used for the direction and protocol pickers.
/// </summary>
/// <remarks>
/// Built on <see cref="SelectingItemsControl"/> so that binding is the same single
/// <c>SelectedItem</c> a ComboBox would give, while every option stays visible. For two to four
/// glyph-sized choices that is both faster to hit and less text on screen than a drop-down.
/// </remarks>
public class SegmentedControl : SelectingItemsControl
{
    private static readonly FuncTemplate<Panel?> DefaultPanel =
        new(() => new StackPanel { Orientation = Orientation.Horizontal });

    static SegmentedControl()
    {
        ItemsPanelProperty.OverrideDefaultValue<SegmentedControl>(DefaultPanel);
        SelectionModeProperty.OverrideDefaultValue<SegmentedControl>(SelectionMode.AlwaysSelected);
    }

    protected override Type StyleKeyOverride => typeof(SegmentedControl);

    protected override Control CreateContainerForItemOverride(object? item, int index, object? recycleKey)
        => new SegmentedItem();

    protected override bool NeedsContainerOverride(object? item, int index, out object? recycleKey)
    {
        recycleKey = null;
        return item is not SegmentedItem;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        if (e.Handled || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;

        // Walk up rather than trusting e.Source: the press usually lands on the item's TextBlock.
        var container = (e.Source as Visual)?.FindAncestorOfType<SegmentedItem>(includeSelf: true);
        if (container is null) return;

        var index = IndexFromContainer(container);
        if (index < 0) return;

        SelectedIndex = index;
        e.Handled = true;
    }
}

/// <summary>One option inside a <see cref="SegmentedControl"/>.</summary>
public class SegmentedItem : ListBoxItem
{
    protected override Type StyleKeyOverride => typeof(SegmentedItem);
}
