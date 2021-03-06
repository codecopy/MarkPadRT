﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using MarkPad.Core;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Windows.Foundation;
using Windows.Security.Authentication.Web;

namespace MarkPad.GitHub
{
    public class GitHubClient : ISource
    {
        private const string ClientId = "ed0cdbf084078f60b8a3";
        private const string ClientSecret = "a0a442815b7530386c90088a98cfd018877624d2";
        private const string RedirectUri = "http://vikingco.de";
        private const string Accesstokenuri = "https://github.com/login/oauth/access_token";
        private const string ApiBaseUrl = "https://api.github.com/";
        private string _accessToken;

        public async Task<bool> Login()
        {
            var url = "https://github.com/login/oauth/authorize?client_id=" + Uri.EscapeDataString(ClientId) + "&redirect_uri=" + Uri.EscapeDataString(RedirectUri) + "&scope=repo&display=popup&response_type=token";
            var startUri = new Uri(url);
            var endUri = new Uri(RedirectUri);
            var webAuthenticationResult = await WebAuthenticationBroker.AuthenticateAsync(WebAuthenticationOptions.None, startUri, endUri);
            string code = "";
            switch (webAuthenticationResult.ResponseStatus)
            {
                case WebAuthenticationStatus.Success:
                    var decoder = new WwwFormUrlDecoder(webAuthenticationResult.ResponseData.Replace(RedirectUri + "/?", ""));
                    code = decoder.First(x => x.Name == "code").Value;
                    break;
                case WebAuthenticationStatus.ErrorHttp:
                    return false;
                    break;
                default:
                    return false;
                    break;
            }
            var c = new HttpClient();
            var data = new Dictionary<string, string>
                           {
                               {"client_id", ClientId},
                               {"client_secret", ClientSecret},
                               {"code", code}
                           };
            var content = new FormUrlEncodedContent(data);
            var request = await c.PostAsync(Accesstokenuri, content);
            var result = await request.Content.ReadAsStringAsync();
            _accessToken = new WwwFormUrlDecoder(result).GetFirstValueByName("access_token");
            return true;
        }

        public Task<IEnumerable<Document>> Open()
        {
            return null;
        }

        public Task<IEnumerable<Document>> Restore()
        {
            return null;
        }

        public async Task Save(Document document)
        {
            return;
        }

        public Document GetFile()
        {
            return null;
        }

        public IEnumerable<Document> GetFiles()
        {
            return null;
        }

        public void Save(IEnumerable<Document> document)
        {
        
        }

        public async Task<List<string>> GetRepos()
        {
            var results = new List<string>();
            var c = new HttpClient();
            var query = await c.GetAsync(GetUrl("user/repos"));
            var obj = JObject.Parse("{ \"repos\": " + await query.Content.ReadAsStringAsync() + "}");
            
            foreach (var x in obj["repos"].AsJEnumerable())
            {
                results.Add(x["name"].ToString());
            }
            return results;
        }

        private string GetUrl(string path)
        {
            return string.Format("{0}{1}?access_token={2}", ApiBaseUrl, path, _accessToken);
        }

        public async Task SaveFile(string name, string contents, string user, string repo)
        {
            var c = new HttpClient();

            var refquery = await c.GetAsync(GetUrl(string.Format("repos/{0}/{1}/git/refs/heads/master", user, repo)));
            var shaLatestCommit = JObject.Parse(await refquery.Content.ReadAsStringAsync())["object"]["sha"].ToString();

            var baseTreeQuery = await c.GetAsync(GetUrl(string.Format("repos/{0}/{1}/git/commits/{2}", user, repo, shaLatestCommit)));
            var shaBaseTree = JObject.Parse(await baseTreeQuery.Content.ReadAsStringAsync())["tree"]["sha"].ToString();

            //Create a new tree
            string url = "/repos/:user/:repo/git/trees/";
            var tree = new
            {
                base_tree = shaBaseTree,
                tree = new[] 
                  {  
                      new
                        {
                            path = name,
                            mode = "100644",
                            type = "blob",
                            content = contents
                        }
                  }
            };

            var newTreeQuery = await c.PostAsync(GetUrl(string.Format("repos/{0}/{1}/git/trees", user, repo)), new StringContent(JsonConvert.SerializeObject(tree)));
            var shaNewTree = JObject.Parse(await newTreeQuery.Content.ReadAsStringAsync())["sha"].ToString();

            //Create a new commit
            var newCommit = new
                {
                    message = "my commit message",
                    parents = new[] { shaLatestCommit },
                    tree = shaNewTree
                };
            var newCommitQuery = await c.PostAsync(GetUrl(string.Format("repos/{0}/{1}/git/commits", user, repo)), new StringContent(JsonConvert.SerializeObject(newCommit)));
            var newCommitResult = await newCommitQuery.Content.ReadAsStringAsync();
            var shaNewCommit = JObject.Parse(newCommitResult)["sha"].ToString();

            //finalise
            var content = "{\"sha\": \"" + shaNewCommit + "\"}";
            var newReferenceQuery = await c.PostAsync(GetUrl(string.Format("repos/{0}/{1}/git/refs/heads/master", user, repo)), new StringContent(content));
            var newReferenceResult = await newReferenceQuery.Content.ReadAsStringAsync();
        }
    }
}
