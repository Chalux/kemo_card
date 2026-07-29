namespace KemoCard.Frame.Content;

/// <summary>
/// Mod 自带翻译注册：从 <c>{contentRoot}/translations/</c> 加载 <c>*.translation</c> 并注册到运行时。
/// </summary>
public interface IContentModTranslationLoader
{
    void ClearRegistered();

    void TryLoadModTranslations(DiscoveredModEntry entry);
}