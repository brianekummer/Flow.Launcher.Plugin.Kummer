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

        /*
        private readonly List<ProcessStartInfo> HOME_SHUTDOWN_COMMANDS = new()
        {
            new ProcessStartInfo("btcom.exe", "-r -b \"38:5C:76:2E:5E:82\" -s111e"),
            new ProcessStartInfo("btcom.exe", "-r -b \"38:5C:76:2E:5E:82\" -s110b"),
            new ProcessStartInfo("radiocontrol.exe", "bluetooth OFF"),
            new ProcessStartInfo("\"C:\\Program Files\\NirCmd\\nircmdc.exe\"", "cmdwait 5000 monitor off")
        };
        private readonly List<ProcessStartInfo> WORK_SHUTDOWN_COMMANDS = new()
        {
            new ProcessStartInfo("shutdown", "/s /t 0")
        };
        */









        /*
        public void Save()
        {
            //File.WriteAllText(ConfigFile, JsonSerializer.Serialize(this));
        }
        */
    }
}
