using Microsoft.Data.SqlClient;
using Cocoar.JsEval;
using Npgsql;
using SqlKata.Compilers;
using SqlKata.Execution;

namespace Cocoar.JsEval.Module.Database;

public class DatabaseModule : IJsModule
{
    public QueryFactory SqlServerConnection(string connectionString)
    {
        var connection = new SqlConnection(connectionString);
        var compiler = new SqlServerCompiler();

        return new QueryFactory(connection, compiler);
    }

    public QueryFactory PostgresConnection(string connectionString)
    {
        var connection = new NpgsqlConnection(connectionString);
        var compiler = new PostgresCompiler();

        return new QueryFactory(connection, compiler);
    }
}
