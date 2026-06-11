public static class ApiResults
{
    public static IResult JsonError(string error, int statusCode)
    {
        return Results.Json(new { error }, statusCode: statusCode);
    }

    public static IResult JsonMessage(string message, int statusCode)
    {
        return Results.Json(new { message }, statusCode: statusCode);
    }
}