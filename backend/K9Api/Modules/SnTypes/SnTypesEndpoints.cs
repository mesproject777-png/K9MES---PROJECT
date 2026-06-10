using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Npgsql;

public static class SnTypesEndpoints
{
    private const int MaxEpvUploadBytes = 10 * 1024 * 1024;
    private const int MaxEpvValues = 50000;

    private static readonly IReadOnlyDictionary<string, string> AllowedSnFieldTypes = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["RY"] = "Reliance Year (2014=A, 2015=B, ...)",
        ["RM"] = "Reliance Month (Jan=A, Feb=B, ...)",
        ["RMA"] = "Reliance RMA indicator (non-RMA/RMA)",
        ["Y"] = "Single digit year",
        ["YY"] = "Two digits year",
        ["YYY"] = "Full year (4 digits)",
        ["M(hex)"] = "Month hexadecimal",
        ["MM(dec)"] = "Month decimal",
        ["R_YY"] = "Reversed two digits year",
        ["R_MM(dec)"] = "Reversed month decimal",
        ["R_WW"] = "Reversed week of year",
        ["WW"] = "Week of year",
        ["DM"] = "Day of week",
        ["DD"] = "Date of month",
        ["DDD"] = "Day of year",
        ["String"] = "Constant string",
        ["Specific by PN"] = "PN specific field",
        ["Sequence(dec)"] = "Decimal counter",
        ["Sequence(hex)"] = "Hexadecimal counter",
        ["Sequence(alpha)"] = "Alphanumeric counter",
        ["Continuous sequence(dec)"] = "Continuous decimal counter",
        ["Continuous sequence(hex)"] = "Continuous hexadecimal counter",
        ["Continuous sequence(alpha)"] = "Continuous alphanumeric counter",
        ["WO"] = "Work Order number",
        ["Lot"] = "Lot number",
        ["SiteCode"] = "Site code with translation",
        ["SNFromEPV"] = "Generate SN from EPV",
        ["EPV"] = "External Provided Value",
        ["MACgen"] = "MAC address",
        ["Programmable"] = "Programmable field"
    };

    private static readonly HashSet<string> SequenceCounterTypes = new(new[]
    {
        "Sequence(dec)",
        "Sequence(hex)",
        "Sequence(alpha)"
    }, StringComparer.Ordinal);

    private static readonly HashSet<string> AllCounterTypes = new(new[]
    {
        "Sequence(dec)",
        "Sequence(hex)",
        "Sequence(alpha)",
        "Continuous sequence(dec)",
        "Continuous sequence(hex)",
        "Continuous sequence(alpha)"
    }, StringComparer.Ordinal);

    private static readonly HashSet<string> StringValueTypes = new(new[]
    {
        "String",
        "Specific by PN",
        "MACgen"
    }, StringComparer.Ordinal);

    private static readonly HashSet<string> EpvFieldTypes = new(new[]
    {
        "EPV",
        "SNFromEPV"
    }, StringComparer.Ordinal);

    private sealed record NormalizedSnTypeField(
        decimal SortOrder,
        string FieldType,
        string? FieldString,
        int? FieldSize,
        int? EpvTypeId,
        int? EpvSubTypeId);
    public static void MapSnTypes(WebApplication app)
    {
        app.MapGet("/api/sn-types", async () =>
        {
            await using var connection = await DbConnectionFactory.OpenConnectionAsync();
            var rows = await SqlQuery.QueryRowsAsync(
                connection,
                """
                SELECT
                  st.id,
                  st.sn_type_name,
                  st.remark,
                  st.created_at,
                  st.updated_at,
                  used_by.pn AS used_by_pn,
                  COALESCE(sf.number_of_fields, 0)::int AS number_of_fields,
                  COALESCE(sf.number_of_fields, 0)::int AS field_count,
                  COUNT(*) OVER ()::int AS total_count
                FROM sn_types st
                LEFT JOIN (
                  SELECT sn_type_id, COUNT(*)::int AS number_of_fields
                  FROM sn_type_fields
                  GROUP BY sn_type_id
                ) sf ON sf.sn_type_id = st.id
                LEFT JOIN LATERAL (
                  SELECT p.pn
                  FROM workflow_part_numbers p
                  WHERE p.sn_type_id = st.id
                  ORDER BY p.updated_at DESC, p.id DESC
                  LIMIT 1
                ) used_by ON TRUE
                ORDER BY st.created_at DESC, st.id DESC
                """);
            var total = rows.Count > 0 ? Convert.ToInt32(rows[0]["total_count"]) : 0;
            return Results.Json(new { data = rows, total });
        });

        app.MapGet("/api/sn-types/reference/field-types", () => Results.Json(AllowedSnFieldTypes));

        app.MapGet("/api/sn-types/{id:int}", async (int id) =>
        {
            await using var connection = await DbConnectionFactory.OpenConnectionAsync();
            var typeRows = await SqlQuery.QueryRowsAsync(connection, "SELECT * FROM sn_types WHERE id = @id", ("id", id));
            if (typeRows.Count == 0)
            {
                return ApiResults.JsonMessage("SN type not found", 404);
            }

            typeRows[0]["fields"] = await GetSnTypeFieldsAsync(connection, id);
            return Results.Json(typeRows[0]);
        });

        app.MapPost("/api/sn-types", async (HttpContext context) =>
        {
            var payload = await JsonBodyReader.ReadJsonBodyAsync(context.Request);
            var name = JsonBodyReader.ReadString(payload, "sn_type_name")?.Trim();
            var remark = JsonBodyReader.ReadString(payload, "remark")?.Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                return ApiResults.JsonMessage("sn_type_name is required", 400);
            }

            await using var connection = await DbConnectionFactory.OpenConnectionAsync();
            await using var transaction = await connection.BeginTransactionAsync();
            try
            {
                var rows = await SqlQuery.QueryRowsAsync(
                    connection,
                    "INSERT INTO sn_types (sn_type_name, remark) VALUES (@name, @remark) RETURNING *",
                    ("name", name),
                    ("remark", ToDbNullable(remark)));

                var defaultFields = await SqlQuery.QueryRowsAsync(
                    connection,
                    """
                    INSERT INTO sn_type_fields (sn_type_id, sort_order, field_type, field_string, field_size)
                    VALUES (@snTypeId, 10, 'Y', NULL, NULL)
                    RETURNING *
                    """,
                    ("snTypeId", rows[0]["id"]));

                await transaction.CommitAsync();
                rows[0]["fields"] = defaultFields;
                return Results.Json(rows[0], statusCode: 201);
            }
            catch (PostgresException ex) when (ex.SqlState == "23505")
            {
                await transaction.RollbackAsync();
                return ApiResults.JsonMessage("SN type already exists", 409);
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        });

        app.MapPut("/api/sn-types/{id:int}", async (int id, HttpContext context) =>
        {
            var payload = await JsonBodyReader.ReadJsonBodyAsync(context.Request);
            await using var connection = await DbConnectionFactory.OpenConnectionAsync();
            var existingRows = await SqlQuery.QueryRowsAsync(connection, "SELECT * FROM sn_types WHERE id = @id", ("id", id));
            if (existingRows.Count == 0)
            {
                return ApiResults.JsonMessage("SN type not found", 404);
            }

            var name = HasJsonProperty(payload, "sn_type_name")
                ? ReadFlexibleString(payload?["sn_type_name"])?.Trim()
                : existingRows[0]["sn_type_name"]?.ToString();
            if (string.IsNullOrWhiteSpace(name))
            {
                return ApiResults.JsonMessage("sn_type_name is required", 400);
            }

            var remark = HasJsonProperty(payload, "remark")
                ? ReadFlexibleString(payload?["remark"])?.Trim()
                : existingRows[0]["remark"]?.ToString();

            try
            {
                var rows = await SqlQuery.QueryRowsAsync(
                    connection,
                    "UPDATE sn_types SET sn_type_name = @name, remark = @remark, updated_at = NOW() WHERE id = @id RETURNING *",
                    ("name", name),
                    ("remark", ToDbNullable(string.IsNullOrWhiteSpace(remark) ? null : remark)),
                    ("id", id));
                return Results.Json(rows[0]);
            }
            catch (PostgresException ex) when (ex.SqlState == "23505")
            {
                return ApiResults.JsonMessage("SN type already exists", 409);
            }
        });

        app.MapDelete("/api/sn-types/{id:int}", async (int id) =>
        {
            await using var connection = await DbConnectionFactory.OpenConnectionAsync();
            await using var transaction = await connection.BeginTransactionAsync();
            try
            {
                await SqlQuery.ExecuteAsync(connection, "DELETE FROM sn_type_fields WHERE sn_type_id = @id", ("id", id));
                var rows = await SqlQuery.QueryRowsAsync(connection, "DELETE FROM sn_types WHERE id = @id RETURNING id", ("id", id));
                await transaction.CommitAsync();
                return rows.Count == 0 ? ApiResults.JsonMessage("SN type not found", 404) : Results.Json(new { message = "SN type deleted successfully" });
            }
            catch (PostgresException ex) when (ex.SqlState == "23503")
            {
                await transaction.RollbackAsync();
                return ApiResults.JsonMessage("SN type is in use and cannot be deleted", 409);
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        });

        app.MapPost("/api/sn-types/{snTypeId:int}/fields", async (int snTypeId, HttpContext context) => await SaveSnTypeFieldAsync(context, snTypeId, null));
        app.MapPut("/api/sn-types/fields/{fieldId:int}", async (int fieldId, HttpContext context) => await SaveSnTypeFieldAsync(context, null, fieldId));
        app.MapDelete("/api/sn-types/fields/{fieldId:int}", async (int fieldId) =>
        {
            await using var connection = await DbConnectionFactory.OpenConnectionAsync();
            var rows = await SqlQuery.QueryRowsAsync(connection, "DELETE FROM sn_type_fields WHERE id = @id RETURNING id", ("id", fieldId));
            return rows.Count == 0 ? ApiResults.JsonMessage("SN type field not found", 404) : Results.Json(new { message = "SN type field deleted successfully" });
        });

        app.MapGet("/api/sn-types/{id:int}/epv-uploads", async (int id) =>
        {
            await using var connection = await DbConnectionFactory.OpenConnectionAsync();
            if (await SqlQuery.ScalarAsync<long>(connection, "SELECT COUNT(*) FROM sn_types WHERE id = @id", ("id", id)) == 0)
            {
                return ApiResults.JsonMessage("SN Type not found", 404);
            }

            var rows = await SqlQuery.QueryRowsAsync(
                connection,
                """
                SELECT u.id, u.sn_type_id, u.file_name, u.mime_type, u.source_kind, u.record_count,
                       u.epv_type_id, et.type_name AS epv_type_name,
                       u.epv_sub_type_id, est.sub_type_name AS epv_sub_type_name,
                       u.created_at,
                       COALESCE(u.extracted_values->>0, '') AS first_value
                FROM sn_type_epv_uploads u
                LEFT JOIN epv_types et ON et.id = u.epv_type_id
                LEFT JOIN epv_sub_types est ON est.id = u.epv_sub_type_id
                WHERE u.sn_type_id = @id
                ORDER BY u.created_at DESC, u.id DESC
                LIMIT 50
                """,
                ("id", id));
            return Results.Json(new { data = rows });
        });

        app.MapPost("/api/sn-types/{id:int}/epv-upload", async (int id, HttpContext context) =>
        {
            var payload = await JsonBodyReader.ReadJsonBodyAsync(context.Request);
            var fileName = JsonBodyReader.ReadString(payload, "file_name")?.Trim();
            var mimeType = JsonBodyReader.ReadString(payload, "mime_type")?.Trim();
            var contentBase64 = JsonBodyReader.ReadString(payload, "file_content_base64");
            var epvTypeId = ReadInt(payload, "epv_type_id");
            var epvSubTypeId = ReadInt(payload, "epv_sub_type_id");

            if (string.IsNullOrWhiteSpace(fileName) || string.IsNullOrWhiteSpace(contentBase64))
            {
                return ApiResults.JsonMessage("file_name and file_content_base64 are required", 400);
            }

            if (epvTypeId is null || epvSubTypeId is null)
            {
                return ApiResults.JsonMessage("epv_type_id and epv_sub_type_id are required", 400);
            }

            byte[] bytes;
            try
            {
                bytes = Convert.FromBase64String(contentBase64);
            }
            catch (FormatException)
            {
                return ApiResults.JsonMessage("Invalid base64 file content", 400);
            }

            if (bytes.Length == 0)
            {
                return ApiResults.JsonMessage("Uploaded file is empty", 400);
            }

            if (bytes.Length > MaxEpvUploadBytes)
            {
                return ApiResults.JsonMessage($"File is too large. Max allowed size is {MaxEpvUploadBytes / (1024 * 1024)}MB", 413);
            }

            var fileKind = DetectFileKind(fileName, mimeType);
            if (fileKind == "unknown")
            {
                return ApiResults.JsonMessage("Unsupported file type. Allowed: .pdf, .txt, .csv, .json", 400);
            }

            var text = fileKind == "pdf"
                ? ExtractTextFromPdfBuffer(bytes)
                : Encoding.UTF8.GetString(bytes);

            if (string.IsNullOrWhiteSpace(text))
            {
                return ApiResults.JsonMessage("No readable text found in uploaded EPV file", 400);
            }

            var values = ExtractEpvValues(text);
            if (values.Length == 0)
            {
                return ApiResults.JsonMessage("No EPV values were detected in the uploaded file", 400);
            }

            await using var connection = await DbConnectionFactory.OpenConnectionAsync();
            if (await SqlQuery.ScalarAsync<long>(connection, "SELECT COUNT(*) FROM sn_types WHERE id = @id", ("id", id)) == 0)
            {
                return ApiResults.JsonMessage("SN Type not found", 404);
            }

            var epvTypeRows = await SqlQuery.QueryRowsAsync(
                connection,
                "SELECT id, type_name, regex_rule FROM epv_types WHERE id = @id",
                ("id", epvTypeId.Value));
            if (epvTypeRows.Count == 0)
            {
                return ApiResults.JsonMessage("EPV type not found", 404);
            }

            var epvSubTypeRows = await SqlQuery.QueryRowsAsync(
                connection,
                "SELECT id, epv_type_id, sub_type_name, regex_rule FROM epv_sub_types WHERE id = @id",
                ("id", epvSubTypeId.Value));
            if (epvSubTypeRows.Count == 0)
            {
                return ApiResults.JsonMessage("EPV sub-type not found", 404);
            }

            if (Convert.ToInt32(epvSubTypeRows[0]["epv_type_id"]) != epvTypeId.Value)
            {
                return ApiResults.JsonMessage("Selected sub-type does not belong to selected EPV type", 400);
            }

            var regexValidation = ValidateEpvValuesAgainstRegex(values, epvTypeRows[0], epvSubTypeRows[0]);
            if (regexValidation is not null)
            {
                return regexValidation;
            }

            await using var transaction = await connection.BeginTransactionAsync();
            try
            {
                var uploadRows = await SqlQuery.QueryRowsAsync(
                    connection,
                    """
                    INSERT INTO sn_type_epv_uploads
                      (sn_type_id, file_name, mime_type, source_kind, record_count, epv_type_id, epv_sub_type_id, extracted_values)
                    VALUES
                      (@snTypeId, @fileName, @mimeType, @sourceKind, @recordCount, @epvTypeId, @epvSubTypeId, @values::jsonb)
                    RETURNING id, sn_type_id, file_name, mime_type, source_kind, record_count, epv_type_id, epv_sub_type_id, created_at
                    """,
                    ("snTypeId", id),
                    ("fileName", fileName),
                    ("mimeType", ToDbNullable(mimeType)),
                    ("sourceKind", fileKind),
                    ("recordCount", values.Length),
                    ("epvTypeId", epvTypeId.Value),
                    ("epvSubTypeId", epvSubTypeId.Value),
                    ("values", JsonSerializer.Serialize(values)));

                var maxOrder = await SqlQuery.ScalarAsync<int>(
                    connection,
                    """
                    SELECT COALESCE(MAX(value_order), 0)::int
                    FROM sn_type_epv_values
                    WHERE epv_type_id = @epvTypeId
                      AND epv_sub_type_id = @epvSubTypeId
                    """,
                    ("epvTypeId", epvTypeId.Value),
                    ("epvSubTypeId", epvSubTypeId.Value));
                var nextOrderStart = maxOrder + 1;

                for (var index = 0; index < values.Length; index++)
                {
                    await SqlQuery.ExecuteAsync(
                        connection,
                        """
                        INSERT INTO sn_type_epv_values
                          (upload_id, sn_type_id, epv_type_id, epv_sub_type_id, value_order, epv_value)
                        VALUES
                          (@uploadId, @snTypeId, @epvTypeId, @epvSubTypeId, @valueOrder, @value)
                        """,
                        new (string Name, object? Value)[]
                        {
                            ("uploadId", uploadRows[0]["id"]),
                            ("snTypeId", id),
                            ("epvTypeId", epvTypeId.Value),
                            ("epvSubTypeId", epvSubTypeId.Value),
                            ("valueOrder", nextOrderStart + index),
                            ("value", values[index])
                        });
                }

                await transaction.CommitAsync();
                return Results.Json(new
                {
                    message = "EPV file uploaded and processed successfully",
                    upload = uploadRows[0],
                    values_preview = values.Take(20).ToArray(),
                    values_total = values.Length
                }, statusCode: 201);
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        });
    }

    private static async Task<IResult> SaveSnTypeFieldAsync(HttpContext context, int? snTypeId, int? fieldId)
    {
        var payload = await JsonBodyReader.ReadJsonBodyAsync(context.Request);

        await using var connection = await DbConnectionFactory.OpenConnectionAsync();
        Dictionary<string, object?>? existingField = null;
        var resolvedSnTypeId = snTypeId;

        if (fieldId is null)
        {
            if (snTypeId is null ||
                await SqlQuery.ScalarAsync<long>(connection, "SELECT COUNT(*) FROM sn_types WHERE id = @id", ("id", snTypeId.Value)) == 0)
            {
                return ApiResults.JsonMessage("SN Type not found", 404);
            }
        }
        else
        {
            var existingRows = await SqlQuery.QueryRowsAsync(connection, "SELECT * FROM sn_type_fields WHERE id = @id", ("id", fieldId.Value));
            if (existingRows.Count == 0)
            {
                return ApiResults.JsonMessage("Field not found", 404);
            }

            existingField = existingRows[0];
            resolvedSnTypeId = Convert.ToInt32(existingField["sn_type_id"]);
        }

        var (field, validationError) = NormalizeSnTypeFieldPayload(payload, existingField);
        if (validationError is not null)
        {
            return validationError;
        }

        if (SequenceCounterTypes.Contains(field!.FieldType) &&
            await HasSequenceCounterConflictAsync(connection, resolvedSnTypeId!.Value, fieldId))
        {
            return ApiResults.JsonMessage("Only one Sequence counter field is allowed per SN Type", 400);
        }

        if (EpvFieldTypes.Contains(field.FieldType))
        {
            var epvMappingError = await ValidateEpvMappingAsync(connection, field.EpvTypeId!.Value, field.EpvSubTypeId!.Value);
            if (epvMappingError is not null)
            {
                return ApiResults.JsonMessage(epvMappingError, 400);
            }
        }

        try
        {
            List<Dictionary<string, object?>> rows;
            if (fieldId is null)
            {
                rows = await SqlQuery.QueryRowsAsync(
                    connection,
                    """
                    INSERT INTO sn_type_fields (sn_type_id, sort_order, field_type, field_string, field_size, epv_type_id, epv_sub_type_id)
                    VALUES (@snTypeId, @sortOrder, @fieldType, @fieldString, @fieldSize, @epvTypeId, @epvSubTypeId)
                    RETURNING *
                    """,
                    ("snTypeId", resolvedSnTypeId!.Value),
                    ("sortOrder", field.SortOrder),
                    ("fieldType", field.FieldType),
                    ("fieldString", ToDbNullable(field.FieldString)),
                    ("fieldSize", ToDbNullable(field.FieldSize)),
                    ("epvTypeId", ToDbNullable(field.EpvTypeId)),
                    ("epvSubTypeId", ToDbNullable(field.EpvSubTypeId)));
                return Results.Json(rows[0], statusCode: 201);
            }

            rows = await SqlQuery.QueryRowsAsync(
                connection,
                """
                UPDATE sn_type_fields
                SET sort_order = @sortOrder,
                    field_type = @fieldType,
                    field_string = @fieldString,
                    field_size = @fieldSize,
                    epv_type_id = @epvTypeId,
                    epv_sub_type_id = @epvSubTypeId,
                    updated_at = NOW()
                WHERE id = @fieldId
                RETURNING *
                """,
                ("sortOrder", field.SortOrder),
                ("fieldType", field.FieldType),
                ("fieldString", ToDbNullable(field.FieldString)),
                ("fieldSize", ToDbNullable(field.FieldSize)),
                ("epvTypeId", ToDbNullable(field.EpvTypeId)),
                ("epvSubTypeId", ToDbNullable(field.EpvSubTypeId)),
                ("fieldId", fieldId.Value));
            return Results.Json(rows[0]);
        }
        catch (PostgresException ex) when (ex.SqlState == "23505")
        {
            return ApiResults.JsonMessage("Sort order already exists for this SN Type", 400);
        }
    }

    private static (NormalizedSnTypeField? Field, IResult? Error) NormalizeSnTypeFieldPayload(
        JsonNode? payload,
        Dictionary<string, object?>? existingField)
    {
        var sortOrder = ReadDecimalFromPayloadOrExisting(payload, "sort_order", existingField);
        if (sortOrder is null)
        {
            return (null, ApiResults.JsonMessage("Sort order is required", 400));
        }

        if (sortOrder.Value <= 0)
        {
            return (null, ApiResults.JsonMessage("Sort order must be a positive number", 400));
        }

        var fieldType = ReadStringFromPayloadOrExisting(payload, "field_type", existingField)?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(fieldType) || !AllowedSnFieldTypes.ContainsKey(fieldType))
        {
            return (null, Results.Json(new
            {
                message = "Invalid field type",
                allowedTypes = AllowedSnFieldTypes.Keys.ToArray()
            }, statusCode: 400));
        }

        var fieldStringInput = ReadStringFromPayloadOrExisting(payload, "field_string", existingField);
        var fieldSizeInput = ReadIntFromPayloadOrExisting(payload, "field_size", existingField);
        var epvTypeIdInput = ReadIntFromPayloadOrExisting(payload, "epv_type_id", existingField);
        var epvSubTypeIdInput = ReadIntFromPayloadOrExisting(payload, "epv_sub_type_id", existingField);

        string? fieldString = null;
        if (StringValueTypes.Contains(fieldType))
        {
            fieldString = fieldStringInput?.Trim();
            if (string.IsNullOrWhiteSpace(fieldString))
            {
                return (null, ApiResults.JsonMessage("String value is required for the selected field type", 400));
            }
        }

        int? fieldSize = null;
        if (AllCounterTypes.Contains(fieldType))
        {
            if (fieldSizeInput is null || fieldSizeInput.Value < 1 || fieldSizeInput.Value > 8)
            {
                return (null, ApiResults.JsonMessage("Field size for sequence types must be an integer between 1 and 8", 400));
            }

            fieldSize = fieldSizeInput.Value;
        }

        int? epvTypeId = null;
        int? epvSubTypeId = null;
        if (EpvFieldTypes.Contains(fieldType))
        {
            if (epvTypeIdInput is null || epvTypeIdInput.Value <= 0)
            {
                return (null, ApiResults.JsonMessage("EPV type is required for selected field type", 400));
            }

            if (epvSubTypeIdInput is null || epvSubTypeIdInput.Value <= 0)
            {
                return (null, ApiResults.JsonMessage("EPV sub-type is required for selected field type", 400));
            }

            epvTypeId = epvTypeIdInput.Value;
            epvSubTypeId = epvSubTypeIdInput.Value;
        }

        return (new NormalizedSnTypeField(sortOrder.Value, fieldType, fieldString, fieldSize, epvTypeId, epvSubTypeId), null);
    }

    private static async Task<bool> HasSequenceCounterConflictAsync(NpgsqlConnection connection, int snTypeId, int? excludeFieldId)
    {
        var sql = excludeFieldId is null
            ? """
              SELECT COUNT(*)::int
              FROM sn_type_fields
              WHERE sn_type_id = @snTypeId
                AND field_type = ANY(@counterTypes)
              """
            : """
              SELECT COUNT(*)::int
              FROM sn_type_fields
              WHERE sn_type_id = @snTypeId
                AND field_type = ANY(@counterTypes)
                AND id <> @fieldId
              """;

        var count = excludeFieldId is null
            ? await SqlQuery.ScalarAsync<int>(connection, sql, ("snTypeId", snTypeId), ("counterTypes", SequenceCounterTypes.ToArray()))
            : await SqlQuery.ScalarAsync<int>(connection, sql, ("snTypeId", snTypeId), ("counterTypes", SequenceCounterTypes.ToArray()), ("fieldId", excludeFieldId.Value));

        return count > 0;
    }

    private static async Task<string?> ValidateEpvMappingAsync(NpgsqlConnection connection, int epvTypeId, int epvSubTypeId)
    {
        var typeRows = await SqlQuery.QueryRowsAsync(connection, "SELECT id, type_name FROM epv_types WHERE id = @id", ("id", epvTypeId));
        if (typeRows.Count == 0)
        {
            return "EPV type not found";
        }

        var subTypeRows = await SqlQuery.QueryRowsAsync(
            connection,
            "SELECT id, epv_type_id, sub_type_name FROM epv_sub_types WHERE id = @id",
            ("id", epvSubTypeId));
        if (subTypeRows.Count == 0)
        {
            return "EPV sub-type not found";
        }

        return Convert.ToInt32(subTypeRows[0]["epv_type_id"]) == epvTypeId
            ? null
            : "Selected sub-type does not belong to selected EPV type";
    }

    private static async Task<List<Dictionary<string, object?>>> GetSnTypeFieldsAsync(NpgsqlConnection connection, int snTypeId)
    {
        return await SqlQuery.QueryRowsAsync(
            connection,
            """
            SELECT f.id, f.sn_type_id, f.sort_order, f.field_type, f.field_string, f.field_size,
                   f.epv_type_id, et.type_name AS epv_type_name,
                   f.epv_sub_type_id, est.sub_type_name AS epv_sub_type_name,
                   f.created_at, f.updated_at
            FROM sn_type_fields f
            LEFT JOIN epv_types et ON et.id = f.epv_type_id
            LEFT JOIN epv_sub_types est ON est.id = f.epv_sub_type_id
            WHERE f.sn_type_id = @snTypeId
            ORDER BY f.sort_order ASC, f.id ASC
            """,
            ("snTypeId", snTypeId));
    }

    private static IResult? ValidateEpvValuesAgainstRegex(
        string[] values,
        Dictionary<string, object?> epvType,
        Dictionary<string, object?> epvSubType)
    {
        var typeName = epvType["type_name"]?.ToString() ?? "selected type";
        var subTypeName = epvSubType["sub_type_name"]?.ToString() ?? "selected sub-type";
        var typeRegexRule = epvType["regex_rule"]?.ToString() ?? string.Empty;
        var subTypeRegexRule = epvSubType["regex_rule"]?.ToString() ?? string.Empty;

        Regex typeRegex;
        try
        {
            typeRegex = new Regex(typeRegexRule);
        }
        catch (ArgumentException)
        {
            return ApiResults.JsonMessage($"EPV type regex is invalid for type {typeName}", 400);
        }

        Regex subTypeRegex;
        try
        {
            subTypeRegex = new Regex(subTypeRegexRule);
        }
        catch (ArgumentException)
        {
            return ApiResults.JsonMessage($"EPV sub-type regex is invalid for sub-type {subTypeName}", 400);
        }

        var valuesFailingTypeRegex = new List<string>();
        var valuesFailingSubTypeRegex = new List<string>();
        foreach (var value in values)
        {
            var normalizedValue = value.Trim();
            if (!typeRegex.IsMatch(normalizedValue))
            {
                valuesFailingTypeRegex.Add(normalizedValue);
            }

            if (!subTypeRegex.IsMatch(normalizedValue))
            {
                valuesFailingSubTypeRegex.Add(normalizedValue);
            }
        }

        if (valuesFailingTypeRegex.Count == 0 && valuesFailingSubTypeRegex.Count == 0)
        {
            return null;
        }

        return Results.Json(new
        {
            message = "Uploaded EPV values do not match selected type/sub-type regex rules",
            selected_type = typeName,
            selected_sub_type = subTypeName,
            checked_count = values.Length,
            type_regex = typeRegexRule,
            sub_type_regex = subTypeRegexRule,
            failed_type_regex_count = valuesFailingTypeRegex.Count,
            failed_sub_type_regex_count = valuesFailingSubTypeRegex.Count,
            failed_type_regex_values_preview = valuesFailingTypeRegex.Take(20).ToArray(),
            failed_sub_type_regex_values_preview = valuesFailingSubTypeRegex.Take(20).ToArray()
        }, statusCode: 400);
    }

    private static string DetectFileKind(string fileName, string? mimeType)
    {
        var normalizedName = fileName.ToLowerInvariant();
        var normalizedMime = (mimeType ?? string.Empty).ToLowerInvariant();

        if (normalizedMime.Contains("pdf", StringComparison.Ordinal) || normalizedName.EndsWith(".pdf", StringComparison.Ordinal))
        {
            return "pdf";
        }

        return normalizedName.EndsWith(".txt", StringComparison.Ordinal) ||
               normalizedName.EndsWith(".csv", StringComparison.Ordinal) ||
               normalizedName.EndsWith(".json", StringComparison.Ordinal)
            ? "text"
            : "unknown";
    }

    private static string[] ExtractEpvValues(string text)
    {
        var directRows = text
            .Split(new[] { "\r\n", "\n" }, StringSplitOptions.None)
            .Select(NormalizeEpvValue)
            .Where(value => value.Length >= 6);

        var tokenMatches = Regex.Matches(text, @"[A-Za-z0-9][A-Za-z0-9._:-]{5,}")
            .Cast<Match>()
            .Select(match => NormalizeEpvValue(match.Value))
            .Where(value => value.Length >= 6);

        var values = directRows.Concat(tokenMatches);
        var deduped = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var value in values)
        {
            if (seen.Add(value))
            {
                deduped.Add(value);
            }

            if (deduped.Count >= MaxEpvValues)
            {
                break;
            }
        }

        return deduped.ToArray();
    }

    private static string NormalizeEpvValue(string value)
    {
        return value.Trim().Trim('\'', '"').Trim();
    }

    private static string ExtractTextFromPdfBuffer(byte[] buffer)
    {
        var latinSource = Encoding.Latin1.GetString(buffer);
        var fragments = CollectPdfTextFragments(latinSource);
        var searchIndex = 0;

        while (true)
        {
            var streamIndex = latinSource.IndexOf("stream", searchIndex, StringComparison.Ordinal);
            if (streamIndex == -1)
            {
                break;
            }

            var streamStartIndex = latinSource.IndexOf('\n', streamIndex);
            if (streamStartIndex == -1)
            {
                break;
            }

            var streamDataStart = streamStartIndex + 1;
            var streamEnd = latinSource.IndexOf("endstream", streamDataStart, StringComparison.Ordinal);
            if (streamEnd == -1)
            {
                break;
            }

            var headerStart = Math.Max(0, streamIndex - 240);
            var streamHeader = latinSource[headerStart..streamIndex];
            if (streamHeader.Contains("/FlateDecode", StringComparison.Ordinal))
            {
                var streamData = latinSource[streamDataStart..streamEnd];
                var inflated = TryInflatePdfStream(Encoding.Latin1.GetBytes(streamData));
                if (!string.IsNullOrEmpty(inflated))
                {
                    fragments.AddRange(CollectPdfTextFragments(inflated));
                }
            }

            searchIndex = streamEnd + "endstream".Length;
        }

        return string.Join("\n", fragments).Replace("\0", string.Empty, StringComparison.Ordinal).Trim();
    }

    private static List<string> CollectPdfTextFragments(string content)
    {
        var fragments = new List<string>();
        foreach (Match match in Regex.Matches(content, @"\(([^()]*(?:\\.[^()]*)*)\)\s*Tj", RegexOptions.Singleline))
        {
            fragments.Add(DecodePdfEscapes(match.Groups[1].Value));
        }

        foreach (Match arrayMatch in Regex.Matches(content, @"\[(.*?)\]\s*TJ", RegexOptions.Singleline))
        {
            foreach (Match tokenMatch in Regex.Matches(arrayMatch.Groups[1].Value, @"\(([^()]*(?:\\.[^()]*)*)\)"))
            {
                fragments.Add(DecodePdfEscapes(tokenMatch.Groups[1].Value));
            }
        }

        return fragments;
    }

    private static string DecodePdfEscapes(string value)
    {
        var escaped = Regex.Replace(value, @"\\([nrtbf()\\])", match => match.Groups[1].Value switch
        {
            "n" => "\n",
            "r" => "\r",
            "t" => "\t",
            "b" => "\b",
            "f" => "\f",
            "(" => "(",
            ")" => ")",
            "\\" => "\\",
            _ => match.Groups[1].Value
        });

        return Regex.Replace(escaped, @"\\([0-7]{1,3})", match =>
        {
            var code = Convert.ToInt32(match.Groups[1].Value, 8);
            return ((char)code).ToString();
        });
    }

    private static string? TryInflatePdfStream(byte[] streamBytes)
    {
        try
        {
            using var input = new MemoryStream(streamBytes);
            using var zlib = new ZLibStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();
            zlib.CopyTo(output);
            return Encoding.Latin1.GetString(output.ToArray());
        }
        catch
        {
            return null;
        }
    }

    private static bool HasJsonProperty(JsonNode? node, string key)
    {
        return node is JsonObject jsonObject && jsonObject.ContainsKey(key);
    }

    private static string? ReadStringFromPayloadOrExisting(
        JsonNode? payload,
        string key,
        Dictionary<string, object?>? existing)
    {
        if (HasJsonProperty(payload, key))
        {
            return ReadFlexibleString(payload?[key]);
        }

        return existing is not null && existing.TryGetValue(key, out var value) ? value?.ToString() : null;
    }

    private static decimal? ReadDecimalFromPayloadOrExisting(
        JsonNode? payload,
        string key,
        Dictionary<string, object?>? existing)
    {
        if (HasJsonProperty(payload, key))
        {
            return ReadFlexibleDecimal(payload?[key]);
        }

        return existing is not null && existing.TryGetValue(key, out var value) ? ReadDecimalFromObject(value) : null;
    }

    private static int? ReadIntFromPayloadOrExisting(
        JsonNode? payload,
        string key,
        Dictionary<string, object?>? existing)
    {
        if (HasJsonProperty(payload, key))
        {
            return ReadFlexibleInt(payload?[key]);
        }

        return existing is not null && existing.TryGetValue(key, out var value) ? ReadIntFromObject(value) : null;
    }

    private static string? ReadFlexibleString(JsonNode? value)
    {
        if (value is null)
        {
            return null;
        }

        if (value is JsonValue jsonValue)
        {
            if (jsonValue.TryGetValue<string>(out var stringValue))
            {
                return stringValue;
            }

            if (jsonValue.TryGetValue<int>(out var intValue))
            {
                return intValue.ToString(CultureInfo.InvariantCulture);
            }

            if (jsonValue.TryGetValue<long>(out var longValue))
            {
                return longValue.ToString(CultureInfo.InvariantCulture);
            }

            if (jsonValue.TryGetValue<decimal>(out var decimalValue))
            {
                return decimalValue.ToString(CultureInfo.InvariantCulture);
            }

            if (jsonValue.TryGetValue<bool>(out var boolValue))
            {
                return boolValue ? "true" : "false";
            }
        }

        return value.ToString();
    }

    private static decimal? ReadFlexibleDecimal(JsonNode? value)
    {
        if (value is null)
        {
            return null;
        }

        if (value is JsonValue jsonValue)
        {
            if (jsonValue.TryGetValue<decimal>(out var decimalValue))
            {
                return decimalValue;
            }

            if (jsonValue.TryGetValue<int>(out var intValue))
            {
                return intValue;
            }

            if (jsonValue.TryGetValue<long>(out var longValue))
            {
                return longValue;
            }

            if (jsonValue.TryGetValue<double>(out var doubleValue))
            {
                return Convert.ToDecimal(doubleValue);
            }

            if (jsonValue.TryGetValue<string>(out var stringValue) &&
                decimal.TryParse(stringValue, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed))
            {
                return parsed;
            }
        }

        return null;
    }

    private static int? ReadFlexibleInt(JsonNode? value)
    {
        var decimalValue = ReadFlexibleDecimal(value);
        return DecimalToInt(decimalValue);
    }

    private static decimal? ReadDecimalFromObject(object? value)
    {
        return value switch
        {
            null => null,
            decimal decimalValue => decimalValue,
            int intValue => intValue,
            long longValue => longValue,
            double doubleValue => Convert.ToDecimal(doubleValue),
            float floatValue => Convert.ToDecimal(floatValue),
            string stringValue when decimal.TryParse(stringValue, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => null
        };
    }

    private static int? ReadIntFromObject(object? value)
    {
        return DecimalToInt(ReadDecimalFromObject(value));
    }

    private static int? DecimalToInt(decimal? value)
    {
        if (value is null ||
            decimal.Truncate(value.Value) != value.Value ||
            value.Value < int.MinValue ||
            value.Value > int.MaxValue)
        {
            return null;
        }

        return (int)value.Value;
    }

    private static int? ReadInt(JsonNode? node, string key)
    {
        var value = node?[key];
        if (value is null)
        {
            return null;
        }

        if (value.GetValueKind() == JsonValueKind.String)
        {
            var stringValue = value.GetValue<string>();
            if (string.IsNullOrWhiteSpace(stringValue))
            {
                return null;
            }

            return int.TryParse(stringValue, out var parsed) ? parsed : null;
        }

        return value.GetValue<int>();
    }

    private static object? ToDbNullable<T>(T? value)
    {
        return value is null ? DBNull.Value : value;
    }
}


