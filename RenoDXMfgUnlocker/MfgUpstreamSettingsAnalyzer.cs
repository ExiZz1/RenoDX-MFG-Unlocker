using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace RenoDXMfgUnlocker;

internal sealed record UpstreamSourceSnapshot(string Tag, string Commit, Dictionary<string,string> Files);
internal sealed record UpstreamSettingFinding(string File, string Section, string Key, string Label, string Description,
    string Source, int Line, MfgCatalogEntry? Definition, string Reason)
{
    internal string Id => File+"|"+Section+"|"+Key;
}
internal sealed record UpstreamSettingsAnalysis(UpstreamSourceSnapshot Snapshot, IReadOnlyList<UpstreamSettingFinding> Findings, bool SchemaRecognized)
{
    internal IReadOnlyList<UpstreamSettingFinding> Editable => Findings.Where(f=>f.Definition is not null).ToArray();
}
internal sealed record UpstreamSettingChange(string Id,string Kind);

/// <summary>Finite C++ declaration grammar, not a C++ evaluator. Only the reviewed standalone Option editor grants metadata.
/// Unrecognized code, readers and derived/runtime conditions never grant writing authority.</summary>
internal static class MfgUpstreamSettingsAnalyzer
{
    internal const string Prefix="src/addons/mfgunlock/";
    internal const string OptionPath=Prefix+"advanced_settings.hpp";
    private sealed record Token(string Text,int Line);
    private sealed record ArrayDeclaration(string Kind,string Name,Token[] Body,int Start,int End);
    private static readonly Regex Identifier=new(@"\A[A-Za-z][A-Za-z0-9_]*\z",RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(100));
    internal static void ValidateSnapshot(UpstreamSourceSnapshot s)
    {
        if(!Regex.IsMatch(s.Commit??"",@"\A[a-f0-9]{40}\z")||!Regex.IsMatch(s.Tag??"",@"\A[0-9]+\.[0-9]+\.[0-9]+\z")
            ||!MfgSettingsCapabilityCatalog.TryStableVersion(s.Tag,out _)||s.Files is null||s.Files.Count is <1 or >256)throw new InvalidDataException("Invalid upstream source snapshot.");
        long total=0;
        foreach(var (path,text) in s.Files){
            if(!SafeSourcePath(path)||text is null||Encoding.UTF8.GetByteCount(text)>512*1024||(total+=Encoding.UTF8.GetByteCount(text))>6*1024*1024)
                throw new InvalidDataException("Upstream source limits exceeded.");
        }
    }
    internal static bool SafeSourcePath(string path)=>path.StartsWith(Prefix,StringComparison.Ordinal)&&path.Length<=240&&!path.Contains("..")&&!path.Contains('\\')
        &&Regex.IsMatch(path,@"\A[A-Za-z0-9_./-]+\.(?:hpp|cpp|inl|h)\z",RegexOptions.CultureInvariant);
    internal static UpstreamSettingsAnalysis Analyze(UpstreamSourceSnapshot snapshot,CancellationToken cancellationToken=default)
    {
        var started=System.Diagnostics.Stopwatch.GetTimestamp();
        void Budget(){cancellationToken.ThrowIfCancellationRequested();if(System.Diagnostics.Stopwatch.GetElapsedTime(started)>TimeSpan.FromSeconds(3))throw new TimeoutException("Upstream analysis limits exceeded.");}
        Budget();
        ValidateSnapshot(snapshot);
        var findings=new List<UpstreamSettingFinding>();var recognized=false;
        if(snapshot.Files.TryGetValue(OptionPath,out var source)){
            try{
                var tokens=Lex(source);var arrays=Arrays(tokens);
                using var stream=typeof(MfgUpstreamSettingsAnalyzer).Assembly.GetManifestResourceStream("RenoDXMfgUnlocker.SettingsCatalog.upstream-option-contract.hpp")!;
                using var reader=new StreamReader(stream,Encoding.UTF8);
                var expected=Lex(reader.ReadToEnd());
                recognized=Framework(tokens,arrays)==Framework(expected,Arrays(expected));
                var choices=new Dictionary<string,string[]>(StringComparer.Ordinal);
                foreach(var array in arrays.Where(a=>a.Kind=="Choice")){
                    Budget();
                    var r=new Reader(array.Body);var values=new List<string>();
                    while(!r.Done){r.Take("{");r.Literal(false);r.Take(",");values.Add(r.Literal(true));r.Take("}");if(!r.Done)r.Take(",");}
                    if(values.Count is <1 or >32||values.Any(v=>!Regex.IsMatch(v,@"\A[^\p{Cc};#=\[\]\\/]{1,128}\z"))||values.Distinct(StringComparer.OrdinalIgnoreCase).Count()!=values.Count||!choices.TryAdd(array.Name,values.ToArray()))throw Invalid();
                }
                foreach(var array in arrays.Where(a=>a.Kind=="Option")){
                    var r=new Reader(array.Body);
                    while(!r.Done){
                        Budget();
                        var line=r.Line;r.Take("{");var section=r.Literal(true);r.Take(",");var key=r.Literal(true);r.Take(",");var label=r.Literal(false);r.Take(",");var help=r.Literal(false);r.Take(",");var fallback=r.Literal(true);r.Take(",");
                        var mode=r.Next();r.Take("(");string type;string[]? values=null;decimal? low=null,high=null;
                        if(mode=="MFGUNLOCK_CHOICES"){
                            var name=r.Next();if(!choices.TryGetValue(name,out values))throw Invalid();type=values.SequenceEqual(new[]{"0","1"})?"Boolean":"Enum";if(type=="Boolean")values=null;
                        }else if(mode=="MFGUNLOCK_NUMBER"){type="Integer";low=r.Integer();r.Take(",");high=r.Integer();}else throw Invalid();
                        r.Take(")");r.Take("}");if(!r.Done)r.Take(",");
                        var url=Evidence(snapshot.Commit,OptionPath,line);
                        // Conservative confirmed floor, not a claim of first introduction. Authority is scoped to this release.
                        var v=Version.Parse(snapshot.Tag);var upper=new Version(v.Major,v.Minor,v.Build,1).ToString();
                        var entry=new MfgCatalogEntry("MFGAdaUnlock.ini",section,key,type,fallback,snapshot.Tag,upper,"Advanced",help,url,true,Values:values,Minimum:low,Maximum:high,Requires:[]);
                        if(!Regex.IsMatch(section,@"\A[A-Za-z][A-Za-z0-9_.]{0,79}\z")||!Regex.IsMatch(key,@"\A[A-Za-z][A-Za-z0-9_.]{0,79}\z")
                            ||label.Length is <1 or >120||help.Length is <1 or >2000||label.Any(char.IsControl)||help.Any(char.IsControl)
                            ||low>high||low < -1000000000||high>1000000000||MfgDynamicSettingsCatalog.Canonical(entry,fallback) is null)throw Invalid();
                        var eligible=recognized&&!section.StartsWith("XeFG.Learned",StringComparison.OrdinalIgnoreCase);
                        findings.Add(new("MFGAdaUnlock.ini",section,key,label,help,OptionPath,line,eligible?entry:null,eligible?"Standalone upstream editor; no INI prerequisite declared. Runtime applicability is not inferred.":"Source editor framework changed or setting is internal; read-only."));
                    }
                }
                if(findings.Count>128||findings.GroupBy(f=>f.Id,StringComparer.OrdinalIgnoreCase).Any(g=>g.Count()>1))throw Invalid();
            }catch(Exception ex)when(ex is InvalidDataException or FormatException or OverflowException or ArgumentException){
                recognized=false;findings.Clear();findings.Add(new("?","?","?","Unrecognized declaration schema","",OptionPath,1,null,"C++ declarations were not unambiguously recognized; no write authority."));
            }
        }
        // Literal readers are discoveries only: path, nonliteral defaults, ranges and dependencies cannot be guessed.
        foreach(var (path,text) in snapshot.Files){
            Budget();
            foreach(Match m in Regex.Matches(text,@"GetPrivateProfile(?:Int|String)W\s*\(\s*L""(?<s>[A-Za-z][A-Za-z0-9_.]*)""\s*,\s*L""(?<k>[A-Za-z][A-Za-z0-9_.]*)""",RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(150))){
                Budget();
                var section=m.Groups["s"].Value;var key=m.Groups["k"].Value;
                if(findings.Any(f=>f.Section==section&&f.Key==key))continue;
                if(findings.Count>=512)throw new InvalidDataException("Too many discovered upstream readers.");
                findings.Add(new("Unresolved",section,key,key,"",path,1+text[..m.Index].Count(c=>c=='\n'),null,
                    section.StartsWith("XeFG.Learned",StringComparison.OrdinalIgnoreCase)?"Learned/internal state; never editable.":"Literal reader only; file/type/range/dependencies require review."));
            }
            if(Regex.IsMatch(text,@"kConfigSection\s*=\s*""RenoDX\.MFGUnlock""",RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(100))){
                foreach(Match m in Regex.Matches(text,@"reshade::get_config_value\s*\(\s*nullptr\s*,\s*kConfigSection\s*,\s*""(?<k>[A-Za-z][A-Za-z0-9_.]*)""",RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(150))){
                    Budget();
                    var key=m.Groups["k"].Value;if(findings.Any(f=>f.File=="ReShade.ini"&&f.Key==key))continue;
                    if(findings.Count>=512)throw new InvalidDataException("Too many discovered upstream readers.");
                    findings.Add(new("ReShade.ini","RenoDX.MFGUnlock",key,key,"",path,1+text[..m.Index].Count(c=>c=='\n'),null,"ReShade reader only; defaults/type/dependencies require review. No write authority."));
                }
            }
        }
        return new(snapshot,findings,recognized);
    }
    internal static string Evidence(string commit,string path,int line)=>"https://github.com/mavismmg/MFGAdaUnlock-RenoDx/blob/"+commit+"/"+path+"#L"+line;
    internal static string CompareKnown(UpstreamSettingFinding f,MfgDynamicSettingsCatalog baseline,string version)
    {
        if(f.File is not ("ReShade.ini" or "MFGAdaUnlock.ini"))return "Reader only; origin/semantics unresolved";
        var service=new MfgUnlockSettingsService(f.File=="MFGAdaUnlock.ini",baseline);
        var old=service.DefinitionsFor(version).FirstOrDefault(d=>d.Key.Equals(f.Key,StringComparison.OrdinalIgnoreCase));
        if(old is null)return f.Definition is null?"Read-only":"New declaration candidate";
        if(f.Definition is not {} e)return "Existing definition retained; source metadata unresolved";
        var values=e.Type=="Boolean"?new[]{"0","1"}:e.Values??[];
        var same=service.SectionFor(old)==e.Section&&old.DefaultValue==e.Default&&old.Choices.Select(c=>c.Value).SequenceEqual(values)
            &&(old.CatalogEntry is not {} previous||previous.Type==e.Type&&previous.Minimum==e.Minimum&&previous.Maximum==e.Maximum);
        return same?"Existing definition retained":"Metadata conflict; existing definition retained; manual review required";
    }
    internal static IReadOnlyList<UpstreamSettingChange> Compare(UpstreamSettingsAnalysis before,UpstreamSettingsAnalysis after)
    {
        var result=new List<UpstreamSettingChange>();
        var a=before.Findings.Where(f=>f.Key!="?").ToDictionary(f=>f.Id,StringComparer.OrdinalIgnoreCase);
        var b=after.Findings.Where(f=>f.Key!="?").ToDictionary(f=>f.Id,StringComparer.OrdinalIgnoreCase);
        var paired=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(var old in a.Values.Where(f=>f.File=="Unresolved")){
            var resolved=b.Values.Where(f=>f.File!="Unresolved"&&f.Section==old.Section&&f.Key==old.Key&&!a.ContainsKey(f.Id)).ToArray();
            if(resolved.Length==1){paired.Add(old.Id);paired.Add(resolved[0].Id);result.Add(new(resolved[0].Id,"Declaration matches earlier untyped reader; not proof of a new setting"));}
        }
        foreach(var id in a.Keys.Union(b.Keys,StringComparer.OrdinalIgnoreCase)){
            if(paired.Contains(id))continue;
            if(!a.TryGetValue(id,out var old)){result.Add(new(id,"New declaration/reader"));continue;}
            if(!b.TryGetValue(id,out var current)){result.Add(new(id,"No longer found (not proof of removal)"));continue;}
            var x=old.Definition;var y=current.Definition;
            if(x is null||y is null){if((x is null)!=(y is null))result.Add(new(id,"Metadata availability changed"));continue;}
            if(x.Type!=y.Type)result.Add(new(id,"Type changed"));
            if(x.Default!=y.Default)result.Add(new(id,"Default changed"));
            if(x.Minimum!=y.Minimum||x.Maximum!=y.Maximum)result.Add(new(id,"Range changed"));
            if(!(x.Values??[]).SequenceEqual(y.Values??[]))result.Add(new(id,"Enum values changed"));
            if(!(x.Requires??[]).SequenceEqual(y.Requires??[]))result.Add(new(id,"Declared dependencies changed"));
        }
        if(before.SchemaRecognized&&!after.SchemaRecognized)result.Add(new("*","Framework/dependency semantics unresolved; all new declarations read-only"));
        return result;
    }
    private static Token[] Lex(string source)
    {
        var result=new List<Token>();var i=0;var line=1;
        while(i<source.Length){
            if(char.IsWhiteSpace(source[i])){if(source[i++]=='\n')line++;continue;}
            if(i+1<source.Length&&source[i]=='/'&&source[i+1]=='/'){while(i<source.Length&&source[i]!='\n')i++;continue;}
            if(i+1<source.Length&&source[i]=='/'&&source[i+1]=='*'){i+=2;while(i+1<source.Length&&!(source[i]=='*'&&source[i+1]=='/')){if(source[i++]=='\n')line++;}if(i+1>=source.Length)throw Invalid();i+=2;continue;}
            var start=i;var tokenLine=line;
            if(source[i]=='"'||source[i]=='L'&&i+1<source.Length&&source[i+1]=='"'){
                if(source[i]=='L')i++;i++;
                while(i<source.Length&&source[i]!='"'){if(source[i]=='\n'||source[i]=='\r')throw Invalid();if(source[i]=='\\'){i++;if(i>=source.Length||source[i] is not ('"' or '\\'))throw Invalid();}i++;}
                if(i>=source.Length)throw Invalid();i++;
            }else if(char.IsLetterOrDigit(source[i])||source[i]=='_'){while(i<source.Length&&(char.IsLetterOrDigit(source[i])||source[i]=='_'))i++;}
            else i++;
            result.Add(new(source[start..i],tokenLine));if(result.Count>40000)throw Invalid();
        }
        return result.ToArray();
    }
    private static List<ArrayDeclaration> Arrays(Token[] tokens)
    {
        var result=new List<ArrayDeclaration>();
        for(var i=0;i+7<tokens.Length;i++){
            if(tokens[i].Text!="inline"||tokens[i+1].Text!="constexpr"||tokens[i+2].Text is not ("Choice" or "Option"))continue;
            if(!Identifier.IsMatch(tokens[i+3].Text)||tokens[i+4].Text!="["||tokens[i+5].Text!="]"||tokens[i+6].Text!="{")throw Invalid();
            var end=i+7;var depth=1;
            while(end<tokens.Length&&depth>0){if(tokens[end].Text=="{")depth++;if(tokens[end].Text=="}")depth--;if(depth>0)end++;}
            if(depth!=0||end+1>=tokens.Length||tokens[end+1].Text!=";")throw Invalid();
            result.Add(new(tokens[i+2].Text,tokens[i+3].Text,tokens[(i+7)..end],i,end+2));i=end+1;
        }
        return result;
    }
    private static string Framework(Token[] tokens,List<ArrayDeclaration> arrays)
    {
        var excluded=arrays.SelectMany(a=>Enumerable.Range(a.Start,a.End-a.Start)).ToHashSet();
        return string.Join('\n',tokens.Where((_,i)=>!excluded.Contains(i)).Select(t=>t.Text));
    }
    private sealed class Reader(Token[] tokens)
    {
        private int index;
        internal bool Done=>index==tokens.Length;
        internal int Line=>!Done?tokens[index].Line:1;
        internal string Next()=>!Done?tokens[index++].Text:throw Invalid();
        internal void Take(string text){if(Next()!=text)throw Invalid();}
        internal string Literal(bool wide){var s=Next();if(wide?!s.StartsWith("L\"",StringComparison.Ordinal):!s.StartsWith('"'))throw Invalid();return JsonSerializer.Deserialize<string>(wide?s[1..]:s)??throw Invalid();}
        internal int Integer(){var s=Next();if(s=="-")s+=Next();return int.Parse(s,NumberStyles.AllowLeadingSign,CultureInfo.InvariantCulture);}
    }
    private static InvalidDataException Invalid()=>new("Unsupported C++ option declaration.");
}
