# Database Module

The Database module provides SQL Server and PostgreSQL connectivity using [SqlKata](https://sqlkata.com/) as the query builder.

**Package:** `Cocoar.JsEval.Module.Database`

## Registration

```csharp
services.AddJsEval(b => b.AddModule<DatabaseModule>());
```

## Connections

### SQL Server

```javascript
import * as db from 'database'

const query = db.SqlServerConnection("Server=localhost;Database=mydb;Trusted_Connection=true");
```

### PostgreSQL

```javascript
import * as db from 'database'

const query = db.PostgresConnection("Host=localhost;Database=mydb;Username=user;Password=pass");
```

## Querying

Both connection methods return a [SqlKata QueryFactory](https://sqlkata.com/docs) which supports a fluent query API:

```javascript
import * as db from 'database'

const factory = db.PostgresConnection(connectionString);

// Select
const users = factory.Query("users")
    .Where("active", true)
    .OrderBy("name")
    .Get();

// Insert
factory.Query("users").Insert({
    name: "John",
    email: "john@example.com"
});

// Update
factory.Query("users")
    .Where("id", 1)
    .Update({ name: "Jane" });

// Delete
factory.Query("users")
    .Where("id", 1)
    .Delete();
```

## Dependencies

- **Npgsql** for PostgreSQL
- **Microsoft.Data.SqlClient** for SQL Server
- **SqlKata** for query building
