using System;
using System.Collections;
using System.Text;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Networking;

namespace WarPoker.Net
{
    /// <summary>Result of a REST call. On failure, Error holds the server code (or "NETWORK").</summary>
    public class ApiResult<T>
    {
        public bool Ok;
        public T Data;
        public string Error;
        public string Message;
        public long Status;
    }

    /// <summary>REST calls from docs/protocol.md using UnityWebRequest + Newtonsoft.</summary>
    public class ApiClient
    {
        readonly MonoBehaviour _host;
        public Func<string> BaseUrl;   // read on every call so a changed address applies at once
        public Func<string> Token;

        const int TimeoutSeconds = 15;

        public ApiClient(MonoBehaviour host, Func<string> baseUrl, Func<string> token)
        {
            _host = host;
            BaseUrl = baseUrl;
            Token = token;
        }

        class Credentials
        {
            public string username;
            public string password;
        }

        public void Register(string username, string password, Action<ApiResult<AuthResponse>> done)
        {
            Send("POST", "/auth/register", new Credentials { username = username, password = password }, false, done);
        }

        public void Login(string username, string password, Action<ApiResult<AuthResponse>> done)
        {
            Send("POST", "/auth/login", new Credentials { username = username, password = password }, false, done);
        }

        public void Me(Action<ApiResult<UserResponse>> done)
        {
            Send("GET", "/me", null, true, done);
        }

        public void Refill(Action<ApiResult<UserResponse>> done)
        {
            Send("POST", "/me/refill", null, true, done);
        }

        public void Rooms(Action<ApiResult<RoomsResponse>> done)
        {
            Send("GET", "/rooms", null, false, done);
        }

        void Send<T>(string method, string path, object body, bool auth, Action<ApiResult<T>> done)
        {
            _host.StartCoroutine(SendRoutine(method, path, body, auth, done));
        }

        IEnumerator SendRoutine<T>(string method, string path, object body, bool auth, Action<ApiResult<T>> done)
        {
            string url = BaseUrl() + path;
            using (var req = new UnityWebRequest(url, method))
            {
                // POST bodies are always JSON ("{}" when there is no payload).
                if (method != "GET")
                {
                    string json = body != null ? JsonConvert.SerializeObject(body) : "{}";
                    req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
                    req.SetRequestHeader("Content-Type", "application/json");
                }
                req.downloadHandler = new DownloadHandlerBuffer();
                req.SetRequestHeader("Accept", "application/json");
                if (auth)
                {
                    string token = Token != null ? Token() : null;
                    if (!string.IsNullOrEmpty(token)) req.SetRequestHeader("Authorization", "Bearer " + token);
                }
                req.timeout = TimeoutSeconds;

                yield return req.SendWebRequest();

                var result = new ApiResult<T> { Status = req.responseCode };
                string text = req.downloadHandler != null ? req.downloadHandler.text : null;

                if (req.result == UnityWebRequest.Result.ConnectionError || req.responseCode == 0)
                {
                    result.Ok = false;
                    result.Error = "NETWORK";
                    result.Message = req.error;
                }
                else if (req.responseCode >= 200 && req.responseCode < 300)
                {
                    try
                    {
                        result.Data = JsonConvert.DeserializeObject<T>(text);
                        result.Ok = true;
                    }
                    catch (Exception e)
                    {
                        result.Ok = false;
                        result.Error = "BAD_RESPONSE";
                        result.Message = e.Message;
                    }
                }
                else
                {
                    result.Ok = false;
                    result.Error = "HTTP_" + req.responseCode;
                    try
                    {
                        var err = JsonConvert.DeserializeObject<ErrorResponse>(text);
                        if (err != null && !string.IsNullOrEmpty(err.error))
                        {
                            result.Error = err.error;
                            result.Message = err.message;
                        }
                    }
                    catch (Exception)
                    {
                        // Not JSON (e.g. a proxy error page) — keep the HTTP code.
                    }
                }

                if (!result.Ok) Debug.LogWarning("[api] " + method + " " + path + " -> " + result.Error + " " + result.Message);
                done?.Invoke(result);
            }
        }
    }
}
