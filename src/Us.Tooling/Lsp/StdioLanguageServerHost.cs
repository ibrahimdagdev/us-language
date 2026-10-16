using System.Text;
using System.Text.Json;

namespace Us.Tooling.Lsp;

/// <summary>
/// Minimal LSP Content-Length framed JSON-RPC host over stdio for VS Code.
/// Semantic work reuses <see cref="UsLanguageServer"/> (existing parser/binder).
/// </summary>
public static class StdioLanguageServerHost
{
    public static int Run(TextReader input, TextWriter output, TextWriter? error = null)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(output);
        var server = new UsLanguageServer();
        try
        {
            while (true)
            {
                var payload = ReadMessage(input);
                if (payload is null)
                {
                    return 0;
                }

                using var document = JsonDocument.Parse(payload);
                var root = document.RootElement;
                var method = root.TryGetProperty("method", out var methodElement) ? methodElement.GetString() : null;
                var id = root.TryGetProperty("id", out var idElement) ? idElement.Clone() : (JsonElement?)null;

                if (method == "initialize" && id is not null)
                {
                    WriteResponse(output, id.Value, """
                        {"capabilities":{"textDocumentSync":1,"hoverProvider":true,"definitionProvider":true,"referencesProvider":true,"renameProvider":true,"completionProvider":{"triggerCharacters":["."]},"signatureHelpProvider":{"triggerCharacters":["(",","]},"documentSymbolProvider":true,"documentFormattingProvider":true}}
                        """);
                    continue;
                }

                if (method == "initialized")
                {
                    continue;
                }

                if (method == "shutdown" && id is not null)
                {
                    WriteResponse(output, id.Value, "null");
                    continue;
                }

                if (method == "exit")
                {
                    return 0;
                }

                if (method == "textDocument/didOpen")
                {
                    var doc = root.GetProperty("params").GetProperty("textDocument");
                    var uri = doc.GetProperty("uri").GetString() ?? "buffer.us";
                    var text = doc.GetProperty("text").GetString() ?? string.Empty;
                    var analysis = server.DidOpen(uri, text);
                    PublishDiagnostics(output, uri, analysis);
                    continue;
                }

                if (method == "textDocument/didChange")
                {
                    var pars = root.GetProperty("params");
                    var uri = pars.GetProperty("textDocument").GetProperty("uri").GetString() ?? "buffer.us";
                    var text = pars.GetProperty("contentChanges")[0].GetProperty("text").GetString() ?? string.Empty;
                    var analysis = server.DidChange(uri, text);
                    PublishDiagnostics(output, uri, analysis);
                    continue;
                }

                if (method == "textDocument/completion" && id is not null)
                {
                    var (uri, position) = ReadTextDocumentPosition(root);
                    var items = server.Completion(uri, position);
                    var json = JsonSerializer.Serialize(items.Select(item => new
                    {
                        label = item.Label,
                        detail = item.Detail,
                        kind = item.Kind switch
                        {
                            CompletionKind.Keyword => 14,
                            CompletionKind.Function => 3,
                            CompletionKind.Variable => 6,
                            CompletionKind.Parameter => 6,
                            CompletionKind.Type => 22,
                            CompletionKind.Field => 5,
                            _ => 1,
                        },
                    }));
                    WriteResponse(output, id.Value, json);
                    continue;
                }

                if (method == "textDocument/hover" && id is not null)
                {
                    var (uri, position) = ReadTextDocumentPosition(root);
                    var hover = server.Hover(uri, position);
                    if (hover is null)
                    {
                        WriteResponse(output, id.Value, "null");
                    }
                    else
                    {
                        var value = hover.Signature ?? $"{hover.Kind} {hover.Name}: {hover.TypeName}";
                        WriteResponse(output, id.Value, JsonSerializer.Serialize(new { contents = new { kind = "plaintext", value } }));
                    }

                    continue;
                }

                if (method == "textDocument/definition" && id is not null)
                {
                    var (uri, position) = ReadTextDocumentPosition(root);
                    var range = server.Definition(uri, position);
                    if (range is null)
                    {
                        WriteResponse(output, id.Value, "null");
                    }
                    else
                    {
                        WriteResponse(output, id.Value, JsonSerializer.Serialize(new
                        {
                            uri,
                            range = new
                            {
                                start = new { line = range.Value.Start.Line, character = range.Value.Start.Character },
                                end = new { line = range.Value.End.Line, character = range.Value.End.Character },
                            },
                        }));
                    }

                    continue;
                }

                if (method == "textDocument/formatting" && id is not null)
                {
                    var uri = root.GetProperty("params").GetProperty("textDocument").GetProperty("uri").GetString() ?? "buffer.us";
                    var documentAnalysis = server.GetDocument(uri);
                    if (documentAnalysis is null)
                    {
                        WriteResponse(output, id.Value, "[]");
                    }
                    else
                    {
                        var formatted = Formatter.Format(documentAnalysis.File.Text.ToString(), uri);
                        var end = documentAnalysis.Positions.ToPosition(documentAnalysis.File.Text.Length);
                        WriteResponse(output, id.Value, JsonSerializer.Serialize(new[]
                        {
                            new
                            {
                                range = new
                                {
                                    start = new { line = 0, character = 0 },
                                    end = new { line = end.Line, character = end.Character },
                                },
                                newText = formatted,
                            },
                        }));
                    }

                    continue;
                }

                if (id is not null)
                {
                    WriteResponse(output, id.Value, "null");
                }
            }
        }
        catch (Exception exception)
        {
            error?.WriteLine($"US LSP host error: {exception.Message}");
            return 1;
        }
    }

    private static (string Uri, LspPosition Position) ReadTextDocumentPosition(JsonElement root)
    {
        var pars = root.GetProperty("params");
        var uri = pars.GetProperty("textDocument").GetProperty("uri").GetString() ?? "buffer.us";
        var pos = pars.GetProperty("position");
        return (uri, new LspPosition(pos.GetProperty("line").GetInt32(), pos.GetProperty("character").GetInt32()));
    }

    private static void PublishDiagnostics(TextWriter output, string uri, DocumentAnalysis analysis)
    {
        var diagnostics = analysis.GetDiagnostics().Select(item => new
        {
            range = new
            {
                start = new { line = item.Range.Start.Line, character = item.Range.Start.Character },
                end = new { line = item.Range.End.Line, character = item.Range.End.Character },
            },
            severity = item.Severity == Us.Core.DiagnosticSeverity.Error ? 1 : 2,
            code = item.Code,
            source = "us",
            message = item.Message,
        });
        var notification = JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            method = "textDocument/publishDiagnostics",
            @params = new { uri, diagnostics },
        });
        WriteRaw(output, notification);
    }

    private static string? ReadMessage(TextReader input)
    {
        var contentLength = -1;
        while (true)
        {
            var line = input.ReadLine();
            if (line is null)
            {
                return null;
            }

            if (line.Length == 0)
            {
                break;
            }

            if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
            {
                contentLength = int.Parse(line["Content-Length:".Length..].Trim());
            }
        }

        if (contentLength < 0)
        {
            return null;
        }

        var buffer = new char[contentLength];
        var read = 0;
        while (read < contentLength)
        {
            var n = input.Read(buffer, read, contentLength - read);
            if (n <= 0)
            {
                break;
            }

            read += n;
        }

        return new string(buffer, 0, read);
    }

    private static void WriteResponse(TextWriter output, JsonElement id, string resultJson)
    {
        var idJson = id.ValueKind == JsonValueKind.String
            ? JsonSerializer.Serialize(id.GetString())
            : id.GetRawText();
        WriteRaw(output, $"{{\"jsonrpc\":\"2.0\",\"id\":{idJson},\"result\":{resultJson}}}");
    }

    private static void WriteRaw(TextWriter output, string body)
    {
        var bytes = Encoding.UTF8.GetByteCount(body);
        output.Write($"Content-Length: {bytes}\r\n\r\n");
        output.Write(body);
        output.Flush();
    }
}
