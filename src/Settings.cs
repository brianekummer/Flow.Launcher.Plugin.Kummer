using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Flow.Launcher.Plugin.Kummer
{
    public class Settings
    {
        public string ConfigFile = null!;

        [JsonPropertyName("slack_token_home")]
        public string SlackTokenHome { get; set; } = "";

        [JsonPropertyName("slack_token_work")]
        public string SlackTokenWork { get; set; } = "";

        [JsonPropertyName("home_assistant_url")]
        public string HomeAssistantUrl { get; set; } = "";

        [JsonPropertyName("home_assistant_token")]
        public string HomeAssistantToken { get; set; } = "";

        [JsonPropertyName("home_shutdown_commands")]
        public List<string> HomeShutdownCommands{ get; set; } = new();

        [JsonPropertyName("work_shutdown_commands")]
        public List<string> WorkShutdownCommands { get; set; } = new();





        /*
        public void Save()
        {
            //File.WriteAllText(ConfigFile, JsonSerializer.Serialize(this));
        }
        */
    }
}
