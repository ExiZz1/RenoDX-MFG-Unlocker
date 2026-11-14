using System.IO;
using RenoDXMfgUnlocker;

internal static class WizardChecks
{
    public static IEnumerable<(string Name,Action Run)> All(){
        yield return ("wizard navigation prevents skipped steps and active-operation jumps",Navigation);
        yield return ("wizard continue requires current game target inputs package and approval",Continue);
        yield return ("wizard filters reflect exact installation entries without mutating them",Filters);
        yield return ("MFG settings 1.7 supports gated RTX50 key and preserves other bytes",Settings17);
        yield return ("Intel Vulkan API version gate rejects old unknown and invalid modes",ApiGate);
    }
    private static void Check(bool value,string message){if(!value)throw new Exception(message);}
    private static void Refuses(Action action){try{action();}catch(InvalidDataException){return;}catch(InvalidOperationException){return;}throw new Exception("Expected rejection");}
    private static void Navigation(){for(var current=0;current<6;current++)for(var requested=-1;requested<8;requested++){Check(SetupWizardView.CanVisit(current,requested,false)==(requested>=0&&requested<=current&&requested<6),"Skipped safety step");Check(!SetupWizardView.CanVisit(current,requested,true),"Jump while busy");}}
    private static void Continue(){
        Check(!SetupWizardView.CanContinue(0,false,false,true,true,true),"Missing game accepted");Check(!SetupWizardView.CanContinue(2,false,true,false,true,true),"Missing target accepted");Check(!SetupWizardView.CanContinue(3,false,true,true,false,true),"No package accepted");Check(!SetupWizardView.CanContinue(4,false,true,true,true,false),"No approval accepted");Check(!SetupWizardView.CanContinue(4,true,true,true,true,true),"Busy accepted");Check(SetupWizardView.CanContinue(4,false,true,true,true,true),"Approved preview disabled");Check(!SetupWizardView.CanContinue(5,false,true,true,true,true),"Completion repeats install");
    }
    private static void Filters(){
        Check(SetupWizardView.DisplayVersion("","test.dll")=="Unknown version"&&SetupWizardView.DisplayVersion("","LICENSE.txt")=="Not applicable","Empty version presentation ambiguous");
        var row=new StreamlineComponentPlanEntry("a.dll","source","target","2","hash","1","old","Replace existing",true,false,"snapshot");var rows=new[]{row,row with {FileName="b.dll",Action="New file",ExistingSha256=null},row with{FileName="c.dll",IsSelected=false,Sha256="old"},row with{FileName="d.dll",IsSelected=false},row with{FileName="e.dll",Action="Blocked",IsSelected=false}};
        Check(SetupWizardView.Filter(rows,"All Files").Count()==5,"All count");Check(SetupWizardView.Filter(rows,"Replace").Single().FileName=="a.dll","Wrong replacement");Check(SetupWizardView.Filter(rows,"Add").Count()==1,"Add count");Check(SetupWizardView.Filter(rows,"Unchanged").Count()==1,"Unchanged count");Check(SetupWizardView.Filter(rows,"Blocked / Skipped").Count()==2,"Blocked/skipped hidden");Check(rows[0]==row,"Presentation mutated plan");
    }
    private static void Settings17(){
        var root=Path.Combine(Path.GetTempPath(),"RenoDX-rc7-settings-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);var path=Path.Combine(root,"ReShade.ini");var input="; keep\r\n[Other]\r\nKey=7\r\n[RenoDX.MFGUnlock]\r\nUnknown=9 ; keep\r\nEnabled=1\r\n";File.WriteAllText(path,input);var service=new MfgUnlockSettingsService();var current=service.Load(path,root,"1.7.0.0");Check(current.IsSupportedAddonVersion&&current.Settings.Count==7,"1.7 key set wrong");Check(current.Settings.Single(x=>x.Definition.Key=="Rtx50QualityFixes").EffectiveValue=="0","RTX50 default changed");var bytes=service.CreateUpdatedBytes(current,new Dictionary<string,string>{{"Rtx50QualityFixes","1"}});Check(System.Text.Encoding.UTF8.GetString(bytes).Contains("Unknown=9 ; keep\r\nEnabled=1\r\n"),"Other bytes changed");Check(File.ReadAllText(path)==input,"Preview wrote INI");Refuses(()=>service.CreateUpdatedBytes(current,new Dictionary<string,string>{{"Rtx50QualityFixes","2"}}));var old=service.Load(path,root,"1.6.0.0");Check(old.Settings.Count==6,"1.6 definitions expanded");Refuses(()=>service.CreateUpdatedBytes(old,new Dictionary<string,string>{{"Rtx50QualityFixes","1"}}));Check(!service.Load(path,root,"1.7.0-beta.1").IsSupportedAddonVersion,"Unknown version unlocked");
    }
    private static void ApiGate(){Check(IntelRuntimeService.SupportsIntegration("1.7.0.0",IntelIntegration.VulkanExperimental),"1.7 Vulkan gate absent");Check(!IntelRuntimeService.SupportsIntegration("1.6.0.0",IntelIntegration.VulkanExperimental),"Old Vulkan allowed");Check(!IntelRuntimeService.SupportsIntegration("1.7.0-beta.1",IntelIntegration.VulkanExperimental),"Unknown allowed");Check(!IntelRuntimeService.SupportsIntegration("1.7.0.0",(IntelIntegration)99),"Invalid enum allowed");Check(IntelRuntimeService.SupportsIntegration("1.5.8.0",IntelIntegration.D3D12),"Old D3D12 regressed");}
}
