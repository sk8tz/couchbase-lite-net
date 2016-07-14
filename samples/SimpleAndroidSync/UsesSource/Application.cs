using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Android.App;
using Android.Content;
using Android.OS;
using Android.Runtime;
using Android.Util;
using Android.Widget;
using Couchbase.Lite;
using Couchbase.Lite.Auth;
using Java.Net;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SimpleAndroidSync;
using Square.OkHttp3;

namespace CouchbaseSample.Android
{
    [Application(AllowBackup = true, Label = "SimpleAndroidSync", Theme = "@style/Theme.AppCompat")]
    public sealed class Application : global::Android.App.Application
    {
        public const string TAG = "GrocerySync";

        private const string DatabaseName = "grocery-sync";
        private const string UserLocalDocId = "user";
        private const string ServerDbUrl = "http://us-west.testfest.couchbasemobile.com:4984/grocery-sync";

        private readonly OkHttpClient _httpClient = new OkHttpClient();

        private Replication _push;
        private Replication _pull;
        private Exception _syncError;
        private Action<Replication> _changedHandler;

        public Database Database { get; private set; }

        public string Username { get; private set; }

        public Application(IntPtr handle, JniHandleOwnership ownerShip) : base(handle, ownerShip)
        {
        }

        public Uri ServerDbUri
        {
            get { return new Uri(ServerDbUrl); }
        }

        public URL ServerDbSessionUri
        {
            get { return new URL($"{ServerDbUrl}/_session"); }
        }

        public void ShowMessage(string message)
        {
            RunOnUiThread(() =>
            {
                Log.Info(TAG, message);
                Toast.MakeText(Application.Context, message, ToastLength.Long).Show();
            });
        }

        public void LoginWithAuthCode()
        {
            StopReplication(false);
            StartPull(r =>
            {
                var callback = OpenIDAuthenticator.GetOIDCCallback(Application.Context);
                r.Authenticator = AuthenticatorFactory.CreateOpenIDAuthenticator(Manager.SharedInstance, callback);
                _changedHandler = r1 =>
                {
                    CheckAuthCodeLoginComplete(r1);
                };
            });
        }

        public void LoginWithGoogleSignin(Activity activity, string idToken)
        {
            var request = new Request.Builder()
                .Url(ServerDbSessionUri)
                .Header("Authorization", $"Bearer {idToken}")
                .Post(new FormBody.Builder().Build())
                .Build();

            _httpClient.NewCall(request).Enqueue((c, r) =>
            {
                if(r.IsSuccessful) {
                    var session = default(IDictionary<string, object>);
                    using(var sr = new StreamReader(r.Body().ByteStream()))
                    using(var jsonReader = new JsonTextReader(sr)) {
                        var serializer = JsonSerializer.Create();
                        session = serializer.Deserialize<IDictionary<string, object>>(jsonReader);
                    }

                    var userInfo = ((JObject)session["userCtx"]).ToObject<IDictionary<string, object>>();
                    var username = (string)userInfo["name"];
                    var cookies = Cookie.ParseAll(HttpUrl.Get(new URL(ServerDbUrl)), r.Headers());
                    if(Login(username, cookies)) {
                        CompleteLogin();
                    }
                }
            }, (c, e) =>
            {
                e.PrintStackTrace();
            });
        }

        public void Logout()
        {
            StopReplication(true);
            Username = null;
            var intent = new Intent(Application.Context, typeof(LoginActivity));
            intent.SetFlags(ActivityFlags.NewTask | ActivityFlags.ClearTask);
            intent.SetAction(LoginActivity.IntentActionLogout);
            StartActivity(intent);
        }

        private bool IsReplicationStarted(Replication repl)
        {
            return repl.Status == ReplicationStatus.Idle && repl.ChangesCount > 0;
        }

        private void CheckAuthCodeLoginComplete(Replication repl)
        {
            if(repl != _pull) {
                return;
            }

            // Check the pull replicator is done authenticating or not.
            // If done, start the push replicator:
            if(Username == null && repl.Username != null && IsReplicationStarted(repl)) {
                if(Login(repl.Username)) {
                    if(_pull != null) {
                        _changedHandler = null;
                        StartPush(r =>
                        {
                            var callback = OpenIDAuthenticator.GetOIDCCallback(Application.Context);
                            r.Authenticator = AuthenticatorFactory.CreateOpenIDAuthenticator(Manager.SharedInstance, callback);
                        });

                        CompleteLogin();
                    }
                }
            }
        }

        private void StartPull(Action<Replication> setupCallback)
        {
            _pull = Database.CreatePullReplication(ServerDbUri);
            _pull.Continuous = true;
            setupCallback?.Invoke(_pull);
            _pull.Changed += OnChanged;
            _pull.Start();
        }

        private void StartPush(Action<Replication> setupCallback)
        {
            _push = Database.CreatePushReplication(ServerDbUri);
            _push.Continuous = true;
            setupCallback?.Invoke(_push);
            _push.Changed += OnChanged;
            _push.Start();
        }

        private void StopReplication(bool clearCredentials)
        {
            _changedHandler = null;
            var pull = _pull;
            _pull = null;
            if(pull != null) {
                pull.Stop();
                pull.Changed -= OnChanged;
            }

            var push = _push;
            _push = null;
            if(push != null) {
                push.Stop();
                push.Changed -= OnChanged;
            }
        }

        private void OnChanged(object sender, ReplicationChangeEventArgs args)
        {
            Log.Verbose(TAG, $"Replication change status {args.Status} [{args.Source}]");

            _changedHandler?.Invoke(args.Source);

            var error = _pull?.LastError;
            if(_push != null) {
                if(error == null) {
                    error = _push?.LastError;
                }
            }

            if(error != _syncError) {
                _syncError = error;
                ShowMessage(_syncError.ToString());
            }
        }

        private bool Login(string username, IList<Cookie> cookies)
        {
            var epoch = new DateTime(1970, 1, 1, 0, 0, 0, 0);
            if(Login(username)) {
                StartPull(r =>
                {
                    foreach(var cookie in cookies) {
                        r.SetCookie(cookie.Name(), cookie.Value(), cookie.Path(), epoch + TimeSpan.FromSeconds(cookie.ExpiresAt()),
                            cookie.Secure(), cookie.HttpOnly());
                    }
                });

                StartPush(r =>
                {
                    foreach(var cookie in cookies) {
                        r.SetCookie(cookie.Name(), cookie.Value(), cookie.Path(), epoch + TimeSpan.FromSeconds(cookie.ExpiresAt()),
                            cookie.Secure(), cookie.HttpOnly());
                    }
                });

                return true;
            }

            return false;
        }

        private bool Login(string username)
        {
            if(username == null) {
                return false;
            }

            var user = Database?.GetExistingLocalDocument(UserLocalDocId);
            if(user != null && !username.Equals(user["username"])) {
                StopReplication(false);
                try {
                    Database.Close().Wait();
                    Database.Delete();
                } catch(Exception e) {
                    return false;
                }

                Database = null;
            }

            if(Database == null) {
                if(!InitializeDatabase()) {
                    return false;
                }
            }

            var userInfo = new Dictionary<string, object> {
                ["username"] = username
            };

            try {
                Database.PutLocalDocument(UserLocalDocId, userInfo);
            } catch(Exception e) {
                return false;
            }

            Username = username;
            return true;
        }

        private void CompleteLogin()
        {
            RunOnUiThread(() =>
            {
                var intent = new Intent(Application.Context, typeof(MainActivity));
                intent.SetFlags(ActivityFlags.NewTask | ActivityFlags.ClearTask);
                StartActivity(intent);
            });
        }

        private static void RunOnUiThread(Action action)
        {
            var mainHandler = new Handler(Application.Context.MainLooper);
            mainHandler.Post(action);
        }

        private bool InitializeDatabase()
        {
            if(Database == null) {
                var opts = new DatabaseOptions {
                    StorageType = StorageEngineTypes.SQLite,
                    Create = true
                };

                // To use this feature, add the Couchbase.Lite.Storage.ForestDB nuget package
                //opts.StorageType = StorageEngineTypes.ForestDB;

                // To use this feature add the Couchbase.Lite.Storage.SQLCipher nuget package,
                // or uncomment the above line and add the Couchbase.Lite.Storage.ForestDB package
                //opts.EncryptionKey = new SymmetricKey("foo");

                try {
                    Database = Manager.SharedInstance.OpenDatabase(DatabaseName, opts);
                } catch(Exception e) {
                    Log.Error(TAG, "Couldn't open database", e);
                    return false;
                }
            }

            return true;
        }

        public override void OnCreate()
        {
            base.OnCreate();
            Couchbase.Lite.Storage.SQLCipher.Plugin.Register();
            InitializeDatabase();
        }
    }
}