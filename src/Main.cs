using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.IO;



using Control = System.Windows.Controls.Control;
using System.Reflection;
using Newtonsoft.Json;
using System.Text;
using static System.Net.WebRequestMethods;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Linq;
using System.Security.Policy;




namespace Flow.Launcher.Plugin.Kummer
{
    /*
     *  Flow.Launcher.Plugin.Kummer
     *  by Brian Kummer, October 2024
     *
     *
     *  Notes
     *  -----
     *    - Because API secret is read when the plugin starts, any change to that requires the plugin to be restarted
     *    - Doing this because my employer's IT has my work laptop locked down and it sometimes prompted me asking if
     *      it should run some batch files, so I pulled everything into this plugin.
     *    
     *  TO DO
     *  -----
     *    - Add validation to the settings, making sure all are populated with reasonable values
     *    - Add UI for settings
     *    - Test making changes to settings in UI
     *    - Can I remove Newtonsoft package?
    */
    public partial class Main : IPlugin, ISettingProvider, IPluginI18n     //, ISavable
    {
        private const string PLUGIN_KEY_PREFIX = "flowlauncher_plugin_kummer";

        private enum HTTP_CLIENT_ENUMS
        {
            SLACK_HOME = 1,
            SLACK_WORK = 2,
            HOME_ASSISTANT = 3
        }

        internal PluginInitContext _context;
        private Settings _settings;
        private Dictionary<string, string> _keywordTitleMappings = new Dictionary<string, string>();
        private List<Result> _commands = new List<Result>();
        private Dictionary<HTTP_CLIENT_ENUMS, HttpClient> _httpClients = new Dictionary<HTTP_CLIENT_ENUMS, HttpClient>();
        private readonly List<ProcessStartInfo> _homeShutdownCommands = new();
        private readonly List<ProcessStartInfo> _workShutdownCommands = new();

        private bool _homeComputer = Environment.GetEnvironmentVariable("COMPUTERNAME") == Environment.GetEnvironmentVariable("USERDOMAIN");


        /*
         *  Initialize the plugin
         *  
         *  @param context - The plugin context
         */
        public void Init(PluginInitContext context)
        {
            _context = context;
            _settings = context.API.LoadSettingJsonStorage<Settings>();

            // Build HTTP clients
            _httpClients.Add(HTTP_CLIENT_ENUMS.SLACK_HOME, CreateHttpClient(_settings.SlackTokenHome));
            _httpClients.Add(HTTP_CLIENT_ENUMS.SLACK_WORK, CreateHttpClient(_settings.SlackTokenWork));
            _httpClients.Add(HTTP_CLIENT_ENUMS.HOME_ASSISTANT, CreateHttpClient(_settings.HomeAssistantToken));

            // Create shutdown objects
            //_context.API.LogInfo("Main.cs", $"_settings.HomeShutdownCommands = {_settings.HomeShutdownCommands}", "Init");
            _settings.HomeShutdownCommands.Split(new string[] { "\n" }, StringSplitOptions.None).ToList().ForEach(c =>
            {
                if (c.Trim().Length > 0)
                {
                    //_context.API.LogInfo("Main.cs", $">>> c = {c}", "Init");
                    var parts = c.Split('|');
                    //_context.API.LogInfo("Main.cs", $">>> c => {parts[0]} | {parts[1]}", "Init");
                    _homeShutdownCommands.Add(new ProcessStartInfo(parts[0], parts[1]));
                }
            });
            //_context.API.LogInfo("Main.cs", $"_settings.WorkShutdownCommands = {_settings.WorkShutdownCommands}", "Init");
            _settings.WorkShutdownCommands.Split(new string[] { "\n" }, StringSplitOptions.None).ToList().ForEach(c =>
            {
                if (c.Trim().Length > 0)
                {
                    //_context.API.LogInfo("Main.cs", $">>> c = {c}", "Init");
                    var parts = c.Split('|');
                    //_context.API.LogInfo("Main.cs", $">>> c => {parts[0]} | {parts[1]}", "Init");
                    _workShutdownCommands.Add(new ProcessStartInfo(parts[0], parts[1]));
                }
            });
        }


        /*
         *  Create an HTTP client with a specific bearer token
         *  
         *  @param bearerToken - The bearer toekn for that HTTP client
         */
        private HttpClient CreateHttpClient(string bearerToken)
        {
            var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
            return httpClient;
        }


        /*
         *  Initialize the plugin, stage 2
         *  
         *  I cannot do these steps in Init() because GetTranslation() doesn't work - it returns
         *  "No Translation for key xxxxx". I don't know why, and I cannot find any FlowLauncher plugin that uses
         *  GetTranslation() in it's Init() as an example.
         *
         *  So I will set it here, if it's not already set, and call it from Query()
         *
         *  Using IAsyncPlugin doesn't solve this problem.
         */
        private void InitStage2()
        {
            if (_keywordTitleMappings.Count == 0)
            {
                new List<string>()
                {
                    $"{PLUGIN_KEY_PREFIX}_air_purifier_toggle_cmd",
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
                _commands = getCommands();
            }
        }


        /*
        *  Execute a query from Flow Launcher
        *  
        *  @param query - The query to execute
        */
        public List<Result> Query(Query query)
        {
            InitStage2();

            var results = new List<Result>();

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
                handleError($"Dynamic Title not found for: {result.Title}", $"Dynamic Title not found for: {result.Title}", null, m.ReflectedType.Name, m.Name);
                return "Title Not Found";
            }

            var translatedTitle = _context.API.GetTranslation(translationKey);

            if (result.Title == translatedTitle)
            {
                return result.Title;
            }

            var englishTitleMatch = _context.API.FuzzySearch(query.Search, result.Title);
            var translatedTitleMatch = _context.API.FuzzySearch(query.Search, translatedTitle);

            return englishTitleMatch.Score >= translatedTitleMatch.Score ? result.Title : translatedTitle;
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
        private Result buildHomeAssistantSceneResult(string subkey, string service, string entityId)
        {
            // assumes
            //   Values of title and subtitle are same except title appends _cmd
            //   icon is named as key.png
            return new Result
            {
                Title = _context.API.GetTranslation($"{PLUGIN_KEY_PREFIX}_{subkey}_cmd"),
                SubTitle = _context.API.GetTranslation($"{PLUGIN_KEY_PREFIX}_{subkey}"),
                //Glyph = null,
                IcoPath = $"Images\\{subkey}.png",
                Action = c =>
                {
                    executeHomeAssistantCommand(service, entityId);
                    return true;
                },
            };
        }


        /*
         *  Get the Flow Launcher commands that are available for this plugin
         * 
         *  @returns a list of query Result objects
         */
        private List<Result> getCommands()
        {
            var results = new List<Result>();
            results.AddRange(new[]
            {   
                new Result
                {
                    Title = _context.API.GetTranslation($"{PLUGIN_KEY_PREFIX}_exit_office_cmd"),
                    SubTitle = _context.API.GetTranslation($"{PLUGIN_KEY_PREFIX}_exit_office"),
                    //Glyph = null,
                    IcoPath = "Images\\exit-office.png",
                    Action = c =>
                    {
                        // Run this async so FlowLauncher can return control to the user
                        Task.Run(() => {
                            setSlackPresence(_homeComputer, "away");
                            setSlackStatus(_homeComputer, "", "");
                            executeHomeAssistantCommand("turn_on", "script.office_exit");

                            if (_homeComputer)
                            {
                                _homeShutdownCommands.ForEach(psi =>
                                {
                                    _context.API.LogInfo("Main.cs", $"Executing >>> {psi.FileName} {psi.Arguments}", "HOME_SHUTDOWN_COMMANDS");

                                    psi.UseShellExecute = true;
                                    psi.WindowStyle = ProcessWindowStyle.Hidden;
                                    Process.Start(psi).WaitForExit();
                                });
                            } else
                            {
                                _workShutdownCommands.ForEach(psi =>
                                {
                                    _context.API.LogInfo("Main.cs", $"Executing >>> {psi.FileName} {psi.Arguments}", "WORK_SHUTDOWN_COMMANDS");

                                    psi.UseShellExecute = true;
                                    psi.WindowStyle = ProcessWindowStyle.Hidden;
                                    Process.Start(psi).WaitForExit();
                                });
                            }
                        });

                        return true;
                    },
                },
                buildHomeAssistantSceneResult("air_purifier_toggle", "turn_on", "script.office_air_purifier_toggle"),
                buildHomeAssistantSceneResult("fan_toggle", "toggle", "switch.office_small_fan"),
                buildHomeAssistantSceneResult("monitor_lights_off", "turn_off", "light.monitor_lights"),
                buildHomeAssistantSceneResult("nighttime", "turn_on", "script.office_nighttime"),
                buildHomeAssistantSceneResult("office_cold", "turn_on", "script.office_cold"),
                buildHomeAssistantSceneResult("office_hot", "turn_on", "script.office_hot"),
                buildHomeAssistantSceneResult("office_lights_daylight", "turn_on", "script.office_lights_to_daylight"),
                buildHomeAssistantSceneResult("office_lights_neutral", "turn_on", "script.office_lights_to_neutral"),
                buildHomeAssistantSceneResult("office_lights_off", "turn_on", "script.office_lights_to_off"),
                buildHomeAssistantSceneResult("office_lights_warm", "turn_on", "script.office_lights_to_warm"),
                buildHomeAssistantSceneResult("outside_bright", "turn_on", "script.office_outside_bright"),
                buildHomeAssistantSceneResult("outside_overcast", "turn_on", "script.office_outside_overcast"),
                buildHomeAssistantSceneResult("outside_dark", "turn_on", "script.office_outside_dark"),

            });

            return results;
        }


        /*
         *  Get the API secret/access token from the plugin's settings file and displays an error message if the API 
         *  secret is not found
         *
         *  @returns - The API secret, or null if the settings file doesn't exist
         */
        /*
        private Settings getSettings()
        {
            var configFile = Environment.ExpandEnvironmentVariables(@"%APPDATA%\FlowLauncher\Settings\Plugins\Flow.Launcher.Plugin.Kummer\Settings.json");
            Settings settings = new Settings(); 

            try
            {
                if (System.IO.File.Exists(configFile))
                {
                    //settings = JsonSerializer.Deserialize<Settings>(File.OpenRead(configFile));
                    // TODO- validate that this works.
                    using (StreamReader reader = new StreamReader(configFile))
                    {
                        string json = reader.ReadToEnd();
                        _context.API.LogInfo("Main.cs", $"json = {json}", "getSettings");
                        settings = JsonConvert.DeserializeObject<Settings>(json);
                    }
                }
                settings.ConfigFile = configFile;

                //if (string.IsNullOrEmpty(settings.SlackTokens))
                //{
                //    handleError("Task was not created because could not find the API secret.", $"Slack tokens is empty", new ArgumentNullException("slackTokens"), "Main.cs", "getSettings");
                //}
            }
            catch (Exception ex)
            {
                handleError("Task was not created because could not find the API secret.", $"Exception getting Slack Tokens from {configFile}", ex, "Main.cs", "getSettings");
            }

            return settings;
        }
        */


        /*
         *  Handle an error. Definitely shows an error message, and logs the exception if that information is provided.
         *  
         *  @param errorMessage - The error message to show the user
         *  @param logMessage - The error message to log
         *  @param ex - The exception to log
         *  @param className - The name of the class that threw the error
         *  @param methodName - The name of the method that threw the error
         */
        private void handleError(string errorMessage, string logMessage = null, Exception ex = null, string className = null, string methodName = null)
        {
            if (className != null)
            {
                _context.API.LogException(className, logMessage, ex, methodName);
            }
            _context.API.ShowMsgError($"Plugin.Kummer Error", errorMessage);
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
        private async void executeHomeAssistantCommand(string service, string entityId)
        {
            var domain = entityId.Split('.')[0];
            var url = $"{_settings.HomeAssistantUrl}/api/services/{domain}/{service}";
            var requestData = $"{{\"entity_id\":\"{entityId}\"}}";

            _context.API.LogInfo("Main.cs", $"Posting to {url} with {requestData}", "executeHACommand");
            await _httpClients[HTTP_CLIENT_ENUMS.HOME_ASSISTANT].PostAsync(url, new StringContent(requestData, Encoding.UTF8, "application/json"));
        }


        /*
         *  Set my Slack presence
         *  
         *  @param homeComputer - Boolean that is true if we're running on my home computer, false means we're running
         *                        on my work computer. This determines if we use the HTTP client for my home Slack 
         *                        account or the one for my work Slack account.
         *  @param presence - Slack presence (auto|away)
         */
        private async void setSlackPresence(bool homeComputer, string presence)
        {
            var url = $"https://slack.com/api/users.setPresence?presence={presence}";

            _context.API.LogInfo("Main.cs", $"Posting to {url}", "setSlackPresence");

            var httpIndex = homeComputer ? HTTP_CLIENT_ENUMS.SLACK_HOME : HTTP_CLIENT_ENUMS.SLACK_WORK;
            await _httpClients[httpIndex].PostAsync(url, new StringContent(""));
        }


        /*
         *  Set my Slack status
         *  
         *  @param homeComputer - Boolean that is true if we're running on my home computer, false means we're running
         *                        on my work computer. This determines if we use the HTTP client for my home Slack 
         *                        account or the one for my work Slack account.
         *  @param emoji - Slack emoji to set my status to (i.e. ":palm_tree")
         *  @param statusText - The text to set my Slack status to
         *  
         *  Notes
         *    - Cannot change the format of requestData, must use single quotes
         */
        private async void setSlackStatus(bool homeComputer, string emoji, string statusText)
        {
            var url = "https://slack.com/api/users.profile.set";
            var requestData = $"profile={{'status_emoji':'{emoji}','status_text':'{statusText}'}}";

            _context.API.LogInfo("Main.cs", $"Posting to {url} with {requestData}", "setSlackStatus");

            var httpIndex = homeComputer ? HTTP_CLIENT_ENUMS.SLACK_HOME : HTTP_CLIENT_ENUMS.SLACK_WORK;
            await _httpClients[httpIndex].PostAsync(url, new StringContent(requestData, Encoding.UTF8, "application/x-www-form-urlencoded"));
        }


        /*
         *  These functions are required by ISavable to display and save settings
         */
        public Control CreateSettingPanel() => new KummerSettings(_settings);
        //public void Save() => _settings?.Save();


        /*
         *  These functions are required by IPluginI18n
         */
        public string GetTranslatedPluginTitle() => _context.API.GetTranslation($"{PLUGIN_KEY_PREFIX}_plugin_name");
        public string GetTranslatedPluginDescription() => _context.API.GetTranslation($"{PLUGIN_KEY_PREFIX}_plugin_description");

    }
}
