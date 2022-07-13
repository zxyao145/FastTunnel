// Licensed under the Apache License, Version 2.0 (the "License").
// You may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//     https://github.com/FastTunnel/FastTunnel/edit/v2/LICENSE
// Copyright (c) 2019 Gui.H

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Http;

namespace FastTunnel.Core.Forwarder;

public class HttpHandler : IHttpHeadersHandler, IHttpRequestLineHandler
{
    public Dictionary<string, string> Headers = new Dictionary<string, string>();
    public bool HeadersComplete = false;

    public void OnHeader(ReadOnlySpan<byte> name, ReadOnlySpan<byte> value)
    {
        var key = UTF8Encoding.UTF8.GetString(name);
        var val = UTF8Encoding.UTF8.GetString(value);
        Headers.Add(key, val);
    }

    public void OnHeadersComplete(bool endStream)
    {
        HeadersComplete = true;
    }

    public void OnStartLine(HttpVersionAndMethod versionAndMethod, TargetOffsetPathLength targetPath, Span<byte> startLine)
    {
        throw new NotImplementedException();
    }

    public void OnStaticIndexedHeader(int index)
    {
        throw new NotImplementedException();
    }

    public void OnStaticIndexedHeader(int index, ReadOnlySpan<byte> value)
    {
        throw new NotImplementedException();
    }
}
