//
// WebSocketChangeTracker.cs
//
// Author:
// 	Jim Borden  <jim.borden@couchbase.com>
//
// Copyright (c) 2016 Couchbase, Inc All rights reserved.
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
// http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
//
#if WINDOWS_UWP
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Couchbase.Lite.Util;
using Couchbase.Lite.Auth;
using System.Collections.Generic;
using Microsoft.IO;
using System.Security.Authentication;
using System.Net;
using System.Net.WebSockets;
using System.Text;

namespace Couchbase.Lite.Internal
{
    internal enum ChangeTrackerMessageType : byte
    {
        Unknown,
        Plaintext,
        GZip,
        EOF
    }

    // Concrete class for receiving changes over web sockets
    internal class WebSocketChangeTracker : ChangeTracker
    {
        
#region Constants

        private static readonly string Tag = typeof(WebSocketChangeTracker).Name;

#endregion

#region Variables

        private ClientWebSocket _client;
        private CancellationTokenSource _cts;

#endregion

#region Properties

        public bool CanConnect { get; set; }

        public override Uri ChangesFeedUrl
        {
            get {
                var dbURLString = DatabaseUrl.ToString().Replace("http", "ws");
                if (!dbURLString.EndsWith("/", StringComparison.Ordinal)) {
                    dbURLString += "/";
                }

                dbURLString += "_changes?feed=websocket";
                return new Uri(dbURLString);
            }
        }

#endregion

#region Constructors

        public WebSocketChangeTracker(ChangeTrackerOptions options) : base(options)
        {
            _responseLogic = new WebSocketLogic();
            CanConnect = true;
        }

#endregion

#region Private Methods

        // Called when the web socket connection is closed
        private void OnClose(WebSocketCloseStatus status, string description)
        {
            if (_client != null) {
                if (status == WebSocketCloseStatus.ProtocolError) {
                    // This is not a valid web socket connection, need to fall back to regular HTTP
                    CanConnect = false;
                    Stopped();
                } else {
                    Log.To.ChangeTracker.I(Tag, "{0} remote  closed connection ({2} {3})",
                        this, status, description);
                    Backoff.DelayAppropriateAmountOfTime().ContinueWith(t => _client?.ConnectAsync(ChangesFeedUrl, _cts.Token)
                    .ContinueWith(t1 => OnConnect()));
                }
            } else {
                Log.To.ChangeTracker.I(Tag, "{0} is closed", this);
                Stopped();
            }
        }

        // Called when the web socket establishes a connection
        private void OnConnect()
        {
            if (_cts.IsCancellationRequested) {
                Log.To.ChangeTracker.I(Tag, "{0} Cancellation requested, aborting in OnConnect", this);
                return;
            }

            Misc.SafeDispose(ref _responseLogic);
            _responseLogic = new WebSocketLogic();
            _responseLogic.OnCaughtUp = () => Client?.ChangeTrackerCaughtUp(this);
            _responseLogic.OnChangeFound = (change) =>
            {
                if (!ReceivedChange(change)) {
                    Log.To.ChangeTracker.W(Tag,  String.Format("change is not parseable"));
                }
            };

            Backoff.ResetBackoff();
            Log.To.ChangeTracker.V(Tag, "{0} websocket opened", this);

            // Now that the WebSocket is open, send the changes-feed options (the ones that would have
            // gone in the POST body if this were HTTP-based.)
            var bytes = GetChangesFeedPostBody().ToArray();
            _client?.SendAsync(new System.ArraySegment<byte>(bytes), WebSocketMessageType.Binary, true, CancellationToken.None);
        }

        // Called when a message is received
        private void OnReceive(WebSocketReceiveResult result, System.ArraySegment<byte> buffer)
        {
            if (_cts.IsCancellationRequested) {
                Log.To.ChangeTracker.I(Tag, "{0} Cancellation requested, aborting in OnReceive", this);
                return;
            }

            if(result.MessageType == WebSocketMessageType.Close) {
                OnClose(result.CloseStatus.Value, result.CloseStatusDescription);
                return;
            }

            try {
                if(buffer.Count == 0) {
                    return;
                }

                var code = ChangeTrackerMessageType.Unknown;
                if(result.MessageType == WebSocketMessageType.Text) {
                    if(buffer.Count == 2 && buffer.ElementAt(0) == '[' && buffer.ElementAt(1) == ']') {
                        code = ChangeTrackerMessageType.EOF;
                    } else {
                        code = ChangeTrackerMessageType.Plaintext;
                    }
                } else {
                    code = ChangeTrackerMessageType.GZip;
                }

                var responseStream = RecyclableMemoryStreamManager.SharedInstance.GetStream("WebSocketChangeTracker", buffer.Count + 1);
                try {
                    responseStream.WriteByte((byte)code);
                    responseStream.Write(buffer.ToArray(), 0, buffer.Count);
                    responseStream.Seek(0, SeekOrigin.Begin);
                    _responseLogic.ProcessResponseStream(responseStream, _cts.Token);
                } finally {
                    responseStream.Dispose();
                }
            } catch(Exception e) {
                Log.To.ChangeTracker.E(Tag, String.Format("{0} is not parseable", GetLogString(result, buffer)), e);
            } finally {
                _client.ReceiveAsync(buffer, _cts.Token).ContinueWith(t => OnReceive(t.Result, buffer));
            }
        }

        private string GetLogString(WebSocketReceiveResult args, System.ArraySegment<byte> buffer)
        {
            if(args.MessageType == WebSocketMessageType.Binary) {
                return "<gzip stream>";
            } else if(args.MessageType == WebSocketMessageType.Text) {
                return Encoding.UTF8.GetString(buffer.ToArray());
            }

            return null;
        }

#endregion

#region Overrides
            
        public override bool Start()
        {
            if (IsRunning) {
                return false;
            }

            IsRunning = true;
            Log.To.ChangeTracker.I(Tag, "Starting {0}...", this);
            _cts = new CancellationTokenSource();

            var authHeader = (_remoteSession.Authenticator as ICustomHeadersAuthorizer)?.AuthorizationHeaderValue;

            // A WebSocket has to be opened with a GET request, not a POST (as defined in the RFC.)
            // Instead of putting the options in the POST body as with HTTP, we will send them in an
            // initial WebSocket message
            _usePost = false;
            _caughtUp = false;
            _client = new ClientWebSocket();
            _client.Options.Cookies.Add(ChangesFeedUrl, Client.GetCookieStore().GetCookies(ChangesFeedUrl));
            if (authHeader != null) {
                _client.Options.SetRequestHeader("Authorization", authHeader.ToString());
            }

            var buffer = new System.ArraySegment<byte>();
            _client.ConnectAsync(ChangesFeedUrl, _cts.Token).ContinueWith(t => OnConnect());
            _client.ReceiveAsync(buffer, _cts.Token).ContinueWith(t => OnReceive(t.Result, buffer));
            return true;
        }

        public override void Stop()
        {
            if (!IsRunning) {
                return;
            }

            IsRunning = false;
            Misc.SafeNull(ref _client, c =>
            {
                Log.To.ChangeTracker.I(Tag, "{0} requested to stop", this);
                c.CloseAsync(WebSocketCloseStatus.NormalClosure, "Client requested close", _cts.Token);
            });
        }

        protected override void Stopped()
        {
            Client?.ChangeTrackerStopped(this);
            Misc.SafeDispose(ref _responseLogic);
        }

#endregion
    }
}

#endif