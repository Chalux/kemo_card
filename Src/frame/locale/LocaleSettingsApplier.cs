using Godot;

namespace KemoCard.Frame.Locale;

public static class LocaleSettingsApplier
{
    public static void Apply(string languageCode)
    {
        if (!LocaleRegistry.TryGet(languageCode, out _))
            languageCode = LocaleSettingKeys.DefaultLanguage;
        TranslationServer.SetLocale(languageCode);
    }
}
