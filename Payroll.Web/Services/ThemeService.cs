namespace Payroll.Web.Services
{
    public class ThemeService
    {
        // Default to light theme
        public string CurrentTheme { get; private set; } = "light";

        public event Action? OnThemeChanged;

        public void SetTheme(string theme)
        {
            if (theme != CurrentTheme)
            {
                CurrentTheme = theme;
                OnThemeChanged?.Invoke();
            }
        }
    }
}