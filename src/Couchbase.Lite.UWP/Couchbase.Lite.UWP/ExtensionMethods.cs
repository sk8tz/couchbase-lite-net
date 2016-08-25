//
//  ExtensionMethods.cs
//
//  Author:
//  	Jim Borden  <jim.borden@couchbase.com>
//
//  Copyright (c) 2016 Couchbase, Inc All rights reserved.
//
//  Licensed under the Apache License, Version 2.0 (the "License");
//  you may not use this file except in compliance with the License.
//  You may obtain a copy of the License at
//
//  http://www.apache.org/licenses/LICENSE-2.0
//
//  Unless required by applicable law or agreed to in writing, software
//  distributed under the License is distributed on an "AS IS" BASIS,
//  WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
//  See the License for the specific language governing permissions and
//  limitations under the License.
//
//
// Mono.Security.Uri
//	Adapted from System.Uri (in System.dll assembly) for its use in corlib
//
// Authors:
//    Lawrence Pit (loz@cable.a2000.nl)
//    Garrett Rooney (rooneg@electricjellyfish.net)
//    Ian MacLean (ianm@activestate.com)
//    Ben Maurer (bmaurer@users.sourceforge.net)
//    Atsushi Enomoto (atsushi@ximian.com)
//    Stephane Delcroix  <stephane@delcroix.org>
//
// (C) 2001 Garrett Rooney
// (C) 2003 Ian MacLean
// (C) 2003 Ben Maurer
// (C) 2003 Novell inc.
// Copyright (C) 2004 Novell, Inc (http://www.novell.com)
//
// Permission is hereby granted, free of charge, to any person obtaining
// a copy of this software and associated documentation files (the
// "Software"), to deal in the Software without restriction, including
// without limitation the rights to use, copy, modify, merge, publish,
// distribute, sublicense, and/or sell copies of the Software, and to
// permit persons to whom the Software is furnished to do so, subject to
// the following conditions:
// 
// The above copyright notice and this permission notice shall be
// included in all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
// EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
// MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
// NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE
// LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION
// OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION
// WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
//

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Couchbase.Lite
{
    internal enum UriPartial
    {
        Scheme = 0,
        Authority = 1,
        Path = 2,
    }

    internal static class UWPExtensionMethods
    {
        private static readonly Tuple<string, string, int>[] schemes = new Tuple<string, string, int>[] {
            Tuple.Create("http", "://", 80),
            Tuple.Create("https", "://", 443),
            Tuple.Create("ftp", "://", 21),
            Tuple.Create("file", "://", -1),
            Tuple.Create("mailto", ":", 25),
            Tuple.Create("news", ":", -1),
            Tuple.Create("nntp", "://", 119),
            Tuple.Create("gopher", "://", 70),
        };

        internal static string GetLeftPart(this Uri uri, UriPartial part)
        {
            int defaultPort;
            switch(part) {
                case UriPartial.Scheme:
                    return uri.Scheme + GetSchemeDelimiter(uri.Scheme);
                case UriPartial.Authority:
                    if(uri.Host == String.Empty ||
                        uri.Scheme == "mailto" ||
                        uri.Scheme == "news")
                        return String.Empty;

                    StringBuilder s = new StringBuilder();
                    s.Append(uri.Scheme);
                    s.Append(GetSchemeDelimiter(uri.Scheme));
                    if(uri.AbsolutePath.Length > 1 && uri.AbsolutePath[1] == ':' && ("file" == uri.Scheme))
                        s.Append('/');  // win32 file
                    if(uri.UserInfo.Length > 0)
                        s.Append(uri.UserInfo).Append('@');
                    s.Append(uri.Host);
                    defaultPort = GetDefaultPort(uri.Scheme);
                    if((uri.Port != -1) && (uri.Port != defaultPort))
                        s.Append(':').Append(uri.Port);
                    return s.ToString();
                case UriPartial.Path:
                    StringBuilder sb = new StringBuilder();
                    sb.Append(uri.Scheme);
                    sb.Append(GetSchemeDelimiter(uri.Scheme));
                    if(uri.AbsolutePath.Length > 1 && uri.AbsolutePath[1] == ':' && ("file" == uri.Scheme))
                        sb.Append('/');  // win32 file
                    if(uri.UserInfo.Length > 0)
                        sb.Append(uri.UserInfo).Append('@');
                    sb.Append(uri.Host);
                    defaultPort = GetDefaultPort(uri.Scheme);
                    if((uri.Port != -1) && (uri.Port != defaultPort))
                        sb.Append(':').Append(uri.Port);
                    sb.Append(uri.Port);
                    return sb.ToString();
            }
            return null;
        }

        internal static string GetSchemeDelimiter(string scheme)
        {
            for(int i = 0; i < schemes.Length; i++)
                if(schemes[i].Item1 == scheme)
                    return schemes[i].Item2;
            return "://";
        }

        internal static int GetDefaultPort(string scheme)
        {
            for(int i = 0; i < schemes.Length; i++)
                if(schemes[i].Item1 == scheme)
                    return schemes[i].Item3;
            return -1;
        }
    }
}
