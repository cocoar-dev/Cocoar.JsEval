using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Cocoar.Reflectensions;
using Cocoar.Reflectensions.ExtensionMethods;
using Cocoar.JsEval;
using DynamicExpresso;
using Microsoft.Extensions.Primitives;
using Nito.AsyncEx.Synchronous;

namespace Cocoar.JsEval.Module.Http;

public class HttpRequestBuilder : IHttpRequestBuilder
{
    private readonly HttpHandlerOptions _httpHandlerOptions;
    private readonly IScriptEngine _scriptEngine;

    private readonly HttpRequestData _requestData = new();

    private List<HttpRetryPolicy> Retries { get; } = [];

    public HttpRequestBuilder(HttpHandlerOptions httpHandlerOptions, IScriptEngine scriptEngine)
    {
        _httpHandlerOptions = httpHandlerOptions;
        _scriptEngine = scriptEngine;
    }

    public HttpRequestBuilder SetPath(params string[] url)
    {
        _requestData.PathSegments = url.Where(u => !string.IsNullOrEmpty(u)).SelectMany(u => u.Split('/')).ToList();
        return this;
    }

    public HttpRequestBuilder AddPath(params string[] url)
    {
        _requestData.PathSegments.AddRange(url.Where(u => !string.IsNullOrEmpty(u)).SelectMany(u => u.Split('/')).ToList());
        return this;
    }

    public HttpRequestBuilder AddHeader(string key, params string[] value)
    {
        if (string.IsNullOrWhiteSpace(key))
            return this;

        key = key.ToLower();

        _requestData.Headers.AddOrUpdate(key,
            _ => value.ToList(),
            (s, list) =>
            {
                list.AddRange(value);
                return list.Distinct().ToList();
            });

        return this;
    }

    public HttpRequestBuilder SetHeaders(IDictionary<string, object?> headers)
    {
        _requestData.Headers.Clear();
        foreach (var (key, value) in headers)
        {
            if (value is not null)
            {
                if (key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase))
                {
                    _requestData.ContentType = value.ToString();
                    continue;
                }

                if (key.Equals("Host", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var list = new List<string>();
                if (value.GetType().IsEnumerableType())
                {
                    foreach (var o in (IEnumerable)value)
                    {
                        if (o is not null)
                        {
                            list.Add(o.ToString()!);
                        }
                    }
                }
                else
                {
                    list.Add(value.ToString()!);
                }

                _requestData.Headers.TryAdd(key, list);
            }
        }

        return this;
    }

    public HttpRequestBuilder SetHeader(string key, params string[] value)
    {
        if (string.IsNullOrWhiteSpace(key))
            return this;

        key = key.ToLower();
        _requestData.Headers.TryRemove(key, out _);
        _requestData.Headers.TryAdd(key, value.ToList());

        return this;
    }

    public HttpRequestBuilder SetContentType(string contentType)
    {
        _requestData.ContentType = contentType;
        return this;
    }

    public HttpRequestBuilder AddQueryParam(string key, params string[] value)
    {
        if (string.IsNullOrWhiteSpace(key))
            return this;

        key = key.ToLower();

        _requestData.QueryParameters.AddOrUpdate(key, s =>
        {
            return new StringValues(value);
        }, (s, sv) =>
        {
            var temp = sv.ToList();
            temp.AddRange(value);
            return new StringValues([.. temp]);
        });

        return this;
    }

    public HttpRequestBuilder SetQueryParams(IDictionary<string, object?> headers)
    {
        _requestData.QueryParameters.Clear();
        foreach (var (key, value) in headers)
        {
            if (value is not null)
            {
                var list = new List<string>();
                if (value.GetType().IsEnumerableType())
                {
                    foreach (var o in (IEnumerable)value)
                    {
                        if (o is not null)
                        {
                            list.Add(o.ToString()!);
                        }
                    }
                }
                else
                {
                    list.Add(value.ToString()!);
                }

                _requestData.QueryParameters.TryAdd(key, new StringValues([.. list]));
            }
        }

        return this;
    }

    public HttpRequestBuilder SetQueryParam(string key, params string[] value)
    {
        if (string.IsNullOrWhiteSpace(key))
            return this;

        key = key.ToLower();

        _requestData.QueryParameters.TryRemove(key, out _);
        _requestData.QueryParameters.TryAdd(key, new StringValues(value));

        return this;
    }

    public HttpRequestBuilder SetBearerToken(string token)
    {
        return SetHeader("authorization", $"Bearer {token}");
    }

    public HttpRequestBuilder SetBasicAuthentication(string username, string password)
    {
        var cred = $"{username}:{password}".EncodeToBase64();
        return SetHeader("authorization", $"Basic {cred}");
    }

    public HttpRequestBuilder AddRetry(string httpStatus, params int[] delay)
    {
        var pol = new HttpRetryPolicy
        {
            Expression = httpStatus
        };

        foreach (var i in delay)
        {
            pol.Delay.Enqueue(i);
        }

        Retries.Add(pol);
        return this;
    }

    public async Task<HttpResponse> SendRequestMessageAsync(HttpRequestMessage httpRequestMessage)
    {
        var cl = HttpHandlerFactory.GetClient(_httpHandlerOptions);

        var tryAgain = true;
        HttpResponseMessage? responseMessage = null;

        while (tryAgain)
        {
            tryAgain = false;
            responseMessage = await cl.SendAsync(httpRequestMessage, this._httpHandlerOptions.HttpCompletionOption);
            foreach (var httpRetryPolicy in Retries.Where(p => p.Delay.Count > 0))
            {
                var expr = RetryHelper.ParseInput(httpRetryPolicy.Expression, $"{(int)responseMessage.StatusCode}");
                var matches = RetryHelper.EvalExpression(expr);
                if (matches)
                {
                    tryAgain = true;
                    await Task.Delay(httpRetryPolicy.Delay.Dequeue());
                }
            }
        }

        return new HttpResponse(responseMessage!, _scriptEngine);
    }

    public Task<HttpResponse> SendAsync(string httpMethod, object? content = null)
    {
        return SendAsync(new HttpMethod(httpMethod), content);
    }

    public async Task<HttpResponse> SendAsync(HttpMethod httpMethod, object? content = null)
    {
        var httpRequestMessage = _requestData.BuildHttpRequestMessage(_httpHandlerOptions, httpMethod, content);

        return await SendRequestMessageAsync(httpRequestMessage);
    }

    public Task<HttpResponse> GetAsync()
    {
        return SendAsync(HttpMethod.Get);
    }

    public Task<HttpResponse> PostAsync(object content)
    {
        return SendAsync(HttpMethod.Post, content);
    }

    public Task<HttpResponse> PutAsync(object content)
    {
        return SendAsync(HttpMethod.Put, content);
    }

    public Task<HttpResponse> PatchAsync(object content)
    {
        return SendAsync("Patch", content);
    }

    public Task<HttpResponse> DeleteAsync(object? content)
    {
        return SendAsync(HttpMethod.Delete, content);
    }

    public Task<HttpResponse> DeleteAsync()
    {
        return DeleteAsync(null);
    }

    public HttpResponse Send(string httpMethod, object? content = null)
    {
        return SendAsync(httpMethod, content).WaitAndUnwrapException();
    }

    public HttpResponse Send(HttpMethod httpMethod, object? content = null)
    {
        return SendAsync(httpMethod, content).WaitAndUnwrapException();
    }

    public HttpResponse SendRequestMessage(HttpRequestMessage httpRequestMessage)
    {
        return SendRequestMessageAsync(httpRequestMessage).WaitAndUnwrapException();
    }

    public HttpResponse Get()
    {
        return GetAsync().WaitAndUnwrapException();
    }

    public HttpResponse Post(object content)
    {
        return PostAsync(content).WaitAndUnwrapException();
    }

    public HttpResponse Put(object content)
    {
        return PutAsync(content).WaitAndUnwrapException();
    }

    public HttpResponse Patch(object content)
    {
        return PatchAsync(content).WaitAndUnwrapException();
    }

    public HttpResponse Delete(object? content)
    {
        return DeleteAsync(content).WaitAndUnwrapException();
    }

    public HttpResponse Delete()
    {
        return Delete(null);
    }
}

public class HttpRetryPolicy
{
    public string Expression { get; set; } = "";

    public Queue<int> Delay { get; set; } = new();

    public HttpRetryPolicy Clone()
    {
        var pol = new HttpRetryPolicy
        {
            Expression = Expression,
            Delay = new Queue<int>(Delay)
        };
        return pol;
    }
}

public static class RetryHelper
{
    public static string ParseInput(string expr, string leftSide)
    {
        if (string.IsNullOrWhiteSpace(expr))
            throw new ArgumentException("Expression cannot be null or empty.", nameof(expr));

        if (string.IsNullOrWhiteSpace(leftSide))
            throw new ArgumentException("Left side of expression cannot be null or empty.", nameof(leftSide));

        expr = expr.Trim();

        //check not(expr)
        var isNegated = false;
        if (expr.StartsWith("not(") && expr.EndsWith(")"))
        {
            expr = expr[4..^1];
            isNegated = true;
        }
        //split to parts
        var exprParts = Regex.Split(expr, @",(?=(?:[^""]*""[^""]*"")*(?![^""]*""))");
        var conditionParts = new List<string>();
        foreach (var exprPart in exprParts)
        {
            var part = exprPart.Trim();

            //<,<=,>=,>
            if (part.StartsWith('<') || part.StartsWith('>'))
            {
                conditionParts.Add($"{leftSide}{part}");
                continue;
            }

            //[xx..yy] (] )[
            if ((part.StartsWith('[') || part.StartsWith(']') || part.StartsWith('(')) &&
                (part.EndsWith(']') || part.EndsWith('[') || part.EndsWith(')')))
            {
                var partInner = part[1..^1];
                var partInnerSplit = partInner.Split([".."], StringSplitOptions.None);
                if (partInnerSplit.Length == 2)
                {
                    var compLeft = part.StartsWith('[') ? ">=" : ">";
                    var compRight = part.EndsWith(']') ? "<=" : "<";
                    conditionParts.Add($"({leftSide}{compLeft}{partInnerSplit[0].Trim()} && {leftSide}{compRight}{partInnerSplit[1].Trim()})");
                    continue;
                }

                throw new FormatException($"Invalid S-FEEL range expression: '{part}'");
            }

            // string in "" "", number,variable name, .. - compare for eq
            conditionParts.Add($"{leftSide}=={part}");
        }

        var condition = string.Join(" || ", conditionParts);
        if (isNegated) condition = $"!({condition})";

        return condition;
    }

    public static bool EvalExpression(string expression)
    {
        if (string.IsNullOrWhiteSpace(expression)) throw new ArgumentException("Expression cannot be null or empty.", nameof(expression));

        var interpreter = new Interpreter();
        var parameters = new List<Parameter>();

        var parsedExpression = interpreter.Parse(expression);
        //Invoke expression to evaluate
        object result;
        try
        {
            result = parsedExpression.Invoke([.. parameters]);
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException($"Exception while invoking the expression '{expression}'.", exception);
        }

        try
        {
            return (bool)Convert.ChangeType(result, typeof(bool));
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException("Cannot convert the expression result to boolean.", exception);
        }
    }
}
