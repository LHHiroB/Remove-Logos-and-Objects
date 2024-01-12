using Microsoft.UI.Xaml;
using IOCore;
using IOCore.Libs;
using ImageMagick;

namespace IOApp
{
    internal class IOApp
    {
        internal static void Init(Application app)
        {
            Meta.APP_INIT_SIZE = new(1200, 740);

            LanguageManager.LANGUAGES[LanguageManager.Culture.EnUs].IsOn = true;
            LanguageManager.LANGUAGES[LanguageManager.Culture.ViVn].IsOn = true;

            ThemeManager.THEMES[ThemeManager.Theme.Dark].IsOn = true;
            ThemeManager.THEMES[ThemeManager.Theme.Light].IsOn = true;

            ThemeManager.Init(app);
            LanguageManager.Init();
        }

        internal static void InitExt()
        {
            ResourceLimits.LimitMemory(new Percentage(90));
        } 
    }
}