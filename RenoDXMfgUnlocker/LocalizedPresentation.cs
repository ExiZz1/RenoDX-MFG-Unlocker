using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;

namespace RenoDXMfgUnlocker;

/// <summary>Localizes display properties while preserving original values, bindings and service identifiers.</summary>
public static class LocalizedPresentation
{
    private sealed class Entry
    {
        public Dictionary<DependencyProperty,string> Originals {get;}=[];
        public List<(DependencyPropertyDescriptor Descriptor,EventHandler Handler)> Subscriptions {get;}=[];
        public bool Updating;
        public EventHandler? GeneratorHandler;
        public EventHandler? PopupHandler;
    }
    private static readonly ConditionalWeakTable<DependencyObject,Entry> entries=new();
    private static readonly List<WeakReference<FrameworkElement>> live=[];
    public static UiLocalization Language {get;private set;}=UiLocalization.Current;
    private static bool initialized;
    private static readonly DataTemplate wrappedText=CreateWrappedText(TextAlignment.Center);
    private static readonly DataTemplate wrappedToggleText=CreateWrappedText(TextAlignment.Left);
    private static DataTemplate CreateWrappedText(TextAlignment alignment)
    {
        var text=new FrameworkElementFactory(typeof(TextBlock));text.SetBinding(TextBlock.TextProperty,new Binding());text.SetValue(TextBlock.TextWrappingProperty,TextWrapping.Wrap);text.SetValue(TextBlock.TextAlignmentProperty,alignment);
        return new DataTemplate {VisualTree=text};
    }
    public static readonly DependencyProperty PreserveOriginalProperty=DependencyProperty.RegisterAttached("PreserveOriginal",typeof(bool),typeof(LocalizedPresentation),new FrameworkPropertyMetadata(false,FrameworkPropertyMetadataOptions.Inherits));
    public static void SetPreserveOriginal(DependencyObject obj,bool value)=>obj.SetValue(PreserveOriginalProperty,value);
    public static bool GetPreserveOriginal(DependencyObject obj)=>(bool)obj.GetValue(PreserveOriginalProperty);
    public static void Initialize(UiLocalization language)
    {
        if(initialized)return;initialized=true;Language=language;
        EventManager.RegisterClassHandler(typeof(FrameworkElement),FrameworkElement.LoadedEvent,new RoutedEventHandler(Loaded),true);
        EventManager.RegisterClassHandler(typeof(FrameworkElement),FrameworkElement.UnloadedEvent,new RoutedEventHandler(Unloaded),true);
        language.Changed+=(_,_)=>RefreshAll();
    }
    private static void Loaded(object sender,RoutedEventArgs args)
    {
        if(sender is not FrameworkElement root)return;
        VisitTree(root);
        // Virtualized item templates may be generated after the owner's Loaded traversal.
        if(root is Window or ItemsControl)QueueVisit(root);
    }
    private static void VisitTree(FrameworkElement root){Attach(root);void Visit(DependencyObject parent){for(int i=0;i<System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent);i++){var child=System.Windows.Media.VisualTreeHelper.GetChild(parent,i);if(child is FrameworkElement item)Attach(item);Visit(child);}}Visit(root);}
    private static void QueueVisit(FrameworkElement root)=>root.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ContextIdle,new Action(()=>{if(root.IsLoaded){root.UpdateLayout();VisitTree(root);}}));
    private static void Attach(FrameworkElement element)
    {
        if(GetPreserveOriginal(element))return;
        if(element is ButtonBase {Content:string content,ContentTemplate:null,ContentTemplateSelector:null} button && content.Length>2)button.SetCurrentValue(ContentControl.ContentTemplateProperty,button is ToggleButton?wrappedToggleText:wrappedText);
        var entry=entries.GetOrCreateValue(element);
        if(entry.Subscriptions.Count>0)return;
        if(element is ItemsControl items){entry.GeneratorHandler=(_,_)=>QueueVisit(items);items.ItemContainerGenerator.StatusChanged+=entry.GeneratorHandler;}
        if(element is ComboBox combo){entry.PopupHandler=(_,_)=>{if(combo.Template?.FindName("PART_Popup",combo) is Popup {Child:FrameworkElement child})QueueVisit(child);};combo.DropDownOpened+=entry.PopupHandler;}
        var properties=new List<DependencyProperty>{FrameworkElement.ToolTipProperty};
        if(element is TextBlock)properties.Add(TextBlock.TextProperty);
        if(element is TextBox {IsReadOnly:true})properties.Add(TextBox.TextProperty);
        if(element is ContentControl)properties.Add(ContentControl.ContentProperty);
        if(element is Window)properties.Add(Window.TitleProperty);
        foreach(var property in properties){
            var binding=BindingOperations.GetBindingBase(element,property);
            var mode=binding switch {Binding b=>b.Mode,MultiBinding b=>b.Mode,_=>BindingMode.OneWay};
            if(mode is BindingMode.TwoWay or BindingMode.OneWayToSource || mode==BindingMode.Default && property.GetMetadata(element.GetType()) is FrameworkPropertyMetadata {BindsTwoWayByDefault:true})continue;
            var descriptor=DependencyPropertyDescriptor.FromProperty(property,element.GetType());if(descriptor is null)continue;
            EventHandler handler=(_,_)=>{if(entry.Updating)return;if(element.GetValue(property) is string original){entry.Originals[property]=original;Apply(element,entry,property,original);}};
            descriptor.AddValueChanged(element,handler);entry.Subscriptions.Add((descriptor,handler));
            if(!entry.Originals.TryGetValue(property,out var source) && element.GetValue(property) is string value)entry.Originals[property]=source=value;
            if(source is not null)Apply(element,entry,property,source);
        }
        live.Add(new(element));
    }
    private static void Unloaded(object sender,RoutedEventArgs args)
    {
        if(sender is not FrameworkElement root)return;
        Detach(root);
        void Visit(DependencyObject parent){for(int i=0;i<System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent);i++){var child=System.Windows.Media.VisualTreeHelper.GetChild(parent,i);if(child is FrameworkElement item)Detach(item);Visit(child);}}
        Visit(root);
    }
    private static void Detach(FrameworkElement element)
    {
        if(!entries.TryGetValue(element,out var entry))return;
        foreach(var (descriptor,handler) in entry.Subscriptions)descriptor.RemoveValueChanged(element,handler);
        entry.Subscriptions.Clear();
        if(element is ItemsControl items && entry.GeneratorHandler is not null){items.ItemContainerGenerator.StatusChanged-=entry.GeneratorHandler;entry.GeneratorHandler=null;}
        if(element is ComboBox combo && entry.PopupHandler is not null){combo.DropDownOpened-=entry.PopupHandler;entry.PopupHandler=null;}
        // Restore the source on detach: a rebound control must not retain an old-language value.
        entry.Updating=true;try{foreach(var pair in entry.Originals)element.SetCurrentValue(pair.Key,pair.Value);}finally{entry.Updating=false;entry.Originals.Clear();}
    }
    private static void Apply(FrameworkElement element,Entry entry,DependencyProperty property,string source)
    {
        entry.Updating=true;
        try {element.SetCurrentValue(property,Language.Translate(source));}
        finally {entry.Updating=false;}
    }
    public static void RefreshAll()
    {
        if(Application.Current is {} application)foreach(Window window in application.Windows)if(window.IsLoaded)VisitTree(window);
        for(var i=live.Count-1;i>=0;i--){
            if(!live[i].TryGetTarget(out var element) || !element.IsLoaded){live.RemoveAt(i);continue;}
            if(entries.TryGetValue(element,out var entry))foreach(var (property,source) in entry.Originals.ToArray())Apply(element,entry,property,source);
        }
    }
    public static void RefreshGeneratedItems(FrameworkElement owner)=>QueueVisit(owner);
    public static string SourceText(FrameworkElement element,DependencyProperty property)=>entries.TryGetValue(element,out var entry) && entry.Originals.TryGetValue(property,out var source)?source:element.GetValue(property) as string??"";
}
