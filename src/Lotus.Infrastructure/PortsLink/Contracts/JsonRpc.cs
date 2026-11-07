using System.Text.Json;
using System.Text.Json.Serialization;

namespace Lotus.Integration.Contracts;

/// <summary>JSON-RPC 2.0 message. A request has an id; a notification (event) has none; a response has result or error.</summary>
public sealed record JsonRpcMessage
{
    [JsonPropertyName("jsonrpc")] public string JsonRpc { get; init; } = "2.0";
    [JsonPropertyName("id")] public string? Id { get; init; }
    [JsonPropertyName("method")] public string? Method { get; init; }
    [JsonPropertyName("params")] public JsonElement? Params { get; init; }
    [JsonPropertyName("result")] public JsonElement? Result { get; init; }
    [JsonPropertyName("error")] public JsonRpcError? Error { get; init; }

    [JsonIgnore] public bool IsRequest => Method is not null && Id is not null;
    [JsonIgnore] public bool IsNotification => Method is not null && Id is null;
    [JsonIgnore] public bool IsResponse => Method is null && Id is not null;

    public static JsonRpcMessage Request<T>(string id, string method, T parameters) =>
        new() { Id = id, Method = method, Params = JsonSerializer.SerializeToElement(parameters, ContractJson.Options) };

    public static JsonRpcMessage Notification<T>(string method, T parameters) =>
        new() { Method = method, Params = JsonSerializer.SerializeToElement(parameters, ContractJson.Options) };

    public static JsonRpcMessage Success<T>(string id, T result) =>
        new() { Id = id, Result = JsonSerializer.SerializeToElement(result, ContractJson.Options) };

    public static JsonRpcMessage Failure(string id, int code, string message) =>
        new() { Id = id, Error = new JsonRpcError(code, message) };

    public T? ParamsAs<T>() => Params is { } p ? p.Deserialize<T>(ContractJson.Options) : default;
    public T? ResultAs<T>() => Result is { } r ? r.Deserialize<T>(ContractJson.Options) : default;
}

public sealed record JsonRpcError([property: JsonPropertyName("code")] int Code, [property: JsonPropertyName("message")] string Message);

/// <summary>Standard JSON-RPC codes plus the integration's own (-32000 range).</summary>
public static class JsonRpcErrorCodes
{
    public const int ParseError = -32700;
    public const int InvalidRequest = -32600;
    public const int MethodNotFound = -32601;
    public const int InvalidParams = -32602;
    public const int InternalError = -32603;
    public const int IncompatibleVersion = -32001;
    public const int PermissionDisabled = -32002;
    public const int UnknownGame = -32003;
}

public static class ContractJson
{
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };
}
