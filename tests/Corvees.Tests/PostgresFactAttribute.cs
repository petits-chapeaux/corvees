public sealed class PostgresFactAttribute : FactAttribute
{
    public PostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("CORVEES_TEST_DATABASE_URL")))
            Skip = "Set CORVEES_TEST_DATABASE_URL to run PostgreSQL integration tests.";
    }
}
