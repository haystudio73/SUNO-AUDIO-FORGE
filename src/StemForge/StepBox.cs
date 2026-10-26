using System.Windows;
using System.Windows.Controls;

namespace StemForge;

/// <summary>A persistent section title, collapsible body and small footer toggle.</summary>
public sealed class StepBox : ContentControl
{
    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(nameof(Title), typeof(string), typeof(StepBox), new PropertyMetadata(""));
    public static readonly DependencyProperty StepIdProperty = DependencyProperty.Register(nameof(StepId), typeof(string), typeof(StepBox), new PropertyMetadata(""));
    public static readonly DependencyProperty IsExpandedProperty = DependencyProperty.Register(nameof(IsExpanded), typeof(bool), typeof(StepBox),
        new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
            (d,e) => ((StepBox)d).RaiseEvent(new RoutedEventArgs(ExpansionChangedEvent))));
    public static readonly RoutedEvent ExpansionChangedEvent = EventManager.RegisterRoutedEvent(nameof(ExpansionChanged), RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(StepBox));
    public event RoutedEventHandler ExpansionChanged { add => AddHandler(ExpansionChangedEvent,value); remove => RemoveHandler(ExpansionChangedEvent,value); }
    public string Title { get => (string)GetValue(TitleProperty); set => SetValue(TitleProperty,value); }
    public string StepId { get => (string)GetValue(StepIdProperty); set => SetValue(StepIdProperty,value); }
    public bool IsExpanded { get => (bool)GetValue(IsExpandedProperty); set => SetValue(IsExpandedProperty,value); }
}
