using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Flow.Launcher.Plugin.Kummer
{
    public class Settings
    {
        [JsonPropertyName("slack_token_home")]
        public string SlackTokenHome { get; set; } = "";

        [JsonPropertyName("slack_token_work")]
        public string SlackTokenWork { get; set; } = "";

        [JsonPropertyName("home_assistant_url")]
        public string HomeAssistantUrl { get; set; } = "";

        [JsonPropertyName("home_assistant_token")]
        public string HomeAssistantToken { get; set; } = "";

        [JsonPropertyName("home_shutdown_commands")]
        public string HomeShutdownCommands { get; set; } = "";

        [JsonPropertyName("work_shutdown_commands")]
        public string WorkShutdownCommands { get; set; } = "";


        public List<string> Validate()
        {
            List<string> validationErrors = new();

            if (SlackTokenHome == null || SlackTokenHome.Length == 0)
                validationErrors.Add("Slack Token Home cannot be empty");
            if (SlackTokenWork == null || SlackTokenWork.Length == 0)
                validationErrors.Add("Slack Token Work cannot be empty");
            if (HomeAssistantUrl == null || !Uri.IsWellFormedUriString(HomeAssistantUrl, UriKind.RelativeOrAbsolute))
                validationErrors.Add("Home Assistant Url is either empty or invalid");
            if (HomeAssistantToken == null || HomeAssistantToken.Length == 0)
                validationErrors.Add("Slack Token Work cannot be empty");
            
            // There is no validation I can do on the shutdown commands 

            return validationErrors;
        }
    }
}
