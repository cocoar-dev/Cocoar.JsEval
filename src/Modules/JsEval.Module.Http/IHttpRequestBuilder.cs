using System.Net.Http;
using System.Threading.Tasks;

#pragma warning disable CA1716 // 'Get', 'Delete', etc. conflict with reserved keywords — JS-facing interface API, cannot rename
namespace Cocoar.JsEval.Module.Http;

public interface IHttpRequestBuilder
{
    Task<HttpResponse> SendAsync(string httpMethod, object? content = null);
    Task<HttpResponse> SendAsync(HttpMethod httpMethod, object? content = null);
    Task<HttpResponse> SendRequestMessageAsync(HttpRequestMessage httpRequestMessage);

    Task<HttpResponse> GetAsync();
    Task<HttpResponse> PostAsync(object content);
    Task<HttpResponse> PutAsync(object content);
    Task<HttpResponse> PatchAsync(object content);
    Task<HttpResponse> DeleteAsync(object? content = null);

    HttpResponse Send(string httpMethod, object? content = null);
    HttpResponse Send(HttpMethod httpMethod, object? content = null);
    HttpResponse SendRequestMessage(HttpRequestMessage httpRequestMessage);
    HttpResponse Get();
    HttpResponse Post(object content);
    HttpResponse Put(object content);
    HttpResponse Patch(object content);
    HttpResponse Delete(object? content = null);
}
#pragma warning restore CA1716
