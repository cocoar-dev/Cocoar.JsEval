# HTTP Module

The HTTP module provides a fluent HTTP client for making requests from scripts.

**Package:** `Cocoar.JsEval.Module.Http`

## Registration

```csharp
services.AddJsEval(b => b.AddModule<HttpModule>());
```

## Usage

### JavaScript / TypeScript

```javascript
import * as http from 'http'

// Simple GET request
const response = http.Client("https://api.example.com")
    .AddPath("users")
    .Get();

console.log(response.StatusCode);
console.log(response.Content);
```

### Fluent Builder

The `Client()` method returns a request builder with a chainable API:

```javascript
const response = http.Client("https://api.example.com")
    .AddPath("api", "v2", "users")
    .AddHeader("Accept", "application/json")
    .SetBearerToken("your-token")
    .AddQueryParam("page", "1")
    .Get();
```

### POST / PUT / PATCH / DELETE

```javascript
const body = { name: "New User", email: "user@example.com" };

const created = http.Client("https://api.example.com")
    .AddPath("users")
    .SetContentType("application/json")
    .Post(body);

const updated = http.Client("https://api.example.com")
    .AddPath("users", "123")
    .Put(body);

const deleted = http.Client("https://api.example.com")
    .AddPath("users", "123")
    .Delete();
```

### Retry Policies

Add retry policies for specific HTTP status codes using S-FEEL expressions:

```javascript
const response = http.Client("https://api.example.com")
    .AddPath("data")
    .AddRetry("429", 1000, 2000, 5000)     // Retry on 429 with delays
    .AddRetry("[500..599]", 2000)           // Retry on any 5xx
    .Get();
```

### Authentication

```javascript
// Bearer token
http.Client(url).SetBearerToken("token").Get();

// Basic authentication
http.Client(url).SetBasicAuthentication("user", "password").Get();
```

### Response

The response object provides:

| Property | Type | Description |
|----------|------|-------------|
| `StatusCode` | `int` | HTTP status code |
| `Content` | `string` | Response body as string |
| `Headers` | `object` | Response headers |
| `IsSuccessStatusCode` | `bool` | Whether status is 2xx |

::: tip
For browser-compatible HTTP requests, consider using the [fetch() API](/guide/fetch) instead. Scripts using `fetch()` can run in both .NET and browser environments without changes.
:::
