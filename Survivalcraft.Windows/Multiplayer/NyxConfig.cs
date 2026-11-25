using System;
using System.IO;
using System.Text.Json;
using Game;

namespace Nyx
{
    public class NyxConfig
    {
        public string Mode { get; set; } = "Off";
        public string Address { get; set; } = "127.0.0.1";
        public int Port { get; set; } = 25565;
        public string Name { get; set; } = "";

        public static string FilePath => Path.Combine(ModsManager.UserDataPath, "nyx_multiplayer.json");

        public static NyxConfig LoadOrCreate()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    var cfg = JsonSerializer.Deserialize<NyxConfig>(File.ReadAllText(FilePath));
                    if (cfg != null) return cfg;
                }
                var created = new NyxConfig();
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                File.WriteAllText(FilePath, JsonSerializer.Serialize(created, new JsonSerializerOptions { WriteIndented = true }));
                return created;
            }
            catch (Exception e)
            {
                Engine.Log.Warning("Nyx: falha ao ler a configuração: " + e.Message);
                return new NyxConfig();
            }
        }
    }
}
