// Licensed under the Apache License, Version 2.0 (the "License").
// You may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//     https://github.com/FastTunnel/FastTunnel/edit/v2/LICENSE
// Copyright (c) 2019 Gui.H

using System;
using System.Buffers;
using System.IO.Pipelines;
using System.Text;
using System.Threading.Tasks;
using FastTunnel.Core.Forwarder.Kestrel.Features;
using FastTunnel.Core.Models;
using FastTunnel.Core.Protocol;
using FastTunnel.Core.Server;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Http;

namespace FastTunnel.Core.Forwarder;

public class FastTunelProtocol
{
    HttpParser<HttpHandler> httpParser;
    HttpHandler HttpHandler;

    public FastTunelProtocol(ConnectionContext context, FastTunnelServer fastTunnelServer)
    {
        this.context = context;
        this.fastTunnelServer = fastTunnelServer;
        httpParser = new HttpParser<HttpHandler>(true);
        HttpHandler = new HttpHandler();
    }

    bool IsFastTunnel => Method == ProtocolConst.HTTP_METHOD_SWAP || MatchWeb != null;

    public WebInfo MatchWeb { get; private set; }

    ConnectionContext context { get; }

    IDuplexPipe Transport => context.Transport;

    const char CharR = '\r';

    private const byte ByteCR = (byte)'\r';
    private const byte ByteLF = (byte)'\n';
    private const byte ByteColon = (byte)':';
    private const byte ByteSpace = (byte)' ';

    private bool Parse(ReadOnlySequence<byte> result, out SequencePosition consumed)
    {
        var reader = new SequenceReader<byte>(result);
        var success = httpParser.ParseHeaders(HttpHandler, ref reader);
        consumed = reader.Position;
        return success;
    }

    internal async Task TryAnalysisPipeV2Async()
    {
        var _input = Transport.Input;

        ReadResult result;
        ReadOnlySequence<byte> readableBuffer;

        result = await _input.ReadAsync();
        readableBuffer = result.Buffer;
        SequencePosition start = readableBuffer.Start;
        var position = readableBuffer.PositionOf(ByteLF);
        var FirstLine = UTF8Encoding.UTF8.GetString(readableBuffer.Slice(0, position.Value));
        Method = FirstLine.Substring(0, FirstLine.IndexOf(" ")).ToUpper();

        switch (Method)
        {
            case ProtocolConst.HTTP_METHOD_SWAP:
                // 客户端发起消息互转
                var endIndex = FirstLine.IndexOf(" ", 7);
                MessageId = FirstLine.Substring(7, endIndex - 7);
                break;
            default:
                // 常规Http请求，需要检查Host决定是否进行代理
                break;
        }

        var readedPosition = readableBuffer.GetPosition(1, position.Value);

        if (!Parse(result.Buffer.Slice(readedPosition), out SequencePosition consumed))
        {
            throw new Exception("解析header失败");
        };

        if (HttpHandler.Headers.TryGetValue("Host", out string host))
        {
            // 匹配Host，
            if (fastTunnelServer.TryGetWebProxyByHost(host.Split(":")[0], out var web))
            {
                MatchWeb = web;
            }
        }

        if (IsFastTunnel)
        {
            context.Features.Set<IFastTunnelFeature>(new FastTunnelFeature()
            {
                MatchWeb = MatchWeb,
                Method = Method,
                Host = Host,
                MessageId = MessageId,
            });

            if (Method == ProtocolConst.HTTP_METHOD_SWAP)
            {
                _input.AdvanceTo(consumed, consumed);
                return;
            }
        }

        _input.AdvanceTo(start, start);
    }

    internal async Task TryAnalysisPipeAsync()
    {
        var _input = Transport.Input;

        ReadResult result;
        ReadOnlySequence<byte> readableBuffer;

        while (true)
        {
            result = await _input.ReadAsync(context.ConnectionClosed);
            readableBuffer = result.Buffer;
            SequencePosition start = readableBuffer.Start;

            SequencePosition? position = null;

            do
            {
                position = readableBuffer.PositionOf(ByteLF);

                if (position != null)
                {
                    var readedPosition = readableBuffer.GetPosition(1, position.Value);
                    if (ProcessHeaderLine(readableBuffer.Slice(0, position.Value), out var _))
                    {
                        if (Method == ProtocolConst.HTTP_METHOD_SWAP)
                        {
                            _input.AdvanceTo(readedPosition, readedPosition);
                        }
                        else
                        {
                            _input.AdvanceTo(start, start);
                        }

                        if (IsFastTunnel)
                        {
                            context.Features.Set<IFastTunnelFeature>(new FastTunnelFeature()
                            {
                                MatchWeb = MatchWeb,
                                Method = Method,
                                Host = Host,
                                MessageId = MessageId,
                            });
                        }
                        return;
                    }

                    readableBuffer = readableBuffer.Slice(readedPosition);
                }
            }
            while (position != null && !readableBuffer.IsEmpty);

            if (result.IsCompleted)
            {
                _input.AdvanceTo(readableBuffer.End, readableBuffer.End);
                return;
            }
        }
    }

    public string Method;
    public string Host = null;
    public string MessageId;
    private bool isFirstLine = true;

    public FastTunnelServer fastTunnelServer { get; }

    /// <summary>
    ///
    /// GET / HTTP/1.1
    /// Host: test.test.cc:1270
    /// Connection: keep-alive
    /// Upgrade-Insecure-Requests: 1
    /// User-Agent: Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/100.0.4896.127 Safari/537.36
    /// Accept: text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,image/apng,*/*;q=0.8,application/signed-exchange;v=b3;q=0.9
    /// Accept-Encoding: gzip, deflate
    /// Accept-Language: zh-CN,zh;q=0.9,en;q=0.8
    /// 
    /// </summary>
    /// <param name="headerLine"></param>
    /// <returns>Header读取完毕？</returns>
    private bool ProcessHeaderLine(ReadOnlySequence<byte> headerLine, out string headerLineStr)
    {
        headerLineStr = Encoding.UTF8.GetString(headerLine);

        if (isFirstLine)
        {
            Method = headerLineStr.Substring(0, headerLineStr.IndexOf(" ")).ToUpper();

            switch (Method)
            {
                case ProtocolConst.HTTP_METHOD_SWAP:
                    // 客户端发起消息互转
                    var endIndex = headerLineStr.IndexOf(" ", 7);
                    MessageId = headerLineStr.Substring(7, endIndex - 7);
                    break;
                default:
                    // 常规Http请求，需要检查Host决定是否进行代理
                    break;
            }

            isFirstLine = false;
        }
        else
        {
            // TrailerHeader
            if (headerLineStr.Equals("\r"))
            {
                return true;
            }

            switch (Method)
            {
                case ProtocolConst.HTTP_METHOD_SWAP:
                    // do nothing
                    break;
                default:
                    // 检查Host决定是否进行代理
                    // Host: test.test.cc:1270
                    var lower = headerLineStr.Trim(CharR).ToLower();
                    if (lower.StartsWith("host:", StringComparison.OrdinalIgnoreCase))
                    {
                        Host = lower.Split(" ")[1];
                        if (Host.Contains(":"))
                        {
                            Host = Host.Split(":")[0];
                        }

                        // 匹配Host，
                        if (fastTunnelServer.TryGetWebProxyByHost(Host, out var web))
                        {
                            MatchWeb = web;
                        }
                    }

                    break;
            }
        }

        return false;
    }
}
