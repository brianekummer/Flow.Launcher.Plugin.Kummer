using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using Control = System.Windows.Controls.Control;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;




namespace Flow.Launcher.Plugin.Kummer
{
    /*
     *  Flow.Launcher.Plugin.Kummer
     *  by Brian Kummer, October 2024
     *
     *  I used to have Flow Launcher run Windows batch files for most of this functionality along with the "Plugin Runner"
     *  plugin, but my employer's IT dept keeps locking down my laptop more and more, so now it often prompts me for approval
     *  to run some of these batch files, which is becoming annoying. So I moved that functionality into this plugin.
     *
     *  Notes
     *  -----
     *    - Because settings are read when the plugin starts, any change to a setting requires the plugin to be restarted
    */
    public partial class Main : IPlugin, ISettingProvider, IPluginI18n
    {
        private const string PLUGIN_KEY_PREFIX = "flowlauncher_plugin_kummer";

        private enum HTTP_CLIENT_ENUMS
        {
            SLACK = 1,
            HOME_ASSISTANT = 2
        }

        internal PluginInitContext _context;
        private Settings _settings;
        private Dictionary<string, string> _keywordTitleMappings = new();
        private List<Result> _commands = new();
        private Dictionary<HTTP_CLIENT_ENUMS, HttpClient> _httpClients = new();
        private List<ProcessStartInfo> _shutdownCommands = new();

        private bool _isHomeComputer = Environment.GetEnvironmentVariable("COMPUTERNAME") == Environment.GetEnvironmentVariable("USERDOMAIN");


        /*
         *  Initialize the plugin
         *  
         *  @param context - The plugin context
         */
        public void Init(PluginInitContext context)
        {
            _context = context;

            // Retrieve and validate the settings
            _settings = context.API.LoadSettingJsonStorage<Settings>();
            List<string> validationErrors = _settings.Validate();
            if (validationErrors.Any())
            {
                MethodBase m = MethodBase.GetCurrentMethod();
                HandleError("Invalid settings", $"Plugin settings failed validation: {string.Join(',', validationErrors)}", null, m.ReflectedType.Name, m.Name);
            }

            // Build HTTP clients
            _httpClients.Add(HTTP_CLIENT_ENUMS.SLACK, CreateHttpClient(_isHomeComputer ? _settings.SlackTokenHome : _settings.SlackTokenWork));
            _httpClients.Add(HTTP_CLIENT_ENUMS.HOME_ASSISTANT, CreateHttpClient(_settings.HomeAssistantToken));

            // Build list of shutdown commands
            _shutdownCommands = BuildShutdownCommands(_isHomeComputer ? _settings.HomeShutdownCommands : _settings.WorkShutdownCommands);
        }


        /*
         *  Create an HTTP client with a specific bearer token
         *  
         *  @param bearerToken - The bearer token for that HTTP client
         */
        private HttpClient CreateHttpClient(string bearerToken)
        {
            HttpClient httpClient = new();
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
            return httpClient;
        }


        /*
         *  Initialize the plugin, stage 2
         *  
         *  I cannot do these steps in Init() because GetTranslation() doesn't work - it returns "No Translation for key xxxxx"
         *    - I don't know why, and I cannot find any Flow Launcher plugin that uses GetTranslation() in its Init() as an 
         *      example
         *    - So I will set it here, if it's not already set, and call this method from Query()
         *    - Using IAsyncPlugin doesn't solve this problem
         *    - I WONDER if it is because the translations aren't yet loaded until AFTER Init() is finished?
         */
        private void InitStage2()
        {
            if (_keywordTitleMappings.Count == 0)
            {
                new List<string>()
                {
                    $"{PLUGIN_KEY_PREFIX}_air_purifier_toggle_cmd",
                    $"{PLUGIN_KEY_PREFIX}_bluetooth_off_cmd",
                    $"{PLUGIN_KEY_PREFIX}_bluetooth_on_cmd",
                    $"{PLUGIN_KEY_PREFIX}_fan_toggle_cmd",
                    $"{PLUGIN_KEY_PREFIX}_exit_office_cmd",
                    $"{PLUGIN_KEY_PREFIX}_monitor_lights_off_cmd",
                    $"{PLUGIN_KEY_PREFIX}_nighttime_cmd",
                    $"{PLUGIN_KEY_PREFIX}_office_cold_cmd",
                    $"{PLUGIN_KEY_PREFIX}_office_hot_cmd",
                    $"{PLUGIN_KEY_PREFIX}_office_lights_daylight_cmd",
                    $"{PLUGIN_KEY_PREFIX}_office_lights_neutral_cmd",
                    $"{PLUGIN_KEY_PREFIX}_office_lights_off_cmd",
                    $"{PLUGIN_KEY_PREFIX}_office_lights_warm_cmd",
                    $"{PLUGIN_KEY_PREFIX}_outside_bright_cmd",
                    $"{PLUGIN_KEY_PREFIX}_outside_overcast_cmd",
                    $"{PLUGIN_KEY_PREFIX}_outside_dark_cmd",
                }
                .ForEach(key => _keywordTitleMappings.Add(_context.API.GetTranslation(key), key));
            }

            if (_commands.Count == 0)
            {
                _commands = GetCommands();
            }
        }


        /*
         *  Build the list of shutdown commands
         *  
         *  @param shutdownCommandsString - List of CRLF-delimited commands to parse into a list of ProcessStartInfo objects can actually be run
         *  @returns a list of ProcessStartInfo objects
         */
        private List<ProcessStartInfo> BuildShutdownCommands(String shutdownCommandsString)
        {
            // Find the first space character that is outside of any optional double-quotes
            Regex rexeg = new Regex("(?:\"([^\"]*)\"|([^\"\\s]+))");
            List<ProcessStartInfo> shutdownCommands = new();

            shutdownCommandsString.Split(new string[] { "\n" }, StringSplitOptions.None).ToList().ForEach(shutdownCommand =>
            {
                shutdownCommand = shutdownCommand.Trim();
                if (shutdownCommand.Length > 0)
                {
                    Match match = rexeg.Match(shutdownCommand);
                    if (match.Groups[0].Success)
                    {
                        string cmd = match.Groups[0].Value;
                        string args = (cmd.Length < shutdownCommand.Length) ? shutdownCommand.Substring(match.Groups[0].Length + 1) : "";
                        shutdownCommands.Add(new ProcessStartInfo(cmd, args));
                    }
                }
            });

            return shutdownCommands;
        }


        /*
         *  Build a query result for executing a Home Assistant scene
         *  
         *  @param subkey - Part of the Flow Launcher key (i.e. "air_purifier_toggle")
         *  @param service - The service name of the Home Assistant service to execute (i.e. "turn_on" or "light")
         *  @param entityId - The entity id of the Home Assistant entity to use (i.e. "script.office_air_purifier_toggle")
         *  
         *  @returns a Flow Launcher query Result object to be used by Query()
         *  
         *  Assumptions
         *    - The keys for the title and subtitle are the same except the title appends "_cmd"
         *    - The icon is name "{subkey}.png"
         */
        private Result BuildHomeAssistantSceneResult(string subkey, string service, string entityId)
        {
            return new Result
            {
                Title = _context.API.GetTranslation($"{PLUGIN_KEY_PREFIX}_{subkey}_cmd"),
                SubTitle = _context.API.GetTranslation($"{PLUGIN_KEY_PREFIX}_{subkey}"),
                IcoPath = $"Images\\{subkey}.png",
                Action = c =>
                {
                    ExecuteHomeAssistantCommand(service, entityId);
                    return true;
                },
            };
        }


        /*
         *  Get the Flow Launcher commands that are available for this plugin
         * 
         *  @returns a list of query Result objects
         */
        private List<Result> GetCommands()
        {
            List<Result> results = new();
            results.AddRange(new[]
            {
                BuildExitOfficeResult(),
                BuildBluetoothResult("off"),
                BuildBluetoothResult("on"),
                BuildHomeAssistantSceneResult("air_purifier_toggle", "turn_on", "script.office_air_purifier_toggle"),
                BuildHomeAssistantSceneResult("fan_toggle", "toggle", "switch.office_small_fan"),
                BuildHomeAssistantSceneResult("monitor_lights_off", "turn_off", "light.monitor_lights"),
                BuildHomeAssistantSceneResult("nighttime", "turn_on", "script.office_nighttime"),
                BuildHomeAssistantSceneResult("office_cold", "turn_on", "script.office_cold"),
                BuildHomeAssistantSceneResult("office_hot", "turn_on", "script.office_hot"),
                BuildHomeAssistantSceneResult("office_lights_daylight", "turn_on", "script.office_lights_to_daylight"),
                BuildHomeAssistantSceneResult("office_lights_neutral", "turn_on", "script.office_lights_to_neutral"),
                BuildHomeAssistantSceneResult("office_lights_warm", "turn_on", "script.office_lights_to_warm"),
                BuildHomeAssistantSceneResult("office_lights_off", "turn_on", "script.office_lights_off"),
                BuildHomeAssistantSceneResult("outside_bright", "turn_on", "script.office_outside_bright"),
                BuildHomeAssistantSceneResult("outside_overcast", "turn_on", "script.office_outside_overcast"),
                BuildHomeAssistantSceneResult("outside_dark", "turn_on", "script.office_outside_dark")
            });

            return results;
        }


        /*
         *  Builds the Result for exiting my office
         * 
         *  @returns a Result object for existing my office
         */ 
        private Result BuildExitOfficeResult()
        {
            return new Result
            {
                Title = _context.API.GetTranslation($"{PLUGIN_KEY_PREFIX}_exit_office_cmd"),
                SubTitle = _context.API.GetTranslation($"{PLUGIN_KEY_PREFIX}_exit_office"),
                IcoPath = "Images\\exit_office.png",
                Action = c =>
                {
                    Task.Run(() =>
                    {
                        SetSlackPresence("away");
                        SetSlackStatus("", "");
                        ExecuteHomeAssistantCommand("turn_on", "script.office_exit");

                        _shutdownCommands.ForEach(psi =>
                        {
                            psi.UseShellExecute = true;
                            psi.WindowStyle = ProcessWindowStyle.Hidden;
                            Process.Start(psi).WaitForExit();
                        });
                    });

                    return true;
                },
            };
        }


        /*
         *  Builds the Result for turning Bluetooth on or off
         * 
         *  @param onOff - on|off
         *
         *  @returns a Result object for turning bluetooth on or off
         */
        private Result BuildBluetoothResult(string onOff)
        {
            return new Result
            {
                Title = _context.API.GetTranslation($"{PLUGIN_KEY_PREFIX}_bluetooth_{onOff}_cmd"),
                SubTitle = _context.API.GetTranslation($"{PLUGIN_KEY_PREFIX}_bluetooth_{onOff}"),
                IcoPath = $"Images\\bluetooth_{onOff}.png",
                Action = c =>
                {
                    Task.Run(() =>
                    {
                        ProcessStartInfo psi = new ProcessStartInfo("radiocontrol.exe", $"bluetooth {onOff}");
                        psi.UseShellExecute = false;
                        Process.Start(psi);
                    });

                    return true;
                },
            };
        }


        /*
         *  Execute a Home Assistant command
         *  
         *  @param service - The service name of the Home Assistant service to execute (i.e. "turn_on" or "light")
         *  @param entityId - The entity id of the Home Assistant entity to use (i.e. "script.office_air_purifier_toggle")
         *  
         *  Notes
         *    - We cannot change the format of requestData, must use double quotes
         */
        private async void ExecuteHomeAssistantCommand(string service, string entityId)
        {
            string domain = entityId.Split('.')[0];
            string url = $"{_settings.HomeAssistantUrl}/api/services/{domain}/{service}";
            string requestData = $"{{\"entity_id\":\"{entityId}\"}}";

            await _httpClients[HTTP_CLIENT_ENUMS.HOME_ASSISTANT].PostAsync(url, new StringContent(requestData, Encoding.UTF8, "application/json"));
        }


        /*
         *  Set my Slack presence
         *  
         *  @param presence - Slack presence (auto|away)
         */
        private async void SetSlackPresence(string presence)
        {
            string url = $"https://slack.com/api/users.setPresence?presence={presence}";
            
            await _httpClients[HTTP_CLIENT_ENUMS.SLACK].PostAsync(url, new StringContent(""));
        }


        /*
         *  Set my Slack status
         *  
         *  @param emoji - Slack emoji to set my status to (i.e. ":palm_tree")
         *  @param statusText - The text to set my Slack status to
         *  
         *  Notes
         *    - Cannot change the format of requestData, must use single quotes
         */
        private async void SetSlackStatus(string emoji, string statusText)
        {
            string url = "https://slack.com/api/users.profile.set";
            string requestData = $"profile={{'status_emoji':'{emoji}','status_text':'{statusText}'}}";

            await _httpClients[HTTP_CLIENT_ENUMS.SLACK].PostAsync(url, new StringContent(requestData, Encoding.UTF8, "application/x-www-form-urlencoded"));
        }



        #region Plugin Infrastructure

        /*
         *  Handle an error. Show an error message and log the exception if that information is provided
         *  
         *  @param errorMessage - The error message to show the user
         *  @param logMessage - The error message to log
         *  @param ex - The exception to log
         *  @param className - The name of the class that threw the error
         *  @param methodName - The name of the method that threw the error
         */
        private void HandleError(string errorMessage, string logMessage = null, Exception ex = null, string className = null, string methodName = null)
        {
            if (className != null)
            {
                _context.API.LogException(className, logMessage, ex, methodName);
            }
            _context.API.ShowMsgError($"Plugin.Kummer Error", errorMessage);
        }


        /*
        *  Execute a query from Flow Launcher
        *  
        *  @param query - The query to execute
        *  @returns a list of Flow Launcher results
        */
        public List<Result> Query(Query query)
        {
            InitStage2();

            List<Result> results = new();

            _commands.ForEach(c => {
                c.Title = GetDynamicTitle(query, c);

                var titleMatch = _context.API.FuzzySearch(query.Search, c.Title);
                var subTitleMatch = _context.API.FuzzySearch(query.Search, c.SubTitle);

                var score = Math.Max(titleMatch.Score, subTitleMatch.Score);
                if (score > 0)
                {
                    c.Score = score;

                    if (score == titleMatch.Score)
                        c.TitleHighlightData = titleMatch.MatchData;

                    results.Add(c);
                }
            });

            return results;
        }


        /*
         *  Get Dynamic Title 
         *  
         *  Stock Flow Launcher plugin code
         */
        private string GetDynamicTitle(Query query, Result result)
        {
            if (!_keywordTitleMappings.TryGetValue(result.Title, out var translationKey))
            {
                MethodBase m = MethodBase.GetCurrentMethod();
                HandleError($"Dynamic Title not found for: {result.Title}", $"Dynamic Title not found for: {result.Title}", null, m.ReflectedType.Name, m.Name);
                return "Title Not Found";
            }

            string translatedTitle = _context.API.GetTranslation(translationKey);

            if (result.Title == translatedTitle)
            {
                return result.Title;
            }

            var englishTitleMatch = _context.API.FuzzySearch(query.Search, result.Title);
            var translatedTitleMatch = _context.API.FuzzySearch(query.Search, translatedTitle);

            return englishTitleMatch.Score >= translatedTitleMatch.Score ? result.Title : translatedTitle;
        }


        /*
         *  These functions are required by ISavable to display and save settings
         */
        public Control CreateSettingPanel() => new KummerSettings(_settings);


        /*
         *  These functions are required by IPluginI18n
         */
        public string GetTranslatedPluginTitle() => _context.API.GetTranslation($"{PLUGIN_KEY_PREFIX}_plugin_name");
        public string GetTranslatedPluginDescription() => _context.API.GetTranslation($"{PLUGIN_KEY_PREFIX}_plugin_description");

        #endregion
    }
}
