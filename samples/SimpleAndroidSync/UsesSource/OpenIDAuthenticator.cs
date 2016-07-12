using System;
using System.Collections.Generic;
using Android.Content;
using Android.OS;
using Couchbase.Lite.Auth;

namespace CouchbaseSample.Android
{
    public static class OpenIDAuthenticator
    {
        private static readonly Dictionary<string, OIDCLoginContinuation> _ContinuationMap =
            new Dictionary<string, OIDCLoginContinuation>();

        public static string RegisterLoginContinuation(OIDCLoginContinuation continuation)
        {
            var key = Guid.NewGuid().ToString();
            _ContinuationMap.Add(key, continuation);
            return key;
        }

        public static OIDCLoginContinuation GetLoginContinuation(string key)
        {
            return _ContinuationMap.ContainsKey(key) ? _ContinuationMap[key] : null;
        }

        public static void UnregisterLoginContinuation(string key)
        {
            _ContinuationMap.Remove(key);
        }

        public static OIDCCallback GetOIDCCallback(Context context)
        {
            OIDCCallback callback = (loginUri, redirectUri, continuation) =>
            {
                RunOnUiThread(context, () =>
                {
                    var continuationKey = RegisterLoginContinuation(continuation);
                    var intent = new Intent(context, typeof(OpenIDActivity));
                    intent.SetFlags(ActivityFlags.NewTask);
                    intent.PutExtra(OpenIDActivity.IntentLoginUrl, loginUri.AbsoluteUri);
                    intent.PutExtra(OpenIDActivity.IntentRedirectUrl, redirectUri.AbsoluteUri);
                    intent.PutExtra(OpenIDActivity.IntentContinuationKey, continuationKey);
                    context.StartActivity(intent);
                });
            };

            return callback;
        }

        private static void RunOnUiThread(Context context, Action action)
        {
            var mainHandler = new Handler(context.MainLooper);
            mainHandler.Post(action);
        }
    }
}