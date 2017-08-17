﻿using ByteSizeLib;
using Discord;
using Discord.Addons.EmojiTools;
using Discord.Commands;
using Discord.WebSocket;
using EveLibCore;
using Matrix.Xmpp.Chatstates;
using Matrix.Xmpp.Client;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using MySql.Data.MySqlClient;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Opux
{
    internal class Functions
    {
        internal static DateTime lastAuthCheck = DateTime.Now;
        internal static DateTime lastFeedCheck = DateTime.Now;
        internal static DateTime nextNotificationCheck = DateTime.FromFileTime(0);
        internal static int lastNotification;
        internal static bool avaliable = false;
        internal static bool running = false;
        internal static bool authRunning = false;
        internal static bool jabberRunning = false;
        internal static string motdtopic;
        internal static DateTime lastTopicCheck = DateTime.Now;

        //Timer is setup here
        #region Timer stuff
        public async static void RunTick(Object stateInfo)
        {
            try
            {
                if (!running && avaliable)
                {
                    running = true;
                    await Async_Tick(stateInfo);
                }
            }
            catch (Exception ex)
            {
                await Client_Log(new LogMessage(LogSeverity.Error, "Aync_Tick", ex.Message, ex));
            }
        }

        private async static Task Async_Tick(object args)
        {
            try
            {
                if (Convert.ToBoolean(Program.Settings.GetSection("config")["authWeb"]))
                {
                    await AuthWeb();
                }
                if (Convert.ToBoolean(Program.Settings.GetSection("config")["authCheck"]))
                {
                    await AuthCheck(null);
                }
                if (Convert.ToBoolean(Program.Settings.GetSection("config")["killFeed"]))
                {
                    await KillFeed(null);
                }
                if (Convert.ToBoolean(Program.Settings.GetSection("config")["notificationFeed"]))
                {
                    await NotificationFeed(null);
                }
                if (Convert.ToBoolean(Program.Settings.GetSection("config")["fleetup"]))
                {
                    await FleetUp();
                }
                if (Convert.ToBoolean(Program.Settings.GetSection("config")["updatetopic"]))
                {
                    await TopicMOTD(null);
                }
                if (Convert.ToBoolean(Program.Settings.GetSection("config")["jabber"]))
                {
                    await Jabber();
                }

                

                running = false;
            }
            catch (Exception ex)
            {
                await Client_Log(new LogMessage(LogSeverity.Error, "Aync_Tick", ex.Message, ex));
                running = false;
            }
        }
        #endregion

        //Needs logging to a file added
        #region Logger
        internal async static Task Client_Log(LogMessage arg)
        {
            try
            {

                var path = Path.Combine(AppContext.BaseDirectory, "logs");
                var file = Path.Combine(path, $"{arg.Source}.log");

                if (!Directory.Exists(path))
                {
                    Directory.CreateDirectory(path);
                }
                if (!File.Exists(file))
                {
                    File.Create(file);
                }

                var cc = Console.ForegroundColor;

                switch (arg.Severity)
                {
                    case LogSeverity.Critical:
                    case LogSeverity.Error:
                        Console.ForegroundColor = ConsoleColor.Red;

                        break;
                    case LogSeverity.Warning:
                        Console.ForegroundColor = ConsoleColor.Yellow;
                        break;
                    case LogSeverity.Info:
                        Console.ForegroundColor = ConsoleColor.White;
                        break;
                    case LogSeverity.Verbose:
                    case LogSeverity.Debug:
                        Console.ForegroundColor = ConsoleColor.DarkGray;
                        break;
                }

                using (StreamWriter logFile = new StreamWriter(File.Open(file, FileMode.Append, FileAccess.Write, FileShare.Write), Encoding.UTF8))
                {
                    if (arg.Exception != null)
                    {
                        await logFile.WriteLineAsync($"{DateTime.Now,-19} [{arg.Severity,8}]: {arg.Message} {Environment.NewLine}{arg.Exception}");
                    }
                    else
                    {
                        await logFile.WriteLineAsync($"{DateTime.Now,-19} [{arg.Severity,8}]: {arg.Message}");
                    }
                }

                Console.WriteLine($"{DateTime.Now,-19} [{arg.Severity,8}] [{arg.Source}]: {arg.Message}");
                Console.ForegroundColor = cc;
                
            }
            catch { }
        }
        #endregion

        //Events are attached here
        #region EVENTS
        internal async static Task Event_UserJoined(SocketGuildUser arg)
        {
            var channel = (dynamic)arg.Guild.DefaultChannel;
            var authurl = Program.Settings.GetSection("auth")["authurl"];
            if (!String.IsNullOrWhiteSpace(authurl))
            {
                await channel.SendMessageAsync($"Welcome {arg.Mention} to the server, To gain access please auth at {authurl} ");
            }
            else
            {
                await channel.SendMessageAsync($"Welcome {arg.Mention} to the server");
            }
        }

        internal static Task Ready()
        {
            avaliable = true;
            return Task.CompletedTask;
        }
        #endregion

        //Auth
        #region AuthWeb
        internal static System.Net.Http.HttpListener listener;

        internal async static Task AuthWeb()
        {
            var callbackurl = (string)Program.Settings.GetSection("auth")["callbackurl"];
            var client_id = (string)Program.Settings.GetSection("auth")["client_id"];
            var secret = (string)Program.Settings.GetSection("auth")["secret"];
            var url = (string)Program.Settings.GetSection("auth")["url"];
            var port = Convert.ToInt32(Program.Settings.GetSection("auth")["port"]);

            if (listener == null || !listener.IsListening)
            {
                await Client_Log(new LogMessage(LogSeverity.Info, "AuthWeb", "Starting AuthWeb Server"));
                listener = new System.Net.Http.HttpListener(IPAddress.Any, port);

                listener.Request += async (sender, context) =>
                {
                    var request = context.Request;
                    var response = context.Response;
                    if (request.HttpMethod == HttpMethod.Get.ToString())
                    {
                        if (request.Url.LocalPath == "/")
                        {
                            await response.WriteContentAsync("<!doctype html>" +
                                "<html>" +
                                "<head>" +
                                "    <title>Discord Authenticator</title>" +
                                "    <meta name=\"viewport\" content=\"width=device-width\">" +
                                "    <link rel=\"stylesheet\" href=\"https://djyhxgczejc94.cloudfront.net/frameworks/bootstrap/3.0.0/themes/cirrus/bootstrap.min.css\">" +
                                "    <script type=\"text/javascript\" src=\"https://ajax.googleapis.com/ajax/libs/jquery/2.0.3/jquery.min.js\"></script>" +
                                "    <script type=\"text/javascript\" src=\"https://netdna.bootstrapcdn.com/bootstrap/3.1.1/js/bootstrap.min.js\"></script>" +
                                "    <style type=\"text/css\">" +
                                "        /* Space out content a bit */" +
                                "        body {" +
                                "            padding-top: 20px;" +
                                "            padding-bottom: 20px;" +
                                "        }" +
                                "        /* Everything but the jumbotron gets side spacing for mobile first views */" +
                                "        .header, .marketing, .footer {" +
                                "            padding-left: 15px;" +
                                "            padding-right: 15px;" +
                                "        }" +
                                "       /* Custom page header */" +
                                "        .header {" +
                                "            border-bottom: 1px solid #e5e5e5;" +
                                "        }" +
                                "        /* Make the masthead heading the same height as the navigation */" +
                                "        .header h3 {" +
                                "            margin-top: 0;" +
                                "            margin-bottom: 0;" +
                                "            line-height: 40px;" +
                                "            padding-bottom: 19px;" +
                                "        }" +
                                "        /* Custom page footer */" +
                                "        .footer {" +
                                "            padding-top: 19px;" +
                                "            color: #777;" +
                                "            border-top: 1px solid #e5e5e5;" +
                                "        }" +
                                "        /* Customize container */" +
                                "        @media(min-width: 768px) {" +
                                "            .container {" +
                                "                max-width: 730px;" +
                                "            }" +
                                "        }" +
                                "        .container-narrow > hr {" +
                                "            margin: 30px 0;" +
                                "        }" +
                                "        /* Main marketing message and sign up button */" +
                                "        .jumbotron {" +
                                "            text-align: center;" +
                                "            border-bottom: 1px solid #e5e5e5;" +
                                "        }" +
                                "        .jumbotron .btn {" +
                                "            font-size: 21px;" +
                                "            padding: 14px 24px;" +
                                "            color: #0D191D;" +
                                "        }" +
                                "        /* Supporting marketing content */" +
                                "        .marketing {" +
                                "            margin: 40px 0;" +
                                "        }" +
                                "        .marketing p + h4 {" +
                                "            margin-top: 28px;" +
                                "        }" +
                                "        /* Responsive: Portrait tablets and up */" +
                                "        @media screen and(min-width: 768px) {" +
                                "            /* Remove the padding we set earlier */" +
                                "            .header, .marketing, .footer {" +
                                "                padding-left: 0;" +
                                "                padding-right: 0;" +
                                "            }" +
                                "            /* Space out the masthead */" +
                                "            .header {" +
                                "                margin-bottom: 30px;" +
                                "            }" +
                                "            /* Remove the bottom border on the jumbotron for visual effect */" +
                                "            .jumbotron {" +
                                "                border-bottom: 0;" +
                                "            }" +
                                "        }" +
                                "    </style>" +
                                "</head>" +
                                "" +
                                "<body background=\"img/background.jpg\">" +
                                "<div class=\"container\">" +
                                "    <div class=\"header\">" +
                                "        <ul class=\"nav nav-pills pull-right\"></ul>" +
                                "    </div>" +
                                "    <div class=\"jumbotron\">" +
                                "        <h1>Discord</h1>" +
                                "        <p class=\"lead\">Click the button below to login with your EVE Online account.</p>" +
                                "        <p><a href=\"https://login.eveonline.com/oauth/authorize?response_type=code&amp;redirect_uri=" + callbackurl + "&amp;client_id=" + client_id + "\"><img src=\"https://images.contentful.com/idjq7aai9ylm/4fSjj56uD6CYwYyus4KmES/4f6385c91e6de56274d99496e6adebab/EVE_SSO_Login_Buttons_Large_Black.png\"/></a></p>" +
                                "    </div>" +
                                "</div>" +
                                "<!-- /container -->" +
                                "</body>" +
                                "</html>");
                        }
                        else if (request.Url.LocalPath == "/callback.php")
                        {
                            var assembly = Assembly.GetEntryAssembly();
                            var temp = assembly.GetManifestResourceNames();
                            var resource = assembly.GetManifestResourceStream("Opux.Discord-01.png");
                            var buffer = new byte[resource.Length];
                            resource.Read(buffer, 0, Convert.ToInt32(resource.Length));
                            var image = Convert.ToBase64String(buffer);
                            string accessToken = "";
                            string responseString;
                            string verifyString;
                            var uid = GetUniqID();
                            var code = "";
                            var add = false;

                            if (!String.IsNullOrWhiteSpace(request.Url.Query))
                            {
                                code = request.Url.Query.TrimStart('?').Split('=')[1];

                                using (HttpClient tokenclient = new HttpClient())
                                {
                                    var values = new Dictionary<string, string>
                                    {
                                        { "grant_type", "authorization_code" },
                                        { "code", $"{code}"}
                                    };
                                    tokenclient.DefaultRequestHeaders.Add("Authorization", $"Basic {Convert.ToBase64String(Encoding.UTF8.GetBytes(client_id + ":" + secret))}");
                                    var content = new FormUrlEncodedContent(values);
                                    var tokenresponse = await tokenclient.PostAsync("https://login.eveonline.com/oauth/token", content);
                                    responseString = await tokenresponse.Content.ReadAsStringAsync();
                                    accessToken = (string)JObject.Parse(responseString)["access_token"];
                                }
                                using (HttpClient verifyclient = new HttpClient())
                                {
                                    verifyclient.DefaultRequestHeaders.Add("Authorization", $"Bearer {accessToken}");
                                    var tokenresponse = await verifyclient.GetAsync("https://login.eveonline.com/oauth/verify");
                                    verifyString = await tokenresponse.Content.ReadAsStringAsync();

                                    var authgroups = Program.Settings.GetSection("auth").GetSection("authgroups").GetChildren().ToList();
                                    var corps = new Dictionary<string, string>();
                                    var alliance = new Dictionary<string, string>();

                                    foreach (var config in authgroups)
                                    {
                                        var configChildren = config.GetChildren();

                                        var corpID = configChildren.FirstOrDefault(x => x.Key == "corpID").Value ?? "";
                                        var allianceID = configChildren.FirstOrDefault(x => x.Key == "allianceID").Value ?? "";
                                        var corpMemberRole = configChildren.FirstOrDefault(x => x.Key == "corpMemberRole").Value ?? "";
                                        var allianceMemberRole = configChildren.FirstOrDefault(x => x.Key == "allianceMemberRole").Value ?? "";

                                        if (Convert.ToInt32(corpID) != 0)
                                        {
                                            corps.Add(corpID, corpMemberRole);
                                        }
                                        if (Convert.ToInt32(allianceID) != 0)
                                        {
                                            alliance.Add(allianceID, allianceMemberRole);
                                        }

                                    }

                                    var CharacterID = JObject.Parse(verifyString)["CharacterID"];
                                    JObject characterDetails;
                                    JObject corporationDetails;
                                    JObject allianceDetails;

                                    using (HttpClient webclient = new HttpClient())
                                    using (HttpResponseMessage _characterDetails = await webclient.GetAsync($"https://esi.tech.ccp.is/latest/characters/{CharacterID}"))
                                    using (HttpContent _characterDetailsContent = _characterDetails.Content)
                                    {
                                        var allianceID = "";
                                        var corpID = "";
                                        characterDetails = JObject.Parse(await _characterDetailsContent.ReadAsStringAsync());
                                        characterDetails.TryGetValue("corporation_id", out JToken corporationid);
                                        using (HttpResponseMessage _corporationDetails = await webclient.GetAsync($"https://esi.tech.ccp.is/latest/corporations/{corporationid}"))
                                        using (HttpContent _corporationDetailsContent = _corporationDetails.Content)
                                        {
                                            corporationDetails = JObject.Parse(await _corporationDetailsContent.ReadAsStringAsync());
                                            corporationDetails.TryGetValue("alliance_id", out JToken allianceid);
                                            string i = (allianceid.IsNullOrEmpty() ? "0" : allianceid.ToString());
                                            string c = (corporationid.IsNullOrEmpty() ? "0" : corporationid.ToString());
                                            allianceID = i;
                                            corpID = c;
                                            if (allianceID != "0")
                                            {
                                                using (HttpResponseMessage _allianceDetails = await webclient.GetAsync($"https://esi.tech.ccp.is/latest/alliances/{allianceid}"))
                                                using (HttpContent _allianceDetailsContent = _allianceDetails.Content)
                                                {
                                                    allianceDetails = JObject.Parse(await _allianceDetailsContent.ReadAsStringAsync());
                                                }
                                            }
                                        }

                                        if (corps.ContainsKey(corpID))
                                        {
                                            add = true;
                                        }
                                        if (alliance.ContainsKey(allianceID))
                                        {
                                            add = true;
                                        }
                                    }
                                    if (add && (string)JObject.Parse(responseString)["error"] != "invalid_request" && (string)JObject.Parse(verifyString)["error"] != "invalid_token")
                                    {
                                        var characterID = CharacterID;
                                        characterDetails.TryGetValue("corporation_id", out JToken corporationid);
                                        corporationDetails.TryGetValue("alliance_id", out JToken allianceid);
                                        var authString = uid;
                                        var active = "1";
                                        var dateCreated = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

                                        var query = "INSERT INTO pendingUsers(characterID, corporationID, allianceID, authString, groups, active, dateCreated) " +
                                        $"VALUES (\"{characterID}\", \"{corporationid}\", \"{allianceid}\", \"{authString}\", \"[]\", \"{active}\", \"{dateCreated}\") ON DUPLICATE KEY UPDATE " +
                                        $"corporationID = \"{corporationid}\", allianceID = \"{allianceid}\", authString = \"{authString}\", groups = \"[]\", active = \"{active}\", dateCreated = \"{dateCreated}\"";
                                        var responce = await MysqlQuery(Program.Settings.GetSection("config")["connstring"], query);
                                        await response.WriteContentAsync("<!doctype html>" +
                                            "<html>" +
                                            "<head>" +
                                            "    <title>Discord Authenticator</title>" +
                                            "    <meta name=\"viewport\" content=\"width=device-width\">" +
                                            "    <link rel=\"stylesheet\" href=\"https://djyhxgczejc94.cloudfront.net/frameworks/bootstrap/3.0.0/themes/cirrus/bootstrap.min.css\">" +
                                            "    <script type=\"text/javascript\" src=\"https://ajax.googleapis.com/ajax/libs/jquery/2.0.3/jquery.min.js\"></script>" +
                                            "    <script type=\"text/javascript\" src=\"https://netdna.bootstrapcdn.com/bootstrap/3.1.1/js/bootstrap.min.js\"></script>" +
                                            "    <style type=\"text/css\">" +
                                            "        /* Space out content a bit */" +
                                            "        body {" +
                                            "            padding-top: 20px;" +
                                            "            padding-bottom: 20px;" +
                                            "        }" +
                                            "        /* Everything but the jumbotron gets side spacing for mobile first views */" +
                                            "        .header, .marketing, .footer {" +
                                            "            padding-left: 15px;" +
                                            "            padding-right: 15px;" +
                                            "        }" +
                                            "        /* Custom page header */" +
                                            "        .header {" +
                                            "            border-bottom: 1px solid #e5e5e5;" +
                                            "        }" +
                                            "        /* Make the masthead heading the same height as the navigation */" +
                                            "        .header h3 {" +
                                            "            margin-top: 0;" +
                                            "            margin-bottom: 0;" +
                                            "            line-height: 40px;" +
                                            "            padding-bottom: 19px;" +
                                            "        }" +
                                            "        /* Custom page footer */" +
                                            "        .footer {" +
                                            "            padding-top: 19px;" +
                                            "            color: #777;" +
                                            "            border-top: 1px solid #e5e5e5;" +
                                            "        }" +
                                            "        /* Customize container */" +
                                            "        @media(min-width: 768px) {" +
                                            "            .container {" +
                                            "                max-width: 730px;" +
                                            "            }" +
                                            "        }" +
                                            "        .container-narrow > hr {" +
                                            "            margin: 30px 0;" +
                                            "        }" +
                                            "        /* Main marketing message and sign up button */" +
                                            "        .jumbotron {" +
                                            "            text-align: center;" +
                                            "            border-bottom: 1px solid #e5e5e5;" +
                                            "            color: #0D191D;" +
                                            "        }" +
                                            "        .jumbotron .btn {" +
                                            "            font-size: 21px;" +
                                            "            padding: 14px 24px;" +
                                            "        }" +
                                            "        /* Supporting marketing content */" +
                                            "        .marketing {" +
                                            "            margin: 40px 0;" +
                                            "        }" +
                                            "        .marketing p + h4 {" +
                                            "            margin-top: 28px;" +
                                            "        }" +
                                            "        /* Responsive: Portrait tablets and up */" +
                                            "        @media screen and(min-width: 768px) {" +
                                            "            /* Remove the padding we set earlier */" +
                                            "            .header, .marketing, .footer {" +
                                            "                padding-left: 0;" +
                                            "                padding-right: 0;" +
                                            "            }" +
                                            "            /* Space out the masthead */" +
                                            "            .header {" +
                                            "                margin-bottom: 30px;" +
                                            "            }" +
                                            "            /* Remove the bottom border on the jumbotron for visual effect */" +
                                            "            .jumbotron {" +
                                            "                border-bottom: 0;" +
                                            "            }" +
                                            "        }" +
                                            "    </style>" +
                                            "</head>" +
                                            "<body>" +
                                            "<div class=\"container\">" +
                                            "    <div class=\"header\">" +
                                            "        <ul class=\"nav nav-pills pull-right\"></ul>" +
                                            "    </div>" +
                                            "    <div class=\"jumbotron\">" +
                                            "        <h1>Discord</h1>" +
                                            "        <p class=\"lead\">Sign in complete.</p>" +
                                            "        <p>If you're not already signed into the server use the link below to get invited. (or right click and copy-link for the Windows/OSX Client)</p>" +
                                            "        <p><a href=\"" + url + "\" target=\"_blank\"><img src=\"data:image/png;base64," + image + "\" width=\"350px\"/></a></p>" +
                                            "        <p>Once you're in chat copy and paste the entire line below to have the bot add you to the correct roles.</p>" +
                                            "        <p><b>!auth " + uid + "</b></p>" +
                                            "    </div>" +
                                            "</div>" +
                                            "<!-- /container -->" +
                                            "</body>" +
                                            "</html>");
                                    }
                                    else
                                    {
                                        var message = "ERROR";
                                        if (!add)
                                        {
                                            message = "You are not Corp/Alliance or Blue";
                                        }
                                        await response.WriteContentAsync("<!doctype html>" +
                                           "<html>" +
                                           "<head>" +
                                           "    <title>Discord Authenticator</title>" +
                                           "    <meta name=\"viewport\" content=\"width=device-width\">" +
                                           "    <link rel=\"stylesheet\" href=\"https://djyhxgczejc94.cloudfront.net/frameworks/bootstrap/3.0.0/themes/cirrus/bootstrap.min.css\">" +
                                           "    <script type=\"text/javascript\" src=\"https://ajax.googleapis.com/ajax/libs/jquery/2.0.3/jquery.min.js\"></script>" +
                                           "    <script type=\"text/javascript\" src=\"https://netdna.bootstrapcdn.com/bootstrap/3.1.1/js/bootstrap.min.js\"></script>" +
                                           "    <style type=\"text/css\">" +
                                           "        /* Space out content a bit */" +
                                           "        body {" +
                                           "            padding-top: 20px;" +
                                           "            padding-bottom: 20px;" +
                                           "        }" +
                                           "        /* Everything but the jumbotron gets side spacing for mobile first views */" +
                                           "        .header, .marketing, .footer {" +
                                           "            padding-left: 15px;" +
                                           "            padding-right: 15px;" +
                                           "        }" +
                                           "        /* Custom page header */" +
                                           "        .header {" +
                                           "            border-bottom: 1px solid #e5e5e5;" +
                                           "        }" +
                                           "        /* Make the masthead heading the same height as the navigation */" +
                                           "        .header h3 {" +
                                           "            margin-top: 0;" +
                                           "            margin-bottom: 0;" +
                                           "            line-height: 40px;" +
                                           "            padding-bottom: 19px;" +
                                           "        }" +
                                           "        /* Custom page footer */" +
                                           "        .footer {" +
                                           "            padding-top: 19px;" +
                                           "            color: #777;" +
                                           "            border-top: 1px solid #e5e5e5;" +
                                           "        }" +
                                           "        /* Customize container */" +
                                           "        @media(min-width: 768px) {" +
                                           "            .container {" +
                                           "                max-width: 730px;" +
                                           "            }" +
                                           "        }" +
                                           "        .container-narrow > hr {" +
                                           "            margin: 30px 0;" +
                                           "        }" +
                                           "        /* Main marketing message and sign up button */" +
                                           "        .jumbotron {" +
                                           "            text-align: center;" +
                                           "            border-bottom: 1px solid #e5e5e5;" +
                                           "            color: #0D191D;" +
                                           "        }" +
                                           "        .jumbotron .btn {" +
                                           "            font-size: 21px;" +
                                           "            padding: 14px 24px;" +
                                           "        }" +
                                           "        /* Supporting marketing content */" +
                                           "        .marketing {" +
                                           "            margin: 40px 0;" +
                                           "        }" +
                                           "        .marketing p + h4 {" +
                                           "            margin-top: 28px;" +
                                           "        }" +
                                           "        /* Responsive: Portrait tablets and up */" +
                                           "        @media screen and(min-width: 768px) {" +
                                           "            /* Remove the padding we set earlier */" +
                                           "            .header, .marketing, .footer {" +
                                           "                padding-left: 0;" +
                                           "                padding-right: 0;" +
                                           "            }" +
                                           "            /* Space out the masthead */" +
                                           "            .header {" +
                                           "                margin-bottom: 30px;" +
                                           "            }" +
                                           "            /* Remove the bottom border on the jumbotron for visual effect */" +
                                           "            .jumbotron {" +
                                           "                border-bottom: 0;" +
                                           "            }" +
                                           "        }" +
                                           "    </style>" +
                                           "</head>" +
                                           "<body>" +
                                           "<div class=\"container\">" +
                                           "    <div class=\"header\">" +
                                           "        <ul class=\"nav nav-pills pull-right\"></ul>" +
                                           "    </div>" +
                                           "    <div class=\"jumbotron\">" +
                                           "        <h1>Discord</h1>" +
                                           "        <p class=\"lead\">Sign in ERROR.</p>" +
                                           "        <p>" + message + "</p>" +
                                           "    </div>" +
                                           "</div>" +
                                           "<!-- /container -->" +
                                           "</body>" +
                                           "</html>");
                                    }
                                }
                            }
                        }
                    }
                    else
                    {
                        response.MethodNotAllowed();
                    }
                // Close the HttpResponse to send it back to the client.
                response.Close();
                };
                listener.Start();
            }
            
        }

        private static string GetUniqID()
        {
            var ts = (DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0));
            double t = ts.TotalMilliseconds / 1000;

            int a = (int)Math.Floor(t);
            int b = (int)((t - Math.Floor(t)) * 1000000);

            return a.ToString("x8") + b.ToString("x5");
        }
        #endregion

        //AuthUser
        #region AuthUser
        internal async static Task AuthUser(ICommandContext context, string remainder)
        {
            var query = $"SELECT * FROM pendingUsers WHERE authString=\"{remainder}\"";
            var responce = await MysqlQuery(Program.Settings.GetSection("config")["connstring"], query);
            if (responce.Count() == 0)
            {
                await context.Channel.SendMessageAsync($"{context.Message.Author.Mention} Key Invalid! Please auth using !auth");
            }
            else if (responce[0]["active"].ToString() == "0")
            {
                await context.Channel.SendMessageAsync($"{context.Message.Author.Mention} Key is not active Please re-auth using !auth");
            }
            else if (responce[0]["active"].ToString() == "1")
            {
                var authgroups = Program.Settings.GetSection("auth").GetSection("authgroups").GetChildren().ToList();
                var corps = new Dictionary<string, string>();
                var alliance = new Dictionary<string, string>();

                foreach (var config in authgroups)
                {
                    var configChildren = config.GetChildren();

                    var corpID = configChildren.FirstOrDefault(x => x.Key == "corpID").Value ?? "";
                    var allianceID = configChildren.FirstOrDefault(x => x.Key == "allianceID").Value ?? "";
                    var corpMemberRole = configChildren.FirstOrDefault(x => x.Key == "corpMemberRole").Value ?? "";
                    var allianceMemberRole = configChildren.FirstOrDefault(x => x.Key == "allianceMemberRole").Value ?? "";

                    if (Convert.ToInt32(corpID) != 0)
                    {
                        corps.Add(corpID, corpMemberRole);
                    }
                    if (Convert.ToInt32(allianceID) != 0)
                    {
                        alliance.Add(allianceID, allianceMemberRole);
                    }

                }

                var CharacterID = responce[0]["characterID"].ToString();
                JObject characterDetails;
                JObject corporationDetails;
                JObject allianceDetails;

                using (HttpClient webclient = new HttpClient())
                using (HttpResponseMessage _characterDetails = await webclient.GetAsync($"https://esi.tech.ccp.is/latest/characters/{CharacterID}"))
                using (HttpContent _characterDetailsContent = _characterDetails.Content)
                {
                    var allianceID = "";
                    var corpID = "";
                    characterDetails = JObject.Parse(await _characterDetailsContent.ReadAsStringAsync());
                    characterDetails.TryGetValue("corporation_id", out JToken corporationid);
                    using (HttpResponseMessage _corporationDetails = await webclient.GetAsync($"https://esi.tech.ccp.is/latest/corporations/{corporationid}"))
                    using (HttpContent _corporationDetailsContent = _corporationDetails.Content)
                    {
                        corporationDetails = JObject.Parse(await _corporationDetailsContent.ReadAsStringAsync());
                        corporationDetails.TryGetValue("alliance_id", out JToken allianceid);
                        string i = (allianceid.IsNullOrEmpty() ? "0" : allianceid.ToString());
                        string c = (corporationid.IsNullOrEmpty() ? "0" : corporationid.ToString());
                        allianceID = i;
                        corpID = c;
                        if (allianceID != "0")
                        {
                            using (HttpResponseMessage _allianceDetails = await webclient.GetAsync($"https://esi.tech.ccp.is/latest/alliances/{allianceid}"))
                            using (HttpContent _allianceDetailsContent = _allianceDetails.Content)
                            {
                                allianceDetails = JObject.Parse(await _allianceDetailsContent.ReadAsStringAsync());
                            }
                        }
                    }

                    var enable = false;

                    if (corps.ContainsKey(corpID))
                    {
                        enable = true;
                    }
                    if (alliance.ContainsKey(allianceID))
                    {
                        enable = true;
                    }

                    if (enable)
                    {
                        var rolesToAdd = new List<SocketRole>();
                        var rolesToTake = new List<SocketRole>();

                        try
                        {
                            var guildID = Convert.ToUInt64(Program.Settings.GetSection("config")["guildId"]);
                            var alertChannel = Convert.ToUInt64(Program.Settings.GetSection("auth")["alertChannel"]);

                            var discordGuild = (SocketGuild)context.Guild;
                            var discordUser = (SocketGuildUser)context.Message.Author;

                            //Check for Corp roles
                            if (corps.ContainsKey(corpID))
                            {
                                var cinfo = corps.FirstOrDefault(x => x.Key == corpID);
                                rolesToAdd.Add(discordGuild.Roles.FirstOrDefault(x => x.Name == cinfo.Value));
                            }

                            //Check for Alliance roles
                            if (alliance.ContainsKey(allianceID))
                            {
                                var ainfo = alliance.FirstOrDefault(x => x.Key == allianceID);
                                rolesToAdd.Add(discordGuild.Roles.FirstOrDefault(x => x.Name == ainfo.Value));
                            }

                            foreach (var r in rolesToAdd)
                            {
                                if (discordUser.Roles.FirstOrDefault(x => x.Id == r.Id) == null)
                                {
                                    var channel = (dynamic)discordGuild.Channels.FirstOrDefault(x => x.Id == alertChannel);
                                    await channel.SendMessageAsync($"Granting Roles to {characterDetails["name"]}");
                                    await discordUser.AddRolesAsync(rolesToAdd);
                                }
                            }
                            var query2 = $"UPDATE pendingUsers SET active=\"0\" WHERE authString=\"{remainder}\"";
                            var responce2 = await MysqlQuery(Program.Settings.GetSection("config")["connstring"], query2);

                            await context.Channel.SendMessageAsync($"{context.Message.Author.Mention},:white_check_mark: **Success**: " +
                                $"{characterDetails["name"]} has been successfully authed.");

                            var eveName = characterDetails["name"];
                            var characterID = CharacterID;
                            var discordID = discordUser.Id;
                            var active = "yes";
                            var addedOn = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

                            query2 = "INSERT INTO authUsers(eveName, characterID, discordID, role, active, addedOn) " +
                            $"VALUES (\"{eveName}\", \"{characterID}\", \"{discordID}\", \"[]\", \"{active}\", \"{addedOn}\") ON DUPLICATE KEY UPDATE " +
                            $"eveName = \"{eveName}\", discordID = \"{discordID}\", role = \"[]\", active = \"{active}\", addedOn = \"{addedOn}\"";

                            responce2 = await MysqlQuery(Program.Settings.GetSection("config")["connstring"], query2);

                            var corpTickers = Convert.ToBoolean(Program.Settings.GetSection("auth")["corpTickers"]);
                            var nameEnforce = Convert.ToBoolean(Program.Settings.GetSection("auth")["nameEnforce"]);

                            if (corpTickers || nameEnforce)
                            {
                                var Nickname = "";
                                if (corpTickers)
                                {
                                    Nickname = $"[{corporationDetails["ticker"]}] ";
                                }
                                if (nameEnforce)
                                {
                                    Nickname += $"{eveName}";
                                }
                                else
                                {
                                    Nickname += $"{discordUser.Username}";
                                }
                                await discordUser.ModifyAsync(x => x.Nickname = Nickname);
                            }
                        }

                        catch (Exception ex)
                        {
                            await Client_Log(new LogMessage(LogSeverity.Error, "authCheck", $"Failed adding Roles to User {characterDetails["name"]}, Reason: {ex.Message}", ex));
                        }
                    }
                }
            }
        }
        #endregion

        //Needs Corp and Standings added
        #region AuthCheck
        internal async static Task AuthCheck(ICommandContext Context)
        {
            //Check inactive users are correct
            if (DateTime.Now > lastAuthCheck.AddMilliseconds(Convert.ToInt32(Program.Settings.GetSection("config")["authInterval"]) * 1000 * 60) || Context != null)
            {
                try
                {
                    await Client_Log(new LogMessage(LogSeverity.Info, "authCheck", $"Running Auth Check"));
                    //Gather details about corps and alliance's to set roles for
                    var authgroups = Program.Settings.GetSection("auth").GetSection("authgroups").GetChildren().ToList();
                    var corps = new Dictionary<string, string>();
                    var alliance = new Dictionary<string, string>();

                    foreach (var config in authgroups)
                    {
                        var configChildren = config.GetChildren();

                        var corpID = configChildren.FirstOrDefault(x => x.Key == "corpID").Value ?? "";
                        var allianceID = configChildren.FirstOrDefault(x => x.Key == "allianceID").Value ?? "";
                        var corpMemberRole = configChildren.FirstOrDefault(x => x.Key == "corpMemberRole").Value ?? "";
                        var allianceMemberRole = configChildren.FirstOrDefault(x => x.Key == "allianceMemberRole").Value ?? "";

                        if (Convert.ToInt32(corpID) != 0)
                        {
                            corps.Add(corpID, corpMemberRole);
                        }
                        if (Convert.ToInt32(allianceID) != 0)
                        {
                            alliance.Add(allianceID, allianceMemberRole);
                        }

                    }

                    string query = "select * from authUsers";
                    var responce = await MysqlQuery(Program.Settings.GetSection("config")["connstring"], query);
                    if (responce.Count > 0)
                    {
                        foreach (var u in responce)
                        {
                            var exemptRoles = Program.Settings.GetSection("auth").GetSection("exempt").GetChildren().ToList();
                            var characterID = u["characterID"];
                            var discordID = u["discordID"];
                            var guildID = Convert.ToUInt64(Program.Settings.GetSection("config")["guildId"]);
                            var logchan = Convert.ToUInt64(Program.Settings.GetSection("auth")["alertChannel"]);
                            JObject characterDetails;
                            JObject corporationDetails;
                            JObject allianceDetails;

                            using (HttpClient webclient = new HttpClient())
                            using (HttpResponseMessage _characterDetails = await webclient.GetAsync($"https://esi.tech.ccp.is/latest/characters/{characterID}"))
                            using (HttpContent _characterDetailsContent = _characterDetails.Content)
                            {
                                var allianceID = "";
                                var corpID = "";
                                characterDetails = JObject.Parse(await _characterDetailsContent.ReadAsStringAsync());
                                characterDetails.TryGetValue("corporation_id", out JToken corporationid);
                                using (HttpResponseMessage _corporationDetails = await webclient.GetAsync($"https://esi.tech.ccp.is/latest/corporations/{corporationid}"))
                                using (HttpContent _corporationDetailsContent = _corporationDetails.Content)
                                {
                                    corporationDetails = JObject.Parse(await _corporationDetailsContent.ReadAsStringAsync());
                                    corporationDetails.TryGetValue("alliance_id", out JToken allianceid);
                                    string i = (allianceid.IsNullOrEmpty() ? "0" : allianceid.ToString());
                                    string c = (corporationid.IsNullOrEmpty() ? "0" : corporationid.ToString());
                                    allianceID = i;
                                    corpID = c;
                                    if (allianceID != "0")
                                    {
                                        using (HttpResponseMessage _allianceDetails = await webclient.GetAsync($"https://esi.tech.ccp.is/latest/alliances/{allianceid}"))
                                        using (HttpContent _allianceDetailsContent = _allianceDetails.Content)
                                        {
                                            allianceDetails = JObject.Parse(await _allianceDetailsContent.ReadAsStringAsync());
                                        }
                                    }
                                }

                                var discordGuild = Program.Client.Guilds.FirstOrDefault(X => X.Id == guildID);

                                var discordUser = discordGuild.Users.FirstOrDefault(x => x.Id == Convert.ToUInt64(u["discordID"]));

                                if (discordUser == null)
                                {
                                    string remquery = $"DELETE FROM authUsers WHERE discordID = {u["discordID"]}";
                                    var remresponce = await MysqlQuery(Program.Settings.GetSection("config")["connstring"], remquery);
                                    await Client_Log(new LogMessage(LogSeverity.Info, "authCheck", $"Removing {characterDetails["name"]} from Database as they have left discord"));
                                    continue;
                                }
                                else
                                {
                                    var rolesToAdd = new List<SocketRole>();
                                    var rolesToTake = new List<SocketRole>();

                                    try
                                    {
                                        rolesToTake.AddRange(discordUser.Roles);
                                        var exemptCheckRoles = new List<SocketRole>(rolesToTake);
                                        foreach (var r in exemptCheckRoles)
                                        {
                                            var name = r.Name;
                                            if (exemptRoles.FindAll(x => x.Key == name).Count > 0)
                                            {
                                                rolesToTake.Remove(rolesToTake.FirstOrDefault(x => x.Name == r.Name));
                                            }
                                        }
                                        rolesToTake.Remove(rolesToTake.FirstOrDefault(x => x.Name == "@everyone"));
                                        //Check for Corp roles
                                        if (corps.ContainsKey(corpID))
                                        {
                                            var cinfo = corps.FirstOrDefault(x => x.Key == corpID);
                                            rolesToAdd.Add(discordGuild.Roles.FirstOrDefault(x => x.Name == cinfo.Value));
                                        }

                                        //Check for Alliance roles
                                        if (alliance.ContainsKey(allianceID))
                                        {
                                            var ainfo = alliance.FirstOrDefault(x => x.Key == allianceID);
                                            rolesToAdd.Add(discordGuild.Roles.FirstOrDefault(x => x.Name == ainfo.Value));
                                        }

                                        foreach (var r in rolesToAdd)
                                        {
                                            if (discordUser.Roles.FirstOrDefault(x => x.Id == r.Id) == null)
                                            {

                                                var channel = (dynamic)discordGuild.Channels.FirstOrDefault(x => x.Id == logchan);
                                                await channel.SendMessageAsync($"Granting Roles to {characterDetails["name"]}");
                                                await discordUser.RemoveRolesAsync(rolesToTake);
                                                await discordUser.AddRolesAsync(rolesToAdd);
                                            }
                                        }

                                        var eveName = characterDetails["name"];

                                        var corpTickers = Convert.ToBoolean(Program.Settings.GetSection("auth")["corpTickers"]);
                                        var nameEnforce = Convert.ToBoolean(Program.Settings.GetSection("auth")["nameEnforce"]);

                                        if (corpTickers || nameEnforce)
                                        {
                                            var Nickname = "";
                                            if (corpTickers)
                                            {
                                                Nickname = $"[{corporationDetails["ticker"]}] ";
                                            }
                                            if (nameEnforce)
                                            {
                                                Nickname += $"{eveName}";
                                            }
                                            else
                                            {
                                                Nickname += $"{discordUser.Username}";
                                            }
                                            if (Nickname != discordUser.Nickname)
                                            {
                                                await discordUser.ModifyAsync(x => x.Nickname = Nickname);
                                            }
                                        }
                                    }

                                    catch (Exception ex)
                                    {
                                        await Client_Log(new LogMessage(LogSeverity.Error, "authCheck", $"Potential ESI Failiure for {u["eveName"]} skipping, Reason: {ex.Message}", ex));
                                        continue;
                                    }

                                    try
                                    {
                                        //Check if roles when should not have any
                                        if (!corps.ContainsKey(corporationid.ToString()) && !alliance.ContainsKey(allianceID.ToString()))
                                        {
                                            if (discordUser != null)
                                            {
                                                rolesToTake.AddRange(discordUser.Roles);
                                                var exemptCheckRoles = new List<SocketRole>(rolesToTake);
                                                foreach (var r in exemptCheckRoles)
                                                {
                                                    var name = r.Name;
                                                    if (exemptRoles.FindAll(x => x.Key == name).Count > 0)
                                                    {
                                                        rolesToTake.Remove(rolesToTake.FirstOrDefault(x => x.Name == r.Name));
                                                    }
                                                }
                                                rolesToTake.Remove(rolesToTake.FirstOrDefault(x => x.Name == "@everyone"));
                                                if (rolesToTake.Count > 0)
                                                {
                                                    var channel = (dynamic)discordGuild.Channels.FirstOrDefault(x => x.Id == logchan);
                                                    await channel.SendMessageAsync($"Taking Roles from {characterDetails["name"]}");
                                                    await discordUser.RemoveRolesAsync(rolesToTake);
                                                }
                                            }
                                            else
                                            {

                                            }
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        await Client_Log(new LogMessage(LogSeverity.Error, "authCheck", $"Failiure for {u["eveName"]} skipping, Reason: {ex.Message}", ex));
                                        continue;
                                    }
                                }
                            }
                            lastAuthCheck = DateTime.Now;
                        }
                    }
                    else
                    {
                        lastAuthCheck = DateTime.Now;
                    }
                }
                catch (Exception ex)
                {
                    //await Logger.logError(ex.Message);
                    await Client_Log(new LogMessage(LogSeverity.Error, "authCheck", ex.Message, ex));
                }
            }
            
        }
        #endregion

        //Complete
        #region killFeed
        private static async Task KillFeed(CommandContext Context)
        {
            try
            {
                lastFeedCheck = DateTime.Now;
                Dictionary<string, IEnumerable<IConfigurationSection>> feedGroups = new Dictionary<string, IEnumerable<IConfigurationSection>>();

                UInt64 guildID = Convert.ToUInt64(Program.Settings.GetSection("config")["guildId"]);
                UInt64 logchan = Convert.ToUInt64(Program.Settings.GetSection("auth")["alertChannel"]);
                var discordGuild = Program.Client.Guilds.FirstOrDefault(X => X.Id == guildID);
                var redisQID = Program.Settings.GetSection("killFeed")["reDisqID"].ToString();
                ITextChannel channel = null;
                using (HttpClient webclient = new HttpClient())
                using (HttpResponseMessage redisqResponse = await webclient.GetAsync(String.IsNullOrEmpty(redisQID) ? $"https://redisq.zkillboard.com/listen.php" : $"https://redisq.zkillboard.com/listen.php?queueID={redisQID}"))
                using (HttpContent _redisqResponseContent = redisqResponse.Content)
                {
                    if (redisqResponse.IsSuccessStatusCode)
                    {
                        var result = await _redisqResponseContent.ReadAsStringAsync();
                        var json = JObject.Parse(result);
                        var killmail = json["package"];
                        if (!killmail.IsNullOrEmpty())
                        {
                            if (killmail.IsNullOrEmpty())
                            {
                                await Client_Log(new LogMessage(LogSeverity.Debug, "killFeed", "Killmail malformed, Probably nothing to post."));
                                return;
                            }

                            var bigKillGlobal = Convert.ToInt64(Program.Settings.GetSection("killFeed")["bigKill"]);
                            var bigKillGlobalChan = Convert.ToUInt64(Program.Settings.GetSection("killFeed")["bigKillChannel"]);
                            var iD = killmail["killmail"]["killID_str"];
                            var killTime = killmail["killmail"]["killTime"];
                            var ship = killmail["killmail"]["victim"]["shipType"]["name"];
                            var value = (double)killmail["zkb"]["totalValue"];
                            var victimCharacter = killmail["killmail"]["victim"]["character"] ?? null;
                            var victimCorp = killmail["killmail"]["victim"]["corporation"];
                            var victimAlliance = killmail["killmail"]["victim"]["alliance"] ?? null;
                            var attackers = killmail["killmail"]["attackers"] ?? null;
                            var sysName = (string)killmail["killmail"]["solarSystem"]["name"];
                            var systemId = (string)killmail["killmail"]["solarSystem"]["id"];
                            var losses = Convert.ToBoolean(Program.Settings.GetSection("killFeed")["losses"]);
                            var radius = Convert.ToInt16(Program.Settings.GetSection("killFeed")["radius"]);
                            var radiusSystem = Program.Settings.GetSection("killFeed")["radiusSystem"];
                            var radiusChannel = Convert.ToUInt64(Program.Settings.GetSection("killFeed")["radiusChannel"]);

                            var post = false;
                            var globalBigKill = false;
                            var bigKill = false;
                            var radiusKill = false;
                            var jumpsAway = 0;

                            foreach (var i in Program.Settings.GetSection("killFeed").GetSection("groupsConfig").GetChildren().ToList())
                            {
                                var minimumValue = Convert.ToInt64(i["minimumValue"]);
                                var minimumLossValue = Convert.ToInt64(i["minimumLossValue"]);
                                var allianceID = Convert.ToInt32(i["allianceID"]);
                                var corpID = Convert.ToInt32(i["corpID"]);
                                var channelGroup = Convert.ToUInt64(i["channel"]);
                                var bigKillValue = Convert.ToInt64(i["bigKill"]);
                                var bigKillChannel = Convert.ToUInt64(i["bigKillChannel"]);
                                var SystemID = "0";

                                if (radius > 0)
                                {
                                    using (HttpClient webClient1 = new HttpClient())
                                    using (HttpResponseMessage SystemName = await webClient1.GetAsync(
                                        $"https://esi.tech.ccp.is/latest/search/?categories=solarsystem&datasource=tranquility" +
                                        $"&language=en-us&search={radiusSystem}&strict=false"))
                                    using (HttpContent SystemNameContent = SystemName.Content)

                                    {
                                        var httpresult = JObject.Parse(await SystemNameContent.ReadAsStringAsync());
                                        if (httpresult["error"] == null)
                                        {
                                            SystemID = httpresult["solarsystem"][0].ToString();

                                            using (HttpClient webClient2 = new HttpClient())
                                            using (HttpResponseMessage _radiusSystems = await webClient2.GetAsync(
                                                $"https://esi.tech.ccp.is/latest/route/{SystemID}/{systemId}/?datasource=tranquility&flag=shortest"))
                                            using (HttpContent _radiusSystemsContent = _radiusSystems.Content)
                                            {
                                                var systemID = (int)killmail["killmail"]["solarSystem"]["id"];
                                                var data = JToken.Parse(await _radiusSystemsContent.ReadAsStringAsync());
                                                if (data.Type == JTokenType.Array)
                                                {
                                                    var systems = data;
                                                    var gg = systems.Count() - 1;
                                                    if (gg < radius)
                                                    {
                                                        jumpsAway = gg;
                                                        radiusKill = true;
                                                    }
                                                }
                                            }
                                        }
                                    }
                                }
                                if (bigKillGlobal != 0 && value >= bigKillGlobal)
                                {
                                    channel = (dynamic)discordGuild.Channels.FirstOrDefault(x => x.Id == bigKillGlobalChan);
                                    globalBigKill = true;
                                    post = true;
                                }
                                else if (allianceID == 0 && corpID == 0)
                                {
                                    if (bigKillValue != 0 && value >= bigKillValue && !globalBigKill)
                                    {
                                        channel = (dynamic)discordGuild.Channels.FirstOrDefault(x => x.Id == bigKillChannel);
                                        bigKill = true;
                                        post = true;
                                    }
                                    else
                                    {
                                        channel = (dynamic)discordGuild.Channels.FirstOrDefault(x => x.Id == channelGroup);
                                        var totalValue = value;
                                        if (minimumValue == 0 || minimumValue <= totalValue)
                                            post = true;
                                    }
                                }
                                else if (!globalBigKill)
                                {
                                    channel = (dynamic)discordGuild.Channels.FirstOrDefault(x => x.Id == channelGroup);
                                    if (victimAlliance != null)
                                    {
                                        if ((Int32)victimAlliance["id"] == allianceID && losses == true ||
                                            (Int32)victimCorp["id"] == corpID && losses == true)
                                        {
                                            if (bigKillValue != 0 && value >= bigKillValue)
                                            {
                                                channel = (dynamic)discordGuild.Channels.FirstOrDefault(x => x.Id == bigKillChannel);
                                                bigKill = true;
                                                post = true;
                                            }
                                            else
                                            {
                                                if (minimumLossValue == 0 || minimumLossValue <= value)
                                                    post = true;
                                            }
                                        }
                                    }
                                    else if ((Int32)victimCorp["id"] == corpID && losses == true)
                                    {
                                        if (bigKillValue != 0 && value >= bigKillValue)
                                        {
                                            channel = (dynamic)discordGuild.Channels.FirstOrDefault(x => x.Id == bigKillChannel);
                                            bigKill = true;
                                            post = true;
                                        }
                                        else
                                        {
                                            if (minimumLossValue == 0 || minimumLossValue <= value)
                                                post = true;
                                        }
                                    }
                                    foreach (var attacker in attackers.ToList())
                                    {
                                        if (attacker["alliance"] != null)
                                        {
                                            if ((Int32)attacker["alliance"]["id"] == allianceID ||
                                                (Int32)attacker["corporation"]["id"] == corpID)
                                            {
                                                if (bigKillValue != 0 && value >= bigKillValue)
                                                {
                                                    channel = (dynamic)discordGuild.Channels.FirstOrDefault(x => x.Id == bigKillChannel);
                                                    bigKill = true;
                                                    post = true;
                                                }
                                                else
                                                {
                                                    if (minimumValue == 0 || minimumValue <= value)
                                                        post = true;
                                                }
                                            }
                                            else if ((Int32)attacker["corporation"]["id"] == corpID)
                                            {
                                                if (bigKillValue != 0 && value >= bigKillValue)
                                                {
                                                    channel = (dynamic)discordGuild.Channels.FirstOrDefault(x => x.Id == bigKillChannel);
                                                    bigKill = true;
                                                    post = true;
                                                }
                                                else
                                                {
                                                    if (minimumValue == 0 || minimumValue <= value)
                                                        post = true;
                                                }
                                            }
                                        }
                                    }
                                }
                            }

                            if (post || bigKill || globalBigKill || radiusKill)
                            {
                                if (victimCharacter == null)// Kill is probably a structure.
                                {
                                    if (victimAlliance == null)
                                    {
                                        if (radiusKill)
                                        {
                                            var _radiusChannel = (dynamic)discordGuild.Channels.FirstOrDefault(x => x.Id == radiusChannel);
                                            var radiusMessage = "";
                                            radiusMessage = $"Killed {jumpsAway} jumps from {Program.Settings.GetSection("killFeed")["radiusSystem"]}{Environment.NewLine}";
                                            radiusMessage += $"{killTime}{Environment.NewLine}{Environment.NewLine}**{ship}** worth **{string.Format("{0:n0}", value)}" +
                                                $" [{victimCorp["name"]}]** killed in **{sysName}** {Environment.NewLine} https://zkillboard.com/kill/{iD}/";
                                            await _radiusChannel.SendMessageAsync(radiusMessage);
                                        }
                                        var message = "";
                                        if (globalBigKill)
                                        {
                                            message = $"**Global Big Kill**{Environment.NewLine}";
                                        }
                                        else if (bigKill)
                                        {
                                            message = $"**Big Kill**{Environment.NewLine}";
                                        }
                                        if (post)
                                        {
                                            message += $"{killTime}{Environment.NewLine}{Environment.NewLine}**{ship}** worth **{string.Format("{0:n0}", value)}" +
                                                $" [{victimCorp["name"]}]** killed in **{sysName}** {Environment.NewLine} " +
                                                $"https://zkillboard.com/kill/{iD}/";
                                            await channel.SendMessageAsync(message);
                                        }
                                    }
                                    else
                                    {
                                        if (radiusKill)
                                        {
                                            var _radiusChannel = (dynamic)discordGuild.Channels.FirstOrDefault(x => x.Id == radiusChannel);
                                            var radiusMessage = "";
                                            radiusMessage = $"Killed {jumpsAway} jumps from {Program.Settings.GetSection("killFeed")["radiusSystem"]}{Environment.NewLine}";
                                            radiusMessage += $"{killTime}{Environment.NewLine}{Environment.NewLine}**{ship}** worth **{string.Format("{0:n0}", value)}" +
                                            $" {victimCorp["name"]} | [{victimAlliance["name"]}]** killed in **{sysName}** {Environment.NewLine} " +
                                            $"https://zkillboard.com/kill/{iD}/";
                                            await _radiusChannel.SendMessageAsync(radiusMessage);
                                        }
                                        var message = "";
                                        if (globalBigKill)
                                        {
                                            message = $"**Global Big Kill**{Environment.NewLine}";
                                        }
                                        else if (bigKill)
                                        {
                                            message = $"**Big Kill**{Environment.NewLine}";
                                        }
                                        if (post)
                                        {
                                            message += $"{killTime}{Environment.NewLine}{Environment.NewLine}**{ship}** worth **{string.Format("{0:n0}", value)}" +
                                                $" {victimCorp["name"]} | [{victimAlliance["name"]}]** killed in **{sysName}** {Environment.NewLine} " +
                                                $"https://zkillboard.com/kill/{iD}/";
                                            await channel.SendMessageAsync(message);
                                        }
                                    }
                                }
                                else if (!victimAlliance.IsNullOrEmpty())
                                {
                                    if (radiusKill)
                                    {
                                        var _radiusChannel = (dynamic)discordGuild.Channels.FirstOrDefault(x => x.Id == radiusChannel);
                                        var radiusMessage = "";
                                        radiusMessage = $"Killed {jumpsAway} jumps from {Program.Settings.GetSection("killFeed")["radiusSystem"]}{Environment.NewLine}";
                                        radiusMessage += $"{killTime}{Environment.NewLine}{Environment.NewLine}**{ship}** worth **{string.Format("{0:n0}", value)}" +
                                        $"** ISK flown by **{victimCharacter["name"]} |**  **[{victimCorp["name"]}] | <{victimAlliance["name"]}>** killed in **{sysName}** {Environment.NewLine} " +
                                        $"https://zkillboard.com/kill/{iD}/";
                                        await _radiusChannel.SendMessageAsync(radiusMessage);
                                    }
                                    var message = "";
                                    if (globalBigKill)
                                    {
                                        message = $"**Global Big Kill**{Environment.NewLine}";
                                    }
                                    else if (bigKill)
                                    {
                                        message = $"**Big Kill**{Environment.NewLine}";
                                    }
                                    if (post)
                                    {
                                        message += $"{killTime}{Environment.NewLine}{Environment.NewLine}**{ship}** worth **{string.Format("{0:n0}", value)}" +
                                            $"** ISK flown by **{victimCharacter["name"]} |**  **[{victimCorp["name"]}] | <{victimAlliance["name"]}>** killed in **{sysName}** {Environment.NewLine} " +
                                            $"https://zkillboard.com/kill/{iD}/";
                                        await channel.SendMessageAsync(message);
                                    }
                                }
                                else
                                {
                                    if (radiusKill)
                                    {
                                        var _radiusChannel = (dynamic)discordGuild.Channels.FirstOrDefault(x => x.Id == radiusChannel);
                                        var radiusMessage = "";
                                        radiusMessage = $"Killed {jumpsAway} jumps from {Program.Settings.GetSection("killFeed")["radiusSystem"]}{Environment.NewLine}";
                                        radiusMessage += $"{killTime}{Environment.NewLine}{Environment.NewLine}**{ship}** worth **{string.Format("{0:n0}", value)}" +
                                        $"** ISK flown by **{victimCharacter["name"]} |** **[{victimCorp["name"]}]** killed in **{sysName}** {Environment.NewLine} " +
                                        $"https://zkillboard.com/kill/{iD}/";
                                        await _radiusChannel.SendMessageAsync(radiusMessage);
                                    }
                                    var message = "";
                                    if (globalBigKill)
                                    {
                                        message = $"**Global Big Kill**{Environment.NewLine}";
                                    }
                                    else if (bigKill)
                                    {
                                        message = $"**Big Kill**{Environment.NewLine}";
                                    }
                                    if (post)
                                    {
                                        message += $"{killTime}{Environment.NewLine}{Environment.NewLine}**{ship}** worth **{string.Format("{0:n0}", value)}" +
                                            $"** ISK flown by **{victimCharacter["name"]} |** **[{victimCorp["name"]}]** killed in **{sysName}** {Environment.NewLine} " +
                                            $"https://zkillboard.com/kill/{iD}/";
                                        await channel.SendMessageAsync(message);
                                    }
                                }
                                await Client_Log(new LogMessage(LogSeverity.Info, "killFeed", $"POSTING Kill/Loss ID:{killmail["killmail"]["killID"]} Value:{string.Format("{0:n0}", value)}"));
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                await Client_Log(new LogMessage(LogSeverity.Error, "killFeed", ex.Message, ex));
            }
        }
        #endregion

        //Needs Doing
        #region Notifications
        internal async static Task NotificationFeed(CommandContext Context)
        {
            #region Notification Type Dictionary
            Dictionary<int, string> types = new Dictionary<int, string>{
                {1, "Legacy"},
                {2, "Character deleted"},
                {3, "Give medal to character"},
                {4, "Alliance maintenance bill"},
                {5, "Alliance war declared"},
                {6, "Alliance war surrender"},
                {7, "Alliance war retracted"},
                {8, "Alliance war invalidated by Concord"},
                {9, "Bill issued to a character"},
                {10,    "Bill issued to corporation or alliance"},
                {11,    "Bill not paid because there's not enough ISK available"},
                {12,    "Bill, issued by a character, paid"},
                {13,    "Bill, issued by a corporation or alliance, paid"},
                {14,    "Bounty claimed"},
                {15,    "Clone activated"},
                {16,    "New corp member application"},
                {17,    "Corp application rejected"},
                {18,    "Corp application accepted"},
                {19,    "Corp tax rate changed"},
                {20,    "Corp news report, typically for shareholders"},
                {21,    "Player leaves corp"},
                {22,    "Corp news, new CEO"},
                {23,    "Corp dividend/liquidation, sent to shareholders"},
                {24,    "Corp dividend payout, sent to shareholders"},
                {25,    "Corp vote created"},
                {26,    "Corp CEO votes revoked during voting"},
                {27,    "Corp declares war"},
                {28,    "Corp war has started"},
                {29,    "Corp surrenders war"},
                {30,    "Corp retracts war"},
                {31,    "Corp war invalidated by Concord"},
                {32,    "Container password retrieval"},
                {33,    "Contraband or low standings cause an attack or items being confiscated"},
                {34,    "First ship insurance"},
                {35,    "Ship destroyed, insurance payed"},
                {36,    "Insurance contract invalidated/runs out"},
                {37,    "Sovereignty claim fails (alliance)"},
                {38,    "Sovereignty claim fails (corporation)"},
                {39,    "Sovereignty bill late (alliance)"},
                {40,    "Sovereignty bill late (corporation)"},
                {41,    "Sovereignty claim lost (alliance)"},
                {42,    "Sovereignty claim lost (corporation)"},
                {43,    "Sovereignty claim acquired (alliance)"},
                {44,    "Sovereignty claim acquired (corporation)"},
                {45,    "Alliance anchoring alert"},
                {46,    "Alliance structure turns vulnerable"},
                {47,    "Alliance structure turns invulnerable"},
                {48,    "Sovereignty disruptor anchored"},
                {49,    "Structure won/lost"},
                {50,    "Corp office lease expiration notice"},
                {51,    "Clone contract revoked by station manager"},
                {52,    "Corp member clones moved between stations"},
                {53,    "Clone contract revoked by station manager"},
                {54,    "Insurance contract expired"},
                {55,    "Insurance contract issued"},
                {56,    "Jump clone destroyed"},
                {57,    "Jump clone destroyed"},
                {58,    "Corporation joining factional warfare"},
                {59,    "Corporation leaving factional warfare"},
                {60,    "Corporation kicked from factional warfare on startup because of too low standing to the faction"},
                {61,    "Character kicked from factional warfare on startup because of too low standing to the faction"},
                {62,    "Corporation in factional warfare warned on startup because of too low standing to the faction"},
                {63,    "Character in factional warfare warned on startup because of too low standing to the faction"},
                {64,    "Character loses factional warfare rank"},
                {65,    "Character gains factional warfare rank"},
                {66,    "Agent has moved"},
                {67,    "Mass transaction reversal message"},
                {68,    "Reimbursement message"},
                {69,    "Agent locates a character"},
                {70,    "Research mission becomes available from an agent"},
                {71,    "Agent mission offer expires"},
                {72,    "Agent mission times out"},
                {73,    "Agent offers a storyline mission"},
                {74,    "Tutorial message sent on character creation"},
                {75,    "Tower alert"},
                {76,    "Tower resource alert"},
                {77,    "Station aggression message"},
                {78,    "Station state change message"},
                {79,    "Station conquered message"},
                {80,    "Station aggression message"},
                {81,    "Corporation requests joining factional warfare"},
                {82,    "Corporation requests leaving factional warfare"},
                {83,    "Corporation withdrawing a request to join factional warfare"},
                {84,    "Corporation withdrawing a request to leave factional warfare"},
                {85,    "Corporation liquidation"},
                {86,    "Territorial Claim Unit under attack"},
                {87,    "Sovereignty Blockade Unit under attack"},
                {88,    "Infrastructure Hub under attack"},
                {89,    "Contact add notification"},
                {90,    "Contact edit notification"},
                {91,    "Incursion Completed"},
                {92,    "Corp Kicked"},
                {93,    "Customs office has been attacked"},
                {94,    "Customs office has entered reinforced"},
                {95,    "Customs office has been transferred"},
                {96,    "FW Alliance Warning"},
                {97,    "FW Alliance Kick"},
                {98,    "AllWarCorpJoined Msg"},
                {99,    "Ally Joined Defender"},
                {100,   "Ally Has Joined a War Aggressor"},
                {101,   "Ally Joined War Ally"},
                {102,   "New war system: entity is offering assistance in a war."},
                {103,   "War Surrender Offer"},
                {104,   "War Surrender Declined"},
                {105,   "FacWar LP Payout Kill"},
                {106,   "FacWar LP Payout Event"},
                {107,   "FacWar LP Disqualified Eventd"},
                {108,   "FacWar LP Disqualified Kill"},
                {109,   "Alliance Contract Cancelled"},
                {110,   "War Ally Declined Offer"},
                {111,   "Your Bounty Was Claimed"},
                {112,   "Bounty placed (Char)"},
                {113,   "Bounty Placed (Corp)"},
                {114,   "Bounty Placed (Alliance)"},
                {115,   "Kill Right Available"},
                {116,   "Kill right Available Open"},
                {117,   "Kill Right Earned"},
                {118,   "Kill right Used"},
                {119,   "Kill Right Unavailable"},
                {120,   "Kill Right Unavailable Open"},
                {121,   "Declare War"},
                {122,   "Offered Surrender"},
                {123,   "Accepted Surrender"},
                {124,   "Made War Mutual"},
                {125,   "Retracts War"},
                {126,   "Offered To Ally"},
                {127,   "Accepted Ally"},
                {128,   "Character Application Accept"},
                {129,   "Character Application Reject"},
                {130,   "Character Application Withdraw"},
                {138,   "Clone activated"},
                {140,   "Loss report available"},
                {141,   "Kill report available"},
                {147,   "Entosis Link started"},
                {148,   "Entosis Link enabled a module"},
                {149,   "Entosis Link disabled a module"},
                {131,   "DustAppAcceptedMsg ?"},
                {132,   "DistrictAttacked ?"},
                {133,   "BattlePunishFriendlyFire ?"},
                {134,   "BountyESSTaken ?"},
                {135,   "BountyESSShared ?"},
                {136,   "IndustryTeamAuctionWon ?"},
                {137,   "IndustryTeamAuctionLost ?"},
                {139,   "Corporation invitation accepted (CorpAppInvitedMsg)"},
                {142,   "Corporation application rejected (CorpAppRejectCustomMsg)"},
                {143,   "Friendly fire enable timer started (CorpFriendlyFireEnableTimerStarted)"},
                {144,   "Friendly fire disable timer started (CorpFriendlyFireDisableTimerStarted)"},
                {145,   "Friendly fire enable timer completed (CorpFriendlyFireEnableTimerCompleted)"},
                {146,   "Friendly fire disable timer completed (CorpFriendlyFireDisableTimerCompleted)"},
                {152,   "Infrastructure hub bill about to expire (InfrastructureHubBillAboutToExpire)"},
                {160,   "Sovereignty structure reinforced (SovStructureReinforced)"},
                {161,   "SovCommandNodeEventStarted ?"},
                {162,   "Sovereignty structure destroyed (SovStructureDestroyed)"},
                {163,   "SovStationEnteredFreeport ?"},
                {164,   "IHubDestroyedByBillFailure ?"},
                {166,   "BuddyConnectContactAdd ?"},
                {165,   "Alliance capital changed (AllianceCapitalChanged)"},
                {167,   "Sovereignty structure self destruction requested (SovStructureSelfDestructRequested)"},
                {168,   "Sovereignty structure self destruction canceled (SovStructureSelfDestructCancel)"},
                {169,   "Sovereignty structure self destruction completed (SovStructureSelfDestructFinished)"},
                {181,   "Structure fuel alert (StructureFuelAlert)"},
                {182,   "Structure anchoring started (StructureAnchoring)"},
                {183,   "Structure unanchoring started (StructureUnanchoring)"},
                {184,   "Structure under attack (StructureUnderAttack)"},
                {185,   "Structure Online (StructureOnline)"},
                {186,   "Structure lost shields (StructureLostShields)"},
                {187,   "Structure lost Armor (StructureLostArmor)"},
                {188,   "Structure destroyed (StructureDestroyed)"},
                {198,   "Structure service offline (StructureServicesOffline)"},
                {199,   "Item delivered (StructureItemsDelivered)"},
                {200,   "Points Awarded"},
                {201,   "StructureCourierContractChanged ?"},
                {1012,  "OperationFinished ?"},
                {1030,  "Game time received (GameTimeReceived)"},
                {1031,  "Game time sent (GameTimeSent)"}
            };
            #endregion
            try
            {
                if (DateTime.Now > nextNotificationCheck)
                {
                    await Client_Log(new LogMessage(LogSeverity.Info, "NotificationFeed", "Running Notification Check"));
                    lastNotification = Convert.ToInt32(await SQLiteDataQuery("cacheData", "data", "lastNotificationID"));
                    var guildID = Convert.ToUInt64(Program.Settings.GetSection("config")["guildId"]);
                    var channelId = Convert.ToUInt64(Program.Settings.GetSection("notifications")["channelId"]);
                    var chan = (dynamic)Program.Client.GetGuild(guildID).GetChannel(channelId);
                    var keyID = "";
                    var vCode = "";
                    var characterID = "";
                    var keys = Program.Settings.GetSection("notifications").GetSection("keys").GetChildren();
                    var keyCount = keys.Count();
                    var nextKey = await SQLiteDataQuery("notifications", "data", "nextKey");
                    var index = 0;
                    var runComplete = false;

                    foreach (var key in keys)
                    {
                        if (key.Key != nextKey && nextKey != null)
                        {
                            index++;
                        }
                        if (nextKey == null && !runComplete || String.IsNullOrWhiteSpace(nextKey) && !runComplete || nextKey == key.Key && !runComplete)
                        {
                            characterID = key["characterID"];
                            keyID = key["keyID"];
                            vCode = key["vCode"];

                            await Client_Log(new LogMessage(LogSeverity.Info, "NotificationFeed", $"Checking characterID:{characterID} keyID:{keyID} vCode:{vCode}"));

                            await EveLib.SetApiKey(keyID, vCode, characterID);
                            var notifications = await EveLib.GetNotifications();
                            var notificationsSort = notifications.OrderBy(x => x.Key);

                            if (notifications.Count > 0)
                            {
                                var notiIDs = new List<int>();

                                foreach (var l in notifications)
                                {
                                    notiIDs.Add((int)l.Key);
                                }

                                var notificationsText = await EveLib.GetNotificationText(notiIDs);

                                foreach (var notification in notificationsSort)
                                {
                                    if ((int)notification.Value["notificationID"] > lastNotification)
                                    {
                                        var notificationText = notificationsText.FirstOrDefault(x => x.Key == notification.Key).Value;
                                        var notificationType = (int)notification.Value["typeID"];

                                        if (notificationType == 5)
                                        {
                                            await Client_Log(new LogMessage(LogSeverity.Info, "NotificationFeed", $"Sending Notification TypeID: {notificationType} " +
                                                $"Type: {types[notificationType]}"));
                                            var againstID = Convert.ToInt64(notificationText["againstID"].AllNodes.ToList()[0].ToString());
                                            var cost = notificationText["cost"].AllNodes.ToList()[0];
                                            var declaredByID = Convert.ToInt64(notificationText["declaredByID"].AllNodes.ToList()[0].ToString());
                                            var delayHours = notificationText["delayHours"].AllNodes.ToList()[0].ToString();
                                            var hostileState = notificationText["hostileState"].AllNodes.ToList()[0].ToString();
                                            var names = await EveLib.IDtoName(new List<Int64> { declaredByID, againstID });
                                            var againstName = names.FirstOrDefault(x => x.Key == againstID);
                                            var declaredByName = names.First(x => x.Key == declaredByID);

                                            await chan.SendMessageAsync($"@everyone {Environment.NewLine}War declared by {declaredByName.Value} against {againstName.Value}" +
                                                $" Fighting begins in roughly {delayHours} hours");
                                        }
                                        else if (notificationType == 7)
                                        {
                                            await Client_Log(new LogMessage(LogSeverity.Info, "NotificationFeed", $"Sending Notification TypeID: {notificationType} " +
                                                $"Type: {types[notificationType]}"));
                                            var againstID = Convert.ToInt64(notificationText["againstID"].AllNodes.ToList()[0].ToString());
                                            var declaredByID = Convert.ToInt64(notificationText["declaredByID"].AllNodes.ToList()[0].ToString());

                                            var stuff = await EveLib.IDtoName(new List<Int64> { againstID, declaredByID });
                                            var againstName = stuff.FirstOrDefault(x => x.Key == againstID).Value;
                                            var declaredByName = stuff.FirstOrDefault(x => x.Key == declaredByID).Value;

                                            await chan.SendMessageAsync($"@everyone {Environment.NewLine}{declaredByName} Retracts War Against {againstName}");
                                        }
                                        else if (notificationType == 27)
                                        {
                                            await Client_Log(new LogMessage(LogSeverity.Info, "NotificationFeed", $"Sending Notification TypeID: {notificationType} " +
                                                $"Type: {types[notificationType]}"));
                                            var againstID = Convert.ToInt64(notificationText["againstID"].AllNodes.ToList()[0].ToString());
                                            var cost = notificationText["cost"].AllNodes.ToList()[0];
                                            var declaredByID = Convert.ToInt64(notificationText["declaredByID"].AllNodes.ToList()[0].ToString());
                                            var names = await EveLib.IDtoName(new List<Int64> { declaredByID, againstID });
                                            var againstName = names.FirstOrDefault(x => x.Key == againstID);
                                            var declaredByName = names.First(x => x.Key == declaredByID);

                                            await chan.SendMessageAsync($"@everyone {Environment.NewLine}War declared by {declaredByName.Value} against {againstName.Value}");
                                        }
                                        else if (notificationType == 30)
                                        {
                                            await Client_Log(new LogMessage(LogSeverity.Info, "NotificationFeed", $"Sending Notification TypeID: {notificationType} " +
                                                $"Type: {types[notificationType]}"));
                                            var againstID = Convert.ToInt64(notificationText["againstID"].AllNodes.ToList()[0].ToString());
                                            var cost = notificationText["cost"].AllNodes.ToList()[0];
                                            var declaredByID = Convert.ToInt64(notificationText["declaredByID"].AllNodes.ToList()[0].ToString());
                                            var names = await EveLib.IDtoName(new List<Int64> { declaredByID, againstID });
                                            var againstName = names.FirstOrDefault(x => x.Key == againstID);
                                            var declaredByName = names.First(x => x.Key == declaredByID);

                                            await chan.SendMessageAsync($"@everyone {Environment.NewLine}{declaredByName.Value} Retracts War Against {againstName.Value}");
                                        }
                                        else if (notificationType == 75)
                                        {
                                            await Client_Log(new LogMessage(LogSeverity.Info, "NotificationFeed", $"Sending Notification TypeID: {notificationType} " +
                                                $"Type: {types[notificationType]}"));
                                            var aggressorAllianceID = Convert.ToInt64(notificationText["aggressorAllianceID"].AllNodes.ToList()[0].ToString());
                                            var aggressorCorpID = Convert.ToInt64(notificationText["aggressorCorpID"].AllNodes.ToList()[0].ToString());
                                            var aggressorID = Convert.ToInt64(notificationText["aggressorID"].AllNodes.ToList()[0].ToString());
                                            var typeID = Convert.ToInt64(notificationText["typeID"].AllNodes.ToList()[0].ToString());
                                            var moonID = Convert.ToInt64(notificationText["moonID"].AllNodes.ToList()[0].ToString());
                                            var solarSystemID = Convert.ToInt64(notificationText["solarSystemID"].AllNodes.ToList()[0].ToString());
                                            var armorValue = string.Format("{0:P2}", Convert.ToDouble(notificationText["armorValue"].AllNodes.ToList()[0].ToString()));
                                            var shieldValue = string.Format("{0:P2}", Convert.ToDouble(notificationText["shieldValue"].AllNodes.ToList()[0].ToString()));
                                            var hullValue = string.Format("{0:P2}", Convert.ToDouble(notificationText["hullValue"].AllNodes.ToList()[0].ToString()));
                                            var names = await EveLib.IDtoName(new List<Int64> { aggressorAllianceID, aggressorCorpID, aggressorID, moonID, solarSystemID });
                                            var aggressorAlliance = names.FirstOrDefault(x => x.Key == aggressorAllianceID).Value;
                                            var aggressorCorpName = names.First(x => x.Key == aggressorCorpID).Value;
                                            var aggressorName = names.First(x => x.Key == aggressorID).Value;
                                            var moonName = names.First(x => x.Key == moonID).Value;
                                            var solarSystemName = names.First(x => x.Key == solarSystemID).Value;
                                            var allyLine = aggressorAllianceID != 0 ? $"{Environment.NewLine}Aggressing Alliance: {aggressorAlliance}" : "";
                                            var TypeName = await EveLib.IDtoTypeName(new List<Int64> { typeID });

                                            await chan.SendMessageAsync($"@everyone {Environment.NewLine}Starbase is under attack{Environment.NewLine}{Environment.NewLine}" +
                                                $"Details{Environment.NewLine}```{Environment.NewLine}System: {moonName}{Environment.NewLine}" +
                                                $"Type: {TypeName.First(x => x.Key == typeID).Value}{Environment.NewLine}{Environment.NewLine}" +
                                                $"Current Shield Level: {shieldValue}{Environment.NewLine}Current Armor Level: {armorValue}{Environment.NewLine}" +
                                                $"Current Hull Level: {hullValue}{Environment.NewLine}{Environment.NewLine}" +
                                                $"Aggressing Pilot: {aggressorName}{Environment.NewLine}Aggressing Corporation: {aggressorCorpName}{allyLine}```");
                                        }
                                        else if (notificationType == 100)
                                        {
                                            await Client_Log(new LogMessage(LogSeverity.Info, "NotificationFeed", $"Sending Notification TypeID: {notificationType} " +
                                                $"Type: {types[notificationType]}"));
                                            var allyID = Convert.ToInt64(notificationText["allyID"].AllNodes.ToList()[0].ToString());
                                            var defenderID = Convert.ToInt64(notificationText["defenderID"].AllNodes.ToList()[0].ToString());

                                            var stuff = await EveLib.IDtoName(new List<Int64> { allyID, defenderID });
                                            var allyName = stuff.FirstOrDefault(x => x.Key == allyID).Value;
                                            var defenderName = stuff.FirstOrDefault(x => x.Key == defenderID).Value;
                                            var startTime = DateTime.FromFileTimeUtc(Convert.ToInt64(notificationText["startTime"].AllNodes.ToList()[0].ToString()));

                                            await chan.SendMessageAsync($"@everyone {Environment.NewLine}{allyName} will join the war against {defenderName} at {startTime} EVE.");
                                        }
                                        else if (notificationType == 121)
                                        {
                                            await Client_Log(new LogMessage(LogSeverity.Info, "NotificationFeed", $"Sending Notification TypeID: {notificationType} " +
                                                $"Type: {types[notificationType]}"));
                                            var aggressorID = Convert.ToInt64(notificationText["entityID"].AllNodes.ToList()[0].ToString());
                                            var defenderID = Convert.ToInt64(notificationText["defenderID"].AllNodes.ToList()[0].ToString());

                                            var stuff = await EveLib.IDtoName(new List<Int64> { aggressorID, defenderID });
                                            var aggressorName = stuff.FirstOrDefault(x => x.Key == aggressorID).Value;
                                            var defenderName = stuff.FirstOrDefault(x => x.Key == defenderID).Value;

                                            await chan.SendMessageAsync($"@everyone {Environment.NewLine}War declared by {aggressorName} against {defenderName}. Fighting begins in roughly 24 hours.");
                                        }
                                        else if (notificationType == 147)
                                        {
                                            await Client_Log(new LogMessage(LogSeverity.Info, "NotificationFeed", $"Sending Notification TypeID: {notificationType} " +
                                                $"Type: {types[notificationType]}"));
                                            var solarSystemID = Convert.ToInt64(notificationText["solarSystemID"].AllNodes.ToList()[0].ToString());
                                            var structureTypeID = Convert.ToInt64(notificationText["structureTypeID"].AllNodes.ToList()[0].ToString());
                                            var names = await EveLib.IDtoName(new List<Int64> { solarSystemID });
                                            var typeNames = await EveLib.IDtoTypeName(new List<Int64> { structureTypeID });
                                            var solarSystemName = names.FirstOrDefault(x => x.Key == solarSystemID);
                                            var structureTypeName = typeNames.FirstOrDefault(x => x.Key == structureTypeID);

                                            await chan.SendMessageAsync($"@everyone {Environment.NewLine}Entosis Link started in {solarSystemName.Value} on {structureTypeName.Value}");
                                        }
                                        else if (notificationType == 160)
                                        {
                                            await Client_Log(new LogMessage(LogSeverity.Info, "NotificationFeed", $"Sending Notification TypeID: {notificationType} " +
                                                $"Type: {types[notificationType]}"));
                                            var campaignEventType = notificationText["campaignEventType"].AllNodes.ToList()[0];
                                            var solarSystemID = Convert.ToInt64((notificationText["solarSystemID"].AllNodes.ToList()[0].ToString()));
                                            var decloakTime = Convert.ToInt64(notificationText["decloakTime"].AllNodes.ToList()[0].ToString());
                                            var names = await EveLib.IDtoName(new List<Int64> { solarSystemID });
                                            var solarSystemName = names.FirstOrDefault(x => x.Key == solarSystemID);
                                            var decloaktime = DateTime.FromFileTime(decloakTime);

                                            await chan.SendMessageAsync($"@everyone {Environment.NewLine}Sovereignty structure reinforced in {solarSystemName.Value} nodes will spawn @{decloaktime}");
                                        }
                                        else if (notificationType == 161)
                                        {
                                            await Client_Log(new LogMessage(LogSeverity.Info, "NotificationFeed", $"Sending Notification TypeID: {notificationType} " +
                                                $"Type: {types[notificationType]}"));
                                            var campaignEventType = notificationText["campaignEventType"].AllNodes.ToList()[0];
                                            var constellationID = notificationText["constellationID"].AllNodes.ToList()[0];
                                            var solarSystemID = Convert.ToInt64(notificationText["solarSystemID"].AllNodes.ToList()[0].ToString());
                                            var names = await EveLib.IDtoName(new List<Int64> { solarSystemID });
                                            var solarSystemName = names.FirstOrDefault(x => x.Key == solarSystemID);

                                            await chan.SendMessageAsync($"@everyone {Environment.NewLine}Command nodes decloaking for {solarSystemName.Value}");

                                        }
                                        else
                                        {
                                            try
                                            {
                                                await Client_Log(new LogMessage(LogSeverity.Info, "NotificationFeed", $"Skipping Notification TypeID: {notificationType} " +
                                                    $"Type: {types[notificationType]} {Environment.NewLine} Text: {notificationText}"));
                                            }
                                            catch (KeyNotFoundException)
                                            {
                                                await Client_Log(new LogMessage(LogSeverity.Info, "NotificationFeed", $"Skipping **NEW** Notification TypeID: {notificationType} " +
                                                    $"{Environment.NewLine} Text: {notificationText}"));
                                            }
                                        }
                                        lastNotification = (int)notification.Value["notificationID"];
                                        await SQLiteDataUpdate("cacheData", "data", "lastNotificationID", lastNotification.ToString());
                                        runComplete = true;
                                    }
                                }
                            }
                            if (keyCount > 1 && keyCount != index + 1)
                            {
                                await SQLiteDataUpdate("notifications", "data", "nextKey", keys.ToList()[index + 1].Key);
                            }
                            else if (keyCount == index + 1)
                            {
                                await SQLiteDataUpdate("notifications", "data", "nextKey", keys.ToList()[0].Key);
                            }
                            else
                            {
                                await SQLiteDataUpdate("notifications", "data", "nextKey", key.Key);
                            }
                        }
                        var interval = 30 / keyCount;
                        await SQLiteDataUpdate("cacheData", "data", "nextNotificationCheck", DateTime.Now.AddMinutes(interval).ToString());
                        nextNotificationCheck = DateTime.Now.AddMinutes(interval);
                        
                    }
                }
            }
            catch (Exception ex)
            {
                await Client_Log(new LogMessage(LogSeverity.Error, "NotificationFeed", ex.Message, ex));
            }
        }
        #endregion

        //Complete
        #region Pricecheck
        internal async static Task PriceCheck(ICommandContext context, string String, string system)
        {
            var NametoId = "https://www.fuzzwork.co.uk/api/typeid.php?typename=";

            using (HttpClient webClient = new HttpClient())
            {
                JObject jObject = new JObject();
                var channel = (dynamic)context.Message.Channel;
                if (String.ToLower() == "short name")
                {
                    String = "Item Name";
                }

                var reply = await webClient.GetStringAsync(NametoId + String);
                jObject = JObject.Parse(reply);
                if ((string)jObject["typeName"] == "bad item")
                {
                    await channel.SendMessageAsync($"{context.Message.Author.Mention} Item {String} does not exist please try again");
                    
                }
                else
                {
                    try
                    {
                        if (system == "")
                        {
                            var eveCentralReply = await webClient.GetAsync($"http://api.eve-central.com/api/marketstat/json?typeid={jObject["typeID"]}");
                            var eveCentralReplyString = eveCentralReply.Content;
                            var centralreply = JToken.Parse(await eveCentralReply.Content.ReadAsStringAsync());
                            await Client_Log(new LogMessage(LogSeverity.Info, "PCheck", $"Sending {context.Message.Author}'s Price check to {channel.Name}"));
                            await channel.SendMessageAsync($"{context.Message.Author.Mention}, System: **Universe**{Environment.NewLine}" +
                                $"**Buy:**{Environment.NewLine}" +
                                $"```Low: {centralreply[0]["buy"]["min"]:n2}{Environment.NewLine}" +
                                $"Avg: {centralreply[0]["buy"]["avg"]:n2}{Environment.NewLine}" +
                                $"High: {centralreply[0]["buy"]["max"]:n2}```" +
                                $"{Environment.NewLine}" +
                                $"**Sell**:{Environment.NewLine}" +
                                $"```Low: {centralreply[0]["sell"]["min"]:n2}{Environment.NewLine}" +
                                $"Avg: {centralreply[0]["sell"]["avg"]:n2}{Environment.NewLine}" +
                                $"High: {centralreply[0]["sell"]["max"]:n2}```");
                        }
                        if (system == "jita")
                        {
                            var eveCentralReply = await webClient.GetAsync($"http://api.eve-central.com/api/marketstat/json?typeid={jObject["typeID"]}&usesystem=30000142");
                            var eveCentralReplyString = eveCentralReply.Content;
                            var centralreply = JToken.Parse(await eveCentralReply.Content.ReadAsStringAsync());
                            await Client_Log(new LogMessage(LogSeverity.Info, "PCheck", $"Sending {context.Message.Author}'s Price check to {channel.Name}"));
                            await channel.SendMessageAsync($"{context.Message.Author.Mention}, System: Jita{Environment.NewLine}" +
                                $"**Buy:**{Environment.NewLine}" +
                                $"```Low: {centralreply[0]["buy"]["min"]:n2}{Environment.NewLine}" +
                                $"Avg: {centralreply[0]["buy"]["avg"]:n2}{Environment.NewLine}" +
                                $"High: {centralreply[0]["buy"]["max"]:n2}```" +
                                $"{Environment.NewLine}" +
                                $"**Sell**:{Environment.NewLine}" +
                                $"```Low: {centralreply[0]["sell"]["min"]:n2}{Environment.NewLine}" +
                                $"Avg: {centralreply[0]["sell"]["avg"]:n2}{Environment.NewLine}" +
                                $"High: {centralreply[0]["sell"]["max"]:n2}```");
                        }
                        if (system == "amarr")
                        {
                            var eveCentralReply = await webClient.GetAsync($"http://api.eve-central.com/api/marketstat/json?typeid={jObject["typeID"]}&usesystem=30002187");
                            var eveCentralReplyString = eveCentralReply.Content;
                            var centralreply = JToken.Parse(await eveCentralReply.Content.ReadAsStringAsync());
                            await Client_Log(new LogMessage(LogSeverity.Info, "PCheck", $"Sending {context.Message.Author}'s Price check to {channel.Name}"));
                            await channel.SendMessageAsync($"{context.Message.Author.Mention}, System: Amarr{Environment.NewLine}" +
                                $"**Buy:**{Environment.NewLine}" +
                                $"```Low: {centralreply[0]["buy"]["min"]:n2}{Environment.NewLine}" +
                                $"Avg: {centralreply[0]["buy"]["avg"]:n2}{Environment.NewLine}" +
                                $"High: {centralreply[0]["buy"]["max"]:n2}```" +
                                $"{Environment.NewLine}" +
                                $"**Sell**:{Environment.NewLine}" +
                                $"```Low: {centralreply[0]["sell"]["min"]:n2}{Environment.NewLine}" +
                                $"Avg: {centralreply[0]["sell"]["avg"]:n2}{Environment.NewLine}" +
                                $"High: {centralreply[0]["sell"]["max"]:n2}```");
                        }
                        if (system == "rens")
                        {
                            var eveCentralReply = await webClient.GetAsync($"http://api.eve-central.com/api/marketstat/json?typeid={jObject["typeID"]}&usesystem=30002510");
                            var eveCentralReplyString = eveCentralReply.Content;
                            var centralreply = JToken.Parse(await eveCentralReply.Content.ReadAsStringAsync());
                            await Client_Log(new LogMessage(LogSeverity.Info, "PCheck", $"Sending {context.Message.Author}'s Price check to {channel.Name}"));
                            await channel.SendMessageAsync($"{context.Message.Author.Mention}, System: Rens{Environment.NewLine}" +
                                $"**Buy:**{Environment.NewLine}" +
                                $"```Low: {centralreply[0]["buy"]["min"]:n2}{Environment.NewLine}" +
                                $"Avg: {centralreply[0]["buy"]["avg"]:n2}{Environment.NewLine}" +
                                $"High: {centralreply[0]["buy"]["max"]:n2}```" +
                                $"{Environment.NewLine}" +
                                $"**Sell**:{Environment.NewLine}" +
                                $"```Low: {centralreply[0]["sell"]["min"]:n2}{Environment.NewLine}" +
                                $"Avg: {centralreply[0]["sell"]["avg"]:n2}{Environment.NewLine}" +
                                $"High: {centralreply[0]["sell"]["max"]:n2}```");
                        }
                        if (system == "dodixe")
                        {
                            var eveCentralReply = await webClient.GetAsync($"http://api.eve-central.com/api/marketstat/json?typeid={jObject["typeID"]}&usesystem=30002659");
                            var eveCentralReplyString = eveCentralReply.Content;
                            var centralreply = JToken.Parse(await eveCentralReply.Content.ReadAsStringAsync());
                            await Client_Log(new LogMessage(LogSeverity.Info, "PCheck", $"Sending {context.Message.Author}'s Price check to {channel.Name}"));
                            await channel.SendMessageAsync($"{context.Message.Author.Mention}, System: Dodixe{Environment.NewLine}" +
                                $"**Buy:**{Environment.NewLine}" +
                                $"      Low: {centralreply[0]["buy"]["min"]:n}{Environment.NewLine}" +
                                $"      Avg: {centralreply[0]["buy"]["avg"]:n}{Environment.NewLine}" +
                                $"      High: {centralreply[0]["buy"]["max"]:n}{Environment.NewLine}" +
                                $"**Sell**:{Environment.NewLine}" +
                                $"      Low: {centralreply[0]["sell"]["min"]:n}{Environment.NewLine}" +
                                $"      Avg: {centralreply[0]["sell"]["avg"]:n}{Environment.NewLine}" +
                                $"      High: {centralreply[0]["sell"]["max"]:n}{Environment.NewLine}");
                        }
                    }
                    catch (Exception ex)
                    {
                        await Client_Log(new LogMessage(LogSeverity.Error, "PC", ex.Message, ex));
                    }
                }
            }
        }
        #endregion

        //Time
        #region Time
        internal async static Task EveTime(ICommandContext context)
        {
            try
            {
                var format = Program.Settings.GetSection("config")["timeformat"];
                var utcTime = DateTime.UtcNow.ToString(format);
                await context.Message.Channel.SendMessageAsync($"{context.Message.Author.Mention} Current EVE Time is {utcTime}");
            }
            catch (Exception ex)
            {
                await Client_Log(new LogMessage(LogSeverity.Error, "EveTime", ex.Message, ex));
            }
        }
        #endregion

        //MOTD
        #region MOTD
        internal async static Task MOTD(ICommandContext context)
        {
            try
            {
                var keyID = Program.Settings.GetSection("motd")["motdkeyID"];
                var vCode = Program.Settings.GetSection("motd")["motdvCode"];
                var CharID = Program.Settings.GetSection("motd")["motdcharid"];
                await EveLib.SetMOTDKey(keyID, vCode, CharID);

                var chanName = Program.Settings.GetSection("motd")["MOTDChan"];

                var rowlist = await EveLib.GetChatChannels();
                foreach (var r in rowlist)
                {
                    var ChName = r["displayName"];
                    string Channel = ChName.ToString();
                    string ChannelName = chanName.ToString();
                    if (Channel == ChannelName)
                    {
                        var comments = r["motd"];
                        string com = comments.ToString();
                        com = com.Replace("<br>", " \n ")
                            .Replace("<u>", "__").Replace("</u>", "__")
                            .Replace("<b>", "**").Replace("</b>", "**")
                            .Replace("<i>", "*").Replace("</i>", "*")
                            .Replace("&amp", "&");

                        com = StripTagsCharArray(com);
                        com = com.Replace("&lt;", "<").Replace("&gt;", ">");

                        var restricted = Convert.ToUInt64(Program.Settings.GetSection("config")["restricted"]);
                        var channel = Convert.ToUInt64(context.Channel.Id);
                        if (channel == restricted)
                        {
                            await context.Message.Channel.SendMessageAsync($" {context.Message.Author.Mention} I cant do that *here.*");
                        }
                        else
                        {
                            await context.Message.Channel.SendMessageAsync($"{context.Message.Author.Mention}{Environment.NewLine}{com}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                await Client_Log(new LogMessage(LogSeverity.Error, "MOTD", ex.Message, ex));
            }
        }
        #endregion

        //Update Topic
        #region Update Topic
        internal async static Task TopicMOTD(ICommandContext context)
        {
            try
            {
                if (DateTime.Now > lastTopicCheck.AddMilliseconds(Convert.ToInt32(Program.Settings.GetSection("motd")["topicInterval"]) * 1000 * 60))
                {
                    await Client_Log(new LogMessage(LogSeverity.Info, "CheckTopic", "Running Topic Check"));
                    motdtopic = Convert.ToString(await SQLiteDataQuery("cacheData", "data", "motd"));
                    {
                        var guildID = Convert.ToUInt64(Program.Settings.GetSection("config")["guildId"]);
                        var channelId = Convert.ToUInt64(Program.Settings.GetSection("motd")["motdtopicchan"]);
                        var chan1 = (dynamic)Program.Client.GetGuild(guildID).GetChannel(channelId);
                        var keyID = Program.Settings.GetSection("motd")["motdkeyID"];
                        var vCode = Program.Settings.GetSection("motd")["motdvCode"];
                        var CharID = Program.Settings.GetSection("motd")["motdcharid"];
                        await EveLib.SetMOTDKey(keyID, vCode, CharID);

                        var chanName = Program.Settings.GetSection("motd")["MOTDChan"];

                        var rowlist = await EveLib.GetChatChannels();
                        foreach (var r in rowlist)
                        {
                            var ChName = r["displayName"];
                            string Channel = ChName.ToString();
                            string ChannelName = chanName.ToString();
                            if (Channel == ChannelName)
                            {
                                var comments = r["motd"];
                                string com = comments.ToString();
                                com = com.Replace("<br>", " \n ")
                                    .Replace("<u>", "__").Replace("</u>", "__")
                                    .Replace("<b>", "**").Replace("</b>", "**")
                                    .Replace("<i>", "*").Replace("</i>", "*")
                                    .Replace("&amp", "&");

                                com = StripTagsCharArray(com);
                                com = com.Replace("&lt;", "<").Replace("&gt;", ">");

                                if (com != motdtopic)
                                {
                                    var chanid = Convert.ToUInt64(Program.Settings.GetSection("motd")["motdtopicchan"]);
                                    var chan = (ITextChannel)Program.Client.Guilds.FirstOrDefault().Channels.FirstOrDefault(x => x.Id == chanid); ;

                                    await SQLiteDataUpdate("cacheData", "data", "motd", com.ToString());
                                    await chan.ModifyAsync(x => x.Topic = com);
                                    await chan1.SendMessageAsync($"@everyone Channel topic has been updated..");
                                }
                            }
                        }
                    }
                    lastTopicCheck = DateTime.Now;
                }
            }
            catch (Exception ex)
            {
                await Client_Log(new LogMessage(LogSeverity.Error, "MOTDTopic", ex.Message, ex));
            }
        }
        #endregion

        //FleetUp Baby
        #region FleetUp
        internal static async Task FleetUp()
        {
            //Check Fleetup Operations
            var lastChecked = await SQLiteDataQuery("cacheData", "data", "fleetUpLastChecked");

            if (DateTime.Now > DateTime.Parse(lastChecked).AddMinutes(1))
            {
                using (HttpClient webRequest = new HttpClient())
                {
                    var UserId = Program.Settings.GetSection("fleetup")["UserId"];
                    var APICode = Program.Settings.GetSection("fleetup")["APICode"];
                    var GroupID = Program.Settings.GetSection("fleetup")["GroupID"];
                    var channelid = Convert.ToUInt64(Program.Settings.GetSection("fleetup")["channel"]);
                    var guildId = Convert.ToUInt64(Program.Settings.GetSection("config")["guildId"]);
                    var lastopid = await SQLiteDataQuery("cacheData", "data", "fleetUpLastPostedOperation");

                    var Json = await webRequest.GetStringAsync($"http://api.fleet-up.com/Api.svc/Ohigwbylcsuz56ue3O6Awlw5e/{UserId}/{APICode}/Operations/{GroupID}");
                    var result = JObject.Parse(Json);
                    foreach (var operation in result["Data"])
                    {
                        if ((int)operation["OperationId"] > Convert.ToInt32(lastopid))
                        {
                            var name = operation["Subject"];
                            var startTime = operation["StartString"];
                            var locationinfo = operation["LocationInfo"];
                            var location = operation["Location"];
                            var details = operation["Details"];
                            var url = $"http://fleet-up.com/Operation#{operation["OperationId"]}";

                            var channel = (dynamic)Program.Client.GetGuild(guildId).GetChannel(channelid);

                            var message = $"@everyone {Environment.NewLine}{Environment.NewLine}" +
                                $"**New Operation Posted** {Environment.NewLine}{Environment.NewLine}" +
                                $"```Title - {name} {Environment.NewLine}" +
                                $"Form Up Time - {startTime} {Environment.NewLine}" +
                                $"Form Up System - {location} - {locationinfo} {Environment.NewLine}" +
                                $"Details - {details}{Environment.NewLine}" +
                                $"```{Environment.NewLine}{url}";

                            var sendres = await channel.SendMessageAsync(message);

                            await sendres.AddReactionAsync(EmojiExtensions.FromText(":white_check_mark:"));
                            await sendres.AddReactionAsync(EmojiExtensions.FromText(":grey_question:"));
                            await sendres.AddReactionAsync(EmojiExtensions.FromText(":x:"));

                            await SQLiteDataUpdate("cacheData", "data", "fleetUpLastPostedOperation", operation["OperationId"].ToString());
                        }
                    }
                    await SQLiteDataUpdate("cacheData", "data", "fleetUpLastChecked", DateTime.Now.ToString());
                }
            }

            
        }

        internal static async Task Ops(ICommandContext context)
        {
            using (HttpClient webRequest = new HttpClient())
            {
                var UserId = Program.Settings.GetSection("fleetup")["UserId"];
                var APICode = Program.Settings.GetSection("fleetup")["APICode"];
                var GroupID = Program.Settings.GetSection("fleetup")["GroupID"];
                var channelid = Convert.ToUInt64(Program.Settings.GetSection("fleetup")["channel"]);
                var guildId = Convert.ToUInt64(Program.Settings.GetSection("config")["guildId"]);
                var lastopid = await SQLiteDataQuery("cacheData", "data", "fleetUpLastPostedOperation");

                var channel = (dynamic)Program.Client.GetGuild(guildId).GetChannel(channelid);

                var Json = await webRequest.GetStringAsync($"http://api.fleet-up.com/Api.svc/Ohigwbylcsuz56ue3O6Awlw5e/{UserId}/{APICode}/Operations/{GroupID}");
                var result = JObject.Parse(Json);
                var message = $"{context.Message.Author.Mention}, {Environment.NewLine}";
                var count = message.Count();
                if (result["Data"].IsNullOrEmpty())
                {
                    await context.Message.Channel.SendMessageAsync($"{message}No Ops Scheduled");
                }
                else
                {
                    foreach (var operation in result["Data"])
                    {
                        var name = operation["Subject"];
                        var startTime = operation["StartString"];
                        var locationinfo = operation["LocationInfo"];
                        var location = operation["Location"];
                        var details = operation["Details"];
                        var url = $"http://fleet-up.com/Operation#{operation["OperationId"]}";

                        var message_temp = $"```Title - {name} {Environment.NewLine}" +
                                    $"Form Up Time - {startTime} {Environment.NewLine}" +
                                    $"Form Up System - {location} - {locationinfo} {Environment.NewLine}" +
                                    $"Details - {details}```" +
                                    $"{url}{Environment.NewLine}{Environment.NewLine}";

                        if (message.Count() + message_temp.Count() >= 2000)
                        {
                            if (message.Count() != count)
                            {
                                await context.Message.Channel.SendMessageAsync($"{message}");
                                message = $"{context.Message.Author.Mention}, {Environment.NewLine}";
                            }
                            else
                            {
                                message += $"{message_temp}";
                                await context.Message.Channel.SendMessageAsync($"{message}");
                                message = $"{context.Message.Author.Mention}, {Environment.NewLine}";
                            }
                        }
                        else
                        {
                            message += $"{message_temp}";
                        }
                    }
                }
                if(message != $"{context.Message.Author.Mention}, {Environment.NewLine}")
                    await context.Message.Channel.SendMessageAsync($"{message}");

                await Client_Log(new LogMessage(LogSeverity.Info, "FleetOps", $"Sending Ops to {context.Message.Channel} for {context.Message.Author}"));
            }

            
        }
        #endregion

        //Jabber Broadcasts
        #region Jabber
        internal static async Task Jabber()
        {
            var username = Program.Settings.GetSection("jabber")["username"];
            var password = Program.Settings.GetSection("jabber")["password"];
            var domain = Program.Settings.GetSection("jabber")["domain"];

            if (!jabberRunning)
            {
                try
                {
                    var xmppWrapper = new ReconnectXmppWrapper(domain, username, password);
                    xmppWrapper.Connect(null);
                    jabberRunning = true;
                }
                catch (Exception ex)
                {
                    await Client_Log(new LogMessage(LogSeverity.Error, "Jabber", ex.Message, ex));
                }
            }

            
        }

        internal static async void OnMessage(object sender, MessageEventArgs e)
        {
            if (e.Message.Chatstate != Chatstate.Composing && !string.IsNullOrWhiteSpace(e.Message.Value))
            {
                if (Convert.ToBoolean(Program.Settings.GetSection("jabber").GetSection("filter").Value))
                {
                    foreach (var filter in Program.Settings.GetSection("jabber").GetSection("filters").GetChildren().ToList())
                    {
                        if (e.Message.Value.ToLower().Contains(filter.Key.ToLower()))
                        {
                            var prepend = Program.Settings.GetSection("jabber")["prepend"];
                            var channel = (dynamic)Program.Client.GetChannel(Convert.ToUInt64(filter.Value));
                            await channel.SendMessageAsync($"{prepend + Environment.NewLine}From: {e.Message.From.User} {Environment.NewLine} Message: ```{e.Message.Value}```");
                        }
                    }
                }
                else if (!string.IsNullOrWhiteSpace(e.Message.Value))
                {
                    var prepend = Program.Settings.GetSection("jabber")["prepend"];
                    var channel = (dynamic)Program.Client.GetChannel(Convert.ToUInt64(Program.Settings.GetSection("jabber")["defchan"]));
                    await channel.SendMessageAsync($"{prepend + Environment.NewLine}From: {e.Message.From.User} {Environment.NewLine} Message: ```{e.Message.Value}```");
                }
            }
        }
        #endregion

        //About
        #region About
        internal async static Task About(ICommandContext context)
        {
            if (AppContext.BaseDirectory.Contains("netcoreapp1.1"))
            {
                var directory = Path.Combine(Directory.GetParent(Directory.GetParent(Directory.GetParent(Directory.GetParent(
                Directory.GetParent(AppContext.BaseDirectory).FullName).FullName).FullName).FullName).FullName);
            }
            else
            {
                var directory = Path.Combine(AppContext.BaseDirectory);
            }
            //using (var repo = new Repository(directory))
            //{
            var channel = (dynamic)context.Channel;
            var botid = Program.Client.CurrentUser.Id;
            var MemoryUsed = ByteSize.FromBytes(Process.GetCurrentProcess().PrivateMemorySize64);
            var RunTime = DateTime.Now - Process.GetCurrentProcess().StartTime;
            var Guilds = Program.Client.Guilds.Count;
            var TotalUsers = 0;
            foreach (var guild in Program.Client.Guilds)
            {
                TotalUsers = TotalUsers + guild.Users.Count;
            }

            channel.SendMessageAsync($"{context.User.Mention},{Environment.NewLine}{Environment.NewLine}" +
                $"```Developer: Jimmy06 (In-game Name: Jimmy06){Environment.NewLine}{Environment.NewLine}" +
                $"Bot ID: {botid}{Environment.NewLine}{Environment.NewLine}" +
                //$"Current Version: {repo.Head.Tip.Id}{Environment.NewLine}" +
                //$"Current Branch: {repo.Head.FriendlyName}{Environment.NewLine}" +
                $"Run Time: {RunTime.Days}:{RunTime.Hours}:{RunTime.Minutes}:{RunTime.Seconds}{Environment.NewLine}{Environment.NewLine}" +
                $"Statistics:{Environment.NewLine}" +
                $"Memory Used: {Math.Round(MemoryUsed.LargestWholeNumberValue, 2)} {MemoryUsed.LargestWholeNumberSymbol}{Environment.NewLine}" +
                $"Total Connected Guilds: {Guilds}{Environment.NewLine}" +
                $"Total Users Seen: {TotalUsers}```");
            //$"GitHub URL: <{repo.Config.ToList().FirstOrDefault(x => x.Key == "remote.origin.url").Value}>");
            //}

            await Task.CompletedTask;
        }
        #endregion

        //Char
        #region Char
        internal async static Task Char(ICommandContext context, string x)
        {
            var channel = (dynamic)context.Channel;
            using (HttpClient webclient = new HttpClient())
            using (HttpResponseMessage _characterid = await webclient.GetAsync($"https://esi.tech.ccp.is/latest/search/?categories=character&datasource=tranquility&language=en-us&search={x}&strict=false"))
            using (HttpContent _characteridContent = _characterid.Content)
            {
                var id = JObject.Parse(await _characteridContent.ReadAsStringAsync())["character"].FirstOrDefault();
                var _character = await webclient.GetAsync($"https://esi.tech.ccp.is/latest/characters/{id}/?datasource=tranquility");
                var _characterContent = JObject.Parse(await _character.Content.ReadAsStringAsync());
                var _corp = await webclient.GetAsync($"https://esi.tech.ccp.is/latest/corporations/{_characterContent["corporation_id"]}/?datasource=tranquility");
                var _corpContent = JObject.Parse(await _corp.Content.ReadAsStringAsync());
                var _zkill = await webclient.GetAsync($"https://zkillboard.com/api/kills/characterID/{id}/");
                var _zkillContent = JArray.Parse(await _zkill.Content.ReadAsStringAsync())[0];
                var lastSystem = await webclient.GetAsync($"https://esi.tech.ccp.is/latest/universe/systems/{_zkillContent["solarSystemID"]}/?datasource=tranquility&language=en-us");
                var _lastSystem = JObject.Parse(await lastSystem.Content.ReadAsStringAsync());
                var lastShipType = "Unknown";
                if (_zkillContent["victim"]["characterID"] == id)
                {
                    lastShipType = _zkillContent["victim"]["shipTypeID"].ToString();
                }
                else
                {
                    foreach (var attacker in _zkillContent["attackers"])
                    {
                        if ((int)attacker["characterID"] == (int)id)
                        {
                            lastShipType = attacker["shipTypeID"].ToString();
                        }
                    }
                }

                var lastShip = await webclient.GetAsync($"https://esi.tech.ccp.is/latest/universe/types/{lastShipType}/?datasource=tranquility&language=en-us");
                var _lastShip = JObject.Parse(await lastShip.Content.ReadAsStringAsync());
                var _lastSeen = _zkillContent["killTime"];

                var _ally = await webclient.GetAsync($"https://esi.tech.ccp.is/latest/alliances/{_corpContent["alliance_id"]}/?datasource=tranquility");
                var _allyContent = JObject.Parse(await _ally.Content.ReadAsStringAsync());
                var alliance = _allyContent["alliance_name"].IsNullOrEmpty() ? "None" : _allyContent["alliance_name"];

                await channel.SendMessageAsync($"```Name: {_characterContent["name"]}{Environment.NewLine}" +
                    $"DOB: {_characterContent["birthday"]}{Environment.NewLine}{Environment.NewLine}" +
                    $"Corporation Name: {_corpContent["corporation_name"]}{Environment.NewLine}" +
                    $"Alliance Name: {alliance}{Environment.NewLine}{Environment.NewLine}" +
                    $"Last System: {_lastSystem["name"]}{Environment.NewLine}" +
                    $"Last Ship: {_lastShip["name"]}{Environment.NewLine}" +
                    $"Last Seen: {_lastSeen}{Environment.NewLine}```" +
                    $"ZKill: https://zkillboard.com/character/{id}/");
            }
            await Task.CompletedTask;
        }
        #endregion

        //Corp
        #region Corp
        internal async static Task Corp(ICommandContext context, string x)
        {
            var channel = (dynamic)context.Channel;
            using (HttpClient webclient = new HttpClient())
            using (HttpResponseMessage _characterid = await webclient.GetAsync($"https://esi.tech.ccp.is/latest/search/?categories=corporation&datasource=tranquility&language=en-us&search={x}&strict=false"))
            using (HttpContent _characteridContent = _characterid.Content)
            {
                var _corpContent = JObject.Parse(await _characteridContent.ReadAsStringAsync());
                var _corpDetails = await webclient.GetAsync($"https://esi.tech.ccp.is/latest/corporations/{_corpContent["corporation"][0]}/?datasource=tranquility");
                var _CorpDetailsContent = JObject.Parse(await _corpDetails.Content.ReadAsStringAsync());
                var _CEOName = await webclient.GetAsync($"https://esi.tech.ccp.is/latest/characters/{_CorpDetailsContent["ceo_id"]}/?datasource=tranquility");
                var _CEONameContent = JObject.Parse(await _CEOName.Content.ReadAsStringAsync());
                var _ally = await webclient.GetAsync($"https://esi.tech.ccp.is/latest/alliances/{_CorpDetailsContent["alliance_id"]}/?datasource=tranquility");
                var _allyContent = JObject.Parse(await _ally.Content.ReadAsStringAsync());
                var alliance = _allyContent["alliance_name"].IsNullOrEmpty() ? "None" : _allyContent["alliance_name"];

                await channel.SendMessageAsync($"```Corp Name: {_CorpDetailsContent["corporation_name"]}{Environment.NewLine}" +
                        $"Corp Ticker: {_CorpDetailsContent["ticker"]}{Environment.NewLine}" +
                        $"CEO: {_CEONameContent["name"]}{Environment.NewLine}" +
                        $"Alliance Name: {alliance}{Environment.NewLine}" +
                        $"Member Count: {_CorpDetailsContent["member_count"]}{Environment.NewLine}```" +
                        $"ZKill: https://zkillboard.com/corporation/{_corpContent["corporation"][0]}/");
            }
            await Task.CompletedTask;
        }
        #endregion

        //Discord Stuff
        #region Discord Modules
        internal static async Task InstallCommands()
        {
            Program.Client.MessageReceived += HandleCommand;
            await Program.Commands.AddModulesAsync(Assembly.GetEntryAssembly());
        }

        internal static async Task HandleCommand(SocketMessage messageParam)
        {

            var message = messageParam as SocketUserMessage;
            if (message == null) return;

            int argPos = 0;

            if (!(message.HasCharPrefix(Program.Settings.GetSection("config")["commandprefix"].ToCharArray()[0], ref argPos) || message.HasMentionPrefix
                    (Program.Client.CurrentUser, ref argPos))) return;

            var context = new CommandContext(Program.Client, message);

            var result = await Program.Commands.ExecuteAsync(context, argPos, Program.ServiceCollection);
            if (!result.IsSuccess && result.ErrorReason == "Unknown command.")
                await context.Channel.SendMessageAsync(result.ErrorReason);
        }
        #endregion

        //Complete
        #region MysqlQuery
        internal static async Task<IList<IDictionary<string, object>>> MysqlQuery(string connstring, string query)
        {
            using (MySqlConnection conn = new MySqlConnection(connstring))
            using (MySqlCommand cmd = conn.CreateCommand())
            {
                List<IDictionary<string, object>> list = new List<IDictionary<string, object>>(); ;
                cmd.CommandText = query;
                try
                {
                    conn.ConnectionString = connstring;
                    conn.Open();
                    using (MySqlDataReader reader = cmd.ExecuteReader())
                    {

                        while (reader.Read())
                        {
                            var record = new Dictionary<string, object>();

                            for (var i = 0; i < reader.FieldCount; i++)
                            {
                                var key = reader.GetName(i);
                                var value = reader[i];
                                record.Add(key, value);
                            }

                            list.Add(record);
                        }

                        return list;
                    }
                }
                catch (MySqlException ex)
                {
                    await Client_Log(new LogMessage(LogSeverity.Error, "mySQL", query + " " + ex.Message, ex));
                }
                await Task.Yield();
                return list;
            }
        }
        #endregion

        //SQLite Query
        #region SQLiteQuery
        internal async static Task<string> SQLiteDataQuery(string table, string field, string name)
        {
            using (SqliteConnection con = new SqliteConnection("Data Source = Opux.db;"))
            using (SqliteCommand querySQL = new SqliteCommand($"SELECT {field} FROM {table} WHERE name = @name", con))
            {
                await con.OpenAsync();
                querySQL.Parameters.Add(new SqliteParameter("@name", name));
                try
                {
                    using (SqliteDataReader r = await querySQL.ExecuteReaderAsync())
                    {
                        var result = await r.ReadAsync();
                        return r.GetString(0) ?? "";
                    }
                }
                catch (Exception ex)
                {
                    await Client_Log(new LogMessage(LogSeverity.Error, "SQLite", ex.Message, ex));
                    return null;
                }
            }
        }
        internal async static Task<List<int>> SQLiteDataQuery(string table)
        {
            using (SqliteConnection con = new SqliteConnection("Data Source = Opux.db;"))
            using (SqliteCommand querySQL = new SqliteCommand($"SELECT * FROM {table}", con))
            {
                await con.OpenAsync();
                try
                {
                    using (SqliteDataReader r = await querySQL.ExecuteReaderAsync())
                    {
                        var list = new List<int>();
                        while (await r.ReadAsync())
                        {
                            list.Add(Convert.ToInt32(r["Id"]));
                        }

                        return list;
                    }
                }
                catch (Exception ex)
                {
                    await Client_Log(new LogMessage(LogSeverity.Error, "SQLite", ex.Message, ex));
                    return null;
                }
            }
        }
        #endregion

        //SQLite Update
        #region SQLiteQuery
        internal async static Task SQLiteDataUpdate(string table, string field, string name, string data)
        {
            using (SqliteConnection con = new SqliteConnection("Data Source = Opux.db;"))
            using (SqliteCommand insertSQL = new SqliteCommand($"UPDATE {table} SET {field} = @data WHERE name = @name", con))
            {
                await con.OpenAsync();
                insertSQL.Parameters.Add(new SqliteParameter("@name", name));
                insertSQL.Parameters.Add(new SqliteParameter("@data", data));
                try
                {
                    insertSQL.ExecuteNonQuery();
                    
                }
                catch (Exception ex)
                {
                    await Client_Log(new LogMessage(LogSeverity.Error, "SQLite", ex.Message, ex));
                }
            }
        }
        #endregion

        //SQLite Delete
        #region SQLiteDelete
        internal async static Task SQLiteDataDelete(string table, string name)
        {
            using (SqliteConnection con = new SqliteConnection("Data Source = Opux.db;"))
            using (SqliteCommand insertSQL = new SqliteCommand($"REMOVE FROM {table} WHERE name = @name", con))
            {
                await con.OpenAsync();
                insertSQL.Parameters.Add(new SqliteParameter("@name", name));
                try
                {
                    insertSQL.ExecuteNonQuery();
                    
                }
                catch (Exception ex)
                {
                    await Client_Log(new LogMessage(LogSeverity.Error, "SQLite", ex.Message, ex));
                }
            }
        }
        #endregion

        //StripHTML Tags From string
        #region StripHTML
        /// <summary>
        /// Remove HTML from string with Regex.
        /// </summary>
        public static string StripTagsRegex(string source)
        {
            return Regex.Replace(source, "<.*?>", string.Empty);
        }

        /// <summary>
        /// Compiled regular expression for performance.
        /// </summary>
        static Regex _htmlRegex = new Regex("<.*?>", RegexOptions.Compiled);

        /// <summary>
        /// Remove HTML from string with compiled Regex.
        /// </summary>
        public static string StripTagsRegexCompiled(string source)
        {
            return _htmlRegex.Replace(source, string.Empty);
        }

        /// <summary>
        /// Remove HTML tags from string using char array.
        /// </summary>
        public static string StripTagsCharArray(string source)
        {
            char[] array = new char[source.Length];
            int arrayIndex = 0;
            bool inside = false;

            for (int i = 0; i < source.Length; i++)
            {
                char let = source[i];
                if (let == '<')
                {
                    inside = true;
                    continue;
                }
                if (let == '>')
                {
                    inside = false;
                    continue;
                }
                if (!inside)
                {
                    array[arrayIndex] = let;
                    arrayIndex++;
                }
            }
            return new string(array, 0, arrayIndex);
        }
        #endregion

        }

    #region JToken null/empty check
    internal static class JsonExtensions
    {
        public static bool IsNullOrEmpty(this JToken token)
        {
            return (token == null) ||
                   (token.Type == JTokenType.Array && !token.HasValues) ||
                   (token.Type == JTokenType.Object && !token.HasValues) ||
                   (token.Type == JTokenType.String && token.HasValues) ||
                   (token.Type == JTokenType.String && token.ToString() == String.Empty) ||
                   (token.Type == JTokenType.Null);
        }
    }
    #endregion

}
