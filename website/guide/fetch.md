# fetch() API

JsEval includes a browser-compatible `fetch()` global function for making HTTP requests from scripts. It must be explicitly enabled -- it is not available by default, so scripts in sandboxed environments cannot make HTTP requests unless the host allows it.

## Enabling fetch()

```csharp
services.AddJsEval(b => b.EnableFetch());
```

## Usage

Once enabled, `fetch()` is available as a global -- no import needed:

```javascript
const response = await fetch('https://api.example.com/data');
const text = await response.text();

// POST with JSON body
const response = await fetch('https://api.example.com/items', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ name: 'test' })
});
```

## Response Object

The response object matches the [Web Fetch API](https://developer.mozilla.org/en-US/docs/Web/API/Response):

| Property/Method | Type | Description |
|---|---|---|
| `ok` | `boolean` | `true` if status is 2xx |
| `status` | `number` | HTTP status code |
| `statusText` | `string` | HTTP status text |
| `url` | `string` | Request URL |
| `headers` | `Record<string, string>` | Response headers |
| `redirected` | `boolean` | Whether a redirect occurred |
| `text()` | `Promise<string>` | Body as string |
| `json()` | `Promise<any>` | Body parsed as JSON |
| `arrayBuffer()` | `Promise<ArrayBuffer>` | Body as bytes |

## Portability

Scripts using only `fetch()` are portable -- they can run in a browser without changes. This is the key difference from the [HTTP module](/guide/module-http), which provides a .NET-specific fluent API.

## fetchOptions

For .NET-specific scenarios (dev environments, proxies), use `fetchOptions` with optional chaining so scripts remain browser-compatible:

```javascript
// No-op in the browser, configures HttpClient in JsEval:
fetchOptions?.ignoreCertificateErrors(true);
fetchOptions?.timeout(60);
fetchOptions?.proxy('http://proxy.corp:8080');

// Chainable:
fetchOptions?.ignoreCertificateErrors(true).timeout(60);
```

## fetch() vs HTTP Module

| | `fetch()` | HTTP Module |
|---|---|---|
| **API style** | Web standard | .NET fluent builder |
| **Portability** | Browser-compatible | JsEval only |
| **Activation** | `EnableFetch()` on builder | `AddModule<HttpModule>()` on builder |
| **Import** | Global (no import) | `import * as http from 'http'` |
| **Retry policies** | Not built-in | S-FEEL expression support |
| **Auth helpers** | Manual headers | `SetBearerToken()`, `SetBasicAuthentication()` |
