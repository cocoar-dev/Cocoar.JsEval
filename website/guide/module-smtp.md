# SMTP Module

The SMTP module provides email sending capabilities using [MailKit](https://github.com/jstedfast/MailKit).

**Package:** `Cocoar.JsEval.Module.Smtp`

## Registration

```csharp
services.AddJsEval(b => b.AddModule<SmtpModule>());
```

## Sending Email

```javascript
import * as smtp from 'smtp'

const message = smtp.CreateMessage()
    .From("sender@example.com")
    .To("recipient@example.com")
    .Subject("Hello from JsEval")
    .Body("This email was sent from a script!");

smtp.Client()
    .UseSmtpServer("smtp.example.com")
    .UseSmtpServerPort(587)
    .UseSSL(true)
    .UseBasicAuthentication("user", "password")
    .SendMessage(message);
```

## Async Sending

```javascript
await smtp.Client()
    .UseSmtpServer("smtp.example.com")
    .UseSmtpServerPort(587)
    .UseSSL(true)
    .SendMessageAsync(message);
```

## SSL Options

```javascript
smtp.Client()
    .UseSmtpServer("smtp.example.com")
    .UseSSL(false)                    // Disable SSL
    .IgnoreSSlError(true)             // Ignore certificate errors
    .SendMessage(message);
```
