namespace ZonderqOS
{
    public static class EnvironmentExpander
    {
        public static string Expand(string input, string currentPath)
        {
            if (string.IsNullOrEmpty(input)) return input;

            // Zamiana tyldy na HOME w ścieżkach
            string expanded = input.Replace("~", EnvironmentManager.Get("HOME"));

            // Prosta ekspansja zmiennych $USER, $HOME, $HOSTNAME, $PATH
            expanded = expanded.Replace("$USER", EnvironmentManager.Get("USER"));
            expanded = expanded.Replace("$HOME", EnvironmentManager.Get("HOME"));
            expanded = expanded.Replace("$HOSTNAME", EnvironmentManager.Get("HOSTNAME"));
            expanded = expanded.Replace("$PATH", EnvironmentManager.Get("PATH"));

            return expanded;
        }
    }
}