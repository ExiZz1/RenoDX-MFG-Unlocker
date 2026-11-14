namespace RenoDXMfgUnlocker;

/// <summary>Presentation state only; Apply still delegates all validation to the existing installer.</summary>
internal static class NvidiaManualWorkflow
{
    internal const string Steps="1. Select DLL files → 2. Verify selected DLLs → 3. Apply verified changes";
    internal const string Changed="Selection changed. Verify selected DLLs before installation.";
    internal static string DisabledReason(NvidiaManualPlan? plan,bool contextMatches,bool busy)=>busy?"An operation is in progress.":plan is null?Changed:!contextMatches?"The verified plan is outdated. Verify selected DLLs again.":plan.Entries.Any(e=>e.Action=="Blocked")?"Resolve blocked files and verify selected DLLs again.":!plan.Entries.Any(e=>e.IsSelected)?"No applicable changes in the verified plan.":!plan.Acknowledged?"Review the verified plan and confirm the risk acknowledgement.":!plan.CanApply?"No applicable changes in the verified plan.":"Verified changes are ready for explicit confirmation.";
}
