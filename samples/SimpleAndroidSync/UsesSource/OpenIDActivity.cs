using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Android.Annotation;
using Android.App;
using Android.Content;
using Android.OS;
using Android.Runtime;
using Android.Support.V7.App;
using Android.Views;
using Android.Webkit;
using Android.Widget;
using Java.Net;
using SimpleAndroidSync;

namespace CouchbaseSample.Android
{ 
    [Activity(Label = "OpenIDActivity")]
    public class OpenIDActivity : AppCompatActivity
    {
        public const string IntentLoginUrl = "loginUrl";
        public const string IntentRedirectUrl = "redirectUrl";
        public const string IntentContinuationKey = "continuationKey";

        private const bool MapLocalhostToServer = true;

        private string _loginUrl;
        private string _redirectUrl;

        private void Cancel()
        {
            var intent = Intent;
            var key = intent.GetStringExtra(IntentContinuationKey);
            OpenIDAuthenticator.UnregisterLoginContinuation(key);
            Finish();
        }

        private void DidFinishAuthentication(string url, string error, string description)
        {
            var intent = Intent;
            var key = intent.GetStringExtra(IntentContinuationKey);
            if(key != null) {
                var continuation = OpenIDAuthenticator.GetLoginContinuation(key);
                var authURL = default(Uri);
                if(url != null) {
                    if(Uri.TryCreate(url, UriKind.Absolute, out authURL)) {
                        // Workaround for localhost development and test with Android emulators
                        // when the providers such as Google don't allow the callback host to be
                        // a non public domain (e.g. IP addresses):
                        if(authURL.Host == "localhost" && MapLocalhostToServer) {
                            var application = (CouchbaseSample.Android.Application)Application;
                            var serverHost = application.ServerDbUri.Host;
                            authURL = new Uri(authURL.AbsoluteUri.Replace("localhost", serverHost));
                        }
                    }
                }

                continuation?.Invoke(authURL, new Exception(error));
            }

            OpenIDAuthenticator.UnregisterLoginContinuation(key);
        }

        protected override void OnCreate(Bundle savedInstanceState)
        {
            base.OnCreate(savedInstanceState);

            SetContentView(Resource.Layout.OpenIDActivity);

            var intent = Intent;
            _loginUrl = intent.GetStringExtra(IntentLoginUrl);
            _redirectUrl = intent.GetStringExtra(IntentRedirectUrl);

            var webView = (WebView)FindViewById(Resource.Id.webview);
            webView.SetWebViewClient(new OpenIDWebViewClient(this));
            webView.Settings.JavaScriptEnabled = true;
            webView.LoadUrl(_loginUrl);
        }

        public override bool OnCreateOptionsMenu(IMenu menu)
        {
            var inflater = MenuInflater;
            inflater.Inflate(Resource.Layout.OpenIDMenu, menu);
            return true;
        }

        public override bool OnOptionsItemSelected(IMenuItem item)
        {
            switch(item.ItemId) {
                case Resource.Id.action_open_id_cancel:
                    Cancel();
                    return true;
                default:
                    return false;
            }
        }

        private sealed class OpenIDWebViewClient : WebViewClient
        {
            private readonly OpenIDActivity _parent;

            public OpenIDWebViewClient(OpenIDActivity parent)
            {
                _parent = parent;
            }

            public override bool ShouldOverrideUrlLoading(WebView view, string url)
            {
                if(url.StartsWith(_parent._redirectUrl)) {
                    var parsed = global::Android.Net.Uri.Parse(url);
                    var error = parsed.GetQueryParameter("error");
                    var description = parsed.GetQueryParameter("error_description");
                    _parent.DidFinishAuthentication(url, error, description);
                    return true;
                }

                return base.ShouldOverrideUrlLoading(view, url);
            }
        }
    }

    
}