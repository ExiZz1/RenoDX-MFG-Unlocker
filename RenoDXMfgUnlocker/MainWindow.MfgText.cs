using System.Windows.Controls;

namespace RenoDXMfgUnlocker;

public partial class MainWindow
{
    private readonly Dictionary<TextBlock,Func<UiLocalization,string>> mfgTextRenderers=[];
    private readonly Dictionary<TextBlock,string> mfgTextKeys=[];
    private void SetMfgText(TextBlock element,string key,params object?[] arguments){SetMfgText(element,l=>l.Format(key,arguments));mfgTextKeys[element]=key;}
    private void SetMfgText(TextBlock element,Func<UiLocalization,string> render)
    {
        mfgTextRenderers[element]=render;
        mfgTextKeys.Remove(element);
        element.Text=render(LocalizedPresentation.Language);
    }
    private bool HasMfgText(TextBlock element,string key)=>mfgTextKeys.GetValueOrDefault(element)==key;
    private void RefreshMfgText(){foreach(var item in mfgTextRenderers)item.Key.Text=item.Value(LocalizedPresentation.Language);}
}
