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
    [Application(AllowBackup = true, Label = "SimpleAndroidSync", Theme = "@style/Theme.AppCompat.Light")]
    public sealed class Application : global::Android.App.Application
    {
        public const string TAG = "GrocerySync";

        private const string DatabaseName = "grocery-sync";
        private const string UserLocalDocId = "user";
        private const string ServerDbUrl = "http://192.168.3.3:4984/openid_db";

        private readonly OkHttpClient _httpClient = new OkHttpClient();

        private Replication _push;
        private Replication _pull;
        private Exception _syncError;
        private bool _shouldStartPushAfterPull = false;
        private int _pullIdleCount = 0;

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

        public void ShowErrorMessage(string message)
        {
            RunOnUiThread(() =>
            {
                Log.Error(TAG, message);
                Toast.MakeText(Application.Context, message, ToastLength.Long).Show();
            });
        }

        public void LoginWithAuthCode(Activity activity)
        {
            StopReplication();
            StartPull(r =>
            {
                _shouldStartPushAfterPull = true;
                var callback = OpenIDAuthenticator.GetOIDCCallback(Application.Context);
                r.Authenticator = AuthenticatorFactory.CreateOpenIDAuthenticator(Manager.SharedInstance, callback);
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
                        StartApplication();
                    }
                }
            }, (c, e) =>
            {
                e.PrintStackTrace();
            });
        }

        private void StartPull(Action<Replication> setupCallback)
        {
            _pullIdleCount = 0;
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

        private void StopReplication()
        {
            _pull?.Stop();
            _push?.Stop();
        }

        private void OnChanged(object sender, ReplicationChangeEventArgs args)
        {
            Log.Verbose(TAG, $"Replication change status {args.Status} [{args.Source}]");
            var error = _pull?.LastError;
            if(_shouldStartPushAfterPull && IsStartedOrError(_pull)) {
                if(error == null) {
                    var username = _pull.Username;
                    if(Login(username)) {
                        StartPush(r =>
                        {
                            var callback = OpenIDAuthenticator.GetOIDCCallback(Application.Context);
                            r.Authenticator = AuthenticatorFactory.CreateOpenIDAuthenticator(Manager.SharedInstance, callback);
                        });
                        StartApplication();
                    }
                }

                _shouldStartPushAfterPull = false;
            }

            if(_push != null) {
                if(error == null) {
                    error = _push?.LastError;
                }
            }

            if(error != _syncError) {
                _syncError = error;
                ShowErrorMessage(_syncError.ToString());
            }
        }

        private bool IsStartedOrError(Replication repl)
        {
            if(repl == null) {
                return false;
            }

            var isIdle = false;
            if(repl == _pull) {
                isIdle = repl.Status == ReplicationStatus.Idle;
                isIdle = isIdle && (++_pullIdleCount > 1);
            } else {
                isIdle = repl.Status == ReplicationStatus.Idle;
            }

            return isIdle || repl.ChangesCount > 0 || repl.LastError != null;
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
            if(user != null && username.Equals(user["username"])) {
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

        private void StartApplication()
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