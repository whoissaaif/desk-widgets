using System;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace DeskWidgets
{
    // Minimal HTTP for widgets. Call from a background thread only.
    public static class Http
    {
        static Http()
        {
            ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072; // TLS 1.2
        }

        public static string Get(string url)
        {
            return Encoding.UTF8.GetString(GetBytes(url));
        }

        // With an OAuth bearer token (null for none).
        internal static string Get(string url, string bearer)
        {
            return Encoding.UTF8.GetString(Send(Request(url, bearer)));
        }

        public static byte[] GetBytes(string url)
        {
            return Send(Request(url, null));
        }

        // With extra request headers (the community gallery's API key). Not part of the plugin API.
        internal static byte[] GetBytes(string url, System.Collections.Generic.IDictionary<string, string> headers)
        {
            var req = Request(url, null);
            foreach (var h in headers) req.Headers[h.Key] = h.Value;
            return Send(req);
        }

        // POSTs an application/x-www-form-urlencoded body.
        internal static string PostForm(string url, string body)
        {
            var req = Request(url, null);
            req.Method = "POST";
            req.ContentType = "application/x-www-form-urlencoded";
            byte[] data = Encoding.UTF8.GetBytes(body);
            using (var s = req.GetRequestStream()) s.Write(data, 0, data.Length);
            return Encoding.UTF8.GetString(Send(req));
        }

        // Sends JSON (POST, PATCH...) with extra headers and returns the response body. A refusal from
        // the server comes back as HttpError with the server's own message when it sent one.
        internal static string SendJson(string method, string url, System.Collections.Generic.IDictionary<string, string> headers, string json)
        {
            var req = Request(url, null);
            req.Method = method;
            req.ContentType = "application/json";
            foreach (var h in headers) req.Headers[h.Key] = h.Value;
            byte[] data = Encoding.UTF8.GetBytes(json ?? "");
            try
            {
                using (var s = req.GetRequestStream()) s.Write(data, 0, data.Length);
                return Encoding.UTF8.GetString(Send(req));
            }
            catch (WebException e)
            {
                var resp = e.Response as HttpWebResponse;
                if (resp == null) throw;
                string body;
                using (resp)
                using (var r = new StreamReader(resp.GetResponseStream(), Encoding.UTF8)) body = r.ReadToEnd();
                throw new HttpError((int)resp.StatusCode, body);
            }
        }

        static HttpWebRequest Request(string url, string bearer)
        {
            if (url.StartsWith("webcal://", StringComparison.OrdinalIgnoreCase)) url = "https://" + url.Substring(9);
            var req = (HttpWebRequest)WebRequest.Create(url);
            req.UserAgent = "DeskWidgets";
            req.Timeout = 20000;
            // gzip only: .NET Framework's "deflate" decoder rejects the zlib-wrapped data most servers send.
            req.AutomaticDecompression = DecompressionMethods.GZip;
            if (bearer != null) req.Headers[HttpRequestHeader.Authorization] = "Bearer " + bearer;
            return req;
        }

        static byte[] Send(HttpWebRequest req)
        {
            using (var resp = req.GetResponse())
            using (var s = resp.GetResponseStream())
            using (var ms = new MemoryStream())
            {
                s.CopyTo(ms);
                return ms.ToArray();
            }
        }
    }

    // A request the server answered with an error status. Message is the server's explanation
    // ("error", "message" or "msg" in a JSON body), or the status when it gave none.
    sealed class HttpError : Exception
    {
        public readonly int Status;
        public readonly string Body;

        public HttpError(int status, string body) : base(Explain(status, body))
        {
            Status = status;
            Body = body;
        }

        static string Explain(int status, string body)
        {
            try
            {
                var d = new System.Web.Script.Serialization.JavaScriptSerializer().DeserializeObject(body) as System.Collections.Generic.Dictionary<string, object>;
                object v;
                if (d != null)
                    foreach (string key in new[] { "error_description", "error", "message", "msg" })
                        if (d.TryGetValue(key, out v) && v is string && ((string)v).Length > 0) return (string)v;
            }
            catch (Exception) { }
            return "The server answered " + status + ".";
        }
    }

    // Values that must not sit in widgets.ini as plain text (tokens, private calendar links):
    // encrypted with Windows DPAPI, readable only by this Windows account on this PC.
    static class Secrets
    {
        public static string Protect(string plain)
        {
            return Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(plain), null, DataProtectionScope.CurrentUser));
        }

        // Null if it was protected on another PC or account.
        public static string Unprotect(string stored)
        {
            if (string.IsNullOrEmpty(stored)) return null;
            try { return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(stored), null, DataProtectionScope.CurrentUser)); }
            catch (Exception) { return null; }
        }
    }
}
