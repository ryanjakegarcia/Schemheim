using System;
using System.IO;

namespace PlanBuild.Client
{
    internal static class ClientPaths
    {
        // Data directories and config files from the mods Schemheim descends from, newest first.
        private static readonly string[] LegacyDataDirectories = { "Buildheim", "PlanBuild" };
        private static readonly string[] LegacyConfigFiles = { "augusdogus.Buildheim.cfg", "marcopogo.PlanBuild.cfg" };

        // Reuse an existing data directory so upgrading never moves or resurrects saved plans.
        public static string DataDirectory(string configRoot)
        {
            string current = Path.Combine(configRoot, "Schemheim");
            if (Directory.Exists(current)) return current;
            foreach (string name in LegacyDataDirectories)
            {
                string legacy = Path.Combine(configRoot, name);
                if (Directory.Exists(legacy)) return legacy;
            }
            return current;
        }

        public static bool TryMigrateConfig(string configRoot, string destination, out bool copied, out string error)
        {
            copied = false;
            error = string.Empty;
            if (File.Exists(destination)) return true;
            foreach (string name in LegacyConfigFiles)
            {
                string legacy = Path.Combine(configRoot, name);
                if (!File.Exists(legacy)) continue;
                try
                {
                    File.Copy(legacy, destination, false);
                    copied = true;
                    return true;
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    error = $"Schemheim could not copy settings from {legacy} to {destination}: {ex.Message} " +
                        "The original settings and blueprints are preserved. Check permissions before restarting.";
                    return false;
                }
            }
            return true;
        }
    }
}
