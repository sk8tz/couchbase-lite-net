using System;

using Android.Content;
using Android.OS;
using Android.Views;
using Android.Widget;
using Android.Gms.Auth.Api;
using Android.Gms.Auth.Api.SignIn;
using Android.Gms.Common;
using Android.Gms.Common.Apis;
using Android.App;
using Android.Support.V7.App;
using Android.Runtime;
using Android.Webkit;

namespace SimpleAndroidSync
{
    [Activity(Label = "SimpleAndroidSync", MainLauncher =true)]
    public class LoginActivity : AppCompatActivity
    {
        public const string IntentActionLogout = "logout";
        private const string UseGoogleSignInKey = "UseGoogleSignIn";
        private const int RcGoogleSignIn = 9001;

        private bool _shouldContinueGSOLogout;
        private GoogleApiClient _googleApiClient;

        private void GSOButtonClicked(object sender, EventArgs e)
        {
            var intent = Auth.GoogleSignInApi.GetSignInIntent(_googleApiClient);
            StartActivityForResult(intent, RcGoogleSignIn);
        }

        private void OpenIDButtonClicked(object sender, EventArgs e)
        {
            var app = (CouchbaseSample.Android.Application)Application;
            app.LoginWithAuthCode();
        }


        private void OnFailed(ConnectionResult result)
        {
            var errorMessage = $"Google Sign-in connection failed : ({result.ErrorCode}) {result.ErrorMessage}";
            var app = (CouchbaseSample.Android.Application)Application;
            app.ShowMessage(errorMessage);
        }

        private void HandleGSOResult(GoogleSignInResult result)
        {
            var success = false;
            var errorMessage = default(string);

            if(result.IsSuccess) {
                var acct = result.SignInAccount;
                var idToken = acct.IdToken;
                if(idToken != null) {
                    var app = (CouchbaseSample.Android.Application)Application;
                    app.LoginWithGoogleSignin(this, idToken);
                    success = true;
                } else {
                    errorMessage = "Google sign-in failed : No ID token returned";
                }

                SetLogInWithGSO(true);
            } else {
                errorMessage = $"Google sign-in failed : ({result.Status.StatusCode}) {result.Status.StatusMessage}";
            }

            if(!success) {
                var app = (CouchbaseSample.Android.Application)Application;
                app.ShowMessage(errorMessage);
            }
        }

        private void SetLogInWithGSO(bool login)
        {
            var sharedPref = GetPreferences(FileCreationMode.Private);
            var editor = sharedPref.Edit();
            editor.PutBoolean(UseGoogleSignInKey, login);
            editor.Commit();
        }

        private bool LoggedInWithGSO()
        {
            var sharedPref = GetPreferences(FileCreationMode.Private);
            return sharedPref.GetBoolean(UseGoogleSignInKey, false);
        }

        private void Logout()
        {
            if(LoggedInWithGSO()) {
                LogoutFromGSO();
            } else {
                ClearWebViewCookies();
                CompleteLogout();
            }
        }

        private void ClearWebViewCookies()
        {
            var cookieManager = CookieManager.Instance;
            if(Build.VERSION.SdkInt >= BuildVersionCodes.Lollipop) {
                cookieManager.RemoveAllCookies(null);
                cookieManager.Flush();
            } else {
                cookieManager.RemoveAllCookie();
                CookieSyncManager.Instance.Sync();
            }
        }

        private void CompleteLogout()
        {
            var application = (CouchbaseSample.Android.Application)Application;
            application.ShowMessage("Logout successfully");
        }

        private void LogoutFromGSO()
        {
            if(_googleApiClient.IsConnected) {
                Auth.GoogleSignInApi.SignOut(_googleApiClient).SetResultCallback(new ResultCallback<Statuses>(s =>
                {
                    var application = (CouchbaseSample.Android.Application)Application;
                    if(s.IsSuccess) {
                        ClearWebViewCookies();
                        SetLogInWithGSO(false);
                        CompleteLogout();
                    } else {
                        application.ShowMessage("Failed to sign out from Google SignIn");
                    }
                }));
            } else {
                _shouldContinueGSOLogout = true;
                if(!_googleApiClient.IsConnecting) {
                    _googleApiClient.Connect();
                }
            }
        }

        protected override void OnCreate(Bundle savedInstanceState)
        {
            base.OnCreate(savedInstanceState);

            SetContentView(Resource.Layout.LoginActivity);

            var authSignInCodeButton = FindViewById<Button>(Resource.Id.authCodeSignInButton);
            authSignInCodeButton.Click += OpenIDButtonClicked;

            var gso = new GoogleSignInOptions.Builder(GoogleSignInOptions.DefaultSignIn)
                .RequestScopes(new Scope(Scopes.PlusLogin))
                .RequestScopes(new Scope(Scopes.PlusMe))
                .Build();

            _googleApiClient = new GoogleApiClient.Builder(Application.Context)
                .EnableAutoManage(this, OnFailed)
                .AddConnectionCallbacks(b =>
                {
                    if(_shouldContinueGSOLogout) {
                        LogoutFromGSO();
                    }

                    _shouldContinueGSOLogout = false;
                }, null)
                .AddApi(Auth.GOOGLE_SIGN_IN_API, gso)
                .Build();

            var googleSignInButton = FindViewById<SignInButton>(Resource.Id.googleSignInButton);
            googleSignInButton.SetSize(SignInButton.SizeStandard);
            googleSignInButton.SetScopes(gso.GetScopeArray());
            googleSignInButton.Click += GSOButtonClicked;

            var action = Intent.Action;
            if(action == IntentActionLogout) {
                Logout();
            }
        }

        protected override void OnActivityResult(int requestCode, [GeneratedEnum] Result resultCode, Intent data)
        {
            base.OnActivityResult(requestCode, resultCode, data);

            if(requestCode == RcGoogleSignIn) {
                var result = Auth.GoogleSignInApi.GetSignInResultFromIntent(data);
                HandleGSOResult(result);
            }
        }
    }
}