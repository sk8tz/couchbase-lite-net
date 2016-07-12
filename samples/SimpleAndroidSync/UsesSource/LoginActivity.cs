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

namespace SimpleAndroidSync
{
    [Activity(Label = "SimpleAndroidSync", MainLauncher =true)]
    public class LoginActivity : AppCompatActivity
    {
        private const int RcGoogleSignIn = 9001;

        private GoogleApiClient _googleApiClient;

        private void GSOButtonClicked(object sender, EventArgs e)
        {
            var intent = Auth.GoogleSignInApi.GetSignInIntent(_googleApiClient);
            StartActivityForResult(intent, RcGoogleSignIn);
        }

        private void OpenIDButtonClicked(object sender, EventArgs e)
        {
            var app = (CouchbaseSample.Android.Application)Application;
            app.LoginWithAuthCode(this);
        }


        private void OnFailed(ConnectionResult result)
        {
            var errorMessage = $"Google Sign-in connection failed : ({result.ErrorCode}) {result.ErrorMessage}";
            var app = (CouchbaseSample.Android.Application)Application;
            app.ShowErrorMessage(errorMessage);
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
            } else {
                errorMessage = $"Google sign-in failed : ({result.Status.StatusCode}) {result.Status.StatusMessage}";
            }

            if(!success) {
                var app = (CouchbaseSample.Android.Application)Application;
                app.ShowErrorMessage(errorMessage);
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
                .AddApi(Auth.GOOGLE_SIGN_IN_API, gso)
                .Build();

            var googleSignInButton = FindViewById<SignInButton>(Resource.Id.googleSignInButton);
            googleSignInButton.SetSize(SignInButton.SizeStandard);
            googleSignInButton.SetScopes(gso.GetScopeArray());
            googleSignInButton.Click += GSOButtonClicked;
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