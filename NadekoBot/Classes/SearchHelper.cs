using NadekoBot.Classes.JSONModels;
using NadekoBot.Extensions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Authentication;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;

namespace NadekoBot.Classes
{
    public enum RequestHttpMethod
    {
        Get,
        Post
    }

    public static class SearchHelper
    {
        private static DateTime lastRefreshed = DateTime.MinValue;
        private static string token { get; set; } = "";

        public static async Task<Stream> GetResponseStreamAsync(string url,
            IEnumerable<KeyValuePair<string, string>> headers = null, RequestHttpMethod method = RequestHttpMethod.Get)
        {
            if (string.IsNullOrWhiteSpace(url))
                throw new ArgumentNullException(nameof(url));
            var httpClient = new HttpClient();
            switch (method)
            {
                case RequestHttpMethod.Get:
                    if (headers != null)
                    {
                        foreach (var header in headers)
                        {
                            httpClient.DefaultRequestHeaders.TryAddWithoutValidation(header.Key, header.Value);
                        }
                    }
                    return await httpClient.GetStreamAsync(url).ConfigureAwait(false);
                case RequestHttpMethod.Post:
                    FormUrlEncodedContent formContent = null;
                    if (headers != null)
                    {
                        formContent = new FormUrlEncodedContent(headers);
                    }
                    var message = await httpClient.PostAsync(url, formContent).ConfigureAwait(false);
                    return await message.Content.ReadAsStreamAsync().ConfigureAwait(false);
                default:
                    throw new NotImplementedException("That type of request is unsupported.");
            }
        }

        public static async Task<string> GetResponseStringAsync(string url,
            IEnumerable<KeyValuePair<string, string>> headers = null,
            RequestHttpMethod method = RequestHttpMethod.Get)
        {

            using (var streamReader = new StreamReader(await GetResponseStreamAsync(url, headers, method).ConfigureAwait(false)))
            {
                return await streamReader.ReadToEndAsync().ConfigureAwait(false);
            }
        }

        public static async Task<AnimeResult> GetAnimeData(string query)
        {
            if (string.IsNullOrWhiteSpace(query))
                throw new ArgumentNullException(nameof(query));

            await RefreshAnilistToken().ConfigureAwait(false);

            var link = "http://anilist.co/api/anime/search/" + Uri.EscapeUriString(query);
            var smallContent = "";
            var cl = new RestSharp.RestClient("http://anilist.co/api");
            var rq = new RestSharp.RestRequest("/anime/search/" + Uri.EscapeUriString(query));
            rq.AddParameter("access_token", token);
            smallContent = cl.Execute(rq).Content;
            var smallObj = JArray.Parse(smallContent)[0];

            rq = new RestSharp.RestRequest("/anime/" + smallObj["id"]);
            rq.AddParameter("access_token", token);
            var content = cl.Execute(rq).Content;

            return await Task.Run(() => JsonConvert.DeserializeObject<AnimeResult>(content)).ConfigureAwait(false);
        }

        public static async Task<MangaResult> GetMangaData(string query)
        {
            if (string.IsNullOrWhiteSpace(query))
                throw new ArgumentNullException(nameof(query));

            await RefreshAnilistToken().ConfigureAwait(false);

            var link = "http://anilist.co/api/anime/search/" + Uri.EscapeUriString(query);
            var smallContent = "";
            var cl = new RestSharp.RestClient("http://anilist.co/api");
            var rq = new RestSharp.RestRequest("/manga/search/" + Uri.EscapeUriString(query));
            rq.AddParameter("access_token", token);
            smallContent = cl.Execute(rq).Content;
            var smallObj = JArray.Parse(smallContent)[0];

            rq = new RestSharp.RestRequest("/manga/" + smallObj["id"]);
            rq.AddParameter("access_token", token);
            var content = cl.Execute(rq).Content;

            return await Task.Run(() => JsonConvert.DeserializeObject<MangaResult>(content)).ConfigureAwait(false);
        }

        private static async Task RefreshAnilistToken()
        {
            if (DateTime.Now - lastRefreshed > TimeSpan.FromMinutes(29))
                lastRefreshed = DateTime.Now;
            else
            {
                return;
            }
            var headers = new Dictionary<string, string> {
                {"grant_type", "client_credentials"},
                {"client_id", "kwoth-w0ki9"},
                {"client_secret", "Qd6j4FIAi1ZK6Pc7N7V4Z"},
            };
            var content = await GetResponseStringAsync(
                            "http://anilist.co/api/auth/access_token",
                            headers,
                            RequestHttpMethod.Post).ConfigureAwait(false);

            token = JObject.Parse(content)["access_token"].ToString();
        }

        public static async Task<bool> ValidateQuery(Discord.Channel ch, string query)
        {
            if (!string.IsNullOrEmpty(query.Trim())) return true;
            await ch.Send("Please specify search parameters.").ConfigureAwait(false);
            return false;
        }

        public static async Task<string> FindYoutubeUrlByKeywords(string keywords)
        {
            if (string.IsNullOrWhiteSpace(NadekoBot.Creds.GoogleAPIKey))
                throw new InvalidCredentialException("Google API Key is missing.");
            if (string.IsNullOrWhiteSpace(keywords))
                throw new ArgumentNullException(nameof(keywords), "Query not specified.");
            if (keywords.Length > 150)
                throw new ArgumentException("Query is too long.");

            //maybe it is already a youtube url, in which case we will just extract the id and prepend it with youtube.com?v=
            var match = new Regex("(?:youtu\\.be\\/|v=)(?<id>[\\da-zA-Z\\-_]*)").Match(keywords);
            if (match.Length > 1)
            {
                return $"http://www.youtube.com?v={match.Groups["id"].Value}";
            }
            var response = await GetResponseStringAsync(
                                    $"https://www.googleapis.com/youtube/v3/search?" +
                                    $"part=snippet&maxResults=1" +
                                    $"&q={Uri.EscapeDataString(keywords)}" +
                                    $"&key={NadekoBot.Creds.GoogleAPIKey}").ConfigureAwait(false);
            dynamic obj = JObject.Parse(response);
            return "http://www.youtube.com/watch?v=" + obj.items[0].id.videoId.ToString();
        }

        public static async Task<string> GetPlaylistIdByKeyword(string query)
        {
            if (string.IsNullOrWhiteSpace(NadekoBot.Creds.GoogleAPIKey))
                throw new ArgumentNullException(nameof(query));

            var link = "https://www.googleapis.com/youtube/v3/search?part=snippet" +
                        "&maxResults=1&type=playlist" +
                       $"&q={Uri.EscapeDataString(query)}" +
                       $"&key={NadekoBot.Creds.GoogleAPIKey}";

            var response = await GetResponseStringAsync(link).ConfigureAwait(false);
            dynamic obj = JObject.Parse(response);

            return obj.items[0].id.playlistId.ToString();
        }

        public static async Task<IEnumerable<string>> GetVideoIDs(string playlist, int number = 30)
        {
            if (string.IsNullOrWhiteSpace(NadekoBot.Creds.GoogleAPIKey))
            {
                throw new ArgumentNullException(nameof(playlist));
            }
            if (number < 1 || number > 100)
                throw new ArgumentOutOfRangeException();
            var link =
                $"https://www.googleapis.com/youtube/v3/playlistItems?part=contentDetails" +
                $"&maxResults={30}" +
                $"&playlistId={playlist}" +
                $"&key={NadekoBot.Creds.GoogleAPIKey}";

            var response = await GetResponseStringAsync(link).ConfigureAwait(false);
            var obj = await Task.Run(() => JObject.Parse(response)).ConfigureAwait(false);

            return obj["items"].Select(item => "http://www.youtube.com/watch?v=" + item["contentDetails"]["videoId"]);
        }


        public static async Task<string> GetDanbooruImageLink(string tag)
        {
            var rng = new Random();

            if (tag == "loli") //loli doesn't work for some reason atm
                tag = "flat_chest";

            var link = $"http://danbooru.donmai.us/posts?" +
                        $"page={rng.Next(0, 15)}";
            if (!string.IsNullOrWhiteSpace(tag))
                link += $"&tags={tag.Replace(" ", "_")}";

            var webpage = await GetResponseStringAsync(link).ConfigureAwait(false);
            var matches = Regex.Matches(webpage, "data-large-file-url=\"(?<id>.*?)\"");

            return $"http://danbooru.donmai.us" +
                   $"{matches[rng.Next(0, matches.Count)].Groups["id"].Value}";
        }

        public static async Task<string> GetGelbooruImageLink(string tag)
        {
            var url =
            $"http://gelbooru.com/index.php?page=dapi&s=post&q=index&limit=100&tags={tag.Replace(" ", "_")}";
            var webpage = await GetResponseStringAsync(url).ConfigureAwait(false);
            var matches = Regex.Matches(webpage, "file_url=\"(?<url>.*?)\"");
            if (matches.Count == 0)
                throw new FileNotFoundException();
            var rng = new Random();
            var match = matches[rng.Next(0, matches.Count)];
            return matches[rng.Next(0, matches.Count)].Groups["url"].Value;
        }

        public static async Task<string> GetSafebooruImageLink(string tag)
        {
            var rng = new Random();
            var url =
            $"http://safebooru.org/index.php?page=dapi&s=post&q=index&limit=100&tags={tag.Replace(" ", "_")}";
            var webpage = await GetResponseStringAsync(url).ConfigureAwait(false);
            var matches = Regex.Matches(webpage, "file_url=\"(?<url>.*?)\"");
            if (matches.Count == 0)
                throw new FileNotFoundException();
            var match = matches[rng.Next(0, matches.Count)];
            return matches[rng.Next(0, matches.Count)].Groups["url"].Value;
        }

        public static async Task<string> GetRule34ImageLink(string tag)
        {
            var rng = new Random();
            var url =
            $"http://rule34.xxx/index.php?page=dapi&s=post&q=index&limit=100&tags={tag.Replace(" ", "_")}";
            var webpage = await GetResponseStringAsync(url).ConfigureAwait(false);
            var matches = Regex.Matches(webpage, "file_url=\"(?<url>.*?)\"");
            if (matches.Count == 0)
                throw new FileNotFoundException();
            var match = matches[rng.Next(0, matches.Count)];
            return "http:" + matches[rng.Next(0, matches.Count)].Groups["url"].Value;
        }


        internal static async Task<string> GetE621ImageLink(string tags)
        {
            try
            {
                ServicePointManager.Expect100Continue = true;
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Ssl3 | SecurityProtocolType.Tls | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls12;
                XDocument doc = XDocument.Load(" https://e621.net/post/index.xml?tags="+Uri.EscapeUriString(tags)+"%20order:random&limit=1");
                int id = Convert.ToInt32(doc.Root.Element("post").Element("id").Value);
                return ("I found a match for the tags **" + tags + "**\nPermalink: https://e621.net/post/show/"+id+" \n\n"+doc.Root.Element("post").Element("file_url").Value);
            }
            catch (Exception)
            {
                return "Error, do you have too many tags?";
            }
        }
		
		internal static async Task<string> GetCowZoneImageLink(string tags)
        {
            try
            {
                Random rId = new Random();
                ServicePointManager.Expect100Continue = true;
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Ssl3 | SecurityProtocolType.Tls | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls12;
                WebClient xmlclient = new WebClient();
                
                string xmlstring = xmlclient.DownloadString(" https://cow.zone/shimmie2/post/index.xml?tags=order=random_" + rId.Next(1, 9999).ToString() + "&limit=1");
                
                XmlReader reader = XmlReader.Create(new StringReader(xmlstring));
                reader.ReadToFollowing("post");
                reader.MoveToAttribute("id");
                int id = Convert.ToInt32(reader.Value);
                reader.MoveToAttribute("file_url");
                string fileurl = reader.Value;

                
                return ("Here is a random image from Dante's Archive \nPermalink: https://cow.zone/shimmie2/post/show/" + id + " \n\n" + fileurl);
            }
            catch (Exception exc)
            {
                return exc.GetBaseException().ToString();
            }
        }

        public static async Task<string> ShortenUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(NadekoBot.Creds.GoogleAPIKey)) return url;
            try
            {
                var httpWebRequest =
                    (HttpWebRequest)WebRequest.Create("https://www.googleapis.com/urlshortener/v1/url?key=" +
                                                       NadekoBot.Creds.GoogleAPIKey);
                httpWebRequest.ContentType = "application/json";
                httpWebRequest.Method = "POST";

                using (var streamWriter = new StreamWriter(await httpWebRequest.GetRequestStreamAsync().ConfigureAwait(false)))
                {
                    var json = "{\"longUrl\":\"" + url + "\"}";
                    streamWriter.Write(json);
                }

                var httpResponse = (await httpWebRequest.GetResponseAsync().ConfigureAwait(false)) as HttpWebResponse;
                if (httpResponse == null) return "HTTP_RESPONSE_ERROR";
                var responseStream = httpResponse.GetResponseStream();
                if (responseStream == null) return "RESPONSE_STREAM ERROR";
                using (var streamReader = new StreamReader(responseStream))
                {
                    var responseText = await streamReader.ReadToEndAsync().ConfigureAwait(false);
                    return Regex.Match(responseText, @"""id"": ?""(?<id>.+)""").Groups["id"].Value;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
                return url;
            }
        }
    }
}
