using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Couchbase.Lite;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;

// The Blank Page item template is documented at http://go.microsoft.com/fwlink/?LinkId=402352&clcid=0x409

namespace UWPSpike
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainPage : Page
    {
        public MainPage()
        {
            this.InitializeComponent();

            var manager = Manager.SharedInstance;
            var db = manager.GetDatabase("db");
            var doc = db.CreateDocument();
            var rev = doc.PutProperties(new Dictionary<string, object> {
                ["jim"] = "borden"
            });

            var push = db.CreatePushReplication(new Uri("http://localhost:4984/db"));
            push.Start();

            var pull = db.CreatePullReplication(new Uri("http://localhost:4984/db"));
            pull.Continuous = true;
            pull.Start();
        }
    }
}
