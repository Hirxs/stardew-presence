using System;
using System.IO;

namespace StardewPresence.Framework.Services
{
    internal static class ConfigFileFormatter
    {
        private static readonly (string Key, string Section)[] Sections =
        {
            ("\"AppId\"", "// ## Core"),
            ("\"TitleMenuDetails\"", "// ## Title Screen"),
            ("\"ImageFrame\"", "// ## Image Frame"),
            ("\"EditorKey\"", "// ## Visual Editor"),
            ("\"FarmerOffsetX\"", "// ## Farmer"),
            ("\"CompanionType\"", "// ## Companion and Spouse"),
            ("\"PetOffsetX\"", "// ## Pet"),
            ("\"ShowFarmer\"", "// ## Visibility"),
            ("\"ForcedSeason\"", "// ## Season and Map Background"),
            ("\"ShowModCount\"", "// ## Presence Features"),
            ("\"CustomLine1Format\"", "// ## Presence Text Lines"),
            ("\"EnableButton1\"", "// ## Buttons")
        };

        public static void RestoreComments(string directoryPath)
        {
            string configPath = Path.Combine(directoryPath, "config.json");
            if (!File.Exists(configPath)) return;

            try
            {
                string content = File.ReadAllText(configPath);
                if (content.Contains("// ## Core", StringComparison.Ordinal)) return;

                foreach (var (key, section) in Sections)
                {
                    content = content.Replace($"  {key}:", $"  {key}:", StringComparison.Ordinal);
                    content = content.Replace($"  {key} ", $"  {key} ", StringComparison.Ordinal);
                }

                string newline = content.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
                foreach (var (key, section) in Sections)
                {
                    string marker = section + newline;
                    int keyIndex = content.IndexOf("  " + key, StringComparison.Ordinal);
                    if (keyIndex >= 0)
                    {
                        content = content.Insert(keyIndex, marker);
                    }
                }

                File.WriteAllText(configPath, content);
            }
            catch
            {
                // Formatting is cosmetic; configuration must remain usable if the file is locked.
            }
        }
    }
}