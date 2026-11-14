using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text.Json;
using RenoDXMfgUnlocker;

// Research-only entry point. Never referenced by the application or the normal regression runner.
internal static class ReShadeCliResearch
{
    internal static int Run(string[] only)
    {
        var installer=Environment.GetEnvironmentVariable("RENODX_TEST_RESHADE_INSTALLER")??throw new Exception("Reviewed fixture required");
        new ReShadeInstallerVerifier().Verify(installer);
        var root=Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(),"..","..","work","ReShadeCli-alpha4",Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(root);var evidence=InspectBinary(installer);var runs=new List<object>();
        var source=Environment.ProcessPath!;
        var cases=new[]{("fresh-d3d11","d3d11",false,false,false,false),("unicode-d3d12","d3d12",true,false,false,false),("foreign-install","d3d12",false,true,false,false),("foreign-forced-update","d3d12",false,true,true,false),("locked-forced-update","d3d12",false,true,true,true),("spaces-d3d12","d3d12",false,false,false,false),("reviewed-update","d3d12",false,false,true,false)};
        foreach(var(name,api,unicode,foreign,update,locked) in cases)
        {
            if(only.Length>0&&!only.Contains(name,StringComparer.Ordinal))continue;
            var dir=Path.Combine(root,unicode?"Ігрова тека Ω é & spaces":name=="spaces-d3d12"?"ASCII space & ampersand (own)":name);Directory.CreateDirectory(dir);var exe=Path.Combine(dir,"Own-"+Guid.NewGuid().ToString("N")+".exe");File.Copy(source,exe);
            var proxy=Path.Combine(dir,api+".dll");if(foreign)File.Copy(source,proxy);
            var ini=Path.Combine(dir,"ReShade.ini");var preset=Path.Combine(dir,"OwnPreset.ini");string? iniBefore=null,presetBefore=null;
            if(name=="reviewed-update"){File.Copy(Environment.GetEnvironmentVariable("RENODX_TEST_RESHADE_FULL_DLL")??throw new Exception("Genuine existing runtime fixture required"),proxy);File.WriteAllText(ini,"; own comment\n[GENERAL]\nPresetPath=OwnPreset.ini\n[Own]\nKeep=7\n");File.WriteAllText(preset,"; own preset\n[Own]\nKeep=42\n");iniBefore=Hash(ini);presetBefore=Hash(preset);}
            var original=File.Exists(proxy)?Hash(proxy):null;
            var product=foreign?FileVersionInfo.GetVersionInfo(proxy).ProductName:null;
            if(foreign&&(string.IsNullOrEmpty(product)||product=="ReShade"))throw new Exception("Synthetic foreign metadata fixture invalid");
            FileStream? held=locked?new(proxy,FileMode.Open,FileAccess.Read,FileShare.Read):null;
            var start=new ProcessStartInfo(installer){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,WorkingDirectory=dir,RedirectStandardOutput=true,RedirectStandardError=true};
            start.ArgumentList.Add(exe);start.ArgumentList.Add("--api");start.ArgumentList.Add(api);start.ArgumentList.Add("--headless");if(update){start.ArgumentList.Add("--state");start.ArgumentList.Add("update");}
            new ReShadeInstallerVerifier().Verify(installer);
            using var process=Process.Start(start)!;var stdout=process.StandardOutput.ReadToEndAsync();var stderr=process.StandardError.ReadToEndAsync();bool timeout=!process.WaitForExit(30000);if(timeout){process.Kill(true);process.WaitForExit();}held?.Dispose();
            var after=File.Exists(proxy)?Hash(proxy):null;string machine="missing";
            if(File.Exists(proxy)){using var f=File.OpenRead(proxy);using var pe=new PEReader(f);machine=pe.PEHeaders.CoffHeader.Machine.ToString();}
            using var input=File.OpenRead(exe);using var inputPe=new PEReader(input);
            runs.Add(new{Name=name,Arguments=start.ArgumentList.ToArray(),ExitCode=process.ExitCode,Timeout=timeout,ForeignProduct=product,OriginalSha256=original,AfterSha256=after,Machine=machine,InputMachine=inputPe.PEHeaders.CoffHeader.Machine.ToString(),IniBefore=iniBefore,IniAfter=File.Exists(ini)?Hash(ini):null,PresetBefore=presetBefore,PresetAfter=File.Exists(preset)?Hash(preset):null,Files=Directory.GetFiles(dir).Select(Path.GetFileName).ToArray(),Stdout=stdout.GetAwaiter().GetResult(),Stderr=stderr.GetAwaiter().GetResult()});
            Console.WriteLine($"RESEARCH {name}: exit {process.ExitCode}, timeout {timeout}, proxy changed {original!=after}, machine {machine}");
        }
        var record=new{InstallerSha256=Hash(installer),SourceTag="v6.8.0",SystemAnsiCodePage=ReShadeSetupPathPolicy.SystemCodePage,Binary=evidence,Runs=runs,Root=root,NoVulkanArguments=true,NoGameExecuted=true,Mode="Research-only headless behavioral probes, not production or visual acceptance"};
        var output=Path.GetFullPath(Path.Combine(root,"..",only.Length==0?"alpha4-cli-evidence.json":"alpha4-cli-additional.json"));File.WriteAllText(output,JsonSerializer.Serialize(record,new JsonSerializerOptions{WriteIndented=true}));Console.WriteLine("Evidence: "+output);return 0;
    }
    private static string Hash(string path)=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    internal static object InspectBinary(string path)
    {
        using var stream=File.OpenRead(path);using var pe=new PEReader(stream);var md=pe.GetMetadataReader();var methods=new Dictionary<string,object>();
        var opcodes=typeof(OpCodes).GetFields(BindingFlags.Public|BindingFlags.Static).Where(f=>f.FieldType==typeof(OpCode)).Select(f=>(OpCode)f.GetValue(null)!).ToDictionary(o=>unchecked((ushort)o.Value));
        foreach(var th in md.TypeDefinitions){var td=md.GetTypeDefinition(th);if(md.GetString(td.Name)!="MainWindow")continue;foreach(var mh in td.GetMethods())
        {
            var m=md.GetMethodDefinition(mh);var name=md.GetString(m.Name);if(name is not (".ctor" or "InstallStep_CheckExistingInstallation" or "RestartWithElevatedPrivileges" or "InstallStep_InstallReShadeModule" or "UpdateStatusAndFinish")&&!name.StartsWith("<.ctor>",StringComparison.Ordinal))continue;
            var bytes=pe.GetMethodBody(m.RelativeVirtualAddress).GetILBytes()!;var strings=new List<string>();var calls=new List<string>();
            for(int i=0;i<bytes.Length;){ushort code=bytes[i++];if(code==0xfe)code=(ushort)(0xfe00|bytes[i++]);var op=opcodes[code];var token=i+4<=bytes.Length?BitConverter.ToInt32(bytes,i):0;
                if(op==OpCodes.Ldstr)strings.Add(md.GetUserString(MetadataTokens.UserStringHandle(token&0xffffff)));
                if(op.OperandType==OperandType.InlineMethod){var handle=MetadataTokens.EntityHandle(token);if(handle.Kind==HandleKind.MethodDefinition)calls.Add(md.GetString(md.GetMethodDefinition((MethodDefinitionHandle)handle).Name));else if(handle.Kind==HandleKind.MemberReference)calls.Add(md.GetString(md.GetMemberReference((MemberReferenceHandle)handle).Name));}
                i+=op.OperandType switch{OperandType.InlineNone=>0,OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar=>1,OperandType.InlineVar=>2,OperandType.InlineI8 or OperandType.InlineR=>8,OperandType.InlineSwitch=>4+4*BitConverter.ToInt32(bytes,i),_=>4};
            }
            methods[name]=new{Strings=strings,Calls=calls,ILSha256=Convert.ToHexString(SHA256.HashData(bytes))};
        }}
        var marshaling=new List<string>();foreach(var th in md.TypeDefinitions){var td=md.GetTypeDefinition(th);if(md.GetString(td.Name)!="PEInfo")continue;foreach(var mh in td.GetMethods()){var m=md.GetMethodDefinition(mh);if(md.GetString(m.Name)!="MapAndLoad")continue;foreach(var ph in m.GetParameters()){var p=md.GetParameter(ph);var descriptor=p.GetMarshallingDescriptor();if(!descriptor.IsNil)marshaling.Add(md.GetString(p.Name)+":"+Convert.ToHexString(md.GetBlobBytes(descriptor)));}}}
        return new{Mvid=md.GetGuid(md.GetModuleDefinition().Mvid),Methods=methods,NativeMarshaling=marshaling};
    }
}
