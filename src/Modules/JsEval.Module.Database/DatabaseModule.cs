using Microsoft.Data.SqlClient;
using Cocoar.JsEval;
using Npgsql;
using SqlKata.Compilers;
using SqlKata.Execution;

#pragma warning disable CA1716 // 'Module' in namespace conflicts with keyword — cannot rename without a breaking change
namespace Cocoar.JsEval.Module.Database;
#pragma warning restore CA1716

#pragma warning disable CA1822 // Instance methods required — Jint invokes these on a registered instance
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
#pragma warning restore CA1822
