using System.Collections.Frozen;
using System.Reflection;
using Jeek.Avalonia.Localization;
using Newtonsoft.Json.Linq;

namespace VRCVideoCacher.Languages;

public class EmbeddedJsonLocalizer : BaseLocalizer
{
    private FrozenDictionary<string, string> _languageStrings = new Dictionary<string, string>().ToFrozenDictionary();
    private FrozenDictionary<string, string> _enLanguageStrings = new Dictionary<string, string>().ToFrozenDictionary();
    private bool _enLanguageLoaded;

    private const string Prefix = "VRCVideoCacher.Languages.";
    private const string Suffix = ".loc.json";

    public EmbeddedJsonLocalizer()
    {
        Reload();
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

    private void EnglishLanguageLoad(Assembly assembly)
    {
        if (_enLanguageLoaded) return;
        var resourceName = assembly.GetManifestResourceNames()
            .First(r => r.Equals($"{Prefix}{FallbackLanguage}{Suffix}"));

        using var stream = assembly.GetManifestResourceStream(resourceName)!;
        using var reader = new StreamReader(stream);
        var json = JObject.Parse(reader.ReadToEnd());

        _enLanguageStrings = json.Properties()
            .ToDictionary(k => k.Name, v => v.Value?.ToString() ?? v.Name)
            .ToFrozenDictionary();

        _enLanguageLoaded = true;
    }

    protected override void OnLanguageChanged()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = assembly.GetManifestResourceNames()
            .First(r => r.Equals($"{Prefix}{_language}{Suffix}"));

        EnglishLanguageLoad(assembly);

        using var stream = assembly.GetManifestResourceStream(resourceName)!;
        using var reader = new StreamReader(stream);
        var json = JObject.Parse(reader.ReadToEnd());

        _languageStrings = json.Properties()
            .ToDictionary(k => k.Name, v => v.Value?.ToString() ?? v.Name)
            .ToFrozenDictionary();
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

        return Language + ":" + key;
    }
}