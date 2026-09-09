using System.Collections.Frozen;
using System.Reflection;
using Jeek.Avalonia.Localization;
using Newtonsoft.Json.Linq;

namespace VRCVideoCacher.Languages;

public class EmbeddedJsonLocalizer : BaseLocalizer
{
    private FrozenDictionary<string, string> _languageStrings = new Dictionary<string, string>().ToFrozenDictionary();
    private FrozenDictionary<string, string> _enLanguageStrings = new Dictionary<string, string>().ToFrozenDictionary();

    private const string Prefix = "VRCVideoCacher.Languages.";
    private const string Suffix = ".loc.json";

    public EmbeddedJsonLocalizer()
    {
        Reload();
        _enLanguageStrings = LoadLanguage(FallbackLanguage);
        OnLanguageChanged();
        FireLanguageChanged();
    }

    public override void Reload()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resources = assembly.GetManifestResourceNames()
            .Where(r => r.StartsWith(Prefix) && r.EndsWith(Suffix))
            .ToList();

        foreach (var resourceName in resources)
        {
            var langId = resourceName[Prefix.Length..^Suffix.Length];
            _languages.Add(langId);
        }

        ValidateLanguage();
        _hasLoaded = true;
        UpdateDisplayLanguages();
    }

    private FrozenDictionary<string,string> LoadLanguage(string language)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = assembly.GetManifestResourceNames()
            .First(r => r.Equals($"{Prefix}{language}{Suffix}"));

        using var stream = assembly.GetManifestResourceStream(resourceName)!;
        using var reader = new StreamReader(stream);
        var json = JObject.Parse(reader.ReadToEnd());

        return json.Properties()
            .ToDictionary(k => k.Name, v => v.Value?.ToString() ?? v.Name)
            .ToFrozenDictionary();
    }

    protected override void OnLanguageChanged()
    {
        _languageStrings = LoadLanguage(Language);
    }

    public override string Get(string key)
    {
        if (!_hasLoaded)
            Reload();

        if (_languageStrings?.TryGetValue(key, out var value) == true)
            return value;

#if !DEBUG
        if (_enLanguageStrings?.TryGetValue(key, out var enValue) == true)
            return enValue;
#endif

        return base.Language + ":" + key;
    }
}