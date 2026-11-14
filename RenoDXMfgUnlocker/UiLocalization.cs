using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace RenoDXMfgUnlocker;

/// <summary>Display-only resources. Never localize transaction/status identifiers in service models.</summary>
public sealed class UiLocalization
{
    public static readonly string[] Cultures=["en-US","uk-UA","pl-PL","de-DE","es-ES","fr-FR","cs-CZ","pt-BR"];
    public static readonly string[] LanguageNames=["English","Українська","Polski","Deutsch","Español","Français","Čeština","Português (Brasil)"];
    public static UiLocalization Current {get;}=new();
    private readonly Dictionary<string,Dictionary<string,string>> catalogs=new(StringComparer.Ordinal);
    private readonly List<(string Key,Regex Pattern)> templates=[];
    private readonly string[] sentencePrefixes;
    private readonly Dictionary<string,HashSet<int>> quantitySlots=new(StringComparer.Ordinal);
    private readonly string? preferencePath;
    public string CultureName {get;private set;}="en-US";
    public CultureInfo Culture=>CultureInfo.GetCultureInfo(CultureName);
    public event EventHandler? Changed;
    public int KeyCount=>catalogs["en-US"].Count;
    public UiLocalization(string? preferencePath=null)
    {
        this.preferencePath=preferencePath;
        foreach(var culture in Cultures){
            using var stream=typeof(UiLocalization).Assembly.GetManifestResourceStream("RenoDXMfgUnlocker.Localization."+culture+".json") ?? throw new InvalidDataException("Missing localization resource: "+culture);
            using var document=JsonDocument.Parse(stream);
            var entries=new Dictionary<string,string>(StringComparer.Ordinal);
            foreach(var property in document.RootElement.EnumerateObject()){
                if(property.Value.ValueKind!=JsonValueKind.String || string.IsNullOrWhiteSpace(property.Value.GetString()) || !entries.TryAdd(property.Name,property.Value.GetString()!)) throw new InvalidDataException("Invalid or duplicate localization entry: "+culture+" / "+property.Name);
            }
            catalogs.Add(culture,entries);
        }
        foreach(var key in catalogs["en-US"].Keys.Where(k=>Regex.IsMatch(k,@"\{\d+\}"))) {
            var pattern=Regex.Escape(key);
            pattern=Regex.Replace(pattern,@"\\\{(\d+)}",m=>"(?<p"+m.Groups[1].Value+">.+?)");
            templates.Add((key,new Regex("^"+pattern+"$",RegexOptions.Singleline|RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(100))));
        }
        templates.Sort((a,b)=>b.Key.Length.CompareTo(a.Key.Length));
        sentencePrefixes=catalogs["en-US"].Keys.Where(k=>k.Length>=12 && !k.Contains('{') && k[^1] is '.' or ':' or '?' or '!').OrderByDescending(k=>k.Length).ToArray();
        using(var stream=typeof(UiLocalization).Assembly.GetManifestResourceStream("RenoDXMfgUnlocker.Localization.number-slots.json") ?? throw new InvalidDataException("Missing numeric display metadata."))
        using(var metadata=JsonDocument.Parse(stream))foreach(var item in metadata.RootElement.EnumerateObject()){
            if(!catalogs["en-US"].ContainsKey(item.Name))throw new InvalidDataException("Unknown numeric display key: "+item.Name);
            var slots=item.Value.EnumerateArray().Select(v=>v.GetInt32()).ToHashSet();
            if(slots.Any(slot=>!item.Name.Contains("{"+slot+"}",StringComparison.Ordinal)) || !quantitySlots.TryAdd(item.Name,slots))throw new InvalidDataException("Invalid numeric display metadata: "+item.Name);
        }
        if(preferencePath is not null) {
            try { var saved=JsonSerializer.Deserialize<LanguagePreference>(File.ReadAllText(preferencePath));if(saved is not null && Cultures.Contains(saved.Culture))CultureName=saved.Culture; }
            catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or JsonException){}
        }
    }
    public static UiLocalization LoadUserPreference()=>new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"RenoDX MFG Unlocker","language.json"));
    public void SetCulture(string culture,bool persist=true)
    {
        if(!Cultures.Contains(culture))throw new ArgumentException("Unsupported language.",nameof(culture));
        if(persist && preferencePath is not null){
            var parent=Path.GetDirectoryName(preferencePath)!;
            StreamlineStagingEvidenceService.EnsureNoReparsePoints(Path.GetPathRoot(parent)!,preferencePath);
            Directory.CreateDirectory(parent);
            var temporary=preferencePath+"."+Guid.NewGuid().ToString("N")+".tmp";
            try {File.WriteAllText(temporary,JsonSerializer.Serialize(new LanguagePreference(culture)));File.Move(temporary,preferencePath,true);}
            finally {if(File.Exists(temporary))File.Delete(temporary);}
        }
        if(CultureName==culture)return;
        CultureName=culture;
        Changed?.Invoke(this,EventArgs.Empty);
    }
    public IReadOnlyDictionary<string,string> GetCatalog(string culture)=>catalogs[culture];
    public string Lookup(string key,string? culture=null)=>(catalogs.GetValueOrDefault(culture??CultureName)??catalogs["en-US"]).GetValueOrDefault(key)??catalogs["en-US"].GetValueOrDefault(key)??key;
    public string Format(string key,params object?[] values)=>string.Format(Culture,Lookup(key),values);
    public string Translate(string text)=>Translate(text,0);
    private string Translate(string text,int depth)
    {
        if(string.IsNullOrEmpty(text) || depth>12 || text.Length>262144)return text;
        if(catalogs["en-US"].ContainsKey(text))return Lookup(text);
        var size=Regex.Match(text,@"^(\d+(?:\.\d+)?) (KB|MB|GB)$",RegexOptions.CultureInvariant);
        if(size.Success && double.TryParse(size.Groups[1].Value,NumberStyles.AllowDecimalPoint,CultureInfo.InvariantCulture,out var bytes))return bytes.ToString("0.#",Culture)+" "+size.Groups[2].Value;
        if(text.Contains('\n'))return string.Join("\n",text.Replace("\r\n","\n").Split('\n').Select(line=>Translate(line,depth+1)));
        if(text.Contains("  ›  "))return string.Join("  ›  ",text.Split("  ›  ").Select(part=>Translate(part,depth+1)));
        if(text.StartsWith("[ ",StringComparison.Ordinal) && text.EndsWith(" ]",StringComparison.Ordinal))return "[ "+Translate(text[2..^2],depth+1)+" ]";
        if(text!=text.Trim() && text.Trim().Length>0){var start=text.Length-text.TrimStart().Length;var end=text.TrimEnd().Length;return text[..start]+Translate(text[start..end],depth+1)+text[end..];}
        foreach(var (key,pattern) in templates){
            Match match;try{match=pattern.Match(text);}catch(RegexMatchTimeoutException){continue;}if(!match.Success)continue;
            return Regex.Replace(Lookup(key),@"\{(\d+)\}",m=>{
                var value=match.Groups["p"+m.Groups[1].Value].Value;
                // Only quantities may be reformatted. Versions, paths, hashes and opaque values stay byte-for-byte text.
                if(quantitySlots.TryGetValue(key,out var slots) && slots.Contains(int.Parse(m.Groups[1].Value,CultureInfo.InvariantCulture))
                    && Regex.IsMatch(value,@"^(?:\d+|\d{1,3}(?:[,\u00A0\u202F ]\d{3})+)$") && long.TryParse(Regex.Replace(value,@"[,\u00A0\u202F ]",""),NumberStyles.None,CultureInfo.InvariantCulture,out var number))return number.ToString("N0",Culture);
                return Translate(value,depth+1);
            });
        }
        foreach(var prefix in sentencePrefixes)if(text.StartsWith(prefix+" ",StringComparison.Ordinal))return Lookup(prefix)+" "+Translate(text[(prefix.Length+1)..],depth+1);
        // Composed reports preserve paths, hashes and original log evidence as opaque values.
        if(text.Contains(" · "))return string.Join(" · ",text.Split(" · ").Select(part=>Translate(part,depth+1)));
        return text;
    }
    public IReadOnlyList<string> ValidateCatalogs()
    {
        var errors=new List<string>();var english=catalogs["en-US"];
        foreach(var (culture,entries) in catalogs){
            foreach(var key in english.Keys){
                if(!entries.TryGetValue(key,out var value)){errors.Add(culture+": missing "+key);continue;}
                var expected=Regex.Matches(key,@"\{\d+\}").Select(m=>m.Value).Order().ToArray();
                var actual=Regex.Matches(value,@"\{\d+\}").Select(m=>m.Value).Order().ToArray();
                if(!expected.SequenceEqual(actual))errors.Add(culture+": placeholders "+key);
                if(value.Contains(@"\n") || value.Contains(@"\r") || value.Contains(@"\t"))errors.Add(culture+": literal escape "+key);
                try {var count=Regex.Matches(value,@"\{(\d+)\}").Select(m=>int.Parse(m.Groups[1].Value,CultureInfo.InvariantCulture)).DefaultIfEmpty(-1).Max()+1;_=string.Format(CultureInfo.InvariantCulture,value,Enumerable.Repeat<object>("test",count).ToArray());}
                catch(FormatException){errors.Add(culture+": invalid format "+key);}
                if(value.Any(c=>c<' ' && c is not '\n' and not '\r' and not '\t'))errors.Add(culture+": control character "+key);
            }
            foreach(var key in entries.Keys.Except(english.Keys))errors.Add(culture+": unknown key "+key);
        }
        return errors;
    }
    private sealed record LanguagePreference(string Culture);
}
